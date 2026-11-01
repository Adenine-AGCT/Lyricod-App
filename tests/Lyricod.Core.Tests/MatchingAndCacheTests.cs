namespace Lyricod.Core.Tests;

public class MatchingAndCacheTests
{
    static Track T(string title, string artist, double duration) => new() { TrackId = "x", Title = title, Artist = artist, Duration = duration };

    [Fact]
    public void MatcherPrefersSameDuration()
    {
        var track = T("晴天", "周杰倫", 269);
        var exact = TrackMatcher.Score(["晴天"], ["周杰伦"], 269.5, track);
        var live = TrackMatcher.Score(["晴天 (Live)"], ["周杰伦"], 320, track);
        Assert.True(exact > live);
        Assert.True(exact >= 3);
    }

    /// Regression: Apple Music's English title + romanised artist picked an unrelated song.
    [Fact]
    public void MatcherUsesAliasesAndBracketedTitles()
    {
        var track = T("You Complete Me (Theme Song from \"Back to the Good Times\")", "Crowd Lu", 228.614);
        var right = TrackMatcher.Score(["几分之几 (You Complete Me)", "电影《花甲大人转男孩》主题曲"], ["卢广仲", "Crowd Lu"], 228.6, track);
        var wrong = TrackMatcher.Score(["You Complete Me"], ["Keyshia Cole"], 230.9, track);
        Assert.True(right > wrong);
    }

    /// Regression: searching by the artist alone accepted another of their songs.
    [Fact]
    public void ArtistAloneNeedsTheSameLength()
    {
        var track = T("晴天", "Jay Chou", 269) with { ArtistAliases = ["周杰伦", "周董"] };
        Assert.True(TrackMatcher.Score(["骑士精神"], ["蔡依林", "周杰伦"], 257.6, track) < 3);
        // An English title for a song listed only in Chinese, same artist and length: still found.
        var english = T("Your Name Engraved Herein", "Crowd Lu", 268) with { ArtistAliases = ["卢广仲"] };
        Assert.True(TrackMatcher.Score(["刻在我心底的名字"], ["卢广仲"], 268.4, english) >= 3);
    }

    /// Regression: NetEase lists RADWIMPS' English version under 夢灯籠 too, with the same length.
    [Fact]
    public void AnotherLanguagesVersionLoses()
    {
        var track = T("夢灯籠", "RADWIMPS", 129.4);
        var english = TrackMatcher.Score(["Dream lantern (English ver.)", "夢灯籠"], ["RADWIMPS"], 129.3, track);
        var original = TrackMatcher.Score(["夢灯籠"], ["RADWIMPS"], 128.1, track);
        Assert.True(original > english);
        Assert.True(original >= 7);
        // Playing the English version: it wins.
        var playingEnglish = T("Dream lantern (English ver.)", "RADWIMPS", 129.3);
        Assert.True(TrackMatcher.Score(["Dream lantern (English ver.)"], ["RADWIMPS"], 129.3, playingEnglish)
                    > TrackMatcher.Score(["夢灯籠", "Dream lantern"], ["RADWIMPS"], 128.1, playingEnglish));
    }

    [Fact]
    public void LatinTitlesForKanaOnes()
    {
        Assert.True(TrackMatcher.SoundsAlike("BETELGEUSE", "ベテルギウス"));
        Assert.True(TrackMatcher.SoundsAlike("さよーならまたいつか！", "Sayonara, Mata Itsuka!"));
        Assert.True(TrackMatcher.SoundsAlike("Lemon", "レモン"));
        Assert.True(TrackMatcher.SoundsAlike("Pretender", "プリテンダー"));
        Assert.False(TrackMatcher.SoundsAlike("Lemon", "メロン"));
        Assert.False(TrackMatcher.SoundsAlike("BETELGEUSE", "なないろメモリー"));
        Assert.False(TrackMatcher.SoundsAlike("晴天", "ベテルギウス"));
        // Regression: YouTube Music's "BETELGEUSE" by "Yuuri" took another singer's song of the same length.
        var track = T("BETELGEUSE", "Yuuri", 230.5) with { ArtistAliases = ["優里", "優莉", "优莉"] };
        Assert.True(TrackMatcher.Score(["ベテルギウス"], ["優里"], 230, track) > TrackMatcher.Score(["なないろメモリー"], ["優莉"], 230.4, track));
    }

