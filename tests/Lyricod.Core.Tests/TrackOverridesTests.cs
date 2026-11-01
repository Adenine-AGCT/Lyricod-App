namespace Lyricod.Core.Tests;

public class TrackOverridesTests
{
    static Track Song(double duration = 120) => new() { Title = "Hello", Artist = "Singer", Duration = duration };

    [Fact]
    public void ReadsAndInputsCannotMutateTheCommittedChoice()
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

    [Theory]
    [InlineData("pin")]
    [InlineData("unpin")]
    [InlineData("offset")]
    [InlineData("delete")]
    public void FailedWriteKeepsCommittedMemoryAndFileAndCanRetry(string action)
    {
        using var files = new StoreFiles();
        var store = new TrackOverrides(files.File);
        var track = Song();
        Assert.True(store.TrySet(track, new TrackOverride { Lyrics = LyricsRef.Netease(7), Offset = 1.5 }));
        var original = File.ReadAllText(files.File);
        var next = store[track];
        switch (action)
        {
            case "pin": next.Lyrics = LyricsRef.Netease(8); break;
            case "unpin": next.Lyrics = null; break;
            case "offset": next.Offset = -0.5; break;
            case "delete": next = new TrackOverride(); break;
        }
        // A real filesystem error: a directory occupies the temporary file's path.
        Directory.CreateDirectory(files.File + ".tmp");
        Assert.False(store.TrySet(track, next));
        AssertChoice(store[track], LyricsRef.Netease(7), 1.5);
        AssertChoice(new TrackOverrides(files.File)[track], LyricsRef.Netease(7), 1.5);
        Assert.Equal(original, File.ReadAllText(files.File));

        Directory.Delete(files.File + ".tmp");
        Assert.True(store.TrySet(track, next));
        AssertChoice(store[track], next.Lyrics, next.Offset);
        AssertChoice(new TrackOverrides(files.File)[track], next.Lyrics, next.Offset);
        Assert.False(File.Exists(files.File + ".tmp"));
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact(SkipUnless = nameof(IsWindows), Skip = "Requires Windows file-sharing semantics.")]
    public void LockedDestinationKeepsTheCommittedFileAndMemoryAndCanRetry()
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
        Assert.True(store.TrySet(track, next));
        AssertChoice(store[track], next.Lyrics, next.Offset);
        AssertChoice(new TrackOverrides(files.File)[track], next.Lyrics, next.Offset);
    }

    [Fact]
    public void FailedReplacementLeavesTheStoreEmptyAndCleansItsTemporaryFile()
    {
        using var files = new StoreFiles();
        Directory.CreateDirectory(files.File);
        var store = new TrackOverrides(files.File);
        var track = Song();
        var choice = new TrackOverride { Lyrics = LyricsRef.Netease(7), Offset = 1.5 };

        Assert.False(store.TrySet(track, choice));
        Assert.True(store[track].IsEmpty);
        Assert.True(Directory.Exists(files.File));
        Assert.False(File.Exists(files.File + ".tmp"));

        Directory.Delete(files.File);
        Assert.True(store.TrySet(track, choice));
        AssertChoice(new TrackOverrides(files.File)[track], choice.Lyrics, choice.Offset);
    }

    [Fact]
    public void InaccessibleParentIsReportedWhenSavingWithoutCrashingConstruction()
    {
        using var files = new StoreFiles();
        var blocker = Path.Combine(files.Directory, "parent");
        File.WriteAllText(blocker, "keep this file");
        var store = new TrackOverrides(Path.Combine(blocker, "overrides.json"));

        Assert.False(store.TrySet(Song(), new TrackOverride { Offset = 0.5 }));
        Assert.True(store[Song()].IsEmpty);
        Assert.Equal("keep this file", File.ReadAllText(blocker));
    }

    [Fact]
    public void FailedMoveKeepsBothKeysAndCanRetryTheFieldMerge()
    {
        using var files = new StoreFiles();
        var store = new TrackOverrides(files.File);
        var from = Song(0);
        var to = Song();
        Assert.True(store.TrySet(from, new TrackOverride { NeteaseId = 7, Offset = 1.5 }));
        Assert.True(store.TrySet(to, new TrackOverride { Lyrics = LyricsRef.Netease(8) }));
        var original = File.ReadAllText(files.File);
        Directory.CreateDirectory(files.File + ".tmp");

        Assert.False(store.TryMove(from, to));
        AssertChoice(store[from], LyricsRef.Netease(7), 1.5);
        AssertChoice(store[to], LyricsRef.Netease(8), null);
        var unchanged = new TrackOverrides(files.File);
        AssertChoice(unchanged[from], LyricsRef.Netease(7), 1.5);
        AssertChoice(unchanged[to], LyricsRef.Netease(8), null);
        Assert.Equal(original, File.ReadAllText(files.File));

        Directory.Delete(files.File + ".tmp");
        Assert.True(store.TryMove(from, to));
        Assert.True(store[from].IsEmpty);
        AssertChoice(store[to], LyricsRef.Netease(8), 1.5);
        var saved = new TrackOverrides(files.File);
        Assert.True(saved[from].IsEmpty);
        AssertChoice(saved[to], LyricsRef.Netease(8), 1.5);
    }

    [Fact]
    public void MoveKeepsAnExplicitZeroOffsetAndLegacyLyrics()
    {
        using var files = new StoreFiles();
        var store = new TrackOverrides(files.File);
        var from = Song(0);
        var to = Song();
        Assert.True(store.TrySet(from, new TrackOverride { NeteaseId = 7, Offset = 1.5 }));
        Assert.True(store.TrySet(to, new TrackOverride { NeteaseId = 8, Offset = 0 }));

        Assert.True(store.TryMove(from, to));
        var saved = new TrackOverrides(files.File);
        AssertChoice(saved[to], LyricsRef.Netease(8), 0);
        Assert.True(saved[from].IsEmpty);
    }

    [Fact]
    public void MissingSourceAndSameKeyMovesNeedNoWrite()
    {
        using var files = new StoreFiles();
        var store = new TrackOverrides(files.File);
        var track = Song();
        Assert.True(store.TrySet(track, new TrackOverride { Offset = 1.5 }));
        var original = File.ReadAllText(files.File);
        Directory.CreateDirectory(files.File + ".tmp");

        Assert.True(store.TryMove(track with { Duration = 0 }, track));
        Assert.True(store.TryMove(track, track with { Duration = 120.5 }));
        Assert.Equal(original, File.ReadAllText(files.File));
        AssertChoice(store[track], null, 1.5);
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
