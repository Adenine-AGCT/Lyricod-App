using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Lyricod;

public sealed partial class IslandWindow
{
    /// <summary>Whether the current line has something under it (its translation, pronunciation or
    /// the next line, as chosen); the second row and its height come and go with it.</summary>
    bool HasSecondRow { get; set; }

    void ApplyLayout()
    {
        double s = LayoutScale;
        LyricRow.Height = new GridLength(RowHeight);
        SecondRow.Height = new GridLength(HasSecondRow ? SecondHeight : 0);
        MainRow.Padding = new Thickness(CoverInset * s, 0, CoverInset * s, 0);
        MainRow.ColumnSpacing = 10 * s;
        CoverColumn.Width = BarsColumn.Width = new GridLength(CoverSize * s);
        ArtFrame.Width = ArtFrame.Height = CoverSize * s;
        ArtFrame.CornerRadius = new CornerRadius(CoverRadius * s);
        Bars.Height = BarHeight;
        Bars.Spacing = 1.5 * s;
        for (int i = 0; i < _bars.Count; i++)
        {
            _bars[i].Width = 2 * s;
            _bars[i].Height = BarHeight;
            _bars[i].RadiusX = _bars[i].RadiusY = s;
            _barVisuals[i].CenterPoint = new Vector3((float)s, (float)BarHeight / 2, 0);
        }
        Second.FontSize = 11 * s;
        Second.Margin = new Thickness(SideWidth, -4 * s, SideWidth, 4 * s);
        Expanded.Padding = new Thickness(18 * s, 6 * s, 18 * s, 10 * s);
        Expanded.Spacing = 6 * s;
        PlayerRow.ColumnSpacing = 10 * s;
        BigArtFrame.Width = BigArtFrame.Height = 34 * s;
        BigArtFrame.CornerRadius = new CornerRadius(7 * s);
        SongLabels.Spacing = s;
        SongTitle.FontSize = 13 * s;
        SongArtist.FontSize = 12 * s;
        PlayerDivider.Width = s;
        PlayerDivider.Height = 18 * s;
        foreach (var button in new[] { LyricsButton, SettingsButton })
        {
            button.Padding = new Thickness(8 * s, 6 * s, 8 * s, 6 * s);
            button.CornerRadius = new CornerRadius(4 * s);
            if (button.Content is FontIcon icon) icon.FontSize = 13 * s;
        }
        _transport.SetLayoutScale(s, 13);
        _progress.SetLayoutScale(s);
        _widths.Clear();
    }

    /// <summary>Fits the displayed text, keeping room for the mini player while expanded.</summary>
    void Resize()
    {
        double required = _textWidth + 2 * SideWidth;
        double width = _expanded ? Math.Max(required, ExpandedMinWidth) : required;
        double height = RowHeight + (HasSecondRow ? SecondHeight : 0) + (_expanded ? ExpandedHeight : 0);
        if (width != _widthTarget)
        {
            double from = Pill.Width;
            _widthAnimation?.Stop();
            Pill.Width = from;
            _widthTarget = width;
            var story = Animate(Pill, "Width", width, from, width > from ? 0.18 : 0.5);
            _widthAnimation = story;
            _widthAnimating = true;
            story.Completed += (_, _) =>
            {
                try
                {
                    if (_closed || !ReferenceEquals(_widthAnimation, story)) return;
                    _widthAnimating = false;
                    _waitingForWidth = false;
                    RevealText();
                    Place();
                }
                catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
            };
            story.Begin();
        }
        // Don't expose new text inside a pill that's still too narrow: hide it only while the pill
        // is widening (an animation's end can land a hair short of its target, so allow half a
        // pixel), and never for long. Shrinking is safe: the incoming text already fits.
        _waitingForWidth = _widthAnimating && Pill.Width < required - 0.5;
        if (_waitingForWidth)
        {
            _lineAnimation?.Stop();
            LyricBox.Opacity = Second.Opacity = 0;
            _revealTimer.Stop();
            _revealTimer.Start();
        }
        if (height != _heightTarget)
        {
            double from = Pill.Height;
            _heightAnimation?.Stop();
            Pill.Height = from;
            _heightTarget = height;
            var story = Animate(Pill, "Height", height, from);
            _heightAnimation = story;
            story.Completed += (_, _) =>
            {
                try { if (!_closed && ReferenceEquals(_heightAnimation, story)) Place(); }
                catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
            };
            story.Begin();
        }
        ApplyCorners();
        if (!_waitingForWidth) RevealText();
    }

    /// <summary>
    /// The default island is square where it meets the top edge; a floating one has the same corner
    /// all round. Either way the cover's corners share their centre with the island's.
    /// </summary>
    void ApplyCorners()
    {
        double radius = (_expanded ? 20 : PillRadius) * LayoutScale;
        Pill.CornerRadius = Floating ? new CornerRadius(radius) : new CornerRadius(0, 0, radius, radius);
        UpdateShadow();
        if (_shaped) Shape();
        UpdateLiquid();
    }

    /// <summary>A fast start and a soft settle, without overshooting.</summary>
    static Storyboard Animate(FrameworkElement element, string property, double to, double from, double seconds = 0.5)
    {
        var animation = new DoubleAnimation
        {
            From = from, To = to, Duration = TimeSpan.FromSeconds(seconds), EnableDependentAnimation = true,
            EasingFunction = new ExponentialEase { Exponent = 6, EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, property);
        return new Storyboard { Children = { animation } };
    }

    void FitText()
    {
        _textWidth = Math.Clamp(Math.Ceiling(Math.Max(_pieceWidth, _secondWidth)), MinText, MaxText);
        Second.MaxWidth = _textWidth;
        Resize();
    }

    void RevealText()
    {
        LyricBox.Opacity = 1;
        Second.Opacity = _secondOpacity;
        if (_transitionPending)
        {
            _transitionPending = false;
            MoveIn();
        }
    }

    double Measure(string text, FontFamily font, double size, Windows.UI.Text.FontWeight weight)
    {
        var key = (text, font.Source, size, weight.Weight);
        if (_widths.TryGetValue(key, out var w)) return w;
        Measurer.FontFamily = font;
        Measurer.FontSize = size;
        Measurer.FontWeight = weight;
        Measurer.Text = text;
        Measurer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        w = Measurer.DesiredSize.Width;
        if (_widths.Count > 2000) _widths.Clear();
        return _widths[key] = w;
    }

    double MeasureLyric(string text) => Measure(text, Lyric.FontFamily, Lyric.FontSize, Lyric.FontWeight);
}
