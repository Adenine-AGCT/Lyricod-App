using System.ComponentModel;
using Lyricod.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using WinUIEx;

namespace Lyricod;

public enum SettingsPane { General, Desktop, Island, LyricsWindow, Lyrics, Shortcuts, About }

/// <summary>
/// Lyricod's settings, like the macOS app's: General, Desktop, Lyricod Island, Lyrics Window,
/// Lyrics, Shortcuts and About. Each control follows its setting while the page is up, so a change from the
/// tray menu or a shortcut shows here too.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    readonly UpdateChecker _updates;
    readonly Settings _settings;
    readonly PlaybackModel _model;
    readonly Action _openLyricsWindow;
    readonly Action _openChooseLyrics;
    readonly GlobalShortcuts _shortcuts;
    readonly HashSet<VirtualKey> _recordedKeys = [];
    ShortcutAction? _recording;
    string? _recordingProblem;
    /// <summary>Refreshes for the controls on the current page, by setting name.</summary>
    readonly Dictionary<string, List<Action>> _watchers = [];
    /// <summary>Refreshes for what follows the song (now playing, this song's timing, lyrics status).</summary>
    readonly List<Action> _songWatchers = [];

    /// <summary>Starts Lyricod again (a new language applies then).</summary>
    public Action? Restart { get; set; }

    /// <summary>Lets screenshots see the liquid glass island for a few seconds.</summary>
    public Action? ShowIslandInScreenshots { get; set; }

    static readonly string[] PresetColours = ["#8CD9FF", "#FFD36E", "#FF8FB1", "#9BF59B", "#FFFFFF"];

    public SettingsWindow(Settings settings, PlaybackModel model, Action openLyricsWindow, Action openChooseLyrics, GlobalShortcuts shortcuts, UpdateChecker updates, SettingsPane pane = SettingsPane.General)
    {
        InitializeComponent();
        _updates = updates;
        _updates.Changed += Refresh;
        _settings = settings;
        _model = model;
        _openLyricsWindow = openLyricsWindow;
        _openChooseLyrics = openChooseLyrics;
        _shortcuts = shortcuts;
        _shortcuts.Changed += RefreshShortcuts;
        Root.PreviewKeyDown += RecordKey;
        Root.PreviewKeyUp += (_, e) =>
        {
            // A recorded Space or Enter must not click the recorder again on release.
            if (_recordedKeys.Remove(e.OriginalKey)) e.Handled = true;
        };
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                _recordedKeys.Clear();
                StopRecording();
            }
        };
        Title = TitleText.Text = L.Get("SettingsTitle");
        TitleIcon.Source = new BitmapImage(new Uri("ms-appx:///Assets/Lyricod-256.png")) { DecodePixelWidth = 32 };
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lyricod.ico"));
        this.CenterOnScreen(1000, 720);

        foreach (var p in Enum.GetValues<SettingsPane>())
            Nav.MenuItems.Add(new NavigationViewItem { Content = PaneTitle(p), Tag = p, Icon = new FontIcon { Glyph = PaneGlyph(p) } });
        _settings.PropertyChanged += OnSettingChanged;
        _model.Changed += OnModelChanged;
        Fonts.Changed += OnFontsChanged;
        Closed += (_, _) =>
        {
            try
            {
                DetachNeteasePage();
                _fontClosed = true;
                try { _fontDownload?.Cancel(); } catch (ObjectDisposedException) { }
                Fonts.Changed -= OnFontsChanged;
                StopRecording();
                _updates.Changed -= Refresh;
                _shortcuts.Changed -= RefreshShortcuts;
                _settings.PropertyChanged -= OnSettingChanged;
                _model.Changed -= OnModelChanged;
            }
            catch (Exception) { /* the window is already closing */ }
        };
        Show(pane);
    }

    public void Show(SettingsPane pane) =>
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (SettingsPane)i.Tag == pane);

    static string PaneTitle(SettingsPane p) => p switch
    {
        SettingsPane.General => L.Get("PaneGeneral"),
        SettingsPane.Desktop => L.Get("PaneDesktop"),
        SettingsPane.Island => L.Get("PaneIsland"),
        SettingsPane.LyricsWindow => L.Get("PaneLyricsWindow"),
        SettingsPane.Lyrics => L.Get("PaneLyrics"),
        SettingsPane.Shortcuts => L.Get("PaneShortcuts"),
        _ => L.Get("PaneAbout"),
    };

    // Segoe Fluent Icons.
    static string PaneGlyph(SettingsPane p) => p switch
    {
        SettingsPane.General => "\uE713",      // Settings
        SettingsPane.Desktop => "\uE7F4",      // TV monitor
        SettingsPane.Island => "\uE8D6",       // Audio
        SettingsPane.LyricsWindow => "\uE8FD", // Bulleted list
        SettingsPane.Lyrics => "\uE8D2",       // Font
        SettingsPane.Shortcuts => "\uE765", // Keyboard
        _ => "\uE946",                         // Info
    };

    void OnPaneChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        try
        {
            if (args.SelectedItem is not NavigationViewItem { Tag: SettingsPane pane }) return;
            DetachNeteasePage();
            StopRecording();
            _fontRefresh = null;
            _watchers.Clear();
            _songWatchers.Clear();
            Page.Children.Clear();
            Scroller.ChangeView(null, 0, null, disableAnimation: true);
            if (pane != SettingsPane.About)
                Page.Children.Add(new TextBlock { Text = PaneTitle(pane), Style = Style("TitleTextBlockStyle"), Margin = new Thickness(1, 16, 0, 12) });
            switch (pane)
            {
                case SettingsPane.General: General(); break;
                case SettingsPane.Desktop: Desktop(); break;
                case SettingsPane.Island: Island(); break;
                case SettingsPane.LyricsWindow: LyricsWindowPane(); break;
                case SettingsPane.Lyrics: Lyrics(); break;
                case SettingsPane.Shortcuts: Shortcuts(); break;
                default: About(); break;
            }
        }
        catch (Exception) { DetachNeteasePage(); }
    }

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null && _watchers.TryGetValue(e.PropertyName, out var refreshes))
            foreach (var refresh in refreshes) refresh();
    }

    void OnModelChanged(PlaybackModel.Change change)
    {
        foreach (var refresh in _songWatchers) refresh();
    }

    void Watch(string property, Action refresh)
    {
        if (!_watchers.TryGetValue(property, out var list)) _watchers[property] = list = [];
        list.Add(refresh);
    }
}
