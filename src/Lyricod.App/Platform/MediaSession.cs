using Lyricod.Core;
using Windows.Media.Control;

namespace Lyricod.Platform;

/// <summary>What a player reports through Windows' media controls.</summary>
/// <param name="AppId">The player's app id (e.g. "Spotify.exe", "MSEdge").</param>
/// <param name="Position">Seconds, as of <paramref name="PositionAt"/>.</param>
/// <param name="HasTimeline">False when the player reports no length or position; Lyricod then times the song itself.</param>
/// <param name="CanSeek">The player lets other apps move it to another point (QQ Music doesn't).</param>
public sealed record NowPlaying(
    string AppId, string Title, string Artist, string Album, double Duration,
    double Position, DateTimeOffset PositionAt, bool IsPlaying, bool HasTimeline, bool CanSeek = false)
{
    /// <summary>Changes when the selected SMTC session changes, even within the same player.</summary>
    public long SessionId { get; init; }
    /// <summary>Identifies the song across reports.</summary>
    public string TrackId => $"{AppId}|{Title}|{Artist}|{Album}";
}

/// <summary>
/// Follows the player Windows considers current through the System Media Transport
/// Controls (SMTC): any app that shows in Windows' media flyout. Events arrive on
/// background threads; <see cref="Changed"/> says something changed, and
/// <see cref="ReadAsync"/> gets the latest.
/// </summary>
public sealed class MediaSession : IDisposable
{
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    readonly Lock _gate = new();
    long _sessionId;
    volatile bool _disposed;

    /// <summary>Raised on a background thread when the song, play state or position changed.</summary>
    public event Action? Changed;

    /// <summary>Raised on a background thread when the song's details (title, cover…) changed:
    /// players often hand over the cover a while after the song.</summary>
    public event Action? DetailsChanged;