    /// Regression: YouTube Music calls the Japanese 夢灯籠 "Dream lantern", the English version's title.
    [Fact]
    public void LearnedTitlesFindTheOriginal()
    {
        var track = T("Dream lantern", "RADWIMPS", 132) with { TitleAliases = ["梦灯笼"] };
        var english = TrackMatcher.Score(["Dream lantern (English ver.)", "梦灯笼"], ["RADWIMPS"], 129.4, track, "君の名は。 English edition");
        var original = TrackMatcher.Score(["夢灯籠", "梦灯笼"], ["RADWIMPS"], 128.6, track, "君の名は。");
        Assert.True(original > english);
        Assert.True(english < 3);
        Assert.Contains("lang:en", TrackMatcher.Versions("君の名は。 English edition"));
    }

    [Fact]
    public void VersionMarkers()
    {
        Assert.Equal(["lang:en"], TrackMatcher.Versions("Dream lantern (English ver.)"));
        Assert.Equal(["lang:yue"], TrackMatcher.Versions("富士山下 (粵語版)"));
        Assert.Equal(["novocals"], TrackMatcher.Versions("晴天 (伴奏)"));
        Assert.Equal(["variant"], TrackMatcher.Versions("晴天 (Live)"));
        Assert.Empty(TrackMatcher.Versions("Live Forever"));
        Assert.Empty(TrackMatcher.Versions("English Man in New York"));
        Assert.Empty(TrackMatcher.Versions("スパークル (movie ver.)"));
        // An instrumental loses to the song even at the same length.
        var track = T("晴天", "周杰伦", 269);
        Assert.True(TrackMatcher.Score(["晴天"], ["周杰伦"], 269, track) - TrackMatcher.Score(["晴天 (伴奏)"], ["周杰伦"], 269, track) >= 4);
    }

    [Fact]
    public void NormaliseFoldsTraditionalCaseAndSuffixes()
    {
        Assert.Equal(TrackMatcher.Normalize("周杰伦"), TrackMatcher.Normalize("周杰倫"));
        Assert.Equal("晴天", TrackMatcher.Normalize("晴天 (Live)"));
        Assert.Equal("yesterday", TrackMatcher.Normalize("Yesterday - 2009 Remaster"));
    }

    /// The same FNV-1a key as the macOS app, so cache files and per-song choices line up.
    [Fact]
    public void CacheKeyMatchesTheMacApp()
    {
        Assert.Equal("e8991c441bd7db86", LyricsProvider.CacheKey(T("晴天", "周杰伦", 269.9)));
    }

