using System.Collections;
using DecodingGif.Core.Editing;

namespace DecodingGif.Core.Models;

public sealed class VirtualHexRowCollection : IList<HexRow>, IList, IReadOnlyList<HexRow>
{
    private readonly byte[] _bytes;
    private readonly IByteEditPolicy _policy;
    private readonly int _bytesPerRow;
    private readonly int _cacheSize;
    private readonly Dictionary<int, (HexRow Row, LinkedListNode<int> Node)> _cache = new();
    private readonly LinkedList<int> _lru = new();

    public VirtualHexRowCollection(byte[] bytes, IByteEditPolicy policy, int bytesPerRow = 16, int cacheSize = 2048)
    {
        _bytes = bytes ?? [];
        _policy = policy;
        _bytesPerRow = Math.Max(1, bytesPerRow);
        _cacheSize = Math.Max(128, cacheSize);
        Count = _bytes.Length == 0 ? 0 : (int)Math.Ceiling(_bytes.Length / (double)_bytesPerRow);
    }

    public int Count { get; }
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    public HexRow this[int index]
    {
        get => GetOrCreate(index);
        set { }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set { }
    }

    public IEnumerator<HexRow> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int IndexOf(HexRow item)
    {
        if (item is null)
            return -1;
        int index = item.Offset / _bytesPerRow;
        return index >= 0 && index < Count ? index : -1;
    }

    public bool Contains(HexRow item) => IndexOf(item) >= 0;

    public void CopyTo(HexRow[] array, int arrayIndex)
    {
        if (array is null)
            throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("Destination array is too small.");

        for (int i = 0; i < Count; i++)
            array[arrayIndex + i] = this[i];
    }

    public void Add(HexRow item) { }
    public void Clear() { }
    public void Insert(int index, HexRow item) { }
    public bool Remove(HexRow item) => false;
    public void RemoveAt(int index) { }

    public int Add(object? value) => -1;
    public bool Contains(object? value) => value is HexRow row && Contains(row);
    public int IndexOf(object? value) => value is HexRow row ? IndexOf(row) : -1;
    public void Insert(int index, object? value) { }
    public void Remove(object? value) { }

    public void CopyTo(Array array, int index)
    {
        if (array is null)
            throw new ArgumentNullException(nameof(array));
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (array.Length - index < Count)
            throw new ArgumentException("Destination array is too small.");

        for (int i = 0; i < Count; i++)
            array.SetValue(this[i], index + i);
    }

    private HexRow GetOrCreate(int index)
    {
        if (index < 0 || index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (_cache.TryGetValue(index, out var cached))
        {
            Touch(cached.Node);
            return cached.Row;
        }

        int offset = index * _bytesPerRow;
        var row = new HexRow(offset, _bytes, _policy);
        var node = _lru.AddFirst(index);
        _cache[index] = (row, node);
        TrimCacheIfNeeded();
        return row;
    }

    private void Touch(LinkedListNode<int> node)
    {
        if (node.List is null || node == _lru.First)
            return;

        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    private void TrimCacheIfNeeded()
    {
        while (_cache.Count > _cacheSize)
        {
            var tail = _lru.Last;
            if (tail is null)
                break;

            _lru.RemoveLast();
            _cache.Remove(tail.Value);
        }
    }
}
