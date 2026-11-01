using Lyricod.Core;
using Lyricod.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lyricod;

public sealed partial class SettingsWindow
{
    void General()
    {
        var startup = new ToggleSwitch { IsOn = Startup.IsEnabled, OnContent = L.Get("ToggleOn"), OffContent = L.Get("ToggleOff") };
        startup.Toggled += (_, _) =>
        {
            if (startup.IsOn != Startup.IsEnabled && !Startup.Set(startup.IsOn)) startup.IsOn = Startup.IsEnabled;
        };
        Card(L.Get("OpenAtStartup"), L.Get("OpenAtStartupDetail"), startup);
        Card(L.Get("AnimationLabel"), L.Get("AnimationDetail"), Choice(nameof(Settings.FrameRate), () => _settings.FrameRate, v => _settings.FrameRate = v,
            (FrameRate.BatterySaver, L.Get("FrameRateLow")), (FrameRate.Balanced, L.Get("FrameRateBalanced")), (FrameRate.Smooth, L.Get("FrameRateSmooth"))));
        // Each language in its own name, so it can be found whatever is showing.
        Card(L.Get("LanguageLabel"), null, Choice(nameof(Settings.Language), () => _settings.Language, v => _settings.Language = v,
            ("", L.Get("LanguageSystem")), ("en-US", "English"), ("zh-Hans", "简体中文")));
        var restart = new Button { Content = L.Get("RestartNow"), Style = Style("AccentButtonStyle") };
        restart.Click += (_, _) => Restart?.Invoke();
        var restartCard = new Border { Style = Style("Card"), Child = RestartRow(restart) };
        void SyncRestart() => restartCard.Visibility = _settings.Language != L.Language ? Visibility.Visible : Visibility.Collapsed;
        Watch(nameof(Settings.Language), SyncRestart);
        SyncRestart();
        Page.Children.Add(restartCard);

        Heading(L.Get("PlayerHeading"));
        // Whichever is playing, or one player always: those Windows knows about now, and the saved one.
        var players = new List<(string, string)> { ("auto", L.Get("FollowAuto")) };
        foreach (var id in _model.PlayerIds.Append(_settings.PlayerChoice).Where(id => id != "auto").Distinct())
            players.Add((id, MediaSession.DisplayName(id)));
        Card(L.Get("FollowLabel"), L.Get("FollowDetail"), Choice(nameof(Settings.PlayerChoice), () => _settings.PlayerChoice, v => _settings.PlayerChoice = v, [.. players]));
        var song = Value();
        var player = Value();
        _songWatchers.Add(() =>
        {
            song.Text = _model.Track is { } t ? $"{t.Title} — {t.Artist}" : L.Get("NothingPlaying");
            player.Text = _model.PlayerName ?? "—";
        });
        Card(L.Get("NowPlayingLabel"), null, song);
        Card(L.Get("PlayerLabel"), null, player);
        Note(L.Get("PlayerNote"));
        NeteaseSyncSection();
        Refresh();
    }

    void Desktop()
    {
        Card(L.Get("ShowDesktopLyrics"), null, Toggle(nameof(Settings.ShowDesktopLyrics), () => _settings.ShowDesktopLyrics, v => _settings.ShowDesktopLyrics = v));
        Card(L.Get("LockDesktopLyrics"), L.Get("LockDetail"), Toggle(nameof(Settings.DesktopLocked), () => _settings.DesktopLocked, v => _settings.DesktopLocked = v));
        var reset = new Button { Content = L.Get("ResetPosition") };
        reset.Click += (_, _) => { _settings.DesktopX = null; _settings.DesktopY = null; };
        Card(L.Get("PositionLabel"), null, reset);

        Heading(L.Get("AppearanceHeading"));
        Card(L.Get("TextSize"), null, TextSize());
        Card(L.Get("SungColour"), null, Colours());
        Card(L.Get("LayoutLabel"), L.Get("LayoutDetail"), Layout(nameof(Settings.SecondLine), () => _settings.SecondLine, v => _settings.SecondLine = v));
    }

