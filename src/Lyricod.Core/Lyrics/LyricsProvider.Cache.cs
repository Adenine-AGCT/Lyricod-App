using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lyricod.Core;

public sealed partial class LyricsProvider
{
    /// <summary>Keeps the lyrics cache to the 1500 most recently saved songs (~60 KB each).</summary>
    static void Trim(string dir)
    {
        var files = new DirectoryInfo(dir).GetFiles("*.json");
        if (files.Length <= 1500) return;
        foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc).Skip(1500))
            try { f.Delete(); } catch (IOException) { }
    }

    /// <summary>Stable, filename-safe key for a song (the macOS app's FNV-1a key, so files match).</summary>
    public static string CacheKey(Track t)
    {
        var s = $"{t.Title}|{t.Artist}|{(long)t.Duration}";
        ulong h = 0xcbf29ce484222325;
        foreach (var b in Encoding.UTF8.GetBytes(s)) h = (h ^ b) * 0x100000001b3;
        return h.ToString("x", CultureInfo.InvariantCulture);
    }

    string CacheFile(string name) => Path.Combine(_cacheDir, name + ".json");

    RawLyrics? ReadCache(string file)
    {
        try { return JsonSerializer.Deserialize<RawLyrics>(File.ReadAllText(file)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    void WriteCache(string file, RawLyrics raw)
    {
        try { File.WriteAllText(file, JsonSerializer.Serialize(raw, CacheJson)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void ClearAll()
    {
        foreach (var f in Directory.GetFiles(_cacheDir)) try { File.Delete(f); } catch (IOException) { }
    }

    public int CachedCount() => Directory.GetFiles(_cacheDir, "*.json").Length;

    public void ClearCache(Track track)
    {
        try { File.Delete(CacheFile(CacheKey(track))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Keep manual choices when the same playing song reports its real length.</summary>
    public bool TryCorrectDuration(Track track, double duration, TrackOverrides overrides, out Track corrected)
    {
        corrected = track;
        var updated = track with { Duration = duration };
        if (!overrides.TryGet(track, out var choice) || !overrides.TryMove(track, updated)) return false;
        var pinned = choice.Lyrics;
        corrected = updated;
        var oldFile = CacheFile(CacheKey(track));
        var newFile = CacheFile(CacheKey(corrected));
        // Only manual lyrics belong to the corrected recording without another search.
        if (oldFile != newFile && pinned != null && ReadCache(oldFile)?.SongRef == pinned)
        {
            try
            {
                if (!File.Exists(newFile) && overrides[corrected].Lyrics == pinned) File.Move(oldFile, newFile);
                else File.Delete(oldFile);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return true;
    }
}
