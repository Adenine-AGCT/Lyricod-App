using Lyricod.Core;
using Lyricod.Platform;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Lyricod;

public sealed partial class IslandWindow
{
    void CheckPointer()
    {
        if (!_shown || _closed || !Win32.GetCursorPos(out var p)) return;
        if (Movable)
        {
            // Being moved: no hover action.
            _pointerTimer.Interval = TimeSpan.FromMilliseconds(250);
            return;
        }
        var (left, top, width, height) = PillBounds();
        // The default island's top is the screen's edge; a floating one gets the same margin as its other sides.
        double above = Floating ? 2 : 0;
        bool inside = p.X >= left - 2 && p.X <= left + width + 2 && p.Y >= top - above && p.Y <= top + height + 2;
        bool gesture = _settings.IslandHover == IslandHover.ByDirection;
        // The gesture needs the pointer's path, so it's followed closely from further out.
        double reach = gesture ? 3 : 1;
        bool near = (!Floating || p.Y >= top - 120 * reach) && p.Y <= top + height + 120 * reach
            && p.X >= left - 160 * reach && p.X <= left + width + 160 * reach;
        _pointerTimer.Interval = TimeSpan.FromMilliseconds(near || _hovered ? 33 : 250);
        double now = PlaybackModel.Now;
        // "By direction" hides the island however the pointer comes in; sweeping sideways and then
        // pulling down over it (floating: up or down; inside or carrying on out of it) opens the mini
        // player instead.
        bool pulled = false;
        if (gesture)
        {
            var path = _trail.ConvertAll(s => (s.X, s.Y));
            path.Add((p.X, p.Y));
            pulled = IslandGesture.IsSweepThenPull(path, (left - 2, top - above, width + 4, height + 2 + above), _overlay.Scale, downOnly: !Floating);
        }
        if (inside)
        {
            _lastInside = now;
            if (!_hovered) SetHovered(true, gesture && !pulled);
            else if (pulled && _hoverAction == IslandHover.Hide) OpenFromHidden(now);
        }
        else
        {
            // Pulled out past its edge: open it; the mini player grows out under the pointer.
            if (pulled && _hovered && _hoverAction == IslandHover.Hide) OpenFromHidden(now + 0.3);
            else if (_hovered && now - _lastInside > 0.3) SetHovered(false); // a moment's grace when brushing past
        }
        // The last moment and a half of movement, for the gesture.
        _trail.Add((p.X, p.Y, now));
        _trail.RemoveAll(s => now - s.T > 1.5);
    }

    /// <summary>The island hid as the pointer came in, then the gesture asked for the mini player.</summary>
    void OpenFromHidden(double insideUntil)
    {
        SetHovered(false);
        SetHovered(true);
        // Time for the player to grow out under a pointer that pulled past the edge.
        _lastInside = insideUntil;
        _trail.Clear();
    }

    void SetHovered(bool hovered, bool hideByDirection = false)
    {
        if (hovered == _hovered) return;
        _hovered = hovered;
        if (hovered)
            _hoverAction = _settings.IslandHover == IslandHover.ByDirection
                ? (hideByDirection ? IslandHover.Hide : IslandHover.Expand) : _settings.IslandHover;
        bool expand = hovered && _hoverAction == IslandHover.Expand;
        // Takes clicks only while expanded; otherwise whatever is underneath stays usable.
        _overlay.SetClickThrough(!expand);
        // "Hide": out of the way while the pointer is over it, so what's underneath can be seen too.
        if (_hoverAction == IslandHover.Hide)
        {
            foreach (var part in new UIElement[] { Pill, ShadowHost })
                Fade(part, hovered ? 0 : 1, hovered ? 0.16 : 0.28, delay: hovered ? 0 : 0.12);
            ApplyTheme(); // the frosted blur goes while it's hidden
        }
        if (expand != _expanded)
        {
            _expanded = expand;
            // Keep the lyric in place; the player has its own cover.
            Fade(ArtFrame, expand ? 0 : 1, expand ? 0.18 : 0.28);
            Fade(Bars, expand ? 0 : 1, expand ? 0.18 : 0.28);
            if (expand)
            {
                Expanded.Visibility = Visibility.Visible;
                UpdateProgress();
                _transport.Update();
            }
            else _progressTimer.Stop();
            // As on the Mac: the mini player fades in once the pill has mostly grown, and goes at once on collapse.
            Fade(Expanded, expand ? 1 : 0, expand ? 0.18 : 0.08, () => { if (!_expanded) Expanded.Visibility = Visibility.Collapsed; },
                delay: expand ? 0.12 : 0);
            Resize();
        }
    }
}