    void Island()
    {
        Card(L.Get("ShowIsland"), L.Get("ShowIslandDetail"), Toggle(nameof(Settings.ShowIsland), () => _settings.ShowIsland, v => _settings.ShowIsland = v));

        // How it looks first, then where it sits and how it reacts, then what it shows.
        Heading(L.Get("StyleHeading"));
        Card(L.Get("IslandTheme"), L.Get("IslandThemeDetail"), Choice(nameof(Settings.IslandTheme), () => _settings.IslandTheme, v => _settings.IslandTheme = v,
            (IslandTheme.Classic, L.Get("IslandThemeClassic")), (IslandTheme.Glass, L.Get("IslandThemeGlass"))));
        // Lyricod Glass's own settings, there while it's the theme.
        var glass = Fold(L.Get("FoldGlass"), null, GlassSettings);
        void SyncGlass() => glass.Visibility = _settings.IslandTheme == IslandTheme.Glass ? Visibility.Visible : Visibility.Collapsed;
        Watch(nameof(Settings.IslandTheme), SyncGlass);
        SyncGlass();
        Card(L.Get("IslandSize"), null, Choice(nameof(Settings.IslandSize), () => _settings.IslandSize, v => _settings.IslandSize = v,
            (IslandSize.Small, L.Get("SizeSmall")), (IslandSize.Medium, L.Get("SizeMedium")),
            (IslandSize.Large, L.Get("SizeLarge")), (IslandSize.ExtraLarge, L.Get("SizeExtraLarge"))));
        Card(L.Get("AlbumColour"), L.Get("AlbumColourDetail"), Toggle(nameof(Settings.IslandAlbumColour), () => _settings.IslandAlbumColour, v => _settings.IslandAlbumColour = v));
        var colours = new ContentControl
        {
            Content = Colours(nameof(Settings.IslandColor), () => _settings.IslandColor, v => _settings.IslandColor = v),
        };
        void SyncColours() => colours.IsEnabled = !_settings.IslandAlbumColour;
        Watch(nameof(Settings.IslandAlbumColour), SyncColours);
        SyncColours();
        Card(L.Get("IslandColour"), null, colours);
        Card(L.Get("AudioBars"), L.Get("AudioBarsDetail"), Toggle(nameof(Settings.IslandAudioBars), () => _settings.IslandAudioBars, v => _settings.IslandAudioBars = v));

        Heading(L.Get("IslandBehaviourHeading"));
        Card(L.Get("IslandStyle"), L.Get("IslandStyleDetail"), Choice(nameof(Settings.IslandStyle), () => _settings.IslandStyle, v => _settings.IslandStyle = v,
            (IslandStyle.Default, L.Get("IslandStyleDefault")), (IslandStyle.Floating, L.Get("IslandStyleFloating"))));
        // Only a floating island can be moved, so only it has these.
        var floating = Fold(L.Get("FoldFloating"), null, () =>
        {
            var resetIsland = new Button { Content = L.Get("ResetIslandPosition") };
            resetIsland.Click += (_, _) => { _settings.IslandX = null; _settings.IslandY = null; };
            Card(L.Get("LockIsland"), L.Get("LockIslandDetail"), Toggle(nameof(Settings.IslandLocked), () => _settings.IslandLocked, v => _settings.IslandLocked = v));
            Card(L.Get("PositionLabel"), null, resetIsland);
        });
        void SyncFloating() => floating.Visibility = _settings.IslandStyle == IslandStyle.Floating ? Visibility.Visible : Visibility.Collapsed;
        Watch(nameof(Settings.IslandStyle), SyncFloating);
        SyncFloating();
        var hoverDetail = Card(L.Get("OnHover"), L.Get("HoverByDirectionDetail"), Choice(nameof(Settings.IslandHover), () => _settings.IslandHover, v => _settings.IslandHover = v,
            (IslandHover.Expand, L.Get("HoverExpand")), (IslandHover.Hide, L.Get("HoverHide")), (IslandHover.Nothing, L.Get("HoverNothing")),
            (IslandHover.ByDirection, L.Get("HoverByDirection"))))!;
        // The line under it explains the gesture, only while "by direction" is chosen. The default
        // island only pulls down; a floating one pulls either way.
        void SyncHoverDetail()
        {
            hoverDetail.Visibility = _settings.IslandHover == IslandHover.ByDirection ? Visibility.Visible : Visibility.Collapsed;
            hoverDetail.Text = L.Get(_settings.IslandStyle == IslandStyle.Floating ? "HoverByDirectionFloatingDetail" : "HoverByDirectionDetail");
        }
        Watch(nameof(Settings.IslandHover), SyncHoverDetail);
        Watch(nameof(Settings.IslandStyle), SyncHoverDetail);
        SyncHoverDetail();
        Card(L.Get("HideWhenPaused"), L.Get("HideWhenPausedDetail"), Toggle(nameof(Settings.IslandHideWhenPaused),
            () => _settings.IslandHideWhenPaused, v => _settings.IslandHideWhenPaused = v));

        Heading(L.Get("TextHeading"));
        Card(L.Get("LayoutLabel"), null, Layout(nameof(Settings.IslandSecondLine), () => _settings.IslandSecondLine, v => _settings.IslandSecondLine = v));
        Fold(L.Get("FoldText"), null, () =>
        {
            Card(L.Get("IslandMaxWidth"), L.Get("IslandMaxWidthDetail"), Choice(nameof(Settings.IslandMaxTextWidth),
                () => _settings.IslandMaxTextWidth, v => _settings.IslandMaxTextWidth = v,
                (320, L.Get("IslandWidthNarrow")), (460, L.Get("IslandWidthStandard")), (600, L.Get("IslandWidthWide"))));
            Card(L.Get("FontWeight"), null, Choice(nameof(Settings.IslandWeight), () => _settings.IslandWeight, v => _settings.IslandWeight = v,
                (IslandWeight.Regular, L.Get("WeightRegular")), (IslandWeight.Medium, L.Get("WeightMedium")), (IslandWeight.Semibold, L.Get("WeightSemibold")),
                (IslandWeight.Bold, L.Get("WeightBold")), (IslandWeight.Heavy, L.Get("WeightHeavy"))));
            Card(L.Get("SungLyrics"), null, Effect(nameof(Settings.IslandEffect), () => _settings.IslandEffect, v => _settings.IslandEffect = v));
            Card(L.Get("NextLine"), null, Choice(nameof(Settings.IslandTransition), () => _settings.IslandTransition, v => _settings.IslandTransition = v,
                (LineTransition.Instant, L.Get("TransitionInstant")), (LineTransition.Slide, L.Get("TransitionSlide")), (LineTransition.Fade, L.Get("TransitionFade"))));
        });
    }

