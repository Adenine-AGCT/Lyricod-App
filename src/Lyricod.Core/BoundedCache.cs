using System.Diagnostics.CodeAnalysis;

namespace Lyricod.Core;

/// <summary>A small, thread-safe LRU cache bounded by both entry count and caller-supplied weight.
/// Values must not change their weight while cached. A rejected replacement removes the old value
/// so callers never read stale data under the same key.</summary>
internal sealed class BoundedCache<TKey, TValue> where TKey : notnull
{
    readonly object _gate = new();
    readonly int _capacity;
    readonly long _maxWeight;
    readonly Dictionary<TKey, LinkedListNode<Entry>> _entries = [];
    readonly LinkedList<Entry> _recent = new();
    long _weight;

    sealed record Entry(TKey Key, TValue Value, long Weight);

    public BoundedCache(int capacity, long maxWeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWeight);
        _capacity = capacity;
        _maxWeight = maxWeight;
    }

    public int Count { get { lock (_gate) return _entries.Count; } }
    public long Weight { get { lock (_gate) return _weight; } }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node)) { value = default; return false; }
            _recent.Remove(node);
            _recent.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value, long weight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(weight);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var previous)) Remove(previous);
            // One large response must not flush useful small entries or stay resident forever.
            if (weight > _maxWeight) return;
            while (_entries.Count >= _capacity || weight > _maxWeight - _weight)
                Remove(_recent.Last!);
            var node = _recent.AddFirst(new Entry(key, value, weight));
            _entries.Add(key, node);
            _weight += weight;
        }
    }

    void Remove(LinkedListNode<Entry> node)
    {
        _recent.Remove(node);
        _entries.Remove(node.Value.Key);
        _weight -= node.Value.Weight;
    }
}
