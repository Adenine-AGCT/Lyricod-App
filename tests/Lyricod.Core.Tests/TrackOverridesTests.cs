namespace Lyricod.Core.Tests;

public class TrackOverridesTests
{
    static Track Song() => new() { Title = "Hello", Artist = "Singer", Duration = 120 };

    [Fact]
    public void SavedChoiceRoundTripsWithoutSharingMutableValues()
    {
        using var files = new StoreFiles();
        var store = new TrackOverrides(files.File);
        var track = Song();
        var input = new TrackOverride { NeteaseId = 7, Offset = 1.5 };
        Assert.True(store.TrySet(track, input));
        var original = File.ReadAllText(files.File);

        input.Lyrics = LyricsRef.Netease(8);
        input.Offset = 2;
        var read = store[track];
        read.Lyrics = null;
        read.Offset = 0;

        AssertChoice(store[track], LyricsRef.Netease(7), 1.5);
        AssertChoice(new TrackOverrides(files.File)[track], LyricsRef.Netease(7), 1.5);
        Assert.Equal(original, File.ReadAllText(files.File));
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact(SkipUnless = nameof(IsWindows), Skip = "Requires Windows file-sharing semantics.")]
    public void StartupReadLockCannotOverwriteExistingChoices()
    {
        using var files = new StoreFiles();
        var song = Song();
        var other = song with { Title = "Another song" };
        var seed = new TrackOverrides(files.File);
        Assert.True(seed.TrySet(song, new TrackOverride { Lyrics = LyricsRef.Netease(7), Offset = 1.5 }));
        Assert.True(seed.TrySet(other, new TrackOverride { Offset = 3 }));
        var original = File.ReadAllText(files.File);

        using (var locked = File.Open(files.File, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var store = new TrackOverrides(files.File);
            Assert.False(store.TryGet(song, out _));
            Assert.False(store.TrySet(other, new TrackOverride { Offset = 4 }));
        }
        Assert.Equal(original, File.ReadAllText(files.File));
        var saved = new TrackOverrides(files.File);
        AssertChoice(saved[song], LyricsRef.Netease(7), 1.5);
        AssertChoice(saved[other], null, 3);
    }

    [Fact(SkipUnless = nameof(IsWindows), Skip = "Requires Windows file-sharing semantics.")]
    public void LockedDestinationKeepsTheCommittedFileAndMemory()
    {
        using var files = new StoreFiles();
        var store = new TrackOverrides(files.File);
        var track = Song();
        Assert.True(store.TrySet(track, new TrackOverride { Lyrics = LyricsRef.Netease(7), Offset = 1.5 }));
        var original = File.ReadAllText(files.File);
        var next = new TrackOverride { Lyrics = LyricsRef.Netease(8), Offset = -0.5 };

        using (var locked = new FileStream(files.File, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.TrySet(track, next));
            AssertChoice(store[track], LyricsRef.Netease(7), 1.5);
        }

        Assert.Equal(original, File.ReadAllText(files.File));
        AssertChoice(new TrackOverrides(files.File)[track], LyricsRef.Netease(7), 1.5);
        Assert.False(File.Exists(files.File + ".tmp"));
    }

    static void AssertChoice(TrackOverride choice, LyricsRef? lyrics, double? offset)
    {
        Assert.Equal(lyrics, choice.Lyrics);
        Assert.Equal(offset, choice.Offset);
    }

    sealed class StoreFiles : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "lyricod-overrides-tests", Guid.NewGuid().ToString());
        public string File => Path.Combine(Directory, "overrides.json");

        public StoreFiles() => System.IO.Directory.CreateDirectory(Directory);

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
