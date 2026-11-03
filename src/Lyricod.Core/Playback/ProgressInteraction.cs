namespace Lyricod.Core;

/// <summary>Progress display state. Hovering can preview time; only releasing a scrub requests a seek.</summary>
public sealed class ProgressInteraction(bool hoverPreview = false)
{
    public const double DwellSeconds = 0.2;
    public const double DwellTolerance = 4;

    string? _trackId;
    long _sessionId;
    double _duration;
    double? _scrub, _hover;
    double _anchorX, _anchorY;
    bool _previewing;

    public bool IsScrubbing => _scrub != null;
    public bool IsPreviewing => _previewing && !IsScrubbing;
    public double? HoverDueAt { get; private set; }

    /// <summary>A changed song, session or duration invalidates any pointer position picked for it.</summary>
    public void SetMedia(string? trackId, long sessionId, double duration)
    {
        duration = double.IsFinite(duration) && duration > 0 ? duration : 0;
        if (trackId != _trackId || sessionId != _sessionId || duration != _duration || duration == 0)
            Cancel();
        _trackId = trackId;
        _sessionId = sessionId;
        _duration = duration;
    }

    /// <summary>Positions are in DIPs; time is monotonic. Returning outside the bar cancels the dwell.</summary>
    public void Hover(double x, double y, double width, bool inside, double now)
    {
        if (!hoverPreview || IsScrubbing || _duration <= 0 || !inside || !double.IsFinite(x)
            || !double.IsFinite(y) || !double.IsFinite(width) || width <= 0)
        {
            CancelPreview();
            return;
        }
        _hover = Math.Clamp(x / width, 0, 1);
        if (_previewing) return;
        double dx = x - _anchorX, dy = y - _anchorY;
        if (HoverDueAt == null || dx * dx + dy * dy > DwellTolerance * DwellTolerance)
        {
            _anchorX = x;
            _anchorY = y;
            HoverDueAt = now + DwellSeconds;
        }
    }

    /// <summary>Called by a single-shot timer, including while playback is paused.</summary>
    public void ActivatePreview(double now)
    {
        if (HoverDueAt is not double due || now < due || _hover == null) return;
        HoverDueAt = null;
        _previewing = true;
    }

    public bool BeginScrub(double ratio, bool canSeek)
    {
        CancelPreview();
        if (!canSeek || _duration <= 0 || !double.IsFinite(ratio)) return false;
        _scrub = Math.Clamp(ratio, 0, 1);
        return true;
    }

    public void MoveScrub(double ratio)
    {
        if (IsScrubbing && double.IsFinite(ratio)) _scrub = Math.Clamp(ratio, 0, 1);
    }

    /// <summary>The only operation that returns a seek target. Preview state can never reach it.</summary>
    public double? ReleaseScrub(bool canSeek)
    {
        double? target = canSeek && _duration > 0 && _scrub is double ratio ? ratio * _duration : null;
        Cancel();
        return target;
    }

    public void CancelPreview()
    {
        _hover = null;
        HoverDueAt = null;
        _previewing = false;
    }

    public void Cancel()
    {
        _scrub = null;
        CancelPreview();
    }

    /// <summary>Hover changes the elapsed label and fill; remaining time keeps following real playback.</summary>
    public ProgressDisplay Display(double playerPosition)
    {
        double actual = double.IsFinite(playerPosition) ? Math.Clamp(playerPosition, 0, _duration) : 0;
        double shown = _scrub is double scrub ? scrub * _duration
            : IsPreviewing && _hover is double hover ? hover * _duration : actual;
        double remaining = _duration - (IsScrubbing ? shown : actual);
        return new ProgressDisplay(shown, remaining, _duration > 0 ? shown / _duration : 0, IsPreviewing);
    }
}

public readonly record struct ProgressDisplay(double Elapsed, double Remaining, double Fraction, bool IsPreviewing);
