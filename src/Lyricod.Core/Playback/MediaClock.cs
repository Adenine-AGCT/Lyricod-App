namespace Lyricod.Core;

/// <summary>The song, play state and raw timeline reported by a media player.</summary>
public readonly record struct MediaClockReport(string TrackId, double Position,
    DateTimeOffset PositionAt, bool IsPlaying, bool HasTimeline);

/// <summary>Keeps a steady playhead when playback toggles without a new timeline.</summary>
public sealed class MediaClock(Playhead playhead)
{
    MediaClockReport? _previous;

    public void Reset() => _previous = null;

    public bool Apply(MediaClockReport report, DateTimeOffset utcNow, double now, out bool trackChanged)
    {
        trackChanged = _previous is not { } previous || previous.TrackId != report.TrackId;
        bool newTimeline = report.HasTimeline && double.IsFinite(report.Position) &&
            (trackChanged || _previous is not { HasTimeline: true } old ||
             old.Position != report.Position || old.PositionAt != report.PositionAt);
        double position = trackChanged ? 0 : playhead.RawPosition(now);
        if (newTimeline)
        {
            position = report.Position;
            double elapsed = (utcNow - report.PositionAt).TotalSeconds;
            // Invalid or constant timestamps describe the reported position without extrapolation.
            if (report.IsPlaying && (trackChanged || _previous?.PositionAt != report.PositionAt) &&
                elapsed >= 0 && elapsed <= TimeSpan.FromDays(1).TotalSeconds)
                position += elapsed;
        }
        // A repeated timeline must not count the paused interval as playing time.
        // Report still anchors a changed play state at the current effective position.
        bool changed = playhead.Report(position, now, report.IsPlaying, force: trackChanged);
        _previous = report;
        return changed;
    }
}
