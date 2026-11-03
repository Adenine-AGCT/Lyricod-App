using System.Diagnostics;
using Lyricod.Core;
using Lyricod.Core.Romanization;
using Lyricod.Platform;
using Microsoft.UI.Dispatching;

namespace Lyricod;

/// <summary>
/// The one source of truth, like the macOS app's PlaybackModel: follows the player through
/// SMTC, keeps the playhead between reports, loads lyrics when the song changes, and moves
/// to each next line on a single timer (no ticking many times a second). Lives on the UI
/// thread; <see cref="Changed"/> fires there.
/// </summary>
public sealed partial class PlaybackModel
{
    [Flags]
    public enum Change { None = 0, Track = 1, Lyrics = 2, Line = 4, Clock = 8, Artwork = 16, Diagnostics = 32 }

    public event Action<Change>? Changed;

    public NowPlaying? NowPlaying { get; private set; }
    public Track? Track { get; private set; }
    /// <summary>Lyrics as shown: credits and pronunciation filtered by the settings.</summary>
    public Lyrics? Lyrics { get; private set; }
    public bool IsSearching { get; private set; }
    public bool IsMusicPlayer => MusicPlayers.IsMusicPlayer(NowPlaying?.AppId);
    public bool AutomaticLookupAllowed => new LyricsSearch(IsMusicPlayer, _settings.LookUpOtherPlayers).AutomaticLookupAllowed;
    SongState Recognition => SongRecognition.Classify(IsMusicPlayer, LyricsPinned, IsSearching, _allLyrics != null);
    public bool IsSong => Track != null && Recognition == SongState.Song;
    public bool IsNotSong => Track != null && Recognition == SongState.NotSong;
    public double? BestMatchScore { get; private set; }
    public int? CurrentIndex { get; private set; }
    public LyricLine? CurrentLine => CurrentIndex is int i && Lyrics is { } l ? l.Lines[i] : null;
    public bool IsPlaying => _playhead.IsPlaying;
    /// <summary>Changes whenever the playhead re-anchors; animations re-plan on it.</summary>
    public int ClockVersion => _playhead.Version;
    /// <summary>The last thing that went wrong, for the debug state file.</summary>
    public string? LastError { get; private set; }
    /// <summary>This song's own offset, on top of the global one.</summary>
    public double SongOffset { get; private set; }
    /// <summary>The cover art (encoded image bytes), once the player hands it over.</summary>
    public byte[]? Artwork { get; private set; }
    /// <summary>The cover's dominant colour, tuned for black backgrounds.</summary>
    public Windows.UI.Color? ArtworkColor { get; private set; }
    /// <summary>The cover's size in pixels, for diagnostics.</summary>
    public (int Width, int Height)? ArtworkSize { get; private set; }
    /// <summary>Song length in seconds; 0 when the player doesn't say.</summary>
    public double Duration => NowPlaying?.Duration ?? 0;
    /// <summary>The player lets Lyricod move it to another point (click a line, drag the progress bar).</summary>
    public bool CanSeek => NowPlaying?.CanSeek == true;
    /// <summary>The player's own position (no offset), for progress bars.</summary>
    public double PlayerPosition => Math.Clamp(_playhead.RawPosition(Now), 0, Duration > 0 ? Duration : double.MaxValue);
    /// <summary>"Microsoft Edge", "Spotify"… for Settings.</summary>
    public string? PlayerName => NowPlaying is { } n ? MediaSession.DisplayName(n.AppId) : null;
    public bool LyricsPinned => Track is { } track && _overrides[track].Lyrics != null;
    public int CachedSongs => _provider.CachedCount();
    /// <summary>The players Windows knows about now, for Settings › General › Follow.</summary>
    public IReadOnlyList<string> PlayerIds => _media.AppIds;

