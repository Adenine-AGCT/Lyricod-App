namespace Lyricod.Core;

[Flags]
public enum OverlayContentChanges
{
    None = 0,
    Lyrics = 1,
    Song = 2,
    Artwork = 4,
    Clock = 8,
    Progress = 16,
    All = Lyrics | Song | Artwork | Clock | Progress,
}

/// <summary>Hidden views discard intermediate updates and take one fresh model snapshot when shown.</summary>
public sealed class OverlayContentGate
{
    bool _needsSnapshot = true;

    public OverlayContentChanges Take(bool visible, OverlayContentChanges changes)
    {
        if (!visible)
        {
            _needsSnapshot = true;
            return OverlayContentChanges.None;
        }
        if (!_needsSnapshot) return changes;
        _needsSnapshot = false;
        return OverlayContentChanges.All;
    }
}
