namespace Lyricod;

public sealed partial class PlaybackModel
{
    /// <summary>How long a cover that came cut short is read again for, every <see cref="_artworkWait"/>.</summary>
    const int ArtworkWaitSeconds = 10;

    /// <summary>
    /// Reads the player's cover for <paramref name="trackId"/>. A player can hand over a cover it's still
    /// downloading: decoded, the rest shows grey, and nothing would read it again. So one that's cut short
    /// is shown, read again shortly for a while (and once more with the late retry), and replaced as soon
    /// as it comes whole. A cut-short cover never replaces a whole one.
    /// </summary>
    async void LoadArtwork(string trackId)
    {
        try
        {
            _artworkTries++;
            var bytes = await _media.ReadArtworkAsync();
            if (bytes == null || NowPlaying?.TrackId != trackId) return;
            if (Artwork != null && bytes.AsSpan().SequenceEqual(Artwork))
            {
                if (!_artworkWhole) ReadArtworkAgainSoon(); // the same cover again
                return;
            }
            bool whole = Lyricod.Core.ArtworkCheck.IsWhole(bytes);
            if (!whole)
            {
                if (Artwork != null && _artworkWhole) return;
                ReadArtworkAgainSoon();
            }
            Windows.UI.Color? colour = null;
            (int, int)? size = null;
            try { (colour, size) = await Inspect(bytes); } catch (Exception) { /* not an image we can read */ }
            if (NowPlaying?.TrackId != trackId) return;
            if (!whole && Artwork != null && _artworkWhole) return; // a whole one came in meanwhile
            Artwork = bytes;
            ArtworkColor = colour;
            ArtworkSize = size;
            _artworkWhole = whole;
            if (whole)
            {
                _artworkCutSince = 0;
                _artworkWait.Stop();
            }
            Changed?.Invoke(Change.Artwork);
        }
        catch (Exception e) { LastError = "artwork: " + e.Message; }
    }

    /// <summary>For a cover that came cut short: reads it again in a moment, until <see cref="ArtworkWaitSeconds"/> have passed.</summary>
    void ReadArtworkAgainSoon()
    {
        long now = Environment.TickCount64;
        if (_artworkCutSince == 0) _artworkCutSince = now;
        if (now - _artworkCutSince >= ArtworkWaitSeconds * 1000L) return;
        _artworkWait.Stop();
        _artworkWait.Start();
    }

    /// <summary>The cover's main colour and its size in pixels.</summary>
    static async Task<(Windows.UI.Color? Colour, (int, int) Size)> Inspect(byte[] bytes)
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            new Windows.Graphics.Imaging.BitmapTransform { ScaledWidth = 32, ScaledHeight = 32 },
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        var size = ((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight);
        return (Lyricod.Core.ArtworkColor.Dominant(pixels.DetachPixelData()) is { } c ? Windows.UI.Color.FromArgb(255, c.R, c.G, c.B) : null, size);
    }
}
