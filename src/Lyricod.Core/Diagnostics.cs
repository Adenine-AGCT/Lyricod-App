using System.Globalization;

namespace Lyricod.Core;

/// <summary>Deliberately English, including numbers and source names, for bug reports.</summary>
public static class Diagnostics
{
    public static string Text(string version, string system, string architecture,
        string? playerName, string? playerId, bool isPlaying, Track? track, Lyrics? lyrics,
        bool searching, bool pinned, double songOffset, double offset, string? lastError = null, string? userProfile = null,
        bool? playerTimeline = null, (int Width, int Height)? cover = null, string? font = null, InstallKind? installKind = null,
        bool? musicPlayer = null, bool? treatedAsSong = null, double? bestMatchScore = null, bool? automaticLookupAllowed = null,
        long? coverBytes = null, bool? coverWhole = null)
    {
        // Error messages and some player ids carry paths with the user's name in them.
        playerId = Redact(playerId, userProfile);
        lastError = Redact(lastError, userProfile);
        var rows = new List<string> { $"Lyricod {version} · {system} · {architecture}" };
        if (installKind is { } installation) rows.Add($"Install: {installation.ToString().ToLowerInvariant()}");
        if (font != null) rows.Add($"Font: {Redact(font, userProfile)}");
        if (playerId != null)
        {
            string player = $"Player: {playerName ?? playerId} ({playerId}) · {(isPlaying ? "playing" : "paused")}";
            // Without the player's position, Lyricod times the song itself, and seeking can put it out of step.
            if (playerTimeline is { } timeline) player += timeline ? " · position from the player" : " · no position from the player";
            rows.Add(player);
        }
        if (track != null)
        {
            string song = $"Song: {track.Title} — {track.Artist}";
            if (double.IsFinite(track.Duration) && track.Duration > 0 && track.Duration < int.MaxValue)
            {
                int seconds = (int)Math.Round(track.Duration);
                song += FormattableString.Invariant($" · {seconds / 60}:{seconds % 60:00}");
            }
            if (track.QqSongMid != null) song += $" · QQ {track.QqSongMid}";
            else if (track.QqSongId != null) song += $" · QQ {track.QqSongId}";
            rows.Add(song);
        }
        if (musicPlayer is { } music) rows.Add($"Music player: {(music ? "yes" : "no")}");
        if (automaticLookupAllowed is { } lookup) rows.Add($"Automatic lyric lookup: {(lookup ? "allowed" : "blocked")}");
        if (treatedAsSong is { } songState)
        {
            rows.Add($"Treated as a song: {(songState ? "yes" : "no")}");
            rows.Add("Best match score: " + (bestMatchScore?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unverified"));
        }
        if (cover is { } c)
        {
            // How big the cover the player handed over is, and whether it came whole (see ArtworkCheck).
            string size = coverBytes is not { } n ? "" : n < 1024 * 1024 ? FormattableString.Invariant($" · {Math.Max(1, n / 1024)} KB")
                : FormattableString.Invariant($" · {n / (1024.0 * 1024):0.0} MB");
            string whole = coverWhole is not { } w ? "" : w ? " · whole" : " · cut short";
            rows.Add(FormattableString.Invariant($"Cover: {c.Width}×{c.Height} px{size}{whole}"));
        }
        if (lyrics != null)
        {
            string source = lyrics.Ref?.Source switch
            {
                LyricsSource.Qq => "QQ Music", LyricsSource.Netease => "NetEase", LyricsSource.Lrclib => "LRCLIB", _ => lyrics.Source,
            };
            string kind = !lyrics.IsSynced ? "unsynced" : lyrics.IsWordSynced ? "word by word" : "line by line";
            string line = $"Lyrics: {source} · {kind} · {lyrics.Lines.Count} lines";
            if (lyrics.Ref is { } reference) line += $" · {reference.Source.ToString().ToLowerInvariant()}:{reference.Id}";
            rows.Add(line + $" · picked by hand: {(pinned ? "yes" : "no")}");
        }
        else if (track != null) rows.Add(searching ? "Lyrics: searching" : "Lyrics: none");
        string songTiming = track == null ? "" : $"this song {Seconds(songOffset)} · ";
        rows.Add($"Timing: {songTiming}all songs {Seconds(offset)}");
        if (!string.IsNullOrEmpty(lastError)) rows.Add($"Last error: {lastError}");
        return string.Join("\n", rows);
    }

    static string? Redact(string? text, string? userProfile) =>
        text == null || string.IsNullOrEmpty(userProfile) ? text
        : text.Replace(userProfile.TrimEnd('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

    static string Seconds(double value) => value.ToString("+0.00;−0.00;+0.00", CultureInfo.InvariantCulture) + " s";
}