    readonly DispatcherQueue _ui;
    readonly MediaSession _media;
    readonly LyricsProvider _provider;
    readonly Romanizer _romanizer;
    readonly TrackOverrides _overrides;
    readonly Settings _settings;
    readonly Playhead _playhead = new();
    readonly DispatcherQueueTimer _lineTimer;
    readonly DispatcherQueueTimer _safetyPoll;
    readonly DispatcherQueueTimer _artworkRetry;
    /// <summary>Reads a cover that came cut short again, 1.5 s on (see <see cref="LoadArtwork"/>).</summary>
    readonly DispatcherQueueTimer _artworkWait;
    /// <summary>The cover shown came whole.</summary>
    bool _artworkWhole;
    /// <summary>When the cover came cut short, waiting for it whole (<see cref="Environment.TickCount64"/>; 0 when not waiting).</summary>
    long _artworkCutSince;
    /// <summary>Everything the source returned, with pronunciation worked out.</summary>
    Lyrics? _allLyrics;
    CancellationTokenSource? _loading;
    int _artworkTries;
    /// <summary>The song's length was corrected after it started (and searched again with it).</summary>
    bool _lengthCorrected;
    bool _refreshing, _refreshAgain;
    /// <summary>Lyricod is quitting (<see cref="Stop"/>).</summary>
    bool _stopped;

    /// <summary>Seconds on a steady clock.</summary>
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public PlaybackModel(DispatcherQueue ui, MediaSession media, LyricsProvider provider, Romanizer romanizer,
                         TrackOverrides overrides, Settings settings)
    {
        _ui = ui;
        _media = media;
        _provider = provider;
        _romanizer = romanizer;
        _overrides = overrides;
        _settings = settings;
        _lineTimer = ui.CreateTimer();
        _lineTimer.IsRepeating = false;
        _lineTimer.Tick += (_, _) => { try { Tick(); } catch (Exception e) { LastError = "line: " + e.Message; } };
        // Players report changes as they happen; this only catches a missed report.
        _safetyPoll = ui.CreateTimer();
        _safetyPoll.Interval = TimeSpan.FromSeconds(2);
        _safetyPoll.Tick += async (_, _) =>
        {
            try { await Refresh(); }
            catch (Exception e) { LastError = "refresh: " + e.Message; }
        };
        // Some players hand over the cover long after the song starts, when the first tries are spent:
        // ask once more a while in, if there's still none.
        _artworkRetry = ui.CreateTimer();
        _artworkRetry.Interval = TimeSpan.FromSeconds(20);
        _artworkRetry.IsRepeating = false;
        _artworkRetry.Tick += (_, _) => { if ((Artwork == null || !_artworkWhole) && NowPlaying is { } n) LoadArtwork(n.TrackId); };
        _artworkWait = ui.CreateTimer();
        _artworkWait.Interval = TimeSpan.FromSeconds(1.5);
        _artworkWait.IsRepeating = false;
        _artworkWait.Tick += (_, _) => { if (NowPlaying is { } n) LoadArtwork(n.TrackId); };
        _media.Changed += OnMediaChanged;
        _media.DetailsChanged += OnMediaDetailsChanged;
        _settings.PropertyChanged += OnSettingChanged;
        _media.Choice = _settings.PlayerChoice;
        ApplyOffset();
    }

    public async Task StartAsync()
    {
        await _media.StartAsync();
        await Refresh();
    }

    /// <summary>
    /// For quitting: stops following the player and its timers, and raises <see cref="Changed"/>
    /// no more, so nothing runs against windows that have closed.
    /// </summary>
    public void Stop()
    {
        _stopped = true;
        Changed = null;
        _lineTimer.Stop();
        _safetyPoll.Stop();
        _artworkRetry.Stop();
        _artworkWait.Stop();
        _loading?.Cancel();
        _settings.PropertyChanged -= OnSettingChanged;
        _media.Changed -= OnMediaChanged;
        _media.DetailsChanged -= OnMediaDetailsChanged;
        _media.Dispose();
    }

    void Post(Action action) => _ui.TryEnqueue(() =>
    {
        try { if (!_stopped) action(); }
        catch (Exception e) { LastError = "display: " + e.Message; }
    });

    void OnMediaChanged() => Post(() => _ = Refresh());

    // A cover can arrive (or be corrected) well after the song starts: read it again each time.
    void OnMediaDetailsChanged() => Post(() => { if (NowPlaying is { } n) LoadArtwork(n.TrackId); });

    /// <summary>The lyric position (offset applied) now, or at a given steady-clock instant.</summary>
    public double Position(double? at = null) => _playhead.Position(at ?? Now);

    // ---- Following the player ----

