using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Lyricod.Core.Tests;

public class LyricsMemoryCacheTests
{
    static Track Song(string title = "Hello", string artist = "Singer") =>
        new() { TrackId = title, Title = title, Artist = artist, Duration = 120 };

    static object Entry(int id, string title = "Hello", string? plain = null) => new
    {
        id, trackName = title, artistName = "Singer", duration = 120,
        syncedLyrics = "[00:01.00]" + title, plainLyrics = plain,
    };

    [Fact]
    public async Task LrclibGrowthIsBoundedAndEvictedPreviewFallsBackToNetwork()
    {
        int searches = 0, fetches = 0;
        using var fixture = new Fixture(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/search")
                return Json(new[] { Entry(++searches) });
            fetches++;
            return Json(Entry(1));
        });
        for (int i = 0; i <= LyricsProvider.LrclibCacheCapacity; i++)
            await fixture.Provider.LrclibCandidates("Hello", Song(), TestContext.Current.CancellationToken);

        var recent = await fixture.Provider.FetchLrclib(searches.ToString(CultureInfo.InvariantCulture), TestContext.Current.CancellationToken);
        Assert.Equal("[00:01.00]Hello", recent?.Lrc);
        Assert.Equal(0, fetches);
        var evicted = await fixture.Provider.FetchLrclib("1", TestContext.Current.CancellationToken);
        Assert.Equal("[00:01.00]Hello", evicted?.Lrc);
        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task AliasGrowthIsBoundedAndReturnedListsCannotGrowTheCache()
    {
        int requests = 0;
        using var fixture = new Fixture(request =>
        {
            requests++;
            var artist = Query(request.RequestUri!, "s");
            return Json(new { result = new { artists = new[] { new { name = artist, alias = new[] { artist + " alias" } } } } });
        });
        for (int i = 0; i <= LyricsProvider.AliasCacheCapacity; i++)
            await fixture.Provider.WithAliases(Song(artist: "Artist" + i), TestContext.Current.CancellationToken);

        int before = requests;
        var recent = await fixture.Provider.WithAliases(Song(artist: "Artist" + LyricsProvider.AliasCacheCapacity), TestContext.Current.CancellationToken);
        Assert.Equal(before, requests);
        Assert.Single(recent.ArtistAliases);
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<string>>(recent.ArtistAliases).Add("caller mutation"));
        var oldest = await fixture.Provider.WithAliases(Song(artist: "Artist0"), TestContext.Current.CancellationToken);
        Assert.Equal(before + 1, requests);
        Assert.Equal("Artist0 alias", Assert.Single(oldest.ArtistAliases));
    }

    [Fact]
    public async Task EvictedPreviewRequestRemainsCancellable()
    {
        int searches = 0;
        var fetching = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/search") return Json(new[] { Entry(++searches) });
            fetching.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(Entry(1));
        });
        for (int i = 0; i <= LyricsProvider.LrclibCacheCapacity; i++)
            await fixture.Provider.LrclibCandidates("Hello", Song(), TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var preview = fixture.Provider.FetchLrclib("1", cancellation.Token);
        await fetching.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview);
    }

    [Fact]
    public async Task ConcurrentSearchesAndPreviewsKeepEachTracksLyrics()
    {
        using var fixture = new Fixture(request =>
        {
            var uri = request.RequestUri!;
            if (uri.Host != "lrclib.net") return Json(new { });
            if (uri.AbsolutePath == "/api/get") return new(HttpStatusCode.NotFound);
            if (uri.AbsolutePath == "/api/search")
            {
                int song = int.Parse(Query(uri, "q").Split(' ')[0][4..], CultureInfo.InvariantCulture);
                return Json(Enumerable.Range(0, 20).Select(offset =>
                    Entry(song * 100 + offset, offset == 0 ? "Song" + song : "Unrelated " + offset)).ToArray());
            }
            int id = int.Parse(uri.Segments[^1], CultureInfo.InvariantCulture);
            return Json(Entry(id, "Song" + id / 100));
        });

        await Task.WhenAll(Enumerable.Range(1, 64).Select(i => Task.Run(async () =>
        {
            var lyrics = await fixture.Provider.GetLyrics(Song("Song" + i), ct: TestContext.Current.CancellationToken);
            Assert.Equal("Song" + i, Assert.Single(Assert.IsType<Lyrics>(lyrics).Lines).Text);
            var preview = await fixture.Provider.FetchLrclib((i * 100).ToString(CultureInfo.InvariantCulture), TestContext.Current.CancellationToken);
            Assert.Equal("[00:01.00]Song" + i, preview?.Lrc);
        }, TestContext.Current.CancellationToken)));
    }

    static string Query(Uri uri, string name) => Uri.UnescapeDataString(uri.Query.TrimStart('?').Split('&')
        .Select(part => part.Split('=', 2)).First(parts => parts[0] == name)[1]);

    static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };

    sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    sealed class Fixture : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "lyricod-memory-cache-tests", Guid.NewGuid().ToString());
        readonly StubHandler _handler;
        public LyricsProvider Provider { get; }

        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request))) { }

        public Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _handler = new StubHandler(respond);
            Provider = new LyricsProvider(_dir, "test", _handler);
        }

        public void Dispose()
        {
            _handler.Dispose();
            Directory.Delete(_dir, recursive: true);
        }
    }
}
