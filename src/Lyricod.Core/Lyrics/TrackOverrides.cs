using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lyricod.Core;

/// <summary>Per-song choices the user made: hand-picked lyrics and a timing offset.</summary>
public sealed class TrackOverride
{
    [JsonPropertyName("pinned")] public LyricsRef? Pinned { get; set; }
    /// <summary>Older macOS files stored only a NetEase id.</summary>
    [JsonPropertyName("neteaseID")] public long? NeteaseId { get; set; }
    [JsonPropertyName("offset")] public double? Offset { get; set; }

    /// <summary>Lyrics the user chose for this song.</summary>
    [JsonIgnore]
    public LyricsRef? Lyrics
    {
        get => Pinned ?? (NeteaseId is { } id ? LyricsRef.Netease(id) : null);
        set { Pinned = value; NeteaseId = null; }
    }

    [JsonIgnore] public bool IsEmpty => Lyrics == null && (Offset ?? 0) == 0;

    public void NudgeOffset(double delta, bool clamp)
    {
        double next = (Offset ?? 0) + delta;
        Offset = Math.Round(clamp ? Math.Clamp(next, -10, 10) : next, 2);
    }

    internal TrackOverride Copy() => new() { Pinned = Pinned, NeteaseId = NeteaseId, Offset = Offset };
}

/// <summary>
/// The user's per-song choices, kept outside the lyrics cache so clearing the cache keeps them.
/// Not thread-safe: use from the UI thread. Reads return copies; writes commit only after saving.
/// </summary>
public sealed class TrackOverrides
{
    readonly string _file;
    Dictionary<string, TrackOverride> _map = [];
    bool _loaded;

    public TrackOverrides(string file)
    {
        _file = file;
        TryLoad();
    }

    bool TryLoad()
    {
        if (_loaded) return true;
        try { _map = JsonSerializer.Deserialize<Dictionary<string, TrackOverride>>(File.ReadAllText(_file)) ?? []; }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or JsonException) { _map = []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        _loaded = true;
        return true;
    }

    /// <summary>A cached choice for display. Use TryGet before editing it.</summary>
    public TrackOverride this[Track track] =>
        _map.TryGetValue(LyricsProvider.CacheKey(track), out var choice) && choice != null ? choice.Copy() : new();

    /// <summary>Read the existing choice before editing, retrying an inaccessible file on demand.</summary>
    public bool TryGet(Track track, out TrackOverride choice)
    {
        bool loaded = TryLoad();
        choice = this[track];
        return loaded;
    }

    /// <summary>Save a choice, leaving both the previous file and in-memory state unchanged on an I/O failure.</summary>
    public bool TrySet(Track track, TrackOverride value)
    {
        if (!TryLoad()) return false;
        var next = new Dictionary<string, TrackOverride>(_map);
        var key = LyricsProvider.CacheKey(track);
        if (value.IsEmpty) next.Remove(key); else next[key] = value.Copy();
        return TrySave(next);
    }

    /// <summary>Fill missing choices at the corrected key, then forget the old key only if saving succeeds.</summary>
    public bool TryMove(Track from, Track to)
    {
        if (!TryLoad()) return false;
        var oldKey = LyricsProvider.CacheKey(from);
        var newKey = LyricsProvider.CacheKey(to);
        if (oldKey == newKey || !_map.TryGetValue(oldKey, out var old) || old == null) return true;
        var choice = _map.GetValueOrDefault(newKey)?.Copy() ?? new TrackOverride();
        if (choice.Lyrics == null) choice.Lyrics = old.Lyrics;
        choice.Offset ??= old.Offset;
        var next = new Dictionary<string, TrackOverride>(_map);
        next.Remove(oldKey);
        if (!choice.IsEmpty) next[newKey] = choice;
        return TrySave(next);
    }

    bool TrySave(Dictionary<string, TrackOverride> next)
    {
        var tmp = _file + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
            File.WriteAllText(tmp, JsonSerializer.Serialize(next));
            File.Move(tmp, _file, overwrite: true);
            _map = next;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { File.Delete(tmp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
