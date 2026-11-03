namespace Lyricod.Core.Tests;

public class BoundedCacheTests
{
    [Fact]
    public void GrowthEvictsLeastRecentlyUsedAndReadsRefreshRecency()
    {
        var cache = new BoundedCache<string, int>(2, 100);
        cache.Set("first", 1, 1);
        cache.Set("second", 2, 1);
        Assert.True(cache.TryGetValue("first", out var first));
        Assert.Equal(1, first);
        cache.Set("third", 3, 1);

        Assert.False(cache.TryGetValue("second", out _));
        Assert.True(cache.TryGetValue("first", out _));
        Assert.True(cache.TryGetValue("third", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void WeightLimitCanEvictSeveralEntriesBeforeCountLimit()
    {
        var cache = new BoundedCache<string, int>(10, 10);
        cache.Set("first", 1, 4);
        cache.Set("second", 2, 4);
        cache.Set("third", 3, 7);

        Assert.False(cache.TryGetValue("first", out _));
        Assert.False(cache.TryGetValue("second", out _));
        Assert.True(cache.TryGetValue("third", out _));
        Assert.Equal(1, cache.Count);
        Assert.Equal(7, cache.Weight);
    }

    [Fact]
    public void ReplacingAKeyReaccountsWeightAndMakesItRecent()
    {
        var cache = new BoundedCache<string, string>(3, 10);
        cache.Set("a", "original", 6);
        cache.Set("b", "other", 3);
        cache.Set("a", "smaller", 2);
        cache.Set("c", "new", 5);
        Assert.Equal(3, cache.Count);
        Assert.Equal(10, cache.Weight);
        Assert.True(cache.TryGetValue("a", out var replacement));
        Assert.Equal("smaller", replacement);

        cache.Set("a", "larger", 8);
        Assert.Equal(1, cache.Count);
        Assert.Equal(8, cache.Weight);
        Assert.False(cache.TryGetValue("b", out _));
        Assert.False(cache.TryGetValue("c", out _));
        Assert.True(cache.TryGetValue("a", out replacement));
        Assert.Equal("larger", replacement);
    }

    [Fact]
    public void OversizedReplacementDropsStaleValueWithoutFlushingOtherEntries()
    {
        var cache = new BoundedCache<string, int>(3, 10);
        cache.Set("a", 1, 2);
        cache.Set("b", 2, 3);
        cache.Set("a", 3, 11);
        cache.Set("too-large", 4, 100);

        Assert.False(cache.TryGetValue("a", out _));
        Assert.False(cache.TryGetValue("too-large", out _));
        Assert.True(cache.TryGetValue("b", out var retained));
        Assert.Equal(2, retained);
        Assert.Equal(1, cache.Count);
        Assert.Equal(3, cache.Weight);
    }

    [Fact]
    public void ZeroWeightEntriesStillRespectCountBound()
    {
        var cache = new BoundedCache<int, int>(3, 1);
        for (int i = 0; i < 1000; i++) cache.Set(i, i, 0);
        Assert.Equal(3, cache.Count);
        Assert.Equal(0, cache.Weight);
        Assert.False(cache.TryGetValue(996, out _));
        Assert.True(cache.TryGetValue(997, out _));
        Assert.True(cache.TryGetValue(999, out _));
    }

    [Fact]
    public void InvalidWeightDoesNotRemoveExistingValueAndLargeBudgetsDoNotOverflow()
    {
        var cache = new BoundedCache<string, int>(3, long.MaxValue);
        cache.Set("a", 1, long.MaxValue - 1);
        cache.Set("b", 2, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Set("a", 3, -1));
        Assert.True(cache.TryGetValue("a", out var original));
        Assert.Equal(1, original);
        Assert.Equal(long.MaxValue, cache.Weight);

        cache.Set("c", 3, long.MaxValue);
        Assert.Equal(1, cache.Count);
        Assert.Equal(long.MaxValue, cache.Weight);
        Assert.True(cache.TryGetValue("c", out _));
    }

    [Fact]
    public void ConcurrentReadsAndReplacementsStayWithinBothBounds()
    {
        var cache = new BoundedCache<int, int>(32, 128);
        Parallel.For(0, 5000, i =>
        {
            int key = i % 127;
            cache.Set(key, key, i % 9 + 1);
            if (cache.TryGetValue(key, out var value)) Assert.Equal(key, value);
            Assert.InRange(cache.Count, 0, 32);
            Assert.InRange(cache.Weight, 0L, 128L);
        });
        Assert.InRange(cache.Count, 1, 32);
        Assert.InRange(cache.Weight, 1L, 128L);
    }
}