    /// <summary>Lyricod Glass's settings, in their fold on the island page.</summary>
    void GlassSettings()
    {
        Card(L.Get("GlassTransparency"), null,
            Percent(nameof(Settings.GlassTransparency), () => _settings.GlassTransparency, v => _settings.GlassTransparency = v, 10, 90));
        Card(L.Get("GlassTint"), null, Choice(nameof(Settings.GlassTint), () => _settings.GlassTint, v => _settings.GlassTint = v,
            (GlassTint.Neutral, L.Get("GlassTintNeutral")), (GlassTint.Album, L.Get("GlassTintAlbum"))));
        // Clear, frosted or liquid (each says what it costs, and where it can't run).
        var effectDetail = Card(L.Get("GlassEffect"), "", Choice(nameof(Settings.GlassEffect), () => _settings.GlassEffect, v => _settings.GlassEffect = v,
            (GlassEffect.Clear, L.Get("GlassEffectClear")), (GlassEffect.Frosted, L.Get("GlassEffectFrosted")), (GlassEffect.Liquid, L.Get("GlassEffectLiquid"))))!;
        var liquidBlur = new ContentControl { Content = Percent(nameof(Settings.LiquidBlur), () => _settings.LiquidBlur, v => _settings.LiquidBlur = v, 0, 100) };
        var refraction = new ContentControl
        {
            Content = Percent(nameof(Settings.LiquidRefraction), () => _settings.LiquidRefraction, v => _settings.LiquidRefraction = v, 0, 100),
        };
        // The 3D effect is the liquid glass's: it lights the edge its lens bends.
        var shine = new ContentControl { Content = Toggle(nameof(Settings.GlassShine), () => _settings.GlassShine, v => _settings.GlassShine = v) };
        var screenshot = new ContentControl { Content = ScreenshotButton() };
        void SyncEffect()
        {
            var effect = _settings.GlassEffect;
            bool supported = effect switch
            {
                GlassEffect.Frosted => FrostedBackdrop.IsSupported,
                GlassEffect.Liquid => LiquidGlass.IsSupported,
                _ => true,
            };
            effectDetail.Text = effect switch
            {
                GlassEffect.Frosted => L.Get("GlassEffectFrostedDetail"),
                GlassEffect.Liquid => L.Get("GlassEffectLiquidDetail"),
                _ => L.Get("GlassEffectClearDetail"),
            } + (supported ? "" : " " + L.Get("GlassEffectUnsupported"));
            liquidBlur.IsEnabled = refraction.IsEnabled = shine.IsEnabled = screenshot.IsEnabled = effect == GlassEffect.Liquid && LiquidGlass.IsSupported;
        }
        Watch(nameof(Settings.GlassEffect), SyncEffect);
        SyncEffect();
        Card(L.Get("LiquidBlur"), null, liquidBlur);
        Card(L.Get("LiquidRefraction"), null, refraction);
        Card(L.Get("GlassShine"), null, shine);
        Card(L.Get("LiquidScreenshot"), L.Get("LiquidScreenshotDetail"), screenshot);
    }

