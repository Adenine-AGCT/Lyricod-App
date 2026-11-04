namespace Lyricod.Core;

public sealed partial class LyricsProvider
{
    /// <summary>
    /// <paramref name="track"/> with its artist's other names from NetEase's artist records:
    /// players often show an English name ("Crowd Lu") where lyric sites use the original
    /// (卢广仲), and the other way round.
    /// </summary>
    public async Task<Track> WithAliases(Track track, CancellationToken ct = default, bool allowGuess = true)
    {
        // The first credited artist ("A & B", "A / B", "A, B").
        var primary = track.Artist.Split(['&', '/', ',', '、', ';'])[0].Trim();
        if (primary.Length == 0) return track;
        if (_aliasCache.TryGetValue((primary, allowGuess), out var cached)) return track with { ArtistAliases = cached };
        var names = new List<string>();
        var res = await GetJson(NeteaseRequest(HttpMethod.Post,
            "https://music.163.com/api/cloudsearch/pc?" + Query(("s", primary), ("type", "100"), ("limit", "3"))), ct);
        var want = TrackMatcher.Normalize(primary);
        var artists = Arr(Field(res, "result", "artists")).Select(a => new[] { Str(Field(a, "name")) }.Concat(Arr(Field(a, "alias")).Select(Str)).Append(Str(Field(a, "trans")))
            .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList()).ToList();
        var found = artists.Where(all => all.Any(s => TrackMatcher.Normalize(s) == want)).Take(1).ToList();
        // A romanised name, as YouTube Music and Spotify show it, may be missing from NetEase's
        // records ("Kenshi Yonezu" for 米津玄師) or listed under someone else too (優莉 lists "Yuuri";
        // 優里, the top hit, doesn't): NetEase's top hit counts as well when it has a Chinese,
        // Japanese or Korean name. A wrong guess still needs the title or the length to agree.
        if (allowGuess && IsLatinName(primary) && artists.FirstOrDefault() is { } top && top.Any(HasCjk) && !found.Contains(top))
            found.Insert(0, top);
        names = found.SelectMany(all => all).Where(s => TrackMatcher.Normalize(s) != want).Distinct().ToList();
        var aliases = names.AsReadOnly();
        // Remembered only when NetEase answered, so a failed lookup is tried again.
        if (res != null) _aliasCache.Set((primary, allowGuess), aliases);
        return track with { ArtistAliases = aliases };
    }

    static bool IsLatinName(string s) => s.Any(char.IsLetter) && s.All(c => c < 'ɐ' || char.IsWhiteSpace(c) || char.IsPunctuation(c));
    static bool HasCjk(string s) => s.Any(c => c >= '⺀');
}
