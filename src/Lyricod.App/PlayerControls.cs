using System.Globalization;
using Lyricod.Core;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Lyricod;

/// <summary>Previous, play/pause and next for the player Lyricod follows.</summary>
sealed partial class TransportButtons : StackPanel
{
    readonly PlaybackModel _model;
    readonly FontIcon _playIcon;

    public TransportButtons(PlaybackModel model, double size = 16)
    {
        _model = model;
        Orientation = Orientation.Horizontal;
        Spacing = 4;
        Children.Add(Button("\uE892", size * 0.8, L.Get("Previous"), () => _ = _model.Previous())); // Previous
        _playIcon = new FontIcon { Glyph = "\uE768", FontSize = size };
        var play = Button(_playIcon, L.Get("PlayPause"), () => _ = _model.PlayPause());
        Children.Add(play);
        Children.Add(Button("\uE893", size * 0.8, L.Get("Next"), () => _ = _model.Next())); // Next
        Update();
    }

    /// <summary>Follows play/pause.</summary>
    public void Update() => _playIcon.Glyph = _model.IsPlaying ? "\uE769" : "\uE768"; // Pause / Play

    /// <summary>Sizes the island's controls through layout, keeping the icons crisp.</summary>
    public void SetLayoutScale(double scale, double size)
    {
        Spacing = 4 * scale;
        foreach (var button in Children.OfType<Button>())
        {
            button.Padding = new Thickness(10 * scale, 8 * scale, 10 * scale, 8 * scale);
            button.CornerRadius = new CornerRadius(8 * scale);
            if (button.Content is FontIcon icon) icon.FontSize = size * scale * (icon == _playIcon ? 1 : 0.8);
        }
    }

    static Button Button(string glyph, double size, string tip, Action action) =>
        Button(new FontIcon { Glyph = glyph, FontSize = size }, tip, action);

    static Button Button(FontIcon icon, string tip, Action action)
    {
        var button = new Button
        {
            Content = icon, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(8),
        };
        button.Click += (_, _) => action();
        ToolTipService.SetToolTip(button, tip);
        return button;
    }
}

/// <summary>
/// Elapsed time, a thin bar, and the time left. Its owner calls <see cref="Update"/> on a slow timer
/// (twice a second, only while it's shown) and when the clock jumps.
/// </summary>
sealed partial class ProgressLine : Grid
{
    readonly PlaybackModel _model;
    readonly ProgressBar _bar;
    readonly TextBlock _elapsed, _left;
    readonly ProgressInteraction _interaction;
    readonly DispatcherQueueTimer? _hoverTimer;
    bool? _seekable;
    static readonly InputCursor Hand = InputSystemCursor.Create(InputSystemCursorShape.Hand);

