using Lyricod.Core;
using Lyricod.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lyricod;

/// <summary>
/// Settings › General › NetEase Cloud Music sync: sets up BetterNCM and InfLink-rs through Lyricod CM
/// Helper (see <see cref="NeteaseSync"/>), saying first what that means. Shown only when NetEase
/// Cloud Music is installed.
/// </summary>
public sealed partial class SettingsWindow
{
    /// <summary>A setup is running; one at a time, whichever page started it.</summary>
    static readonly NeteaseSetupState NeteaseState = new();
    Action? _neteaseRefresh;

    void DetachNeteasePage()
    {
        if (_neteaseRefresh != null) NeteaseState.Changed -= _neteaseRefresh;
        _neteaseRefresh = null;
        _neteaseFold = null;
    }

    static async void RunNeteaseSetup(NeteaseInstall netease, bool removing)
    {
        int? operation = null;
        (NeteaseResult Result, string? Error) outcome = (NeteaseResult.Failed, null);
        try
        {
            operation = NeteaseState.Start(removing);
            if (operation == null) return;
            int current = operation.Value;
            var progress = new Progress<NeteaseStage>(stage =>
            {
                try { NeteaseState.Report(current, stage); } catch (Exception) { }
            });
            outcome = await NeteaseSetup.RunAsync(netease, removing, progress);
        }
        catch (Exception e) { outcome = (NeteaseResult.Failed, e.Message); }
        finally
        {
            try { if (operation is int current) NeteaseState.Complete(current, outcome); }
            catch (Exception) { }
        }
    }

    /// <summary>The section on the page that's up, and whether to open it when the page is built.</summary>
    Expander? _neteaseFold;
    bool _openNeteaseSync;

    /// <summary>Shows General with NetEase Cloud Music sync open (the tray's NetEase tip).</summary>
    public void ShowNeteaseSync()
    {
        _openNeteaseSync = true;
        Show(SettingsPane.General); // builds the page if another was up, and the section opens itself
        // General was already up: open the section on it (a section from an earlier page has no parent).
        if (_openNeteaseSync && _neteaseFold is { Parent: not null } fold) OpenNeteaseFold(fold);
    }

    void OpenNeteaseFold(Expander fold)
    {
        _openNeteaseSync = false;
        fold.IsExpanded = true;
        // Scrolled to once it's on screen (a window just opened shows it a moment later).
        void Reveal()
        {
            try
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    try
                    {
                        if (ReferenceEquals(_neteaseFold, fold))
                            fold.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = true });
                    }
                    catch (Exception) { }
                });
            }
            catch (Exception) { }
        }
        if (fold.IsLoaded) Reveal();
        else
        {
            void Loaded(object sender, RoutedEventArgs e)
            {
                try { fold.Loaded -= Loaded; Reveal(); } catch (Exception) { }
            }
            fold.Loaded += Loaded;
        }
    }

    void NeteaseSyncSection()
    {
        _neteaseFold = null;
        if (NeteaseSetup.Find() is not { } netease) return;
        TextBlock? status = null;
        var install = new Button { Style = Style("AccentButtonStyle") };
        var remove = new Button { Content = L.Get("NeteaseSyncRemove") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(remove);
        buttons.Children.Add(install);
        var fold = Fold(L.Get("NeteaseSyncTitle"), L.Get("NeteaseSyncDetail"), () =>
        {
            Card(L.Get("NeteaseSyncWhat"), L.Get("NeteaseSyncWhatDetail"),
                new HyperlinkButton { Content = L.Get("NeteaseSyncLearnMore"), NavigateUri = new Uri(NeteaseSync.HelperPage) });
            // The README's steps, for doing it by hand (or with NetEase Cloud Music 2.x).
            Card(L.Get("NeteaseSyncBefore"), L.Get("NeteaseSyncBeforeDetail"),
                new HyperlinkButton { Content = L.Get("NeteaseSyncManual"), NavigateUri = new Uri(L.Get("NeteaseGuideUrl")) });
            var version = Value();
            version.Text = L.Format(NeteaseSync.IsTested(netease.Version) ? "NeteaseSyncTested" : "NeteaseSyncUntested", netease.Version.ToString(3));
            Card(L.Get("NeteaseSyncVersion"), netease.Is64Bit ? null : L.Get("NeteaseSyncUnsupported"), version);
            // Only the 64-bit NetEase Cloud Music 3.x can take BetterNCM 1.3.4.
            if (netease.Is64Bit) status = Card(L.Get("NeteaseSyncStatus"), "", buttons);
        });
        _neteaseFold = fold;
        if (_openNeteaseSync) OpenNeteaseFold(fold);
        if (status is not { } line) return;

        void Show()
        {
            try
            {
                bool working = _model.NowPlaying is { HasTimeline: true } now && MusicPlayers.IsNetease(now.AppId);
                install.Content = L.Get(netease.SyncInstalled ? "NeteaseSyncUpdate" : "NeteaseSyncInstall");
                install.Visibility = working ? Visibility.Collapsed : Visibility.Visible;
                remove.Visibility = netease.SyncInstalled ? Visibility.Visible : Visibility.Collapsed;
                install.IsEnabled = remove.IsEnabled = !NeteaseState.Busy;
                string? result = NeteaseState.Outcome is { } outcome ? outcome.Result switch
                {
                    NeteaseResult.Done => L.Get(NeteaseState.Removing ? "NeteaseSyncRemoved" : "NeteaseSyncDone"),
                    NeteaseResult.Cancelled => L.Get("NeteaseSyncCancelled"),
                    NeteaseResult.CouldntDownload => L.Get("NeteaseSyncCouldntDownload"),
                    _ => L.Format("NeteaseSyncFailed", outcome.Error ?? ""),
                } : null;
                line.Text = NeteaseState.Busy
                    ? L.Get(NeteaseState.Stage == NeteaseStage.Downloading ? "NeteaseSyncDownloading" : "NeteaseSyncWaiting")
                    : working ? L.Get("NeteaseSyncWorking")
                    : result ?? L.Get(netease.SyncInstalled ? "NeteaseSyncInstalled" : "NeteaseSyncNotInstalled");
            }
            catch (Exception) { /* the page closed meanwhile */ }
        }

        install.Click += (_, _) => { try { RunNeteaseSetup(netease, removing: false); } catch (Exception) { } };
        remove.Click += (_, _) => { try { RunNeteaseSetup(netease, removing: true); } catch (Exception) { } };
        void RefreshSetup()
        {
            try
            {
                if (!NeteaseState.Busy) netease = NeteaseSetup.Find() ?? netease;
                Show();
            }
            catch (Exception) { /* the page closed meanwhile */ }
        }

        _neteaseRefresh = RefreshSetup;
        NeteaseState.Changed += RefreshSetup;
        _songWatchers.Add(Show); // follows NetEase Cloud Music starting or stopping
        Show();
    }
}
