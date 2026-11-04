using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyricod.Core.Tests;

public class LyricsProviderTests
{
    const string LineJson = """{"lyric":"[00:01.00]Hello"}""";
    const string LegacyJson = """{"data":{"song":{"list":[{"songmid":"qq-song","songid":42,"songname":"Hello","singer":[{"name":"Singer"}],"interval":120}]}}}""";
    const string ModernJson = """{"req":{"data":{"body":{"song":{"list":[{"mid":"wrong-song","id":43,"name":"Hello","singer":[{"name":"Singer"}],"interval":120}]}}}}}""";
    static Track Track() => new() { TrackId = "test", Title = "Hello", Artist = "Singer", Duration = 120, QqSongMid = "qq-song", QqSongId = 42 };

    [Fact]
    public async Task FailedWordCheckShowsCacheFirstAndIsRetriedNextDay()
    {
        var events = new ConcurrentQueue<string>();
        using var fixture = new Fixture(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            events.Enqueue(path);
            if (path.Contains("lyric_download")) return Response("unavailable", HttpStatusCode.ServiceUnavailable);
            return Response(path.Contains("fcg_query_lyric") ? LineJson : "{}");
        });
        var track = Track();
        var file = fixture.Seed(track);
        var original = File.ReadAllBytes(file);
        // Due (saved two days ago), then not (just tried), then due again the next day.
        foreach (var daysAgo in new[] { 2, 0, 2 })
        {
            if (daysAgo > 0) Fixture.Age(file, daysAgo);
            Lyrics? shown = null;
            var result = await fixture.Provider.GetLyrics(track, ct: TestContext.Current.CancellationToken, onCached: cached =>
            {
                Assert.Equal("Hello", cached.Lines[0].Text);
                shown = cached;
                events.Enqueue("cached");
            });
            Assert.Equal("NetEase", result?.Source);
            Assert.False(result?.IsWordSynced);
            // Unchanged: the very lyrics already shown, so the app needn't show them again.
            if (shown != null) Assert.Same(shown, result);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        Assert.Equal("cached", events.First());
        Assert.Equal(2, events.Count(e => e.Contains("lyric_download")));
        Assert.Equal(2, events.Count(e => e == "cached"));
    }

