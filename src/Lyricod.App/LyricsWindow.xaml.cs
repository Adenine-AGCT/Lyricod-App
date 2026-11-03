using System.ComponentModel;
using Lyricod.Core;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinUIEx;

namespace Lyricod;

/// <summary>
/// The whole song, like the macOS app's lyrics window: the line being sung sweeps word by word
/// (in rows that fit the width) and stays a third of the way down; the others shrink a little and
/// fade and blur with distance, and scrolling eases like a spring. The softened cover drifts behind.
/// Click a line to jump there. Wide windows and full screen put a large cover beside the lyrics.
/// </summary>
public sealed partial class LyricsWindow : Window
{
    readonly PlaybackModel _model;
    readonly Settings _settings;
    readonly Action _chooseLyrics;
    readonly TransportButtons _transport;
    readonly ProgressLine _progress;
    readonly DispatcherQueueTimer _progressTimer;
    readonly DispatcherQueueTimer _relayout;
    readonly ArtworkDrift _drift;
    /// <summary>Fires when the pointer has rested a few seconds (controls then fade, when wide).</summary>
    readonly DispatcherQueueTimer _idle;
    bool _pointerIdle, _closed;
    readonly List<LineView> _views = [];
    int? _current;
    bool _wide;
    double _fontSize = 30;
    /// <summary>Text widths in the current line's font, cleared when the font or size changes.</summary>
    readonly Dictionary<string, double> _widths = [];

    /// <summary>One lyric line on screen; while it's being sung, its rows and their sweeps.</summary>
    sealed class LineView(LyricLine line, StackPanel panel)
    {
        public LyricLine Line { get; } = line;
        public StackPanel Panel { get; } = panel;
        public List<(LyricLine Piece, double Width, SweepPainter Sweep)> Rows { get; } = [];
        /// <summary>The line's texts that soften with distance (none while it's being sung).</summary>
        public List<SoftText> Soft { get; } = [];
    }

    static readonly TimeSpan Transition = TimeSpan.FromSeconds(0.35);

    public LyricsWindow(PlaybackModel model, Settings settings, Action chooseLyrics)
    {
        InitializeComponent();
        _model = model;
        _settings = settings;
        _chooseLyrics = chooseLyrics;
        Title = L.Get("LyricsWindowTitle");
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lyricod.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        // Caption buttons readable over the dark artwork.
        var bar = AppWindow.TitleBar;
        bar.ButtonForegroundColor = Colors.White;
        bar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(160, 255, 255, 255);
        bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
        bar.ButtonHoverForegroundColor = Colors.White;
        this.CenterOnScreen(520, 760);
        AppWindow.Changed += (_, e) => { if (e.DidPresenterChange) UpdateFullScreenIcon(); };

        _drift = new ArtworkDrift(ArtHost);
        _transport = new TransportButtons(model);
        _progress = new ProgressLine(model);
        ToolTipService.SetToolTip(TranslationButton, L.Get("TranslationsTip"));
        ToolTipService.SetToolTip(PronunciationButton, L.Get("PronunciationTip"));
        ToolTipService.SetToolTip(FullScreenButton, L.Get("FullScreenTip"));
        ToolTipService.SetToolTip(ChooseButton, L.Get("ChooseLyricsTip"));

        _progressTimer = DispatcherQueue.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromSeconds(0.5);
        _progressTimer.Tick += (_, _) => { try { _progress.Update(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); } };
        // Rows depend on the width: rebuild the sung line once resizing settles.
        _relayout = DispatcherQueue.CreateTimer();
        _relayout.IsRepeating = false;
        _relayout.Interval = TimeSpan.FromMilliseconds(120);
        _relayout.Tick += (_, _) => { try { Relayout(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); } };

        Root.SizeChanged += (_, _) => _relayout.Start();
        _idle = DispatcherQueue.CreateTimer();
        _idle.IsRepeating = false;
        _idle.Interval = TimeSpan.FromSeconds(3);
        _idle.Tick += (_, _) => { try { _pointerIdle = true; UpdateControls(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); } };
        Root.PointerMoved += (_, _) =>
        {
            _idle.Stop();
            _idle.Start();
            if (_pointerIdle) { _pointerIdle = false; UpdateControls(); }
        };
        foreach (var element in new UIElement[] { TopButtons, BigProgressHost, BigTransportHost })
            element.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromSeconds(0.4) };
        Root.KeyDown += OnKeyDown;
        _model.Changed += OnModelChanged;
        Fonts.Changed += OnFontChanged;
        _settings.PropertyChanged += OnSettingChanged;
        Activated += (_, e) => UpdateTimer();
        Closed += (_, _) =>
        {
            _closed = true;
            _model.Changed -= OnModelChanged;
            Fonts.Changed -= OnFontChanged;
            _settings.PropertyChanged -= OnSettingChanged;
            _progressTimer.Stop();
            _relayout.Stop();
            _idle.Stop();
        };

