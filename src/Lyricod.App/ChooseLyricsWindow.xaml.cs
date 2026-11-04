using Lyricod.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WinUIEx;

namespace Lyricod;

/// <summary>Checks a source's lyrics before remembering a choice for this song.</summary>
public sealed partial class ChooseLyricsWindow : Window
{
    readonly PlaybackModel _model;
    List<LyricsCandidate> _candidates = [];
    LyricsSource? _filter;
    LyricsRef? _selection;
    Lyrics? _preview;
    int _searchVersion, _previewVersion;
    bool _closed, _rebuilding, _followCurrent = true;

    public ChooseLyricsWindow(PlaybackModel model)
    {
        InitializeComponent();
        _model = model;
        Title = TitleText.Text = L.Get("ChooseLyricsTitle");
        PinnedText.Text = L.Get("ChooseLyricsPinned");
        Query.PlaceholderText = L.Get("ChooseLyricsSearch");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Query, L.Get("ChooseLyricsSearch"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SourceFilters, L.Get("ChooseLyricsSource"));
        AllFilter.Content = L.Get("ChooseLyricsAll");
        NeteaseFilter.Content = L.Get("ChooseLyricsNetease");
        QqFilter.Content = L.Get("QQMusic");
        LrclibFilter.Content = L.Get("ChooseLyricsLrclib");
        AutomaticButton.Content = L.Get("ChooseLyricsAutomatic");
        CancelButton.Content = L.Get("ChooseLyricsCancel");
        UseButton.Content = L.Get("ChooseLyricsUse");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lyricod.ico"));
        this.CenterOnScreen(800, 540);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
        _model.Changed += OnModelChanged;
        Fonts.Changed += OnFontChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _searchVersion++;
            _previewVersion++;
            _model.Changed -= OnModelChanged;
            Fonts.Changed -= OnFontChanged;
        };
        UpdateSong();
    }

    void OnFontChanged()
    {
        try
        {
            RenderPreview(PreviewProgress.IsActive);
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("font refresh: " + e); }
    }

    void OnModelChanged(PlaybackModel.Change change)
    {
        if (change.HasFlag(PlaybackModel.Change.Track)) UpdateSong();
        else if (change.HasFlag(PlaybackModel.Change.Lyrics))
        {
            UpdatePinned();
            RebuildResults();
            SelectCurrent();
        }
        if (change.HasFlag(PlaybackModel.Change.Artwork)) _ = UpdateArtwork();
    }

    void UpdateSong()
    {
        _searchVersion++;
        _candidates = [];
        _followCurrent = true;
        SetSelection(null);
        var track = _model.Track;
        SongTitle.Text = track?.Title ?? L.Get("NothingPlaying");
        SongDetail.Text = track == null ? "" : $"{track.Artist} · {FormatDuration(track.Duration)}";
        Query.Text = track == null ? "" : $"{TrackMatcher.BareTitle(track.Title)} {track.Artist}";
        // Panels can't be disabled in WinUI; the controls in the search column are, one by one.
        foreach (var control in new Control[] { Query, AllFilter, NeteaseFilter, QqFilter, LrclibFilter, Results })
            control.IsEnabled = track != null;
        SetSearching(false);
        UpdatePinned();
        RebuildResults();
        _ = UpdateArtwork();
        if (track != null) _ = Search();
    }

    void UpdatePinned()
    {
        PinnedBadge.Visibility = Shown(_model.LyricsPinned);
        AutomaticButton.IsEnabled = _model.LyricsPinned;
    }

    async Task UpdateArtwork()
    {
        var bytes = _model.Artwork;
        Cover.Source = null;
        var image = await Images.FromBytes(bytes, 96);
        if (!_closed && ReferenceEquals(bytes, _model.Artwork)) Cover.Source = image;
    }

    async Task Search()
    {
        if (_model.Track == null) return;
        int version = ++_searchVersion;
        _followCurrent = true;
        SetSearching(true);
        var candidates = await _model.LyricsCandidates(Query.Text.Trim());
        if (_closed || version != _searchVersion) return;
        _candidates = candidates;
        if (_selection != null && !_candidates.Any(c => c.Id == _selection)) SetSelection(null);
        SetSearching(false);
        RebuildResults();
        SelectCurrent();
    }

    void SetSearching(bool searching)
    {
        SearchProgress.IsActive = searching;
        SearchProgress.Visibility = Shown(searching);
        UpdateEmptyResults();
    }

    void UpdateEmptyResults()
    {
        EmptyTitle.Text = L.Get(_model.Track == null ? "NothingPlaying" : "ChooseLyricsNoResults");
        EmptyHint.Text = _model.Track == null ? "" : L.Get("ChooseLyricsSearchHint");
        EmptyResults.Visibility = Shown(Results.Items.Count == 0 && !SearchProgress.IsActive);
    }

    void OnFilter(object sender, RoutedEventArgs e)
    {
        _filter = ReferenceEquals(sender, NeteaseFilter) ? LyricsSource.Netease
            : ReferenceEquals(sender, QqFilter) ? LyricsSource.Qq : ReferenceEquals(sender, LrclibFilter) ? LyricsSource.Lrclib : null;
        foreach (var button in new ToggleButton[] { AllFilter, NeteaseFilter, QqFilter, LrclibFilter })
            button.IsChecked = ReferenceEquals(sender, button);
        RebuildResults();
        SelectCurrent();
    }

    void RebuildResults()
    {
        var visible = _candidates.Where(c => _filter == null || c.Id.Source == _filter).ToList();
        var best = visible.FirstOrDefault() is { Score: >= 7 } first ? first.Id : null;
        var rows = visible.Select(c => new ResultRow(c, _model.Lyrics?.Ref, best, _filter == null, _model.Track?.Duration ?? 0)).ToList();
        _rebuilding = true;
        Results.ItemsSource = rows;
        Results.SelectedItem = rows.FirstOrDefault(r => r.Candidate.Id == _selection);
        _rebuilding = false;
        UpdateEmptyResults();
    }

    /// <summary>The automatic load may finish after the candidate search.</summary>
    void SelectCurrent()
    {
        if (!_followCurrent || _model.Lyrics?.Ref is not { } current) return;
        var row = Results.Items.OfType<ResultRow>().FirstOrDefault(r => r.Candidate.Id == current);
        if (row == null) return;
        _rebuilding = true;
        Results.SelectedItem = row;
        _rebuilding = false;
        if (_selection != current) SetSelection(current);
        Results.ScrollIntoView(row);
    }

    void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding) return;
        _followCurrent = false;
        SetSelection((Results.SelectedItem as ResultRow)?.Candidate.Id);
    }

    void SetSelection(LyricsRef? reference)
    {
        _selection = reference;
        _preview = null;
        int version = ++_previewVersion;
        RenderPreview(reference != null);
        if (reference != null) _ = LoadPreview(reference, version);
    }

    async Task LoadPreview(LyricsRef reference, int version)
    {
        var lyrics = await _model.PreviewLyrics(reference);
        if (_closed || version != _previewVersion) return;
        _preview = lyrics;
        RenderPreview(false);
    }

    void RenderPreview(bool loading)
    {
        PreviewLines.Children.Clear();
        PreviewProgress.IsActive = loading;
        PreviewProgress.Visibility = Shown(loading);
        PreviewScroll.Visibility = Shown(!loading && _preview != null);
        PreviewMessage.Visibility = Shown(!loading && _preview == null);
        UseButton.IsEnabled = _model.Track != null && _selection != null && _preview != null && !loading;
        PreviewTitle.Text = L.Get(_model.Track == null ? "NothingPlaying" : _selection == null ? "ChooseLyricsSelect" : "ChooseLyricsNoLyrics");
        PreviewHint.Text = _model.Track == null ? "" : L.Get(_selection == null ? "ChooseLyricsPreviewHint" : "ChooseLyricsNoLyricsHint");
        if (loading || _preview is not { } lyrics) return;
        string kind = L.Get(!lyrics.IsSynced ? "KindPlain" : lyrics.IsWordSynced ? "KindWords" : "KindLines");
        if (lyrics.Lines.Any(line => line.Translation != null)) kind += " " + L.Get("ChooseLyricsTranslated");
        PreviewLines.Children.Add(new TextBlock { Text = kind, Style = (Style)Root.Resources["Detail"] });
        foreach (var line in lyrics.Lines.Where(line => !line.IsCredit).Take(40))
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(LyricText(line.Text, false));
            if (line.Translation is { } translation) row.Children.Add(LyricText(translation, true));
            PreviewLines.Children.Add(row);
        }
        PreviewScroll.ChangeView(null, 0, null, true);
    }

    TextBlock LyricText(string text, bool translation) => new()
    {
        Text = text,
        Style = (Style)Root.Resources[translation ? "TranslationText" : "PreviewText"],
        FontFamily = Fonts.For(text, null),
        FontWeight = Fonts.SemiboldWeight,
    };

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { e.Handled = true; Close(); }
        else if (e.Key == VirtualKey.Enter)
        {
            // Enter in the search field always searches, even when a preview is ready.
            if (Query.FocusState != FocusState.Unfocused) { e.Handled = true; _ = Search(); }
            else if (UseButton.IsEnabled) { e.Handled = true; UseSelection(); }
        }
    }

    void OnAutomatic(object sender, RoutedEventArgs e) { if (_model.TryUnpinLyrics()) Close(); }
    void OnCancel(object sender, RoutedEventArgs e) => Close();
    void OnUse(object sender, RoutedEventArgs e) => UseSelection();

    void UseSelection()
    {
        if (!UseButton.IsEnabled || _selection is not { } reference) return;
        if (_model.TryPinLyrics(reference)) Close();
    }

    static Visibility Shown(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    static string FormatDuration(double value)
    {
        int seconds = (int)Math.Max(0, value);
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    public sealed class ResultRow(LyricsCandidate candidate, LyricsRef? current, LyricsRef? best, bool showSource, double duration)
    {
        public LyricsCandidate Candidate { get; } = candidate;
        public string Title => Candidate.Title;
        public string Detail => string.Join(" · ", new[] { Candidate.Artists, Candidate.Album }.Where(s => !string.IsNullOrEmpty(s)));
        public string Duration => FormatDuration(Candidate.Duration);
        public string Source => L.Get(Candidate.Id.Source switch
        {
            LyricsSource.Netease => "ChooseLyricsNetease", LyricsSource.Qq => "QQMusic", _ => "ChooseLyricsLrclib",
        });
        public string InUseText => L.Get("ChooseLyricsInUse");
        public string BestText => L.Get("ChooseLyricsBest");
        public Visibility InUse => Shown(Candidate.Id == current);
        public Visibility Best => Shown(Candidate.Id != current && Candidate.Id == best);
        public Visibility ShowSource => Shown(showSource);
        public Visibility MatchingLength => Shown(Math.Abs(Candidate.Duration - duration) < 2);
        public Visibility OtherLength => Shown(Math.Abs(Candidate.Duration - duration) >= 2);
    }
}
