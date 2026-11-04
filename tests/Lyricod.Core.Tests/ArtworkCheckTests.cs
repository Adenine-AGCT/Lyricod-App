using Lyricod.Core;

namespace Lyricod.Core.Tests;

public class ArtworkCheckTests
{
    static byte[] Segment(byte marker, params byte[] payload) =>
        [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];

    // Coded data with a stuffed 0xFF and a restart marker, as a real scan has.
    static readonly byte[] Coded = [0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56, 0x78, 0x9A];

    static byte[] Jpeg(bool progressive = false, byte[]? app1 = null) =>
    [
        0xFF, 0xD8,
        .. Segment(0xE0, "JFIF\0"u8.ToArray()),
        .. app1 ?? [],
        .. Segment(0xDB, new byte[65]),
        .. Segment(progressive ? (byte)0xC2 : (byte)0xC0, new byte[15]),
        .. Segment(0xC4, new byte[20]),
        .. Segment(0xDA, new byte[10]), .. Coded,
        .. progressive ? [.. Segment(0xC4, new byte[20]), .. Segment(0xDA, new byte[10]), .. Coded] : Array.Empty<byte>(),
        0xFF, 0xD9,
    ];

    static byte[] Chunk(string type, int length) =>
        [(byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length, .. System.Text.Encoding.ASCII.GetBytes(type), .. new byte[length], 1, 2, 3, 4];

    static byte[] Png() => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", 13), .. Chunk("IDAT", 40), .. Chunk("IEND", 0)];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeJpegIsWholeAndEveryCutIsNot(bool progressive)
    {
        var jpeg = Jpeg(progressive);
        Assert.True(ArtworkCheck.IsWhole(jpeg));
        for (int length = 3; length < jpeg.Length; length++)
            Assert.False(ArtworkCheck.IsWhole(jpeg.AsSpan(0, length)), $"cut at {length} of {jpeg.Length}");
    }

    [Fact]
    public void JpegCutShortThenFilledWithZerosIsNotWhole()
    {
        // A cover file set to its full size before the download has filled it.
        var jpeg = Jpeg();
        for (int length = 3; length < jpeg.Length - 2; length++)
        {
            var filled = new byte[jpeg.Length + 100];
            jpeg.AsSpan(0, length).CopyTo(filled);
            Assert.False(ArtworkCheck.IsWhole(filled), $"cut at {length}");
        }
    }

    [Fact]
    public void JpegWithPaddingAfterItsEndIsWhole()
    {
        Assert.True(ArtworkCheck.IsWhole([.. Jpeg(), .. new byte[64]]));
        Assert.True(ArtworkCheck.IsWhole([.. Jpeg(), 0xFF, 0xFF, 0x12]));
    }

    [Fact]
    public void JpegCutAfterAnEmbeddedPreviewIsNotWhole()
    {
        // An Exif preview has its own start and end markers; only the main image's end counts.
        byte[] preview = [.. "Exif\0\0"u8.ToArray(), 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0x11, 0xFF, 0xD9];
        var jpeg = Jpeg(app1: Segment(0xE1, preview));
        Assert.True(ArtworkCheck.IsWhole(jpeg));
        int afterPreview = 2 + Segment(0xE0, "JFIF\0"u8.ToArray()).Length + Segment(0xE1, preview).Length;
        Assert.False(ArtworkCheck.IsWhole(jpeg.AsSpan(0, afterPreview + 10)));
    }

    [Fact]
    public void WholePngIsWholeAndEveryCutIsNot()
    {
        var png = Png();
        Assert.True(ArtworkCheck.IsWhole(png));
        for (int length = 8; length < png.Length; length++)
            Assert.False(ArtworkCheck.IsWhole(png.AsSpan(0, length)), $"cut at {length}");
        var filled = new byte[png.Length];
        png.AsSpan(0, 40).CopyTo(filled);
        Assert.False(ArtworkCheck.IsWhole(filled));
    }

    [Fact]
    public void OtherFormatsAreCheckedByTheirEnds()
    {
        Assert.True(ArtworkCheck.IsWhole([.. "GIF89a"u8.ToArray(), 1, 2, 3, 0x3B]));
        Assert.True(ArtworkCheck.IsWhole([.. "GIF89a"u8.ToArray(), 1, 2, 3, 0x3B, 0, 0]));
        Assert.False(ArtworkCheck.IsWhole([.. "GIF89a"u8.ToArray(), 1, 2, 3]));

        byte[] webp = [.. "RIFF"u8.ToArray(), 12, 0, 0, 0, .. "WEBP"u8.ToArray(), 1, 2, 3, 4, 5, 6, 7, 8];
        Assert.True(ArtworkCheck.IsWhole(webp));
        Assert.False(ArtworkCheck.IsWhole(webp.AsSpan(0, 16)));

        byte[] bmp = [(byte)'B', (byte)'M', 10, 0, 0, 0, 0, 0, 0, 0];
        Assert.True(ArtworkCheck.IsWhole(bmp));
        Assert.False(ArtworkCheck.IsWhole(bmp.AsSpan(0, 8)));
    }

    [Fact]
    public void UnknownImagesCountAsWhole()
    {
        Assert.True(ArtworkCheck.IsWhole([1, 2, 3, 4]));
        Assert.True(ArtworkCheck.IsWhole([]));
    }

    [Fact]
    public void JpegSegmentWithAnImpossibleLengthIsNotWhole()
    {
        // Zeros where a segment's length should be: a file not yet filled in.
        Assert.False(ArtworkCheck.IsWhole([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0x00, 0xFF, 0xD9]));
    }
}
