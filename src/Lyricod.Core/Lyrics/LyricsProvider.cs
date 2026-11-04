using System.Text.Json;

namespace Lyricod.Core;

/// <summary>
/// Finds lyrics for a track: NetEase first (best coverage for Chinese music, has
/// translations and word timing), then QQ Music, then LRCLIB. Results are cached on disk.
/// A port of the macOS app's LyricsProvider; behaviour should stay the same.
/// </summary>
public sealed partial class LyricsProvider
{
    readonly HttpClient _http;
    readonly string _cacheDir;
    internal const int AliasCacheCapacity = 256, LrclibCacheCapacity = 128;
    /// <summary>An artist's other names, by the name a player shows (see <see cref="WithAliases"/>).</summary>
    readonly BoundedCache<(string Artist, bool AllowGuess), IReadOnlyList<string>> _aliasCache = new(AliasCacheCapacity);
    /// <summary>LRCLIB search results carry the lyrics too; kept so picking one needs no second request.</summary>
    readonly BoundedCache<string, RawLyrics> _lrclibSeen = new(LrclibCacheCapacity);

    static readonly JsonSerializerOptions CacheJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <param name="cacheDir">Where fetched lyrics are kept (one JSON file per song).</param>
    /// <param name="appVersion">Sent in the User-Agent: LRCLIB asks clients to name themselves.</param>
    public LyricsProvider(string cacheDir, string appVersion, HttpMessageHandler? handler = null)
    {
        _http = handler != null ? new HttpClient(handler) : new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(8);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) Lyricod/{appVersion} (+https://github.com/Indarkdust/Lyricod-App)");
        _cacheDir = cacheDir;
        Directory.CreateDirectory(cacheDir);
        Trim(cacheDir);
    }

    // ---- Main lookup ----