    public ProgressLine(PlaybackModel model, bool hoverPreview = false)
    {
        _model = model;
        _interaction = new ProgressInteraction(hoverPreview);
        if (hoverPreview)
        {
            _hoverTimer = DispatcherQueue.CreateTimer();
            _hoverTimer.IsRepeating = false;
            _hoverTimer.Tick += (_, _) =>
            {
                try
                {
                    _interaction.ActivatePreview(PlaybackModel.Now);
                    Update();
                    SchedulePreview();
                }
                catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
            };
        }
        // As on the Mac: press or drag on the bar to pick a point, let go to move the player there.
        Background = new SolidColorBrush(Colors.Transparent); // so the whole strip takes the pointer
        PointerPressed += OnPointerPressed;
        PointerEntered += OnPointerMoved;
        PointerMoved += OnPointerMoved;
        PointerExited += (_, _) => CancelPreview();
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => { if (_interaction.IsScrubbing) { _interaction.Cancel(); Update(); } };
        RowSpacing = 4;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 4, CornerRadius = new CornerRadius(2) };
        Children.Add(_bar);
        _elapsed = new TextBlock { FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Left };
        _left = new TextBlock { FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Right };
        SetRow(_elapsed, 1);
        SetRow(_left, 1);
        Children.Add(_elapsed);
        Children.Add(_left);
        Update();
    }

    public Brush BarBrush { set => _bar.Foreground = value; }

    public void SetLayoutScale(double scale)
    {
        RowSpacing = 4 * scale;
        _bar.Height = 4 * scale;
        _bar.CornerRadius = new CornerRadius(2 * scale);
        _elapsed.FontSize = _left.FontSize = 11 * scale;
    }

    bool HasDuration => double.IsFinite(_model.Duration) && _model.Duration > 0;
    bool CanSeek => _model.CanSeek && HasDuration;

    void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (_interaction.IsScrubbing) return;
            CancelPreview();
            if (!CanSeek) return;
            var p = e.GetCurrentPoint(_bar).Position;
            if (p.Y < -10 || p.Y > _bar.ActualHeight + 10) return; // the bar, not the times under it
            if (_bar.ActualWidth <= 0 || !CapturePointer(e.Pointer)) return;
            _interaction.BeginScrub(p.X / Math.Max(_bar.ActualWidth, 1), CanSeek);
            Update();
            e.Handled = true;
        }
        catch (Exception) { /* a pointer that went away */ }
    }

    void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            var point = e.GetCurrentPoint(_bar);
            var p = point.Position;
            if (_interaction.IsScrubbing)
            {
                _interaction.MoveScrub(p.X / Math.Max(_bar.ActualWidth, 1));
                Update();
                e.Handled = true;
                return;
            }
            if (_hoverTimer == null) return;
            // Do not preview over the time labels or while the pointer is pressed.
            bool inside = !point.IsInContact && p.X >= 0 && p.X <= _bar.ActualWidth
                && p.Y >= -10 && p.Y < _bar.ActualHeight + RowSpacing;
            bool wasPreviewing = _interaction.IsPreviewing;
            double? due = _interaction.HoverDueAt;
            _interaction.Hover(p.X, p.Y, _bar.ActualWidth, inside, PlaybackModel.Now);
            if (_interaction.HoverDueAt != due) SchedulePreview();
            if (wasPreviewing || _interaction.IsPreviewing) Update();
        }
        catch (Exception) { }
    }

    void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (!_interaction.IsScrubbing) return;
            var point = e.GetCurrentPoint(_bar);
            _interaction.MoveScrub(point.Position.X / Math.Max(_bar.ActualWidth, 1));
            double? target = _interaction.ReleaseScrub(_model.Duration, CanSeek);
            ReleasePointerCapture(e.Pointer);
            if (target is double seconds) _model.SeekPlayer(seconds);
            Update();
            e.Handled = true;
        }
        catch (Exception) { }
    }

    void SchedulePreview()
    {
        if (_hoverTimer == null) return;
        _hoverTimer.Stop();
        if (_interaction.HoverDueAt is not double due) return;
        _hoverTimer.Interval = TimeSpan.FromSeconds(Math.Max(due - PlaybackModel.Now, 0.001));
        _hoverTimer.Start();
    }

    void CancelPreview()
    {
        _hoverTimer?.Stop();
        _interaction.CancelPreview();
        Update();
    }

    public void Update()
    {
        Visibility = HasDuration ? Visibility.Visible : Visibility.Collapsed;
        bool seekable = CanSeek;
        if (seekable != _seekable)
        {
            _seekable = seekable;
            ProtectedCursor = seekable ? Hand : null;
            ToolTipService.SetToolTip(this, seekable ? null : L.Get("CantSeek"));
        }
        if (!HasDuration) return;
        var display = _interaction.Display(_model.PlayerPosition, _model.Duration);
        _bar.Value = display.Fraction;
        _elapsed.Text = Time(display.Elapsed);
        _elapsed.Opacity = display.IsPreviewing ? 0.85 : 0.6;
        _left.Text = "-" + Time(display.Remaining);
    }

    static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