    [Fact]
    public async Task RecentlyCheckedCacheSkipsNetwork()
    {
        var requests = 0;
        using var fixture = new Fixture(_ => { requests++; return Response("{}"); });
        var track = Track();
        fixture.Seed(track);
        var callbacks = 0;
        var result = await fixture.Provider.GetLyrics(track, ct: TestContext.Current.CancellationToken, onCached: _ => callbacks++);
        Assert.Equal("Hello", result?.Lines[0].Text);
        Assert.Equal(0, requests);
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task OldCheckedMarkIsIgnored()
    {
        var paths = new ConcurrentQueue<string>();
        using var fixture = new Fixture(request => { paths.Enqueue(request.RequestUri!.AbsolutePath); return Response("{}"); });
        var track = Track();
        var file = fixture.Seed(track);
        // 0.1.0 marked songs as checked even when QQ Music couldn't be asked.
        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["wordsChecked"] = true;
        File.WriteAllText(file, json.ToJsonString());
        Fixture.Age(file, 2);
        await fixture.Provider.GetLyrics(track, ct: TestContext.Current.CancellationToken);
        Assert.Contains(paths, p => p.Contains("lyric_download"));
    }

    [Fact]
    public async Task CancellationAfterCachedCallbackDoesNotRequestOrMutateCache()
    {
        var requests = 0;
        using var fixture = new Fixture(_ => { requests++; return Response("{}"); });
        using var cancellation = new CancellationTokenSource();
        var track = Track();
        var file = fixture.Seed(track);
        var original = File.ReadAllBytes(file);
        var savedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, savedAt);
        var callbacks = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider.GetLyrics(track, ct: cancellation.Token,
            onCached: _ => { callbacks++; cancellation.Cancel(); }));
        Assert.Equal(1, callbacks);
        Assert.Equal(0, requests);
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Equal(savedAt, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public async Task CancellationDuringFetchDoesNotCreateCache()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture(_ => { cancellation.Cancel(); return Response(LineJson); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider.GetLyrics(Track(),
            pinned: new LyricsRef(LyricsSource.Qq, "qq-song"), ct: cancellation.Token));
        Assert.Equal(0, fixture.Provider.CachedCount());
    }

    [Fact]
    public async Task PinnedCacheDoesNotRequestNetwork()
    {
        var requests = 0;
        using var fixture = new Fixture(_ => { requests++; return Response("{}"); });
        var track = Track();
        fixture.Seed(track);
        var result = await fixture.Provider.GetLyrics(track, pinned: LyricsRef.Netease(7), ct: TestContext.Current.CancellationToken);
        Assert.Equal("Hello", result?.Lines[0].Text);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task OddNeteaseReplyFallsThroughToTheNextSource()
    {
        using var fixture = new Fixture(request => Response(
            request.RequestUri!.Host.EndsWith("163.com") ? """{"result":42}"""
            : request.RequestUri.AbsolutePath.Contains("fcg_query_lyric") ? LineJson : "{}"));
        var result = await fixture.Provider.GetLyrics(Track(), ct: TestContext.Current.CancellationToken);
        Assert.Equal("QQ Music", result?.Source);
    }

    [Theory]
    [InlineData("{\"req\":0}")]
    [InlineData("{\"req\":{\"data\":{\"body\":{\"song\":{\"list\":[0]}}}}}")]
    [InlineData("not-json")]
    public async Task MalformedQqSearchFallsBackToLegacy(string malformed)
    {
        var hosts = new List<string>();
        using var fixture = new Fixture(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return Response(request.RequestUri.Host == "u.y.qq.com" ? malformed : LegacyJson);
        });
        var results = await fixture.Provider.QqCandidates("Hello Singer", Track(), TestContext.Current.CancellationToken);
        Assert.Equal("qq-song", Assert.Single(results).Id.Id);
        Assert.Equal(["u.y.qq.com", "shc.y.qq.com"], hosts);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "wrong-song")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "qq-song")]
    public async Task QqSearchStatusControlsWhetherItsJsonIsAccepted(HttpStatusCode status, string expected)
    {
        using var fixture = new Fixture(request => request.RequestUri!.Host == "u.y.qq.com"
            ? Response(ModernJson, status) : Response(LegacyJson));
        var results = await fixture.Provider.QqCandidates("Hello Singer", Track(), TestContext.Current.CancellationToken);
        Assert.Equal(expected, Assert.Single(results).Id.Id);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task TitleOnlyQqMatchDependsOnThePlayer(bool musicPlayer, bool accepted)
    {
        using var fixture = new Fixture(request => Response(
            request.RequestUri!.Host == "u.y.qq.com" ? ModernJson
            : request.RequestUri.AbsolutePath.Contains("fcg_query_lyric") ? LineJson : "{}"));
        var search = new LyricsSearch(musicPlayer);
        var result = await fixture.Provider.GetLyrics(new Track { Title = "Hello" }, search: search, ct: TestContext.Current.CancellationToken);
        Assert.Equal(accepted, result != null);
        Assert.Equal(accepted ? 1 : 0, fixture.Provider.CachedCount());
        Assert.Equal(3, search.BestScore);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(3.0, true)]
    [InlineData(5.5, false)]
    [InlineData(8.5, null)]
    public async Task UnverifiedOrWeakCacheIsNeverShownInABrowser(double? score, bool? titleMatched)
    {
        int requests = 0, callbacks = 0;
        using var fixture = new Fixture(_ => { requests++; return Response("{}"); });
        var track = Track();
        var file = fixture.Seed(track, score, titleMatched);
        var original = File.ReadAllText(file);
        var result = await fixture.Provider.GetLyrics(track, search: new LyricsSearch(false),
            onCached: _ => callbacks++, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
        Assert.True(requests > 0);
        Assert.Equal(0, callbacks);
        Assert.Equal(original, File.ReadAllText(file));
    }

    [Fact]
    public async Task ConfidentCacheSkipsNetworkAndReportsItsScore()
    {
        using var fixture = new Fixture(_ => throw new InvalidOperationException("Unexpected request"));
        var track = Track();
        fixture.Seed(track, 6, true);
        var search = new LyricsSearch(false);
        Assert.NotNull(await fixture.Provider.GetLyrics(track, search: search, ct: TestContext.Current.CancellationToken));
        Assert.Equal(6, search.BestScore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PinnedLyricsBypassTheBrowserBar(bool cached)
    {
        int requests = 0;
        using var fixture = new Fixture(_ => { requests++; return Response("""{"lrc":{"lyric":"[00:01.00]Chosen"}}"""); });
        var track = Track();
        if (cached) fixture.Seed(track);
        var result = await fixture.Provider.GetLyrics(track, pinned: LyricsRef.Netease(7), search: new LyricsSearch(false),
            ct: TestContext.Current.CancellationToken);
        Assert.Equal(cached ? "Hello" : "Chosen", result?.Lines[0].Text);
        Assert.Equal(cached ? 0 : 1, requests);
    }

    const string NeteaseSong = """{"result":{"songs":[{"id":7,"name":"Hello","ar":[{"name":"Singer"}],"dt":120000}]}}""";
    const string NeteaseLyrics = """{"lrc":{"lyric":"[00:01.00]Hello"}}""";
    const string LrclibSong = """{"id":9,"trackName":"Hello","artistName":"Singer","duration":120,"syncedLyrics":"[00:01.00]Hello"}""";

    [Theory]
    [InlineData("netease")]
    [InlineData("qq")]
    [InlineData("lrclib-exact")]
    [InlineData("lrclib-search")]
    public async Task ConfidentMusicVideoIsScoredCachedAndReused(string source)
    {
        int requests = 0;
        using var fixture = new Fixture(request =>
        {
            requests++;
            var uri = request.RequestUri!;
            return Response(source switch
            {
                "netease" when uri.AbsolutePath.Contains("cloudsearch") && uri.Query.Contains("type=1&") => NeteaseSong,
                "netease" when uri.AbsolutePath.Contains("lyric/v1") => NeteaseLyrics,
                "qq" when uri.Host == "u.y.qq.com" => ModernJson,
                "qq" when uri.AbsolutePath.Contains("fcg_query_lyric") => LineJson,
                "lrclib-exact" when uri.Host == "lrclib.net" && uri.AbsolutePath == "/api/get" => LrclibSong,
                "lrclib-search" when uri.Host == "lrclib.net" && uri.AbsolutePath == "/api/search" => "[" + LrclibSong + "]",
                _ => "{}",
            });
        });
        var track = new Track { Title = "Singer - Hello (Official Video)", Artist = "Singer", Duration = 120 };
        // Even an old, unscored entry is searched again and replaced with evidence.
        var file = fixture.Seed(track);
        var search = new LyricsSearch(false);
        var result = await fixture.Provider.GetLyrics(track, search: search, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(8.5, search.BestScore);
        var raw = JsonSerializer.Deserialize<RawLyrics>(File.ReadAllText(file))!;
        Assert.Equal(8.5, raw.MatchScore);
        Assert.True(raw.TitleMatched);
        var count = requests;
        Assert.NotNull(await fixture.Provider.GetLyrics(track, search: new LyricsSearch(false), ct: TestContext.Current.CancellationToken));
        Assert.Equal(count, requests);
    }

    [Theory]
    [InlineData("netease")]
    [InlineData("lrclib-exact")]
    [InlineData("lrclib-search")]
    public async Task TitleOnlyMatchesAreNotCachedForBrowsers(string source)
    {
        using var fixture = new Fixture(request =>
        {
            var uri = request.RequestUri!;
            return Response(source switch
            {
                "netease" when uri.AbsolutePath.Contains("cloudsearch") => NeteaseSong,
                "netease" when uri.AbsolutePath.Contains("lyric/v1") => NeteaseLyrics,
                "lrclib-exact" when uri.Host == "lrclib.net" && uri.AbsolutePath == "/api/get" => LrclibSong,
                "lrclib-search" when uri.Host == "lrclib.net" && uri.AbsolutePath == "/api/search" => "[" + LrclibSong + "]",
                _ => "{}",
            });
        });
        var search = new LyricsSearch(false);
        Assert.Null(await fixture.Provider.GetLyrics(new Track { Title = "Hello" }, search: search, ct: TestContext.Current.CancellationToken));
        Assert.Equal(3, search.BestScore);
        Assert.Equal(0, fixture.Provider.CachedCount());
    }

    [Fact]
    public async Task BrowserWordUpgradeCannotUseAnUnscoredQqId()
    {
        var paths = new List<string>();
        using var fixture = new Fixture(request => { paths.Add(request.RequestUri!.AbsolutePath); return Response(ModernJson); });
        var track = Track() with { Title = "Other" };
        var file = fixture.Seed(track, 8.5, true);
        Fixture.Age(file, 2);
        // The search reply has the wrong title, despite a tempting direct QQ id on the track.
        var result = await fixture.Provider.GetLyrics(track, search: new LyricsSearch(false), ct: TestContext.Current.CancellationToken);
        Assert.Equal("NetEase", result?.Source);
        Assert.Equal(8.5, JsonSerializer.Deserialize<RawLyrics>(File.ReadAllText(file))!.MatchScore);
        Assert.DoesNotContain(paths, p => p.Contains("lyric_download") || p.Contains("fcg_query_lyric"));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task NeteaseFallbackCannotDropBelowTheBrowserBar(bool musicPlayer, bool accepted)
    {
        const string songs = """{"result":{"songs":[{"id":7,"name":"Hello","ar":[{"name":"Singer"}]},{"id":8,"name":"Hello there","ar":[{"name":"Singer"}]}]}}""";
        using var fixture = new Fixture(request =>
        {
            var uri = request.RequestUri!;
            return Response(uri.AbsolutePath.Contains("cloudsearch") ? songs
                : uri.AbsolutePath.Contains("lyric/v1") && uri.Query.Contains("id=8&") ? NeteaseLyrics : "{}");
        });
        var search = new LyricsSearch(musicPlayer);
        var result = await fixture.Provider.GetLyrics(new Track { Title = "Hello", Artist = "Singer" }, search: search,
            ct: TestContext.Current.CancellationToken);
        Assert.Equal(accepted, result != null);
        Assert.Equal(6, search.BestScore);
        Assert.Equal(accepted ? 1 : 0, fixture.Provider.CachedCount());
    }

    [Fact]
    public async Task BrowserDoesNotTrustAnArtistGuessFromAnEarlierMusicSearch()
    {
        const string artists = """{"result":{"artists":[{"name":"歌手"}]}}""";
        using var fixture = new Fixture(_ => Response(artists));
        var track = new Track { Artist = "Vlog channel" };
        var guessed = await fixture.Provider.WithAliases(track, TestContext.Current.CancellationToken);
        Assert.Contains("歌手", guessed.ArtistAliases);
        var verified = await fixture.Provider.WithAliases(track, TestContext.Current.CancellationToken, allowGuess: false);
        Assert.Empty(verified.ArtistAliases);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ArtistAndDurationWithoutTitleOnlyWorkForMusicPlayers(bool musicPlayer)
    {
        using var fixture = new Fixture(request => Response(request.RequestUri!.Host == "u.y.qq.com" ? ModernJson
            : request.RequestUri.AbsolutePath.Contains("fcg_query_lyric") ? LineJson : "{}"));
        var search = new LyricsSearch(musicPlayer);
        var track = new Track { Title = "Unrelated", Artist = "Singer", Duration = 120 };
        var result = await fixture.Provider.GetLyrics(track, search: search, ct: TestContext.Current.CancellationToken);
        Assert.Equal(musicPlayer, result != null);
        Assert.Equal(5.5, search.BestScore);
        Assert.Equal(musicPlayer ? 1 : 0, fixture.Provider.CachedCount());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WordUpgradeKeepsItsSevenPointBar(bool cached)
    {
        var paths = new List<string>();
        using var fixture = new Fixture(request =>
        {
            var uri = request.RequestUri!;
            paths.Add(uri.AbsolutePath);
            return Response(uri.AbsolutePath.Contains("cloudsearch") ? NeteaseSong
                : uri.AbsolutePath.Contains("lyric/v1") ? NeteaseLyrics
                : uri.Host == "u.y.qq.com" ? ModernJson : "{}");
        });
        var track = new Track { Title = "Hello", Artist = "Singer" };
        if (cached) Fixture.Age(fixture.Seed(track, 6, true), 2);
        var result = await fixture.Provider.GetLyrics(track, search: new LyricsSearch(false), ct: TestContext.Current.CancellationToken);
        Assert.Equal("NetEase", result?.Source);
        Assert.DoesNotContain(paths, p => p.Contains("lyric_download") || p.Contains("fcg_query_lyric"));
    }

    [Fact]
    public async Task DirectQqIdDoesNotBypassBrowserChecks()
    {
        using var fixture = new Fixture(request => Response(request.RequestUri!.AbsolutePath.Contains("fcg_query_lyric") ? LineJson : "{}"));
        Assert.Null(await fixture.Provider.GetLyrics(Track(), search: new LyricsSearch(false), ct: TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Provider.CachedCount());
    }

    [Fact]
    public async Task LrclibExactReplyWithoutMetadataIsUnverified()
    {
        using var fixture = new Fixture(request => Response(request.RequestUri!.Host == "lrclib.net"
            ? """{"id":9,"syncedLyrics":"[00:01.00]Hello"}""" : "{}"));
        var search = new LyricsSearch(false);
        Assert.Null(await fixture.Provider.GetLyrics(Track(), search: search, ct: TestContext.Current.CancellationToken));
        Assert.Null(search.BestScore);
        Assert.Equal(0, fixture.Provider.CachedCount());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DurationCorrectionKeepsManualLyricsAndTiming(bool existingChoice)
    {
        using var fixture = new Fixture(_ => throw new Exception("Manual cache should avoid the network"));
        var stale = Track() with { Duration = 0 };
        var corrected = stale with { Duration = 120 };
        var oldFile = fixture.Seed(stale);
        var overrideFile = Path.Combine(Path.GetDirectoryName(oldFile)!, "overrides");
        var overrides = new TrackOverrides(overrideFile);
        Assert.True(overrides.TrySet(stale, new TrackOverride { Lyrics = LyricsRef.Netease(7), Offset = 1.5 }));
        if (existingChoice)
        {
            var newFile = fixture.Seed(corrected);
            File.WriteAllText(newFile, File.ReadAllText(newFile).Replace("Hello", "Correct recording").Replace("\"7\"", "\"8\""));
            Assert.True(overrides.TrySet(corrected, new TrackOverride { Lyrics = LyricsRef.Netease(8), Offset = 0.25 }));
        }
        Assert.True(fixture.Provider.TryCorrectDuration(stale, 120, overrides, out var transition));
        var saved = new TrackOverrides(overrideFile);
        var lyrics = await fixture.Provider.GetLyrics(transition, pinned: saved[transition].Lyrics, ct: TestContext.Current.CancellationToken);
        Assert.Equal(stale.TrackId, transition.TrackId);
        Assert.Equal(existingChoice ? "Correct recording" : "Hello", lyrics?.Lines[0].Text);
        Assert.Equal(LyricsRef.Netease(existingChoice ? 8 : 7), lyrics?.Ref);
        Assert.Equal(existingChoice ? 0.25 : 1.5, saved[transition].Offset);
        Assert.True(saved[stale].IsEmpty);
        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public void ClearCacheToleratesADirectoryAtTheCacheFilePath()
    {
        using var fixture = new Fixture(_ => throw new Exception("Clearing a cache should not request the network"));
        var track = Track();
        var file = fixture.Seed(track);
        File.Delete(file);
        Directory.CreateDirectory(file);

        fixture.Provider.ClearCache(track);

        Assert.True(Directory.Exists(file));
    }

    [Fact]
    public void FailedDurationCorrectionKeepsTheTrackChoicesAndManualCache()
    {
        using var fixture = new Fixture(_ => throw new Exception("Duration correction should not request the network"));
        var stale = Track() with { Duration = 0 };
        var corrected = stale with { Duration = 120 };
        var oldFile = fixture.Seed(stale);
        var newFile = Path.Combine(Path.GetDirectoryName(oldFile)!, LyricsProvider.CacheKey(corrected) + ".json");
        var overrideFile = Path.Combine(Path.GetDirectoryName(oldFile)!, "overrides");
        var overrides = new TrackOverrides(overrideFile);
        Assert.True(overrides.TrySet(stale, new TrackOverride { Lyrics = LyricsRef.Netease(7), Offset = 1.5 }));
        var oldCache = File.ReadAllText(oldFile);
        var oldOverrides = File.ReadAllText(overrideFile);
        Directory.CreateDirectory(overrideFile + ".tmp");

        Assert.False(fixture.Provider.TryCorrectDuration(stale, 120, overrides, out var unchanged));
        Assert.Equal(stale, unchanged);
        Assert.Equal(LyricsRef.Netease(7), overrides[stale].Lyrics);
        Assert.Equal(1.5, overrides[stale].Offset);
        Assert.True(overrides[corrected].IsEmpty);
        Assert.Equal(oldOverrides, File.ReadAllText(overrideFile));
        Assert.Equal(oldCache, File.ReadAllText(oldFile));
        Assert.False(File.Exists(newFile));
    }

    public static IEnumerable<object[]> LookupCases()
    {
        foreach (bool music in new[] { false, true })
        foreach (bool setting in new[] { false, true })
        foreach (bool cached in new[] { false, true })
        foreach (bool pinned in new[] { false, true })
            yield return [music, setting, cached, pinned];
    }

    [Theory]
    [MemberData(nameof(LookupCases))]
    public async Task AutomaticLookupRespectsPlayerSettingAndLocalChoices(bool music, bool setting, bool cached, bool pinned)
    {
        var requests = new List<string>();
        using var fixture = new Fixture(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add(path);
            return Response(path.Contains("lyric/v1") ? NeteaseLyrics : "{}");
        });
        var track = Track();
        if (cached) Fixture.Age(fixture.Seed(track, 6, true), 2); // a word upgrade would normally be due
        var search = new LyricsSearch(MusicPlayers.IsMusicPlayer(music ? "Spotify.exe" : "MSEdge"), setting);
        Assert.Equal(music || setting, search.AutomaticLookupAllowed);
        var result = await fixture.Provider.GetLyrics(track, search: search,
            pinned: pinned ? LyricsRef.Netease(7) : null, ct: TestContext.Current.CancellationToken);
        bool expectedRequests = pinned ? !cached : music || setting;
        Assert.Equal(expectedRequests, requests.Count > 0);
        Assert.Equal(cached || pinned, result != null);
        if (pinned) Assert.All(requests, path => Assert.Contains("lyric/v1", path));
        if (!music && !setting && !cached && !pinned)
            Assert.Equal(SongState.NotSong, SongRecognition.Classify(music, pinned, search.AutomaticLookupAllowed, result != null));
    }

    [Fact]
    public async Task OptOutKeepsUnverifiedLocalLyricsWithoutUpgradingThem()
    {
        using var fixture = new Fixture(_ => throw new Exception("Opt-out must not request the network"));
        Fixture.Age(fixture.Seed(Track()), 2);
        var result = await fixture.Provider.GetLyrics(Track(), search: new LyricsSearch(false, false), ct: TestContext.Current.CancellationToken);
        Assert.Equal("Hello", result?.Lines[0].Text);
    }

    [Fact]
    public async Task ChangingLookupSettingAppliesToTheSameTrack()
    {
        var requests = 0;
        using var fixture = new Fixture(_ => { requests++; return Response("{}"); });
        var track = Track();
        foreach (bool setting in new[] { false, true, false, true })
        {
            int before = requests;
            Assert.Null(await fixture.Provider.GetLyrics(track, search: new LyricsSearch(false, setting), ct: TestContext.Current.CancellationToken));
            Assert.Equal(setting, requests > before);
        }
    }

    static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    sealed class Fixture : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "lyricod-provider-tests", Guid.NewGuid().ToString());
        readonly StubHandler _handler;
        public LyricsProvider Provider { get; }
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _handler = new StubHandler(respond);
            Provider = new LyricsProvider(_dir, "test", _handler);
        }
        public string Seed(Track track, double? score = null, bool? titleMatched = null)
        {
            var file = Path.Combine(_dir, LyricsProvider.CacheKey(track) + ".json");
            File.WriteAllText(file, JsonSerializer.Serialize(new RawLyrics
            {
                Source = "NetEase", Lrc = "[00:01.00]Hello", Ref = LyricsRef.Netease(7), MatchScore = score, TitleMatched = titleMatched,
            }));
            return file;
        }
        /// <summary>Saved <paramref name="days"/> ago, as far as the once-a-day word check is concerned.</summary>
        public static void Age(string file, double days) => File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-days));
        public void Dispose() { _handler.Dispose(); Directory.Delete(_dir, recursive: true); }
    }
}
