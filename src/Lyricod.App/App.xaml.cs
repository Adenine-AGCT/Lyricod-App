using Lyricod.Core;
using Lyricod.Core.Romanization;
using Lyricod.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using WinUIEx;

namespace Lyricod;

/// <summary>
/// A tray app, like the Mac's menu-bar app: no main window. The tray icon's menu shows and hides
/// the views and opens the lyrics window and Settings.
/// </summary>
public partial class App : Application
{
    UpdateChecker? _updates;
    Settings? _settings;
    PlaybackModel? _model;
    DesktopLyricsWindow? _desktop;
    IslandWindow? _island;
    LyricsWindow? _lyricsWindow;
    SettingsWindow? _settingsWindow;
    ChooseLyricsWindow? _chooseLyricsWindow;
    TrayIcon? _tray;
    TrayPanel? _panel;
    GlobalShortcuts? _shortcuts;
    bool _quitting;

    internal string GlassCaptureDiagnostics => _island?.LiquidCaptureStatus ?? "inactive";

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { await LaunchAsync(); }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine(e);
            Quit();
        }
    }

    async Task LaunchAsync()
    {
        // One Lyricod at a time.
        if (!AppInstance.FindOrRegisterForKey("Lyricod").IsCurrent)
        {
            Platform.UpdateHealth.SignalStarted(); // it runs: another Lyricod is just already open
            Exit();
            return;
        }
        // Closing a window mustn't quit a tray app.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        _settings = Settings.Load();
        // A first run gets Lyricod Glass where its liquid effect can run (Windows 11); elsewhere the classic island.
        if (_settings.IsNew && LiquidGlass.IsSupported) _settings.IslandTheme = IslandTheme.Glass;
        // The language chosen in Settings, read once: like the Mac, a change applies after a restart.
        L.Start(_settings.Language);
        Fonts.Start(_settings);
        _updates = new UpdateChecker(_settings, DispatcherQueue.GetForCurrentThread());
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "dev";
        var provider = new LyricsProvider(Path.Combine(Settings.DataDir, "lyrics"), version);
        var overrides = new TrackOverrides(Path.Combine(Settings.DataDir, "song-overrides.json"));
        _model = new PlaybackModel(DispatcherQueue.GetForCurrentThread(), new MediaSession(), provider,
            new Romanizer(JapaneseReader.IsAvailable ? new JapaneseReader() : null), overrides, _settings);
        _desktop = new DesktopLyricsWindow(_model, _settings);
        _shortcuts = new GlobalShortcuts(_desktop, _settings, PerformShortcut);
        _island = new IslandWindow(_model, _settings, OpenLyricsWindow, () => OpenSettings());

        _tray = new TrayIcon(1, Path.Combine(AppContext.BaseDirectory, "Assets", "Lyricod.ico"), "Lyricod");
        // A click opens the panel (as the Mac's menu-bar icon does); right-click, Windows' usual short menu.
        _panel = new TrayPanel(_model, _settings, OpenLyricsWindow, () => OpenSettings(), OpenChooseLyrics, Quit, _updates)
        {
            OpenNeteaseSync = OpenNeteaseSync,
        };
        _tray.ContextMenu += (_, e) => e.Flyout = BuildMenu();
        _tray.Selected += (_, _) => _panel.Toggle();
        _tray.IsVisible = true;
        _model.Changed += change =>
        {
            if (change.HasFlag(PlaybackModel.Change.Track) && _tray != null)
                _tray.Tooltip = _model.Track is { } t ? Trim($"Lyricod · {t.Title} — {t.Artist}", 127) : "Lyricod";
        };

        await _model.StartAsync();
        // After a portable update: windows, tray icon and media tracking are all up, so the launcher can
        // let the old copy go. Anything above that throws ends here without this, and the launcher puts it back.
        Platform.UpdateHealth.SignalStarted();
    }

    static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    void OpenLyricsWindow()
    {
        if (_lyricsWindow == null)
        {
            _lyricsWindow = new LyricsWindow(_model!, _settings!, OpenChooseLyrics);
            _lyricsWindow.Closed += (_, _) => _lyricsWindow = null;
        }
        _lyricsWindow.Activate();
    }

    void OpenChooseLyrics()
    {
        if (_chooseLyricsWindow == null)
        {
            _chooseLyricsWindow = new ChooseLyricsWindow(_model!);
            _chooseLyricsWindow.Closed += (_, _) => _chooseLyricsWindow = null;
        }
        _chooseLyricsWindow.Activate();
    }

    void PerformShortcut(ShortcutAction action)
    {
        var model = _model!;
        var settings = _settings!;
        switch (action)
        {
            case ShortcutAction.PlayPause: _ = model.PlayPause(); break;
            case ShortcutAction.Next: _ = model.Next(); break;
            case ShortcutAction.Previous: _ = model.Previous(); break;
            case ShortcutAction.ToggleDesktop: settings.ShowDesktopLyrics = !settings.ShowDesktopLyrics; break;
            case ShortcutAction.ToggleIsland: settings.ShowIsland = !settings.ShowIsland; break;
            case ShortcutAction.ToggleLyricsWindow:
                if (_lyricsWindow == null) OpenLyricsWindow(); else _lyricsWindow.Close();
                break;
            case ShortcutAction.LyricsEarlier: model.SetSongOffset(model.SongOffset + 0.5); break;
            case ShortcutAction.LyricsLater: model.SetSongOffset(model.SongOffset - 0.5); break;
        }
    }

    void OpenSettings(SettingsPane? pane = null)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_settings!, _model!, OpenLyricsWindow, OpenChooseLyrics, _shortcuts!, _updates!, pane ?? SettingsPane.General)
            {
                Restart = Restart,
                ShowIslandInScreenshots = () => _island?.ShowInScreenshots(),
            };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        else if (pane is { } p) _settingsWindow.Show(p);
        _settingsWindow.Activate();
    }

    /// <summary>The tray's NetEase tip: Settings › General › NetEase Cloud Music sync where it can be set up, else the guide.</summary>
    void OpenNeteaseSync()
    {
        if (Platform.NeteaseSetup.Find() is { Is64Bit: true })
        {
            OpenSettings(SettingsPane.General);
            _settingsWindow?.ShowNeteaseSync();
            return;
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(L.Get("NeteaseGuideUrl")) { UseShellExecute = true })?.Dispose(); }
        catch (Exception) { }
    }

    MenuFlyout BuildMenu()
    {
        var s = _settings!;
        var m = _model!;
        var menu = new MenuFlyout();

        var song = m.Track is { } t ? Trim($"{t.Title} — {t.Artist}", 60) : L.Get("NothingPlaying");
        menu.Items.Add(new MenuFlyoutItem { Text = song, IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());

        var show = new ToggleMenuFlyoutItem { Text = L.Get("DesktopLyrics"), IsChecked = s.ShowDesktopLyrics };
        show.Click += (_, _) => s.ShowDesktopLyrics = !s.ShowDesktopLyrics;
        menu.Items.Add(show);
        var locked = new ToggleMenuFlyoutItem { Text = L.Get("LockDesktopLyrics"), IsChecked = s.DesktopLocked, IsEnabled = s.ShowDesktopLyrics };
        locked.Click += (_, _) => s.DesktopLocked = !s.DesktopLocked;
        menu.Items.Add(locked);
        var island = new ToggleMenuFlyoutItem { Text = L.Get("PaneIsland"), IsChecked = s.ShowIsland };
        island.Click += (_, _) => s.ShowIsland = !s.ShowIsland;
        menu.Items.Add(island);
        // Only a floating island can be moved, so only it can be locked.
        if (s.IslandStyle == IslandStyle.Floating)
        {
            var islandLocked = new ToggleMenuFlyoutItem { Text = L.Get("LockIsland"), IsChecked = s.IslandLocked, IsEnabled = s.ShowIsland };
            islandLocked.Click += (_, _) => s.IslandLocked = !s.IslandLocked;
            menu.Items.Add(islandLocked);
        }
        menu.Items.Add(new MenuFlyoutSeparator());

        var lyrics = new MenuFlyoutItem { Text = L.Get("LyricsWindowMenu"), Icon = new FontIcon { Glyph = "\uE8FD" } };
        lyrics.Click += (_, _) => OpenLyricsWindow();
        menu.Items.Add(lyrics);
        var settings = new MenuFlyoutItem { Text = L.Get("SettingsMenu"), Icon = new FontIcon { Glyph = "\uE713" } };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);
        menu.Items.Add(new MenuFlyoutSeparator());
        var quit = new MenuFlyoutItem { Text = L.Get("Quit") };
        quit.Click += (_, _) => Quit();
        menu.Items.Add(quit);
        return menu;
    }

    /// <summary>Starts a new Lyricod and quits this one (to change language).</summary>
    void Restart() => Quit(restart: true);

    /// <summary>
    /// Quits once the click that asked for it (the tray panel's button, the tray menu's item or
    /// Settings' Restart) has finished, since its window closes on the way out.
    /// </summary>
    void Quit() => Quit(restart: false);

    public void QuitForUpdate() => Quit(restart: false);

    void Quit(bool restart)
    {
        if (_quitting) return;
        _quitting = true;
        void Finish()
        {
            try { ShutDown(restart); }
            catch (Exception) { Exit(); }
        }
        try { if (!DispatcherQueue.GetForCurrentThread().TryEnqueue(Finish)) Finish(); }
        catch (Exception) { Finish(); }
    }

    /// <summary>
    /// Stops everything that calls back on its own (the player's reports, timers, the audio meter,
    /// shortcuts and the tray icon), closes the windows, then exits. Anything left running would
    /// run against closed windows, and an exception there crashes the process (0xC000027B in
    /// CoreMessagingXP) instead of letting it exit.
    /// </summary>
    void ShutDown(bool restart)
    {
        _model?.Stop();
        _updates?.Dispose();
        _shortcuts?.Dispose();
        if (_tray != null)
        {
            // An open tray menu would otherwise close with its window and touch it as it goes.
            _tray.CloseFlyout();
            _tray.Dispose();
            _tray = null;
        }
        // Each window stops its own timers as it closes.
        _chooseLyricsWindow?.Close();
        _settingsWindow?.Close();
        _lyricsWindow?.Close();
        _panel?.Close();
        _island?.Close();
        _desktop?.Close();
        if (restart) StartAgain();
        Exit();
    }

    /// <summary>A new Lyricod, started once this one has let go of its shortcuts and tray icon.</summary>
    static void StartAgain()
    {
        // Let the new one register as the single instance.
        AppInstance.GetCurrent().UnregisterKey();
        try
        {
            if (Environment.ProcessPath is { } path)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception) { } // it couldn't start: this one still quits, as it would have
    }
}