    /// <summary>Shows the island in screenshots for a few seconds, counting them down.</summary>
    Button ScreenshotButton()
    {
        var button = new Button { Content = L.Get("LiquidScreenshotButton") };
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        int left = 0;
        void Show() => button.Content = left > 0 ? L.Format("LiquidScreenshotCountdown", left) : L.Get("LiquidScreenshotButton");
        timer.Tick += (_, _) =>
        {
            left--;
            if (left <= 0) timer.Stop();
            Show();
        };
        button.Click += (_, _) =>
        {
            ShowIslandInScreenshots?.Invoke();
            left = IslandWindow.ScreenshotSeconds;
            timer.Stop();
            timer.Start();
            Show();
        };
        button.Unloaded += (_, _) => timer.Stop();
        return button;
    }

    ComboBox Effect(string property, Func<SungEffect> get, Action<SungEffect> set) => Choice(property, get, set,
        (SungEffect.Sweep, L.Get("EffectSweep")), (SungEffect.Glow, L.Get("EffectGlow")), (SungEffect.Line, L.Get("EffectLine")));

    /// <summary>"Lyricod needs to restart to change language", and the button.</summary>
    Grid RestartRow(Button restart)
    {
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = L.Get("RestartToChangeLanguage"), Style = Style("Detail"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(restart, 1);
        restart.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(restart);
        return grid;
    }

    void LyricsWindowPane()
    {
        var open = new Button { Content = L.Get("Open") };
        open.Click += (_, _) => _openLyricsWindow();
        Card(L.Get("LyricsWindowLabel"), L.Get("LyricsWindowDetail"), open);
        Card(L.Get("BackgroundLabel"), null, Choice(nameof(Settings.LyricsBackground), () => _settings.LyricsBackground, v => _settings.LyricsBackground = v,
            (LyricsBackground.Artwork, L.Get("BackgroundArtwork")), (LyricsBackground.StillArtwork, L.Get("BackgroundStillArtwork")),
            (LyricsBackground.Colour, L.Get("BackgroundColour")),
            (LyricsBackground.Dark, L.Get("BackgroundDark"))));

        Heading(L.Get("TextHeading"));
        Card(L.Get("TextSize"), null, Choice(nameof(Settings.LyricsTextSize), () => _settings.LyricsTextSize, v => _settings.LyricsTextSize = v,
            (LyricsTextSize.Automatic, L.Get("SizeAutomatic")), (LyricsTextSize.Small, L.Get("SizeSmall")),
            (LyricsTextSize.Medium, L.Get("SizeMedium")), (LyricsTextSize.Large, L.Get("SizeLarge"))));
        Card(L.Get("ShowTranslations"), null, Toggle(nameof(Settings.ShowTranslations), () => _settings.ShowTranslations, v => _settings.ShowTranslations = v));
        Card(L.Get("ShowPronunciation"), null, Toggle(nameof(Settings.ShowPronunciation), () => _settings.ShowPronunciation, v => _settings.ShowPronunciation = v));
        Card(L.Get("CentreLines"), null, Toggle(nameof(Settings.LyricsCentered), () => _settings.LyricsCentered, v => _settings.LyricsCentered = v));
        Card(L.Get("BlurDistant"), null, Toggle(nameof(Settings.LyricsBlurDistant), () => _settings.LyricsBlurDistant, v => _settings.LyricsBlurDistant = v));
        Card(L.Get("SungLyrics"), null, Effect(nameof(Settings.LyricsEffect), () => _settings.LyricsEffect, v => _settings.LyricsEffect = v));
        Card(L.Get("AutoHideControls"), L.Get("AutoHideControlsDetail"), Toggle(nameof(Settings.LyricsAutoHide), () => _settings.LyricsAutoHide, v => _settings.LyricsAutoHide = v));
    }

    void Lyrics()
    {
        Heading(L.Get("TimingHeading"));
        Card(L.Get("AllSongs"), null, Offset(() => _settings.Offset, v => _settings.Offset = v, () => true, nameof(Settings.Offset)));
        Card(L.Get("ThisSong"), null, Offset(() => _model.SongOffset, _model.SetSongOffset, () => _model.Track != null, null));
        Note(L.Get("TimingNote"));

        Heading(L.Get("DisplayHeading"));
        FontPicker();
        var languages = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var (language, key) in new[] { (LyricLanguage.Japanese, "Japanese"), (LyricLanguage.Korean, "Korean"), (LyricLanguage.Chinese, "Chinese") })
        {
            var box = new CheckBox { Content = L.Get(key), MinWidth = 0 };
            void Sync() => box.IsChecked = _settings.PronunciationLanguages.Contains(language);
            Sync();
            box.Click += (_, _) =>
            {
                var set = _settings.PronunciationLanguages.ToHashSet();
                if (box.IsChecked == true) set.Add(language); else set.Remove(language);
                _settings.PronunciationLanguages = [.. set.OrderBy(l => l)];
            };
            Watch(nameof(Settings.PronunciationLanguages), Sync);
            languages.Children.Add(box);
        }
        Card(L.Get("PronunciationFor"), L.Get("PronunciationForDetail"), languages);
        Card(L.Get("ShowCredits"), null, Toggle(nameof(Settings.ShowCredits), () => _settings.ShowCredits, v => _settings.ShowCredits = v));
        Note(L.Get(Lyricod.Platform.JapaneseReader.IsAvailable ? "PronunciationNote" : "PronunciationNoteNoReader"));

        Heading(L.Get("SourcesHeading"));
        Card(L.Get("LookUpOtherPlayers"), L.Get("LookUpOtherPlayersDetail"),
            Toggle(nameof(Settings.LookUpOtherPlayers), () => _settings.LookUpOtherPlayers, v => _settings.LookUpOtherPlayers = v));
        var status = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var title = new TextBlock { TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 };
        var kind = new TextBlock { Style = Style("Detail"), TextAlignment = TextAlignment.Right };
        status.Children.Add(title);
        status.Children.Add(kind);
        var again = new Button { Content = L.Get("SearchAgain") };
        again.Click += (_, _) => _model.SearchAgain();
        var choose = new Button { Content = L.Get("ChooseLyricsButton") };
        choose.Click += (_, _) => _openChooseLyrics();
        _songWatchers.Add(() =>
        {
            title.Text = _model.Track?.Title ?? L.Get("NothingPlaying");
            kind.Text = LyricsStatus();
            again.IsEnabled = _model.Track != null && !_model.IsSearching && _model.AutomaticLookupAllowed;
            choose.IsEnabled = _model.Track != null;
        });
        Card(L.Get("CurrentSong"), null, Row(status, again, choose));

        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Style = Style("Detail") };
        var clear = new Button { Content = L.Get("Clear") };
        void Count()
        {
            int n = _model.CachedSongs;
            count.Text = n == 1 ? L.Get("OneSong") : L.Format("NSongs", n);
            clear.IsEnabled = n > 0;
        }
        clear.Click += (_, _) => { _model.ClearSavedLyrics(); Count(); };
        Count();
        Card(L.Get("SavedLyrics"), L.Get("SavedLyricsDetail"), Row(count, clear));
        Refresh();
    }

    string LyricsStatus()
    {
        if (_model.Track == null) return "—";
        if (_model.IsSearching) return L.Get("Searching");
        if (_model.Lyrics is not { } l) return L.Get("NoLyrics");
        var kind = !l.IsSynced ? L.Get("KindPlain") : l.IsWordSynced ? L.Get("KindWords") : L.Get("KindLines");
        return $"{l.Source} · {kind}";
    }

    void About()
    {
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 24, 0, 4) };
        top.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/Lyricod-256.png")) { DecodePixelWidth = 160 },
            Width = 80, Height = 80, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8),
        });
        top.Children.Add(Centred(new TextBlock { Text = "Lyricod", Style = Style("TitleTextBlockStyle") }));
        top.Children.Add(Centred(new TextBlock { Text = L.Format("Version", UpdateChecker.DisplayVersion), Style = Style("Detail") }));
        top.Children.Add(Centred(new TextBlock { Text = L.Get("Tagline"), Margin = new Thickness(0, 6, 0, 0) }));
        top.Children.Add(Centred(new HyperlinkButton { Content = "GitHub", NavigateUri = new Uri("https://github.com/Indarkdust/Lyricod-App") }));
        Page.Children.Add(top);

        Heading(L.Get("Updates"));
        Card(L.Get("AutomaticUpdates"), null, Toggle(nameof(Settings.CheckForUpdatesAutomatically),
            () => _settings.CheckForUpdatesAutomatically, v => _settings.CheckForUpdatesAutomatically = v));
        UpdateControls();

        Heading(L.Get("Diagnostics"));
        var diagnostics = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
            Visibility = Visibility.Collapsed, // hidden until asked for
        };
        void RefreshDiagnostics() => diagnostics.Text = _model.DiagnosticsText + "\nGlass capture: " + ((App)Application.Current).GlassCaptureDiagnostics;
        _songWatchers.Add(RefreshDiagnostics);
        Watch(nameof(Settings.Offset), RefreshDiagnostics);
        RefreshDiagnostics();
        var copy = new Button { Content = L.Get("CopyDiagnostics") };
        copy.Click += (_, _) =>
        {
            RefreshDiagnostics();
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            data.SetText(diagnostics.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        };
        var show = new Button { Content = L.Get("ShowDiagnostics") };
        show.Click += (_, _) =>
        {
            bool shown = diagnostics.Visibility == Visibility.Visible;
            diagnostics.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            show.Content = L.Get(shown ? "ShowDiagnostics" : "HideDiagnostics");
        };
        Group(new TextBlock { Text = L.Get("DiagnosticsDetail"), Style = Style("Detail"), TextWrapping = TextWrapping.Wrap },
              Row(show, copy), diagnostics);

        Heading(L.Get("LyricsHeader"));
        Group(Entry(L.Get("NetEase"), L.Get("SourceDetail"), "https://music.163.com"),
              Entry(L.Get("QQMusic"), L.Get("SourceDetail"), "https://y.qq.com"),
              Entry("Lyricify Lyrics Helper by WXRIW", L.Get("LyricsHelperDetail"), "https://github.com/WXRIW/Lyricify-Lyrics-Helper"),
              Entry("LRCLIB", L.Get("LrclibDetail"), "https://lrclib.net"),
              new TextBlock { Text = L.Get("LyricsRights"), Style = Style("Detail") });

        Heading(L.Get("FontHeader"));
        var licence = new HyperlinkButton { Content = L.Get("ViewLicence"), Padding = new Thickness(0, 4, 0, 0) };
        licence.Click += (_, _) => Open(MiSansDownload.LicencePath);
        void ShowFontLicence() => licence.Visibility = MiSansDownload.IsDownloaded ? Visibility.Visible : Visibility.Collapsed;
        _songWatchers.Add(ShowFontLicence);
        ShowFontLicence();
        Group(Entry(L.Get("MiSansName"), L.Get("MiSansDetail"), "https://hyperos.mi.com/font/"), licence);

        Heading(L.Get("BuiltWith"));
        Group(Entry("WinUIEx by Morten Nielsen", L.Get("WinUIExDetail"), "https://github.com/dotMorten/WinUIEx"),
              Entry("ToolGood.Words", L.Get("ToolGoodDetail"), "https://github.com/toolgood/ToolGood.Words"),
              Entry("Win2D by Microsoft", L.Get("Win2DDetail"), "https://github.com/microsoft/Win2D"),
              new TextBlock { Text = L.Get("PlatformNote"), Style = Style("Detail") });

        Heading(L.Get("Trademarks"));
        Group(new TextBlock { Text = L.Get("TrademarksText"), Style = Style("Detail") });
    }
}
