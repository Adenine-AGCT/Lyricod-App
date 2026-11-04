using System.Diagnostics.CodeAnalysis;

namespace Lyricod.Core;

/// <summary>A small, thread-safe LRU cache bounded by entry count.</summary>
internal sealed class BoundedCache<TKey, TValue> where TKey : notnull
{
    readonly object _gate = new();
    readonly int _capacity;
    readonly Dictionary<TKey, LinkedListNode<Entry>> _entries = [];
    readonly LinkedList<Entry> _recent = new();

    sealed record Entry(TKey Key, TValue Value);

    public BoundedCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public int Count { get { lock (_gate) return _entries.Count; } }

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

    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var previous)) Remove(previous);
            if (_entries.Count >= _capacity)
                Remove(_recent.Last!);
            var node = _recent.AddFirst(new Entry(key, value));
            _entries.Add(key, node);
        }
    }

    void Remove(LinkedListNode<Entry> node)
    {
        _recent.Remove(node);
        _entries.Remove(node.Value.Key);
    }
}