    async Task Refresh()
    {
        // One read at a time; a change during a read triggers one more.
        if (_refreshing) { _refreshAgain = true; return; }
        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                Apply(await _media.ReadAsync());
            } while (_refreshAgain);
        }
        catch (Exception e) { LastError = "refresh: " + e.Message; }
        finally { _refreshing = false; }
    }

    void Apply(NowPlaying? now)
    {
        if (_stopped) return; // a read that finished after quitting
        var change = Change.None;
        if (now == null)
        {
            if (NowPlaying != null)
            {
                NowPlaying = null; Track = null; Lyrics = null; _allLyrics = null; CurrentIndex = null;
                Artwork = null; ArtworkColor = null; ArtworkSize = null; _artworkWhole = false; _artworkCutSince = 0;
                _loading?.Cancel();
                BestMatchScore = null;
                IsSearching = false; // the cancelled search won't say it's done
                SongOffset = 0; _playhead.Offset = _settings.Offset;
                _playhead.Anchor(0, Now, false);
                change = Change.Track | Change.Lyrics | Change.Line | Change.Clock | Change.Artwork;
            }
            _safetyPoll.Stop();
            _artworkRetry.Stop();
            _artworkWait.Stop();
            Finish(change);
            return;
        }

        bool trackChanged = now.TrackId != NowPlaying?.TrackId;
        var previous = NowPlaying;
        NowPlaying = now;
        double at = Now;
        if (now.HasTimeline)
        {
            // The report's position, carried to this instant.
            double position = now.Position + (now.IsPlaying ? (DateTimeOffset.Now - now.PositionAt).TotalSeconds : 0);
            if (_playhead.Report(position, at, now.IsPlaying, force: trackChanged)) change |= Change.Clock;
        }
        else if (trackChanged)
        {
            // No position from the player: time the song from now.
            _playhead.Anchor(0, at, now.IsPlaying);
            change |= Change.Clock;
        }
        else if (previous?.IsPlaying != now.IsPlaying)
        {
            _playhead.Anchor(_playhead.RawPosition(at), at, now.IsPlaying);
            change |= Change.Clock;
        }

        if (trackChanged)
        {
            Track = new Track { TrackId = now.TrackId, Title = now.Title, Artist = now.Artist, Album = now.Album, Duration = now.Duration };
            change |= Change.Track;
            ApplyOffset();
            LoadLyrics(Track, ignoreCache: false);
            Artwork = null; ArtworkColor = null; ArtworkSize = null; _artworkTries = 0;
            _artworkWhole = false; _artworkCutSince = 0;
            _artworkWait.Stop();
            _artworkRetry.Stop();
            _artworkRetry.Start();
            _lengthCorrected = false;
            change |= Change.Artwork;
        }
        else if (Track is { } current && !_lengthCorrected && now.Duration > 0 && Math.Abs(now.Duration - current.Duration) > 2)
        {
            // Some players (QQ Music) report a new song with the last one's length for a moment.
            // The length helps pick the right lyrics, so search again with the real one, once.
            _lengthCorrected = true;
            _loading?.Cancel();
            Track = _provider.CorrectDuration(current, now.Duration, _overrides);
            change |= Change.Track;
            ApplyOffset();
            LoadLyrics(Track, ignoreCache: false);
        }
        // Browsers hand over the cover a moment after the song: ask again on the next few reports.
        if (Artwork == null && _artworkTries < 4) LoadArtwork(now.TrackId);
        // Polled whenever a song is loaded: a missed report (a player starting after loading)
        // mustn't leave the clock stopped.
        _safetyPoll.Start();
        Finish(change);
    }

    void Finish(Change change)
    {
        if (change != Change.None) Changed?.Invoke(change);
        Tick();
    }

    // ---- Controls ----

    /// <summary>Moves the player to where <paramref name="line"/> starts.</summary>
    public void Seek(LyricLine line) => SeekPlayer(line.Start - _playhead.Offset);

    /// <summary>Moves the player to <paramref name="seconds"/> of its own time. Lyricod's clock moves
    /// there at once, as on the Mac: some players report the new position only a second later.</summary>
    public async void SeekPlayer(double seconds)
    {
        try
        {
            double target = Math.Clamp(seconds, 0, Duration > 0 ? Duration : double.MaxValue);
            if (!await _media.Seek(target) || _stopped) return;
            _playhead.Anchor(target, Now, _playhead.IsPlaying);
            Finish(Change.Clock);
            await Refresh();
        }
        catch (Exception) { /* the player went away */ }
    }

    public Task PlayPause() => _media.PlayPause();
    public Task Next() => _media.Next();
    public Task Previous() => _media.Previous();
}
