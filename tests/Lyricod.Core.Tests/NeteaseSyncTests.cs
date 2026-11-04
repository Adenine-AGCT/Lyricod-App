using Lyricod.Core;

namespace Lyricod.Core.Tests;

public class NeteaseSyncTests
{
    const string Hash = "a7c77af418d7940e63faa58ea036fba1f4baad497947109ea52ed78c8e86608f";

    static string Release(string name = NeteaseSync.HelperName, long size = 3_000_000,
        string url = "https://github.com/darkdust-c/Lyricod-CM-Helper/releases/download/v1.0.0/Lyricod-CM-Helper.exe",
        string? digest = "sha256:" + Hash) =>
        $$"""
        {"tag_name":"v1.0.0","assets":[
          {"name":"{{name}}","size":{{size}},"browser_download_url":"{{url}}"{{(digest == null ? "" : $",\"digest\":\"{digest}\"")}}}
        ]}
        """;

    [Fact]
    public void FindsTheHelperWithItsChecksum()
    {
        var helper = NeteaseSync.FindHelper(Release());
        Assert.NotNull(helper);
        Assert.Equal(3_000_000, helper.Size);
        Assert.Equal(Hash, helper.Sha256);
        Assert.Equal("https://github.com/darkdust-c/Lyricod-CM-Helper/releases/download/v1.0.0/Lyricod-CM-Helper.exe", helper.Url.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("md5:0123")]
    [InlineData("sha256:not-hex")]
    public void WithoutAChecksumTheHelperIsntUsed(string? digest) => Assert.Null(NeteaseSync.FindHelper(Release(digest: digest)));

    [Theory]
    [InlineData("http://github.com/darkdust-c/Lyricod-CM-Helper/releases/download/v1.0.0/Lyricod-CM-Helper.exe")]
    [InlineData("https://example.com/darkdust-c/Lyricod-CM-Helper/releases/download/v1.0.0/Lyricod-CM-Helper.exe")]
    [InlineData("https://github.com/someone/else/releases/download/v1.0.0/Lyricod-CM-Helper.exe")]
    [InlineData("https://user@github.com/darkdust-c/Lyricod-CM-Helper/releases/download/v1.0.0/Lyricod-CM-Helper.exe")]
    public void OnlyFromTheHelpersOwnReleases(string url) => Assert.Null(NeteaseSync.FindHelper(Release(url: url)));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"message\":\"Not Found\"}")]
    public void OddRepliesFindNothing(string json) => Assert.Null(NeteaseSync.FindHelper(json));

    [Fact]
    public void AnEmptyOrOtherFileFindsNothing()
    {
        Assert.Null(NeteaseSync.FindHelper(Release(size: 0)));
        Assert.Null(NeteaseSync.FindHelper(Release(name: "other.exe")));
    }

    [Theory]
    [InlineData(3, 1, 20, true)]
    [InlineData(3, 1, 42, true)]
    [InlineData(3, 1, 30, true)]
    [InlineData(3, 1, 19, false)]
    [InlineData(3, 1, 43, false)]
    [InlineData(2, 10, 13, false)]
    public void KnowsTheTestedVersions(int major, int minor, int build, bool tested) =>
        Assert.Equal(tested, NeteaseSync.IsTested(new Version(major, minor, build, 204712)));

    [Fact]
    public void QuotesThePaths()
    {
        Assert.Equal(
            "install --netease \"C:\\Program Files\\NetEase\\CloudMusic\" --data \"C:\\betterncm\" --result \"C:\\Temp\\r.json\"",
            NeteaseSync.Arguments(false, @"C:\Program Files\NetEase\CloudMusic", @"C:\betterncm", @"C:\Temp\r.json"));
        // A trailing backslash would escape the closing quote: it's doubled.
        Assert.Equal("remove --netease \"D:\\CloudMusic\\\\\" --data \"D:\\\\\" --result \"C:\\r.json\"",
            NeteaseSync.Arguments(true, @"D:\CloudMusic\", @"D:\", @"C:\r.json"));
        Assert.Throws<ArgumentException>(() => NeteaseSync.Arguments(false, "C:\\a\"b", @"C:\betterncm", @"C:\r.json"));
    }

    [Fact]
    public void ReadsWhatTheHelperWrote()
    {
        Assert.Equal(new NeteaseHelperOutcome(true, null), NeteaseSync.ReadResult("{\"ok\":true}", 0));
        Assert.Equal(new NeteaseHelperOutcome(false, "Couldn't close NetEase Cloud Music."),
            NeteaseSync.ReadResult("{\"ok\":false,\"error\":\"Couldn't close NetEase Cloud Music.\"}", 1));
        Assert.Equal(new NeteaseHelperOutcome(false, "The helper stopped (code 5)."), NeteaseSync.ReadResult(null, 5));
        Assert.Equal(new NeteaseHelperOutcome(false, "The helper stopped (code 1)."), NeteaseSync.ReadResult("garbage", 1));
        // Says ok but the exit code disagrees: not done.
        Assert.False(NeteaseSync.ReadResult("{\"ok\":true}", 1).Ok);
    }

    static string DataFolder() => Directory.CreateTempSubdirectory("lyricod-betterncm-").FullName;

    static void Packed(string data, string file, string? manifest)
    {
        Directory.CreateDirectory(Path.Combine(data, "plugins"));
        using var zip = System.IO.Compression.ZipFile.Open(Path.Combine(data, "plugins", file), System.IO.Compression.ZipArchiveMode.Create);
        if (manifest != null)
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(manifest);
    }

    [Theory]
    [InlineData("InfLink-rs.plugin")]      // as the helper installs it
    [InlineData("InfLinkrs-3.4.0.plugin")] // as BetterNCM's plugin store does
    public void FindsInfLinkWhateverItsFileIsCalled(string file)
    {
        string data = DataFolder();
        Packed(data, "PluginMarket.plugin", "{\"name\":\"PluginMarket\"}");
        Assert.False(NeteaseSync.HasInfLink(data));
        Packed(data, file, "{\"name\":\"InfLink-rs\",\"version\":\"3.4.0\"}");
        Assert.True(NeteaseSync.HasInfLink(data));
    }

    [Fact]
    public void FindsAnUnpackedInfLink()
    {
        string data = DataFolder();
        Directory.CreateDirectory(Path.Combine(data, "plugins_dev", "inflink"));
        File.WriteAllText(Path.Combine(data, "plugins_dev", "inflink", "manifest.json"), "{\"name\":\"InfLink-rs\"}");
        Assert.True(NeteaseSync.HasInfLink(data));
    }

    [Fact]
    public void OtherPluginsAreNotInfLink()
    {
        string data = DataFolder();
        Packed(data, "InfLink-1.1.1.plugin", "{\"name\":\"InfLink\"}"); // the older plugin
        Packed(data, "nameless.plugin", null);
        File.WriteAllText(Path.Combine(data, "plugins", "broken.plugin"), "not a zip");
        Directory.CreateDirectory(Path.Combine(data, "plugins_dev", "empty"));
        Assert.False(NeteaseSync.HasInfLink(data));
        Assert.False(NeteaseSync.HasInfLink(Path.Combine(data, "missing")));
    }

    static MemoryStream Exe(ushort machine)
    {
        var bytes = new byte[0x86];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
        "PE\0\0"u8.ToArray().CopyTo(bytes, 0x80);
        BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);
        return new MemoryStream(bytes);
    }

    [Fact]
    public void ReadsAnExesProcessor()
    {
        Assert.Equal(NeteaseSync.X64, NeteaseSync.Machine(Exe(0x8664)));
        Assert.Equal(0x014C, NeteaseSync.Machine(Exe(0x014C)));
        Assert.Equal(0, NeteaseSync.Machine(new MemoryStream("not an exe, just text long enough to have a header slot"u8.ToArray())));
    }

    [Fact]
    public void OnlyTheReviewedHelperRuns()
    {
        Assert.False(NeteaseSync.IsPinned(NeteaseSync.FindHelper(Release())!));
        var reviewed = NeteaseSync.FindHelper(Release(digest: "sha256:" + NeteaseSync.HelperSha256.ToUpperInvariant()));
        Assert.True(NeteaseSync.IsPinned(reviewed!));
        Assert.EndsWith("/releases/tags/" + NeteaseSync.HelperVersion, NeteaseSync.HelperEndpoint);
    }
}
