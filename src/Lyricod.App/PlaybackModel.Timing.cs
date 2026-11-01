using Lyricod.Core;

namespace Lyricod;

public sealed partial class PlaybackModel
{
    void ApplyOffset()
    {
        SongOffset = Track is { } t ? _overrides[t].Offset ?? 0 : 0;
        _playhead.Offset = _settings.Offset + SongOffset;
        // Re-anchoring where it is bumps the clock version, so the sweep re-plans.
        _playhead.Anchor(_playhead.RawPosition(Now), Now, _playhead.IsPlaying);
        Changed?.Invoke(Change.Clock);
        Tick();
    }

    /// <summary>Nudges this song's timing (positive shows lyrics earlier) and remembers it.</summary>
    public void SetSongOffset(double value)
    {
        if (!TryApplyPendingDuration() || Track is not { } t) return;
        var o = _overrides[t];
        o.Offset = Math.Round(value, 2);
        if (TrySaveSongChoice(t, o)) ApplyOffset();
    }

    /// <summary>
    /// Updates the current line, then sets one timer for when the next line starts. Anything
    /// that moves the playhead, pauses, or changes the lyrics calls this again.
    /// </summary>
    void Tick()
    {
        if (_stopped) return;
        double now = Position();
        var index = Lyrics?.LineIndex(now);
        if (index != CurrentIndex)
        {
            CurrentIndex = index;
            Changed?.Invoke(Change.Line);
        }
        WriteDebugState(now);
        _lineTimer.Stop();
        if (!IsPlaying || Lyrics is not { IsSynced: true } lyrics) return;
        int next = index is int i ? i + 1 : 0;
        if (next >= lyrics.Lines.Count) return;
        _lineTimer.Interval = TimeSpan.FromSeconds(Math.Max(lyrics.Lines[next].Start - now, 0) + 0.005);
        _lineTimer.Start();
    }

    /// <summary>Debug: LYRICOD_STATE_FILE=path writes the playhead and line for checks from outside.</summary>
    static readonly string? DebugStatePath = Environment.GetEnvironmentVariable("LYRICOD_STATE_FILE");

    public string DiagnosticsText => Diagnostics.Text(
        UpdateChecker.TestBuild is { } build ? $"{UpdateChecker.Version} (test build {build})"
            : $"{UpdateChecker.Version} ({typeof(App).Assembly.GetName().Version?.Revision ?? 0})",
        $"Windows {Environment.OSVersion.Version}",
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        PlayerName, NowPlaying?.AppId, IsPlaying, Track, Lyrics, IsSearching, LyricsPinned,
        SongOffset, _settings.Offset, LastError, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        NowPlaying?.HasTimeline, ArtworkSize, Fonts.DiagnosticsText, UpdateChecker.Kind, IsMusicPlayer, IsSong, BestMatchScore, AutomaticLookupAllowed);

    void WriteDebugState(double position)
    {
        if (DebugStatePath == null) return;
        try
        {
            File.WriteAllText(DebugStatePath, System.Text.Json.JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.Now, title = NowPlaying?.Title, artist = NowPlaying?.Artist, app = NowPlaying?.AppId,
                duration = NowPlaying?.Duration, searchedWithDuration = Track?.Duration, source = Lyrics?.Source,
                reportedPosition = NowPlaying?.Position, reportedAt = NowPlaying?.PositionAt, hasTimeline = NowPlaying?.HasTimeline,
                reportedPlaying = NowPlaying?.IsPlaying, playing = IsPlaying, position, lines = Lyrics?.Lines.Count,
                synced = Lyrics?.IsSynced, firstLine = Lyrics?.Lines.FirstOrDefault()?.Start, index = CurrentIndex, line = CurrentLine?.Text, error = LastError,
                meter = Platform.AudioMeter.LastStatus,
                musicPlayer = IsMusicPlayer, song = IsSong, bestMatchScore = BestMatchScore,
                automaticLookupAllowed = AutomaticLookupAllowed,
            }));
        }
        catch (IOException) { }
    }
}
