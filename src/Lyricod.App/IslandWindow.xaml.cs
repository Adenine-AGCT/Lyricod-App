using System.ComponentModel;
using System.Numerics;
using Lyricod.Core;
using Lyricod.Platform;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Lyricod;

/// <summary>
/// Lyricod Island, like the macOS app's: a black pill at the top centre of the screen with the
/// cover, the line being sung (sweeping in the album's colour) and six bars that move to the
/// player's sound while music plays.
/// The pill fits the current piece and its second row. While
/// the pointer is over it, it takes clicks and (by default) grows into a mini player; otherwise
/// it's click-through. The floating style (IslandWindow.Floating.cs) is rounded all round and sits
/// wherever it was dragged.
/// </summary>
public sealed partial class IslandWindow : Window
{
    double LayoutScale => _settings.IslandSize switch
    {
        IslandSize.Small => 0.85,
        IslandSize.Large => 1.2,
        IslandSize.ExtraLarge => 1.4,
        _ => 1,
    };
    // The cover sits as far from the side as from the bottom, and its corner is the island's corner
    // minus that gap, so the two corners share a centre (as on the Mac: 12 and 6).
    const double CoverInset = 6, CoverSize = 22, CoverRadius = 6, PillRadius = CoverInset + CoverRadius;
    double RowHeight => (CoverSize + 2 * CoverInset) * LayoutScale;
    double SecondHeight => 18 * LayoutScale;
    double ExpandedHeight => 84 * LayoutScale;
    double SideWidth => (CoverInset + CoverSize + 10) * LayoutScale; // inset, cover (or bars) and gap on each side
    double MinText => 80 * LayoutScale; // a short line still gets a pill, not a stub
    double MaxText => Math.Min(_settings.IslandMaxTextWidth * LayoutScale,
        Math.Max(MinText, _screenWidth - 2 * SideWidth - 20 * LayoutScale));
    double ExpandedMinWidth => 460 * LayoutScale;
    double BarHeight => 14 * LayoutScale;
    double WindowWidth => Math.Max(2 * SideWidth + MaxText, ExpandedMinWidth) + 20 * LayoutScale;
    double WindowHeight => RowHeight + SecondHeight + ExpandedHeight + 12 * LayoutScale;
    double _screenWidth = double.PositiveInfinity;

    readonly PlaybackModel _model;
    readonly Settings _settings;
    readonly Overlay _overlay;
    readonly SweepPainter _sweep;
    readonly TransportButtons _transport;
    readonly ProgressLine _progress;
    readonly DispatcherQueueTimer _pieceTimer, _pointerTimer, _progressTimer, _barTimer, _revealTimer, _pauseTimer;
    /// <summary>Hidden because the music stopped (Settings › Lyricod Island › Hide when paused).</summary>
    bool _pausedAway;
    readonly List<Rectangle> _bars = [];
    readonly List<Visual> _barVisuals = [];
    readonly AudioMeter _meter = new();
    readonly OverlayContentGate _content = new();
    readonly Dictionary<(string Text, string Font, double Size, ushort Weight), double> _widths = [];

    List<LyricLine> _pieces = [];
    LyricLine? _piece;
    double _pieceWidth;
    double _textWidth = 80, _secondWidth, _secondOpacity = 0.6;
    double _widthTarget = double.NaN, _heightTarget = double.NaN;
    Storyboard? _widthAnimation, _heightAnimation, _lineAnimation;
    bool _waitingForWidth, _widthAnimating, _transitionPending;
    bool _shown, _hovered, _expanded, _barsRunning, _closed;
    IslandHover _hoverAction;
    /// <summary>Where the pointer has been outside the island lately (screen pixels, steady-clock seconds).</summary>
    readonly List<(double X, double Y, double T)> _trail = [];
    int _barRate;
    /// <summary>The player the meter listens to, or null when it's off.</summary>
    string? _meteredApp;
    /// <summary>A lyric line has shown for this song (the next ones get the transition).</summary>
    bool _lineShown;
    double _lastInside;

