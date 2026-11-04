using System.Buffers.Binary;

namespace Lyricod.Core;

/// <summary>
/// Whether a cover's encoded image arrived whole. A player can hand over a cover it's still
/// downloading: decoded, the missing part shows as grey, and nothing else says it was cut short.
/// Only whether the image reaches its end is checked, by walking its structure; a format it doesn't
/// know counts as whole. A cover that never reads as whole is still shown in the end (see
/// <c>PlaybackModel.LoadArtwork</c>), so a false "cut short" only delays it.
/// </summary>
public static class ArtworkCheck
{
    static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsWhole(ReadOnlySpan<byte> image)
    {
        if (image.Length >= 3 && image[0] == 0xFF && image[1] == 0xD8 && image[2] == 0xFF) return JpegWhole(image);
        if (image.StartsWith(PngSignature)) return PngWhole(image);
        if (image.StartsWith("GIF8"u8)) return image.TrimEnd((byte)0) is { Length: > 0 } gif && gif[^1] == 0x3B; // the trailer
        if (image.Length >= 12 && image.StartsWith("RIFF"u8) && image[8..12].SequenceEqual("WEBP"u8))
            return 8L + BinaryPrimitives.ReadUInt32LittleEndian(image[4..8]) <= image.Length;
        if (image.Length >= 6 && image.StartsWith("BM"u8)) return BinaryPrimitives.ReadUInt32LittleEndian(image[2..6]) <= image.Length;
        return true;
    }

    /// <summary>Marker by marker, through each scan's coded data, to the end-of-image marker.</summary>
    static bool JpegWhole(ReadOnlySpan<byte> jpeg)
    {
        int i = 2; // past the start-of-image marker
        while (true)
        {
            // Decoders skip stray bytes before a marker, and a marker may be padded with extra 0xFFs.
            int ff = jpeg[i..].IndexOf((byte)0xFF);
            if (ff < 0) return false;
            i += ff;
            while (i < jpeg.Length && jpeg[i] == 0xFF) i++;
            if (i >= jpeg.Length) return false;
            byte marker = jpeg[i++];
            if (marker == 0xD9) return true; // end of image
            if (marker is 0x00 or 0x01 or >= 0xD0 and <= 0xD7) continue; // no length follows
            if (i + 2 > jpeg.Length) return false;
            int length = (jpeg[i] << 8) | jpeg[i + 1];
            if (length < 2) return false; // impossible: what a file not yet filled in (zeros) reads as
            i += length;
            if (i > jpeg.Length) return false;
            if (marker != 0xDA) continue;
            // A scan: coded data up to the next marker. In it, 0xFF is followed by 0x00 (a stuffed byte) or a restart marker.
            while (true)
            {
                ff = jpeg[i..].IndexOf((byte)0xFF);
                if (ff < 0 || i + ff + 1 >= jpeg.Length) return false;
                i += ff;
                byte next = jpeg[i + 1];
                if (next is 0x00 or >= 0xD0 and <= 0xD7) i += 2;
                else if (next == 0xFF) i++;
                else break;
            }
        }
    }

    /// <summary>Chunk by chunk, to a complete end chunk.</summary>
    static bool PngWhole(ReadOnlySpan<byte> png)
    {
        long i = PngSignature.Length;
        while (i + 8 <= png.Length)
        {
            long length = BinaryPrimitives.ReadUInt32BigEndian(png.Slice((int)i, 4));
            bool end = png.Slice((int)i + 4, 4).SequenceEqual("IEND"u8);
            i += 12 + length; // length, type, data, checksum
            if (i > png.Length) return false;
            if (end) return true;
        }
        return false;
    }
}
