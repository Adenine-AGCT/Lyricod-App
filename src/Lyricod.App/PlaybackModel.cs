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
    public enum Change { None = 0, Track = 1, Lyrics = 2, Line = 4, Clock = 8, Artwork = 16, Diagnostics = 32, Progress = 64, Session = 128 }

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
    /// <summary>A recoverable per-song save failure, shown by the settings, chooser and tray.</summary>
    public string? SongSaveError { get; private set; }
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
    readonly MediaClock _mediaClock;
    readonly DispatcherQueueTimer _lineTimer;
    readonly DispatcherQueueTimer _safetyPoll;
    readonly DispatcherQueueTimer _artworkRetry;
    /// <summary>Everything the source returned, with pronunciation worked out.</summary>
    Lyrics? _allLyrics;
    CancellationTokenSource? _loading;
    int _artworkTries;
    /// <summary>The song's length correction was already attempted automatically.</summary>
    bool _lengthCorrected;
    /// <summary>A failed correction can be retried by a manual save, without a polling write loop.</summary>
    (Track Track, double Duration)? _pendingDurationCorrection;
    bool _refreshing, _refreshAgain;
    int _seekRequest;
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
        _mediaClock = new MediaClock(_playhead);
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
        _artworkRetry.Tick += (_, _) => { if (Artwork == null && NowPlaying is { } n) LoadArtwork(n.TrackId); };
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
                long revision = _media.Revision;
                var report = await _media.ReadAsync();
                if (revision != _media.Revision) { _refreshAgain = true; continue; }
                Apply(report);
            } while (_refreshAgain && !_stopped);
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
                Artwork = null; ArtworkColor = null; ArtworkSize = null;
                _loading?.Cancel();
                BestMatchScore = null;
                IsSearching = false; // the cancelled search won't say it's done
                SongSaveError = null;
                _pendingDurationCorrection = null;
                SongOffset = 0; _playhead.Offset = _settings.Offset;
                _playhead.Anchor(0, Now, false);
                _mediaClock.Reset();
                change = Change.Track | Change.Lyrics | Change.Line | Change.Clock | Change.Artwork;
            }
            _safetyPoll.Stop();
            _artworkRetry.Stop();
            Finish(change);
            return;
        }

        if (now.SessionId != NowPlaying?.SessionId) change |= Change.Session;
        if (now.Duration != NowPlaying?.Duration || now.CanSeek != NowPlaying?.CanSeek) change |= Change.Progress;
        NowPlaying = now;
        double at = Now;
        if (_mediaClock.Apply(new MediaClockReport(now.SessionId, now.Revision, now.TrackId, now.Position, now.PositionAt,
                now.IsPlaying, now.HasTimeline), DateTimeOffset.UtcNow, at, out bool trackChanged)) change |= Change.Clock;

        if (trackChanged)
        {
            SongSaveError = null;
            _pendingDurationCorrection = null;
            Track = new Track { TrackId = now.TrackId, Title = now.Title, Artist = now.Artist, Album = now.Album, Duration = now.Duration };
            change |= Change.Track;
            ApplyOffset();
            LoadLyrics(Track, ignoreCache: false);
            Artwork = null; ArtworkColor = null; ArtworkSize = null; _artworkTries = 0;
            _artworkRetry.Stop();
            _artworkRetry.Start();
            _lengthCorrected = false;
            change |= Change.Artwork;
        }
        else if (Track is { } current && !_lengthCorrected && now.Duration > 0 && Math.Abs(now.Duration - current.Duration) > 2)
        {
            // Some players (QQ Music) report a new song with the last one's length for a moment.
            // The length helps pick the right lyrics, so search again with the real one, once.
            _lengthCorrected = true; // one automatic attempt per song, including an I/O failure
            _pendingDurationCorrection = (current, now.Duration);
            if (TryApplyPendingDuration()) SetSongSaveError(null);
        }
        // Browsers hand over the cover a moment after the song: ask again on the next few reports.
        if (Artwork == null && _artworkTries < 4) LoadArtwork(now.TrackId);
        // Polled whenever a song is loaded: a missed report (a player starting after loading)
        // mustn't leave the clock stopped.
        _safetyPoll.Start();
        Finish(change);
    }

    /// <summary>Manual saves must use the corrected key and read its merged choices afresh.</summary>
    bool TryApplyPendingDuration()
    {
        if (_pendingDurationCorrection is not { } pending) return true;
        if (!ReferenceEquals(Track, pending.Track))
        {
            _pendingDurationCorrection = null;
            return true; // never apply an old song's correction to the current song
        }
        if (!_provider.TryCorrectDuration(pending.Track, pending.Duration, _overrides, out var corrected))
        {
            SetSongSaveError("SongSettingsMoveFailed");
            return false;
        }
        _pendingDurationCorrection = null;
        _loading?.Cancel();
        Track = corrected;
        // The migration has committed even if a following manual edit cannot be saved.
        ApplyOffset();
        LoadLyrics(corrected, ignoreCache: false);
        Changed?.Invoke(Change.Track);
        return true;
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
        MediaSeekRequest? seek = null;
        try
        {
            double target = Math.Clamp(seconds, 0, Duration > 0 ? Duration : double.MaxValue);
            if (NowPlaying is not { } source) return;
            int request = ++_seekRequest;
            seek = _mediaClock.BeginSeek(target, DateTimeOffset.UtcNow, Now);
            if (!await _media.Seek(target, source) || _stopped || request != _seekRequest || source.Revision != _media.Revision ||
                    source.SessionId != NowPlaying?.SessionId || source.TrackId != NowPlaying?.TrackId) return;
            _mediaClock.Seek(seek, Now);
            Finish(Change.Clock);
            await Refresh();
        }
        catch (Exception) { /* the player went away */ }
        finally { if (seek != null) _mediaClock.CancelSeek(seek); }
    }

    public async Task PlayPause()
    {
        if (await _media.PlayPause(NowPlaying) && !_stopped) await Refresh();
    }
    public Task Next() => _media.Next();
    public Task Previous() => _media.Previous();
}
