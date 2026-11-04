using System.Globalization;
using System.Text.Json.Nodes;

namespace Lyricod.Core;

public sealed partial class LyricsProvider
{
    static RawLyrics? LrclibRaw(JsonNode? r) => r == null ? null : new RawLyrics
    {
        Source = "LRCLIB", Lrc = Str(Field(r, "syncedLyrics")), Plain = Str(Field(r, "plainLyrics")),
        Ref = Long(Field(r, "id")) is { } id ? new LyricsRef(LyricsSource.Lrclib, id.ToString(CultureInfo.InvariantCulture)) : null,
    };

    async Task<RawLyrics?> FromLrclib(Track track, bool requireTitle, CancellationToken ct, LyricsSearch search)
    {
        var url = "https://lrclib.net/api/get?" + Query(("track_name", track.Title), ("artist_name", track.Artist),
            ("album_name", track.Album), ("duration", ((long)Math.Round(track.Duration)).ToString(CultureInfo.InvariantCulture)));
        // The exact lookup goes by title, so it's only worth asking on the first pass.
        if (requireTitle)
        {
            var response = await GetJson(new HttpRequestMessage(HttpMethod.Get, url), ct, require200: true);
            if (LrclibRaw(response) is { } exact)
            {
                var title = Str(Field(response, "trackName"));
                if (title == null)
                {
                    if (search.IsMusicPlayer) return exact;
                }
                else
                {
                    var artist = Str(Field(response, "artistName")) ?? "";
                    double score = TrackMatcher.Score([title], [artist], Num(Field(response, "duration")) ?? 0, track, Str(Field(response, "albumName")));
                    bool titleMatched = TrackMatcher.TitleMatches([title], track);
                    search.Observe(score);
                    if (search.Accepts(score, titleMatched)) return exact.Matched(score, titleMatched);
                }
            }
        }
        // The exact lookup needs the album too, which browsers often don't report: search, and
        // take the best match that has timed lyrics.
        // Keep this response's lyrics until selection finishes, even if concurrent searches evict them.
        var results = await SearchLrclib($"{track.Title} {track.Artist}", track, ct);
        foreach (var result in results) search.Observe(result.Candidate.Score);
        foreach (var (c, raw) in results)
        {
            if (c.Score < (search.IsMusicPlayer ? 6 : LyricsSearch.ConfidentScore)) break;
            if (requireTitle && !c.TitleMatched) continue;
            if (!search.Accepts(c.Score, c.TitleMatched)) continue;
            if (!string.IsNullOrEmpty(raw.Lrc)) return raw.Matched(c.Score, c.TitleMatched);
        }
        return null;
    }

    public async Task<RawLyrics?> FetchLrclib(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_lrclibSeen.TryGetValue(id, out var seen)) return seen;
        return LrclibRaw(await GetJson(new HttpRequestMessage(HttpMethod.Get, "https://lrclib.net/api/get/" + Uri.EscapeDataString(id)), ct, require200: true));
    }

    /// <summary>LRCLIB search results scored against <paramref name="track"/>, best first. Records without lyrics are left out.</summary>
    public async Task<List<LyricsCandidate>> LrclibCandidates(string query, Track track, CancellationToken ct = default) =>
        [.. (await SearchLrclib(query, track, ct)).Select(result => result.Candidate)];

    async Task<List<(LyricsCandidate Candidate, RawLyrics Raw)>> SearchLrclib(string query, Track track, CancellationToken ct)
    {
        var res = await GetJson(new HttpRequestMessage(HttpMethod.Get, "https://lrclib.net/api/search?" + Query(("q", query))), ct, require200: true);
        var list = new List<(LyricsCandidate Candidate, RawLyrics Raw)>();
        foreach (var r in Arr(res).Take(20))
        {
            ct.ThrowIfCancellationRequested();
            if (Long(Field(r, "id")) is not { } id || Str(Field(r, "trackName")) is not { } title) continue;
            if (string.IsNullOrEmpty(Str(Field(r, "syncedLyrics")) ?? Str(Field(r, "plainLyrics")))) continue;
            var key = id.ToString(CultureInfo.InvariantCulture);
            var reference = new LyricsRef(LyricsSource.Lrclib, key);
            var raw = LrclibRaw(r)!;
            raw.Ref = reference;
            _lrclibSeen.Set(key, raw);
            var artist = Str(Field(r, "artistName")) ?? "";
            var duration = Num(Field(r, "duration")) ?? 0;
            var candidate = new LyricsCandidate(reference, title, artist, Str(Field(r, "albumName")) ?? "", duration,
                TrackMatcher.Score([title], [artist], duration, track, Str(Field(r, "albumName"))), TrackMatcher.TitleMatches([title], track));
            list.Add((candidate, raw));
        }
        return [.. list.OrderByDescending(result => result.Candidate.Score)];
    }
}
