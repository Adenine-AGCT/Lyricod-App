namespace Lyricod.Core.Tests;

public class BoundedCacheTests
{
    [Fact]
    public void GrowthEvictsLeastRecentlyUsedAndReadsRefreshRecency()
    {
        var cache = new BoundedCache<string, int>(2);
        cache.Set("first", 1);
        cache.Set("second", 2);
        Assert.True(cache.TryGetValue("first", out var first));
        Assert.Equal(1, first);
        cache.Set("third", 3);

        Assert.False(cache.TryGetValue("second", out _));
        Assert.True(cache.TryGetValue("first", out _));
        Assert.True(cache.TryGetValue("third", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void ReplacingAKeyMakesItRecentWithoutEvictingAnotherEntry()
    {
        var cache = new BoundedCache<string, string>(2);
        cache.Set("a", "original");
        cache.Set("b", "other");
        cache.Set("a", "replacement");
        Assert.Equal(2, cache.Count);
        cache.Set("c", "new");

        Assert.False(cache.TryGetValue("b", out _));
        Assert.True(cache.TryGetValue("a", out var replacement));
        Assert.Equal("replacement", replacement);
        Assert.True(cache.TryGetValue("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void ConcurrentReadsAndReplacementsStayWithinCapacity()
    {
        var cache = new BoundedCache<int, int>(32);
        Parallel.For(0, 5000, i =>
        {
            int key = i % 127;
            cache.Set(key, key);
            if (cache.TryGetValue(key, out var value)) Assert.Equal(key, value);
            Assert.InRange(cache.Count, 0, 32);
        });
        Assert.InRange(cache.Count, 1, 32);
    }
}
