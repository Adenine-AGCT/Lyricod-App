using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace Lyricod;

public sealed partial class IslandWindow
{
    /// <summary>Top centre of the main display, or (floating) where it was dragged.</summary>
    void Place()
    {
        double k = _overlay.Scale;
        if (Floating)
        {
            PlaceFloating(k);
            UpdateShadow(); // follows the pill's new alignment
            if (_shaped) Shape();
            UpdateLiquid();
            return;
        }
        Pill.HorizontalAlignment = HorizontalAlignment.Center;
        Pill.VerticalAlignment = VerticalAlignment.Top;
        var area = DisplayArea.Primary.OuterBounds;
        _screenWidth = area.Width / k;
        // Keep room for an old, larger layout until its shrink animation finishes.
        double width = Math.Max(WindowWidth, Pill.Width + 20 * LayoutScale);
        double height = Math.Max(WindowHeight, Pill.Height + 12 * LayoutScale);
        var size = new SizeInt32((int)Math.Ceiling(width * k), (int)Math.Ceiling(height * k));
        int x = area.X + (area.Width - size.Width) / 2;
        if (AppWindow.Size.Width != size.Width || AppWindow.Size.Height != size.Height || AppWindow.Position.X != x || AppWindow.Position.Y != area.Y)
            AppWindow.MoveAndResize(new RectInt32(x, area.Y, size.Width, size.Height));
        if (_shaped) Shape();
        UpdateLiquid();
    }

    bool PausedLongEnough => _settings.IslandHideWhenPaused && _model.Track != null && !_model.IsPlaying;

    /// <summary>Starts the wait before hiding when the music stops; brings the island back when it plays.</summary>
    void UpdatePaused()
    {
        if (PausedLongEnough)
        {
            if (!_pausedAway && !_pauseTimer.IsRunning) _pauseTimer.Start();
            return;
        }
        _pauseTimer.Stop();
        if (!_pausedAway) return;
        _pausedAway = false;
        UpdateVisibility();
    }

    void UpdateVisibility()
    {
        bool visible = _settings.ShowIsland && _model.IsSong && !_pausedAway;
        if (visible == _shown) return;
        _shown = visible;
        ApplyTheme(); // frosted blur only while it shows: it can't fade with the island
        if (visible)
        {
            Place();
            AppWindow.Show(false);
            _overlay.StripFrame();
            _pointerTimer.Start();
            AnimateShown(true);
        }
        else
        {
            _pointerTimer.Stop();
            _progressTimer.Stop();
            _progress.SetInteractionEnabled(false);
            SetHovered(false);
            _trail.Clear();
            AnimateShown(false, () => { if (!_shown && !_closed) AppWindow.Hide(); });
        }
        UpdateBars();
    }

    /// <summary>Comes and goes like the Mac's: a fade with a slight shrink toward the top edge
    /// (floating: toward its middle).</summary>
    void AnimateShown(bool show, Action? done = null)
    {
        double seconds = show ? 0.3 : 0.35;
        if (Pill.RenderTransform is not ScaleTransform scale) Pill.RenderTransform = scale = new ScaleTransform();
        scale.CenterX = Pill.ActualWidth > 0 ? Pill.ActualWidth / 2 : Pill.Width / 2;
        scale.CenterY = Floating ? (Pill.ActualHeight > 0 ? Pill.ActualHeight : Pill.Height) / 2 : 0;
        if (show && Root.Opacity < 0.05) scale.ScaleX = scale.ScaleY = 0.9;
        var story = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var grow = new DoubleAnimation
            {
                To = show ? 1 : 0.9, Duration = TimeSpan.FromSeconds(seconds),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(grow, scale);
            Storyboard.SetTargetProperty(grow, property);
            story.Children.Add(grow);
        }
        story.Begin();
        Fade(Root, show ? 1 : 0, seconds, done);
    }

    static void Fade(UIElement element, double to, double seconds, Action? done = null, double delay = 0)
    {
        var animation = new DoubleAnimation
        {
            To = to, Duration = TimeSpan.FromSeconds(seconds), BeginTime = TimeSpan.FromSeconds(delay),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var story = new Storyboard { Children = { animation } };
        if (done != null) story.Completed += (_, _) => { try { done(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); } };
        story.Begin();
    }
}