    /// <param name="search">The player's acceptance bar and this lookup's best score.</param>
    /// <param name="pinned">Lyrics the user chose for this track; skips searching.</param>
    /// <param name="onCached">Saved lyrics to show now while QQ Music is checked for a word-by-word
    /// copy; the result is then this same object unless the check found one.</param>
    public async Task<Lyrics?> GetLyrics(Track track, bool ignoreCache = false, LyricsRef? pinned = null, CancellationToken ct = default, Action<Lyrics>? onCached = null, LyricsSearch? search = null)
    {
        search ??= new LyricsSearch(isMusicPlayer: true);
        var file = CacheFile(CacheKey(track));
        track = search.Prepare(track);
        if (!ignoreCache && ReadCache(file) is { } cachedRaw && (pinned == null || cachedRaw.SongRef == pinned)
            && (pinned != null || !search.AutomaticLookupAllowed || search.Accepts(cachedRaw.MatchScore, cachedRaw.TitleMatched))
            && cachedRaw.Parse() is { } cached) // cached junk (e.g. credits only) is fetched again
        {
            search.Observe(cachedRaw.MatchScore);
            if (!search.AutomaticLookupAllowed || pinned != null || !NeedsWordCheck(cachedRaw, cached) || !WordCheckDue(file)) return cached;
            onCached?.Invoke(cached);
            ct.ThrowIfCancellationRequested();
            var words = await WordTimed(await WithAliases(track, ct, allowGuess: search.IsMusicPlayer), cachedRaw, requireTitle: true, ct, search);
            ct.ThrowIfCancellationRequested();
            if (words?.Parse() is not { } upgraded)
            {
                // Not this time (no word-by-word copy, or QQ Music didn't answer): try again tomorrow.
                try { File.SetLastWriteTimeUtc(file, DateTime.UtcNow); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                return cached;
            }
            WriteCache(file, words);
            return upgraded;
        }
        if (!search.AutomaticLookupAllowed && pinned == null) return null;
        if (pinned == null) track = await WithAliases(track, ct, allowGuess: search.IsMusicPlayer);
        RawLyrics? raw;
        if (pinned != null) raw = await Fetch(pinned, ct);
        else
        {
            // Timed lyrics from any source beat untimed ones from an earlier source (QQ Music has
            // plain uploads where LRCLIB has the timed version); untimed is the last resort. Word
            // timing beats line timing: see WordTimed.
            // Results whose title matches come first, from every source; one that only has the
            // artist and length (an English title for a song listed only in Chinese) is a fallback.
            // A player can report the wrong length (QQ Music did), and the artist's other song of
            // that length then won from the first source searched.
            raw = null;
            foreach (bool requireTitle in search.IsMusicPlayer ? new[] { true, false } : new[] { true })
            {
                RawLyrics? untimed = null;
                foreach (var source in new Func<Track, bool, CancellationToken, Task<RawLyrics?>>[]
                {
                    (t, r, c) => FromNetease(t, r, c, search),
                    (t, r, c) => FromQq(t, r, c, search: search),
                    (t, r, c) => FromLrclib(t, r, c, search),
                })
                {
                    var found = await source(track, requireTitle, ct);
                    if (found?.Parse() is not { } candidate) continue;
                    if (candidate.IsSynced) { raw = found; break; }
                    untimed ??= found;
                }
                if (raw != null) { raw = await WordTimed(track, raw, requireTitle, ct, search) ?? raw; break; }
                if (untimed != null) { raw = untimed; break; }
            }
        }
        if (raw == null || pinned == null && !search.Accepts(raw.MatchScore, raw.TitleMatched)
            || raw.Parse() is not { } parsed) return null;
        ct.ThrowIfCancellationRequested();
        WriteCache(file, raw);
        return parsed;
    }

    /// <summary>NetEase lyrics with line timing only: QQ Music may have the song word by word.</summary>
    static bool NeedsWordCheck(RawLyrics raw, Lyrics parsed) =>
        raw.SongRef?.Source == LyricsSource.Netease && parsed is { IsSynced: true, IsWordSynced: false };

    /// <summary>
    /// Saved line-timed lyrics are checked against QQ Music at most once a day, so a song QQ Music
    /// doesn't have word by word costs one lookup a day, and one it couldn't be asked about
    /// (offline, throttled) is tried again. The file's date marks the last try.
    /// </summary>
    static bool WordCheckDue(string file) => DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(24);

    /// <summary>
    /// NetEase comes first but has many songs with line timing only that QQ Music has word by word
    /// (周杰伦's 龙卷风): for those, QQ Music's copy, when it's clearly this recording (title, artist
    /// and length agree, or the exact song QQ Music is playing) and keeps any translation. Null
    /// when <paramref name="raw"/> should stay.
    /// </summary>
    async Task<RawLyrics?> WordTimed(Track track, RawLyrics raw, bool requireTitle, CancellationToken ct, LyricsSearch search)
    {
        if (raw.Parse() is not { } lines || !NeedsWordCheck(raw, lines)) return null;
        if (await FromQq(track, requireTitle, ct, minScore: 7, search: search) is not { } qq || qq.Parse() is not { IsWordSynced: true } words) return null;
        ct.ThrowIfCancellationRequested();
        static bool Translated(Lyrics l) => l.Lines.Any(x => x.Translation != null);
        return Translated(lines) && !Translated(words) ? null : qq;
    }

    /// <summary>Lyrics for one song in any source.</summary>
    public Task<RawLyrics?> Fetch(LyricsRef r, CancellationToken ct = default) => r.Source switch
    {
        LyricsSource.Netease when long.TryParse(r.Id, out var id) => FetchNetease(id, ct),
        LyricsSource.Netease => Task.FromResult<RawLyrics?>(null),
        LyricsSource.Qq => FetchQqSong(r.Id, r.QqId, ct),
        _ => FetchLrclib(r.Id, ct),
    };

    /// <summary>
    /// Search results from every source, scored against <paramref name="track"/>, best first.
    /// For Choose Lyrics. Among good matches, <paramref name="preferred"/> sources come first:
    /// LRCLIB alone often has a dozen uploads of one song.
    /// </summary>
    public async Task<List<LyricsCandidate>> Candidates(string query, Track track, IReadOnlyList<LyricsSource> preferred, CancellationToken ct = default)
    {
        track = await WithAliases(track, ct);
        var netease = NeteaseCandidates(query, track, ct);
        var qq = QqCandidates(query, track, ct);
        var lrclib = LrclibCandidates(query, track, ct);
        var all = (await netease).Concat(await qq).Concat((await lrclib).Take(10)).ToList();
        static int Tier(LyricsCandidate c) => c.Score >= 7 ? 2 : c.Score >= 3 ? 1 : 0;
        int Rank(LyricsCandidate c) { var i = preferred.ToList().IndexOf(c.Id.Source); return i < 0 ? preferred.Count : i; }
        return all.Select((c, i) => (c, i))
            .OrderByDescending(x => Tier(x.c)).ThenBy(x => Rank(x.c)).ThenByDescending(x => x.c.Score).ThenBy(x => x.i)
            .Select(x => x.c).ToList();
    }
}
