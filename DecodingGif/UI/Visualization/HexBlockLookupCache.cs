using System.Runtime.CompilerServices;
using DecodingGif.Core.Models;

namespace DecodingGif.UI.Visualization;

internal sealed class HexBlockLookup
{
    private readonly GifByteRange[] _sorted;
    private readonly Dictionary<int, GifByteRange> _gceFrameRangeByStart = new();
    public int MaxBlockLength { get; }

    public HexBlockLookup(IEnumerable<GifByteRange> blocks)
    {
        _sorted = blocks.OrderBy(b => b.Start).ToArray();
        MaxBlockLength = _sorted.Length == 0 ? 1 : Math.Max(1, _sorted.Max(b => b.Length));
    }

    public GifByteRange? FindContaining(int offset)
    {
        int lo = 0;
        int hi = _sorted.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            var candidate = _sorted[mid];
            if (offset < candidate.Start)
            {
                hi = mid - 1;
                continue;
            }

            if (offset >= candidate.EndExclusive)
            {
                lo = mid + 1;
                continue;
            }

            return candidate;
        }

        return null;
    }

    public GifByteRange? GetCompleteRangeForOffset(int offset)
    {
        var block = FindContaining(offset);
        if (block is null)
            return null;

        if (block.Kind != GifBlockKind.GraphicControlExtension)
            return block;

        if (_gceFrameRangeByStart.TryGetValue(block.Start, out var cached))
            return cached;

        var computed = BuildFrameRangeFromGce(block);
        _gceFrameRangeByStart[block.Start] = computed;
        return computed;
    }

    private GifByteRange BuildFrameRangeFromGce(GifByteRange gce)
    {
        int start = gce.Start;
        int endExclusive = gce.EndExclusive;
        int startIndex = Array.FindIndex(_sorted, b =>
            b.Kind == GifBlockKind.GraphicControlExtension
            && b.Start == gce.Start
            && b.Length == gce.Length);
        if (startIndex < 0)
            return new GifByteRange(GifBlockKind.GraphicControlExtension, "Frame Segment", start, Math.Max(1, endExclusive - start));

        for (int i = startIndex + 1; i < _sorted.Length; i++)
        {
            var block = _sorted[i];
            if (block.Start < gce.EndExclusive)
                continue;

            if (block.Kind is GifBlockKind.ImageDescriptor or GifBlockKind.LocalColorTable or GifBlockKind.ImageData)
            {
                endExclusive = Math.Max(endExclusive, block.EndExclusive);
                continue;
            }

            if (block.Kind is GifBlockKind.GraphicControlExtension or GifBlockKind.Trailer)
                break;
        }

        return new GifByteRange(GifBlockKind.GraphicControlExtension, "Frame Segment", start, Math.Max(1, endExclusive - start));
    }
}

internal static class HexBlockLookupCache
{
    private static readonly ConditionalWeakTable<IEnumerable<GifByteRange>, HexBlockLookup> Cache = new();

    public static HexBlockLookup Get(IEnumerable<GifByteRange> blocks) =>
        Cache.GetValue(blocks, b => new HexBlockLookup(b));
}