    [Fact]
    public void OverridesRoundTripAndForgetEmptyOnes()
    {
        var file = Path.Combine(Path.GetTempPath(), "lyricod-tests", Guid.NewGuid() + ".json");
        var track = T("晴天", "周杰伦", 269);
        var overrides = new TrackOverrides(file);
        Assert.True(overrides.TrySet(track, new TrackOverride { Lyrics = new LyricsRef(LyricsSource.Qq, "0039MnYb0qxYhV", 97773), Offset = 0.5 }));
        var reloaded = new TrackOverrides(file);
        Assert.Equal(new LyricsRef(LyricsSource.Qq, "0039MnYb0qxYhV", 97773), reloaded[track].Lyrics);
        Assert.Equal(0.5, reloaded[track].Offset);
        Assert.True(reloaded.TrySet(track, new TrackOverride()));
        Assert.DoesNotContain("0039MnYb0qxYhV", File.ReadAllText(file));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MovingOverridesFillsEachMissingFieldAndRemovesOldKey(bool hasPin, bool hasOffset)
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyricod-tests", Guid.NewGuid().ToString());
        var file = Path.Combine(dir, "overrides.json");
        try
        {
            var stale = T("Hello", "Singer", 0);
            var to = stale with { Duration = 120 };
            var overrides = new TrackOverrides(file);
            Assert.True(overrides.TrySet(stale, new TrackOverride { NeteaseId = 123, Offset = 1.5 }));
            Assert.True(overrides.TrySet(to, new TrackOverride { Lyrics = hasPin ? LyricsRef.Netease(456) : null, Offset = hasOffset ? 0.25 : null }));
            Assert.True(overrides.TryMove(stale, to));
            var saved = new TrackOverrides(file);
            Assert.Equal(LyricsRef.Netease(hasPin ? 456 : 123), saved[to].Lyrics);
            Assert.Equal(hasOffset ? 0.25 : 1.5, saved[to].Offset);
            Assert.True(saved[stale].IsEmpty);
            Assert.DoesNotContain(LyricsProvider.CacheKey(stale), File.ReadAllText(file));
            Assert.True(saved.TryMove(stale, to)); // missing source is harmless
            Assert.True(saved.TryMove(to, to with { Duration = 120.5 })); // same whole-second key
            Assert.Equal(saved[to].Lyrics, new TrackOverrides(file)[to].Lyrics);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    /// Cache files written by the macOS app read the same here.
    [Fact]
    public void ReadsMacCacheFormat()
    {
        const string json = """{"source":"QQ Music","lrc":"[00:01.00]晴天","ref":{"source":"qq","id":"abc","qqID":5}}""";
        var raw = System.Text.Json.JsonSerializer.Deserialize<RawLyrics>(json)!;
        Assert.Equal(new LyricsRef(LyricsSource.Qq, "abc", 5), raw.SongRef);
        Assert.Equal("晴天", raw.Parse()!.Lines[0].Text);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<RawLyrics>("""{"source":"NetEase","lrc":"[00:01.00]x","neteaseID":186001}""")!;
        Assert.Equal(LyricsRef.Netease(186001), legacy.SongRef);
    }
}

public class LinePiecesTests
{
    /// Every character is 10 points wide.
    static double Measure(string s) => s.Length * 10;

    [Fact]
    public void ShortLineStaysWhole()
    {
        var line = new LyricLine { Start = 1, End = 3, Text = "用一生" };
        Assert.Equal(["用一生"], LinePieces.Split(line, 100, Measure).Select(p => p.Text));
    }

    [Fact]
    public void UntimedCjkLineSplitsEvenlyWithEstimatedStarts()
    {
        var line = new LyricLine { Id = 3, Start = 10, End = 22, Text = "用一生去等待一个人的出现在这里" };
        var pieces = LinePieces.Split(line, 100, Measure);
        Assert.Equal(2, pieces.Count);
        Assert.Equal(line.Text, string.Concat(pieces.Select(p => p.Text)));
        Assert.All(pieces, p => Assert.True(Measure(p.Text) <= 100));
        Assert.Equal(10, pieces[0].Start);
        Assert.InRange(pieces[1].Start, 10.0001, 21.9999);
        Assert.Equal(pieces[1].Start, pieces[0].End);
        Assert.All(pieces, p => { Assert.Null(p.Words); Assert.Equal(3, p.Id); });
        Assert.Equal(pieces[0].Text, LinePieces.Current(pieces, 9)?.Text);
        Assert.Equal(pieces[1].Text, LinePieces.Current(pieces, 21)?.Text);
    }

    [Fact]
    public void TimedLineBreaksBetweenWordsAndKeepsTiming()
    {
        string[] texts = ["I ", "will ", "always ", "love ", "you, ", "wherever ", "you ", "go"];
        var words = texts.Select((t, i) => new LyricWord(i, 1, t)).ToList();
        var line = new LyricLine { Start = 0, End = 8, Text = string.Concat(texts), Words = words };
        var pieces = LinePieces.Split(line, 250, Measure);
        Assert.Equal(2, pieces.Count);
        Assert.Equal("I will always love", pieces[0].Text);
        Assert.Equal("you, wherever you go", pieces[1].Text);
        Assert.Equal(4, pieces[1].Start);
        Assert.Equal("you, ", pieces[1].Words![0].Text);
    }

    [Fact]
    public void LatinWordsStayTogetherWithoutTiming()
    {
        var words = LinePieces.EstimatedWords(new LyricLine { Start = 0, End = 6, Text = "hello there my old friend" });
        Assert.Equal(["hello ", "there ", "my ", "old ", "friend"], words.Select(w => w.Text));
        Assert.Equal(0, words[0].Start);
    }
}
