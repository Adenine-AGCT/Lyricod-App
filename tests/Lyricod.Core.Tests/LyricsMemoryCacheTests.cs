using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Lyricod.Core.Tests;

public class LyricsMemoryCacheTests
{
    static Track Song(string artist = "Singer") =>
        new() { Title = "Hello", Artist = artist, Duration = 120 };

    static object Entry(int id) => new
    {
        id, trackName = "Hello", artistName = "Singer", duration = 120,
        syncedLyrics = "[00:01.00]Hello",
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
    public async Task AliasGrowthIsBoundedAndEvictedArtistFallsBackToNetwork()
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
        var oldest = await fixture.Provider.WithAliases(Song(artist: "Artist0"), TestContext.Current.CancellationToken);
        Assert.Equal(before + 1, requests);
        Assert.Equal("Artist0 alias", Assert.Single(oldest.ArtistAliases));
    }

    static string Query(Uri uri, string name) => Uri.UnescapeDataString(uri.Query.TrimStart('?').Split('&')
        .Select(part => part.Split('=', 2)).First(parts => parts[0] == name)[1]);

    static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };

    sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    sealed class Fixture : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "lyricod-memory-cache-tests", Guid.NewGuid().ToString());
        readonly StubHandler _handler;
        public LyricsProvider Provider { get; }

        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
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