        SyncButtons();
        ApplyLayout(force: true);
        _ = ApplyArtwork();
        UpdateSong();
        BuildLines();
    }

    // ---- Model and settings ----

    void OnFontChanged()
    {
        try
        {
            BuildLines();
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("font refresh: " + e); }
    }

    void OnModelChanged(PlaybackModel.Change change)
    {
        if (change.HasFlag(PlaybackModel.Change.Track) || change.HasFlag(PlaybackModel.Change.Lyrics)) UpdateSong();
        if (change.HasFlag(PlaybackModel.Change.Artwork)) _ = ApplyArtwork();
        if (change.HasFlag(PlaybackModel.Change.Lyrics) || change.HasFlag(PlaybackModel.Change.Track)) BuildLines();
        else if (change.HasFlag(PlaybackModel.Change.Line)) MoveToCurrent(animate: true);
        if (change.HasFlag(PlaybackModel.Change.Clock))
        {
            AnimateCurrent();
            _drift.Moving = _model.IsPlaying && _settings.LyricsBackground == LyricsBackground.Artwork;
            _transport.Update();
            _progress.Update();
            UpdateTimer();
            UpdateControls();
        }
        else if (change.HasFlag(PlaybackModel.Change.Progress))
        {
            // Position-independent metadata can arrive while paused; it must not re-plan lyric sweeps.
            _progress.Update();
            UpdateTimer();
        }
    }

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Settings.LyricsEffect):
                if (_current is int i && i < _views.Count) RenderLine(_views[i], current: true);
                AnimateCurrent();
                break;
            case nameof(Settings.LyricsAutoHide):
                UpdateControls();
                break;
            case nameof(Settings.LyricsBlurDistant):
                foreach (var view in _views) ApplyDistance(view);
                break;
            case nameof(Settings.ShowTranslations) or nameof(Settings.ShowPronunciation) or nameof(Settings.LyricsCentered):
                SyncButtons();
                BuildLines();
                break;
            case nameof(Settings.LyricsTextSize):
                ApplyLayout(force: true);
                break;
            case nameof(Settings.LyricsBackground):
                _ = ApplyArtwork();
                break;
        }
    }

    void UpdateTimer()
    {
        // Closing deactivates the window, which comes here too.
        if (!_closed && _model.IsPlaying && _model.Duration > 0) _progressTimer.Start(); else _progressTimer.Stop();
    }

    void UpdateSong()
    {
        var t = _model.Track;
        SmallTitle.Text = BigTitle.Text = t?.Title ?? "";
        // Just the artist (the source is in Choose Lyrics), with a pin when the lyrics were chosen by hand.
        string artist = t?.Artist ?? "";
        SmallArtist.Text = BigArtist.Text = _model.LyricsPinned ? artist + " \U0001F4CC" : artist;
        ChooseButton.IsEnabled = t != null;
        ControlBar.Visibility = t != null && !_wide ? Visibility.Visible : Visibility.Collapsed;
        _transport.Update();
        _progress.Update();
        UpdateTimer();
    }

    async Task ApplyArtwork()
    {
        var bytes = _model.Artwork;
        var cover = await Images.FromBytes(bytes, 480);
        var small = await Images.FromBytes(bytes, 96);
        if (!ReferenceEquals(bytes, _model.Artwork)) return; // the song moved on
        BigCover.Source = cover;
        SmallCover.Source = small;
        var style = _settings.LyricsBackground;
        _drift.Moving = _model.IsPlaying && style == LyricsBackground.Artwork;
        await _drift.SetCover(style is LyricsBackground.Artwork or LyricsBackground.StillArtwork ? bytes : null);
        if (style == LyricsBackground.Colour && _model.ArtworkColor is { } c)
        {
            BackdropColour.Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = Shade(c, 0.45), Offset = 0 },
                    new GradientStop { Color = Shade(c, 0.22), Offset = 1 },
                },
            };
            BackdropColour.Visibility = Visibility.Visible;
        }
        else BackdropColour.Visibility = Visibility.Collapsed;
    }

    static Windows.UI.Color Shade(Windows.UI.Color c, double k) =>
        Windows.UI.Color.FromArgb(255, (byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
}