    public async Task StartAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        if (_disposed) return;
        _manager = manager;
        manager.CurrentSessionChanged += OnSessionsChanged;
        manager.SessionsChanged += OnSessionsChanged;
        Pick();
    }

    /// <summary>"auto", or a player's app id to always follow (Settings › General › Follow).</summary>
    public string Choice
    {
        get => _choice;
        set
        {
            if (value == _choice) return;
            _choice = value;
            Pick();
        }
    }
    string _choice = "auto";

    /// <summary>The players Windows knows about now, by app id.</summary>
    public IReadOnlyList<string> AppIds
    {
        get
        {
            try { return _manager?.GetSessions().Select(s => s.SourceAppUserModelId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList() ?? []; }
            catch (Exception) { return []; }
        }
    }

    /// <summary>The session to follow (see <see cref="SessionPick.Choose"/>), or null.</summary>
    GlobalSystemMediaTransportControlsSession? Choose()
    {
        if (_manager is not { } manager) return null;
        static bool Playing(GlobalSystemMediaTransportControlsSession s) => Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        return SessionPick.Choose(manager.GetSessions(), manager.GetCurrentSession(), _choice, s => s.SourceAppUserModelId, Playing, HasTimeline);
    }

    /// <summary>
    /// Follows the chosen player when it's open. Otherwise one that's playing (several players can
    /// be open), preferring the one Windows shows in its media controls, else the one Windows
    /// calls current; of one app's sessions, the one that reports the song's position.
    /// </summary>
    void Pick()
    {
        if (_manager == null || _disposed) return;
        GlobalSystemMediaTransportControlsSession? chosen = null;
        try
        {
            var sessions = _manager.GetSessions();
            chosen = Choose();
            foreach (var s in sessions)
            {
                // Re-pick when any player starts or stops, so the one playing is followed.
                s.PlaybackInfoChanged -= OnAnyPlaybackInfo;
                s.PlaybackInfoChanged += OnAnyPlaybackInfo;
            }
        }
        catch (Exception) { /* the session list can change underneath us */ }

        lock (_gate)
        {
            if (_disposed) return;
            if (!ReferenceEquals(chosen, _session))
            {
                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnDetailsChanged;
                    _session.TimelinePropertiesChanged -= OnSessionChanged;
                }
                _session = chosen;
                _sessionId++;
                if (_session != null)
                {
                    _session.MediaPropertiesChanged += OnDetailsChanged;
                    _session.TimelinePropertiesChanged += OnSessionChanged;
                }
            }
        }
        Changed?.Invoke();
    }

    void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args) => Pick();
    void OnAnyPlaybackInfo(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Pick();
    void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => Changed?.Invoke();

    void OnDetailsChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _session) || _disposed) return;
        }
        DetailsChanged?.Invoke();
        Changed?.Invoke();
    }

    static GlobalSystemMediaTransportControlsSessionPlaybackStatus? Status(GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.GetPlaybackInfo()?.PlaybackStatus; } catch (Exception) { return null; }
    }

    static bool HasTimeline(GlobalSystemMediaTransportControlsSession s)
    {
        try { var t = s.GetTimelineProperties(); return t.EndTime > t.StartTime; } catch (Exception) { return false; }
    }

    /// <summary>The followed player's song and position, or null when nothing is loaded.</summary>
    public async Task<NowPlaying?> ReadAsync()
    {
        GlobalSystemMediaTransportControlsSession? s;
        long sessionId;
        lock (_gate) { s = _session; sessionId = _sessionId; }
        if (s == null) return null;
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            if (props == null || string.IsNullOrWhiteSpace(props.Title)) return null;
            var info = s.GetPlaybackInfo();
            var timeline = s.GetTimelineProperties();
            double duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
            bool hasTimeline = duration > 0;
            // The app's other session may tell the position (it can appear after this one): switch to it.
            // Pick raises Changed, so the next read comes from that one.
            if (!hasTimeline)
            {
                try { if (Choose() is { } better && !ReferenceEquals(better, s)) Pick(); }
                catch (Exception) { /* the session list can change underneath us */ }
            }
            // Keep the raw timestamp, including empty/invalid values. MediaClock recognises
            // repeated snapshots before deciding whether a timestamp can be extrapolated.
            var at = timeline.LastUpdatedTime;
            return new NowPlaying(
                s.SourceAppUserModelId ?? "", props.Title.Trim(), props.Artist?.Trim() ?? "", props.AlbumTitle?.Trim() ?? "",
                hasTimeline ? duration : 0, (timeline.Position - timeline.StartTime).TotalSeconds, at,
                info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, hasTimeline,
                info?.Controls?.IsPlaybackPositionEnabled == true) { SessionId = sessionId };
        }
        catch (Exception) { return null; } // the player quit while being read
    }

    GlobalSystemMediaTransportControlsSession? Current { get { lock (_gate) return _session; } }
    public long SessionId { get { lock (_gate) return _sessionId; } }

    /// <summary>The followed player's cover art (encoded image bytes), or null.</summary>
    public async Task<byte[]?> ReadArtworkAsync()
    {
        if (Current is not { } s) return null;
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var best = await ReadThumbnail(props);
            // An app that shows up twice (NetEase with its progress option) can hand over a small cover
            // in one session and the full one in the other: take the biggest for this song.
            if (_manager is { } manager && props != null)
                foreach (var other in manager.GetSessions())
                {
                    if (ReferenceEquals(other, s) || other.SourceAppUserModelId != s.SourceAppUserModelId) continue;
                    var otherProps = await other.TryGetMediaPropertiesAsync();
                    if (otherProps?.Title != props.Title) continue;
                    if (await ReadThumbnail(otherProps) is { } bytes && bytes.Length > (best?.Length ?? 0)) best = bytes;
                }
            return best;
        }
        catch (Exception) { return null; } // no art, or the player went away
    }

    static async Task<byte[]?> ReadThumbnail(GlobalSystemMediaTransportControlsSessionMediaProperties? props)
    {
        try
        {
            if (props?.Thumbnail is not { } thumb) return null;
            using var stream = await thumb.OpenReadAsync();
            if (stream.Size is 0 or > 20_000_000) return null;
            var bytes = new byte[stream.Size];
            using var reader = new Windows.Storage.Streams.DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception) { return null; }
    }

    /// <summary>A readable name for a player's app id ("MSEdge" → "Microsoft Edge").</summary>
    public static string DisplayName(string appId)
    {
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                && Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(appId)?.DisplayInfo?.DisplayName is { Length: > 0 } name)
                return name;
        }
        catch (Exception) { /* desktop apps often have no app info */ }
        return appId switch
        {
            "MSEdge" => "Microsoft Edge",
            "Chrome" => "Google Chrome",
            _ => Path.GetFileNameWithoutExtension(appId.Split('!')[^1]),
        };
    }

    // Pick can switch the followed session before its UI notification is delivered.
    GlobalSystemMediaTransportControlsSession? CommandTarget(NowPlaying? displayed)
    {
        lock (_gate)
            return !_disposed && (displayed == null || displayed.SessionId == _sessionId)
                ? _session : null;
    }

    public Task<bool> PlayPause() => PlayPause(null);

    internal async Task<bool> PlayPause(NowPlaying? displayed)
    {
        if (CommandTarget(displayed) is not { } s) return false;
        try { return await s.TryTogglePlayPauseAsync(); }
        catch (Exception) { return false; } // the player went away or rejected the request
    }
    public async Task Next() { if (Current is { } s) await s.TrySkipNextAsync(); }
    public async Task Previous() { if (Current is { } s) await s.TrySkipPreviousAsync(); }

    /// <summary>Moves the player to <paramref name="seconds"/>, if it allows seeking.</summary>
    public Task<bool> Seek(double seconds) => Seek(seconds, null);

    internal async Task<bool> Seek(double seconds, NowPlaying? displayed) =>
        CommandTarget(displayed) is { } s &&
            await s.TryChangePlaybackPositionAsync((long)(Math.Max(seconds, 0) * TimeSpan.TicksPerSecond));

    /// <summary>Stops listening to Windows' media controls; no more <see cref="Changed"/>.</summary>
    public void Dispose()
    {
        _disposed = true;
        Changed = null;
        DetailsChanged = null;
        try
        {
            if (_manager is { } manager)
            {
                manager.CurrentSessionChanged -= OnSessionsChanged;
                manager.SessionsChanged -= OnSessionsChanged;
                foreach (var s in manager.GetSessions()) s.PlaybackInfoChanged -= OnAnyPlaybackInfo;
            }
        }
        catch (Exception) { /* Windows' media service or a player went away */ }
        lock (_gate)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnDetailsChanged;
                _session.TimelinePropertiesChanged -= OnSessionChanged;
            }
            _session = null;
        }
    }
}