    public IslandWindow(PlaybackModel model, Settings settings, Action openLyrics, Action openSettings)
    {
        InitializeComponent();
        _model = model;
        _settings = settings;
        _overlay = new Overlay(this);
        _overlay.SetClickThrough(true);
        _sweep = new SweepPainter(Lyric, LyricSweep) { Effect = settings.IslandEffect };
        _transport = new TransportButtons(model, 13);
        TransportHost.Children.Add(_transport);
        _progress = new ProgressLine(model, hoverPreview: true);
        ProgressHost.Children.Add(_progress);
        LyricsButton.Click += (_, _) => openLyrics();
        SettingsButton.Click += (_, _) => openSettings();
        ToolTipService.SetToolTip(LyricsButton, L.Get("LyricsWindowTitle"));
        ToolTipService.SetToolTip(SettingsButton, L.Get("SettingsTitle"));
        // One bar per band of the meter, sized as on the Mac (2 wide, 1.5 apart, 3 to 14 tall).
        for (int i = 0; i < AudioMeter.BandCount; i++)
        {
            var bar = new Rectangle { Width = 2, Height = BarHeight, RadiusX = 1, RadiusY = 1, Fill = new SolidColorBrush(Colors.White) };
            _bars.Add(bar);
            Bars.Children.Add(bar);
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.CenterPoint = new Vector3(1, (float)BarHeight / 2, 0);
            visual.Scale = new Vector3(1, 3 / 14f, 1);
            _barVisuals.Add(visual);
        }

        _pieceTimer = DispatcherQueue.CreateTimer();
        _pieceTimer.IsRepeating = false;
        _pieceTimer.Tick += (_, _) => { try { Refresh(OverlayContentChanges.Clock); } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); } };
        // The pointer is checked often only near the island; a few times a second otherwise.
        _pointerTimer = DispatcherQueue.CreateTimer();
        _pointerTimer.Interval = TimeSpan.FromMilliseconds(250);
        _pointerTimer.Tick += (_, _) => { try { CheckPointer(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); } };
        _progressTimer = DispatcherQueue.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromSeconds(0.5);
        _progressTimer.Tick += (_, _) => { try { if (!_closed && _expanded) _progress.Update(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); } };
        _barTimer = DispatcherQueue.CreateTimer();
        _barTimer.Tick += (_, _) => { try { DrawBars(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); } };
        // Text hidden while the pill widens shows again by this at the latest.
        _revealTimer = DispatcherQueue.CreateTimer();
        _revealTimer.IsRepeating = false;
        _revealTimer.Interval = TimeSpan.FromSeconds(0.35);
        _revealTimer.Tick += (_, _) =>
        {
            try { if (_waitingForWidth && !_closed) { _waitingForWidth = false; RevealText(); } }
            catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
        };

        // Hide when paused: only once the music has stayed stopped a moment, so the gap players
        // report between songs doesn't make the island blink.
        _pauseTimer = DispatcherQueue.CreateTimer();
        _pauseTimer.IsRepeating = false;
        _pauseTimer.Interval = TimeSpan.FromSeconds(2.5);
        _pauseTimer.Tick += (_, _) =>
        {
            try
            {
                if (_closed || !PausedLongEnough) return;
                if (_hovered) { _pauseTimer.Start(); return; } // not from under the pointer
                _pausedAway = true;
                Refresh(OverlayContentChanges.None);
            }
            catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
        };

        _model.Changed += OnModelChanged;
        Fonts.Changed += OnFontChanged;
        _settings.PropertyChanged += OnSettingChanged;
        // Dragged while floating and unlocked.
        Pill.PointerPressed += OnPillPressed;
        // Lyricod Glass's shadow and frosted shape keep the pill's size as it grows and shrinks.
        Pill.SizeChanged += (_, _) =>
        {
            try
            {
                UpdateShadow();
                if (_shaped) Shape();
                UpdateLiquid();
            }
            catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
        };
        // Nothing may touch the window once it's gone (the pointer check reads its position).
        Closed += (_, _) =>
        {
            _closed = true;
            _model.Changed -= OnModelChanged;
            Fonts.Changed -= OnFontChanged;
            _settings.PropertyChanged -= OnSettingChanged;
            _pieceTimer.Stop();
            _pointerTimer.Stop();
            _progressTimer.Stop();
            _barTimer.Stop();
            _revealTimer.Stop();
            _pauseTimer.Stop();
            _widthAnimation?.Stop();
            _heightAnimation?.Stop();
            _lineAnimation?.Stop();
            _meter.Dispose();
            StopLiquid();
        };
        ApplyMoveMode();
        ApplyLayout();
        Place();
        UpdatePaused();
        Refresh();
    }

    // ---- Model ----

    void OnFontChanged()
    {
        if (_closed) return;
        try
        {
            _widths.Clear();
            Refresh();
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("font refresh: " + e); }
    }

    void OnModelChanged(PlaybackModel.Change change)
    {
        if (_closed) return;
        bool track = change.HasFlag(PlaybackModel.Change.Track);
        UpdatePaused();
        var updates = OverlayContentChanges.None;
        if (track) updates |= OverlayContentChanges.Song | OverlayContentChanges.Lyrics;
        if (change.HasFlag(PlaybackModel.Change.Lyrics) || change.HasFlag(PlaybackModel.Change.Line)) updates |= OverlayContentChanges.Lyrics;
        if (change.HasFlag(PlaybackModel.Change.Artwork)) updates |= OverlayContentChanges.Artwork;
        if (change.HasFlag(PlaybackModel.Change.Clock)) updates |= OverlayContentChanges.Clock;
        if (change.HasFlag(PlaybackModel.Change.Progress)) updates |= OverlayContentChanges.Progress;
        Refresh(updates);
    }

    /// <summary>Hidden updates collapse into one current snapshot when the island is shown.</summary>
    void Refresh(OverlayContentChanges changes = OverlayContentChanges.Lyrics)
    {
        if (_closed) return;
        UpdateVisibility();
        if (_closed) return;
        var update = _content.Take(_shown, changes);
        if (update == OverlayContentChanges.None) return;
        if (update.HasFlag(OverlayContentChanges.Song)) UpdateSong();
        if (update.HasFlag(OverlayContentChanges.Artwork)) _ = ApplyArtwork();
        if (update.HasFlag(OverlayContentChanges.Lyrics)) Render();
        else if (update.HasFlag(OverlayContentChanges.Clock)) ShowPiece();
        if (update.HasFlag(OverlayContentChanges.Song) || update.HasFlag(OverlayContentChanges.Clock))
        {
            _transport.Update();
            UpdateBars();
            UpdateProgress();
        }
        else if (update.HasFlag(OverlayContentChanges.Progress)) UpdateProgress();
    }

    void UpdateProgress()
    {
        if (_closed || !_expanded) { _progressTimer.Stop(); return; }
        _progress.Update();
        if (_model.IsPlaying && double.IsFinite(_model.Duration) && _model.Duration > 0) _progressTimer.Start();
        else _progressTimer.Stop();
    }

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closed) return;
        try
        {
            switch (e.PropertyName)
            {
                case nameof(Settings.ShowIsland):
                    Refresh(OverlayContentChanges.None);
                    break;
                case nameof(Settings.IslandAlbumColour) or nameof(Settings.IslandColor):
                    ApplyColour();
                    break;
                case nameof(Settings.IslandSecondLine):
                    Refresh();
                    break;
                case nameof(Settings.IslandHideWhenPaused):
                    UpdatePaused();
                    Refresh(OverlayContentChanges.None);
                    break;
                case nameof(Settings.IslandHover):
                    SetHovered(false);
                    Pill.Opacity = ShadowHost.Opacity = 1;
                    break;
                case nameof(Settings.IslandStyle):
                    ApplyMoveMode();
                    ApplyCorners();
                    Place();
                    break;
                case nameof(Settings.IslandLocked):
                    ApplyMoveMode();
                    break;
                case nameof(Settings.IslandTheme) or nameof(Settings.GlassTransparency) or nameof(Settings.GlassTint) or nameof(Settings.GlassShine)
                    or nameof(Settings.GlassEffect) or nameof(Settings.LiquidBlur) or nameof(Settings.LiquidRefraction):
                    ApplyTheme();
                    break;
                case nameof(Settings.IslandY) when _settings.IslandX == null && _settings.IslandY == null:
                    // "Reset to top centre" in Settings.
                    Place();
                    break;
                case nameof(Settings.IslandSize) or nameof(Settings.IslandMaxTextWidth):
                    ApplyLayout();
                    Place();
                    Refresh(OverlayContentChanges.Lyrics | OverlayContentChanges.Artwork);
                    break;
                case nameof(Settings.IslandWeight):
                    _widths.Clear();
                    Refresh();
                    break;
                case nameof(Settings.IslandEffect):
                    _sweep.Effect = _settings.IslandEffect;
                    Refresh(OverlayContentChanges.Clock);
                    break;
                case nameof(Settings.IslandAudioBars) or nameof(Settings.FrameRate):
                    UpdateBars();
                    break;
            }
        }
        catch (Exception) { StopLiquid(); }
    }

    void UpdateSong()
    {
        _lineShown = false;
        SongTitle.Text = _model.Track?.Title ?? "";
        SongArtist.Text = _model.Track?.Artist ?? "";
    }

    async Task ApplyArtwork()
    {
        var bytes = _model.Artwork;
        double scale = LayoutScale;
        var small = await Images.FromBytes(bytes, (int)Math.Ceiling(44 * scale));
        if (_closed || !ReferenceEquals(bytes, _model.Artwork) || scale != LayoutScale) return;
        var big = await Images.FromBytes(bytes, (int)Math.Ceiling(68 * scale));
        if (_closed || !ReferenceEquals(bytes, _model.Artwork) || scale != LayoutScale) return;
        Art.Source = small;
        BigArt.Source = big;
        ApplyColour();
    }

    /// <summary>The album's colour, or the island's chosen colour.</summary>
    Windows.UI.Color LyricColour =>
        _settings.IslandAlbumColour && _model.ArtworkColor is { } c ? c : DesktopLyricsWindow.ParseColor(_settings.IslandColor);

    void ApplyColour()
    {
        var colour = LyricColour;
        _sweep.Color = colour;
        var brush = new SolidColorBrush(colour);
        foreach (var bar in _bars) bar.Fill = brush;
        _progress.BarBrush = brush;
        ApplyTheme(); // glass tinted with the album's colour follows the cover
    }
}
