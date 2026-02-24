using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DecodingGif.Core.Models;
using MediaColor = System.Windows.Media.Color;

namespace DecodingGif.Core.Services;

public sealed class BatchColorOperationService
{
    private sealed record FramePaletteInfo(
        int FrameIndex,
        GifByteRange ImageDescriptor,
        GifByteRange? LocalColorTable,
        GifByteRange? ImageData,
        int Width,
        int Height);

    public BatchColorOperationResult ReplaceColorInstances(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        PaletteTarget paletteType,
        RgbColor oldColor,
        RgbColor newColor,
        IProgress<BatchColorOperationProgress>? progress = null)
    {
        var context = ResolvePaletteContext(file, blocks, paletteType);
        var changed = new List<int>();
        for (int i = 0; i < context.PaletteSize; i++)
        {
            progress?.Report(new BatchColorOperationProgress("ReplaceColorInstances", i + 1, context.PaletteSize, $"Checking color #{i}"));
            var color = ReadColor(file.Bytes, context.Range.Start, i);
            if (color != oldColor)
                continue;
            WriteColor(file.Bytes, context.Range.Start, i, newColor);
            changed.Add(i);
        }

        return new BatchColorOperationResult
        {
            Operation = $"ReplaceColorInstances {oldColor} -> {newColor}",
            PaletteSize = context.PaletteSize,
            AffectedColorIndexes = changed
        };
    }

    public BatchColorOperationResult AdjustBrightness(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        PaletteTarget paletteType,
        IReadOnlyCollection<int> colorIndexes,
        double factor,
        IProgress<BatchColorOperationProgress>? progress = null)
    {
        if (factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor), "Brightness factor must be > 0.");

        var context = ResolvePaletteContext(file, blocks, paletteType);
        var indexes = NormalizeIndexes(colorIndexes, context.PaletteSize);
        var changed = new List<int>(indexes.Count);
        for (int i = 0; i < indexes.Count; i++)
        {
            int paletteIndex = indexes[i];
            progress?.Report(new BatchColorOperationProgress("AdjustBrightness", i + 1, indexes.Count, $"Adjusting color #{paletteIndex}"));
            var color = ReadColor(file.Bytes, context.Range.Start, paletteIndex);
            var adjusted = AdjustBrightnessPreserveHue(color, factor);
            if (adjusted == color)
                continue;
            WriteColor(file.Bytes, context.Range.Start, paletteIndex, adjusted);
            changed.Add(paletteIndex);
        }

        return new BatchColorOperationResult
        {
            Operation = $"AdjustBrightness x{factor.ToString("0.###", CultureInfo.InvariantCulture)}",
            PaletteSize = context.PaletteSize,
            AffectedColorIndexes = changed
        };
    }

    public BatchColorOperationResult AdjustContrast(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        PaletteTarget paletteType,
        IReadOnlyCollection<int> colorIndexes,
        double factor,
        IProgress<BatchColorOperationProgress>? progress = null)
    {
        if (factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor), "Contrast factor must be > 0.");

        var context = ResolvePaletteContext(file, blocks, paletteType);
        var indexes = NormalizeIndexes(colorIndexes, context.PaletteSize);
        var changed = new List<int>(indexes.Count);
        for (int i = 0; i < indexes.Count; i++)
        {
            int paletteIndex = indexes[i];
            progress?.Report(new BatchColorOperationProgress("AdjustContrast", i + 1, indexes.Count, $"Adjusting color #{paletteIndex}"));
            var color = ReadColor(file.Bytes, context.Range.Start, paletteIndex);
            var adjusted = new RgbColor(
                ApplyContrast(color.R, factor),
                ApplyContrast(color.G, factor),
                ApplyContrast(color.B, factor));
            if (adjusted == color)
                continue;
            WriteColor(file.Bytes, context.Range.Start, paletteIndex, adjusted);
            changed.Add(paletteIndex);
        }

        return new BatchColorOperationResult
        {
            Operation = $"AdjustContrast x{factor.ToString("0.###", CultureInfo.InvariantCulture)}",
            PaletteSize = context.PaletteSize,
            AffectedColorIndexes = changed
        };
    }

    public BatchColorOperationResult ShiftHue(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        PaletteTarget paletteType,
        IReadOnlyCollection<int> colorIndexes,
        double degrees,
        IProgress<BatchColorOperationProgress>? progress = null)
    {
        var context = ResolvePaletteContext(file, blocks, paletteType);
        var indexes = NormalizeIndexes(colorIndexes, context.PaletteSize);
        var changed = new List<int>(indexes.Count);
        for (int i = 0; i < indexes.Count; i++)
        {
            int paletteIndex = indexes[i];
            progress?.Report(new BatchColorOperationProgress("ShiftHue", i + 1, indexes.Count, $"Shifting color #{paletteIndex}"));
            var color = ReadColor(file.Bytes, context.Range.Start, paletteIndex);
            var shifted = ShiftHue(color, degrees);
            if (shifted == color)
                continue;
            WriteColor(file.Bytes, context.Range.Start, paletteIndex, shifted);
            changed.Add(paletteIndex);
        }

        return new BatchColorOperationResult
        {
            Operation = $"ShiftHue {degrees.ToString("0.###", CultureInfo.InvariantCulture)}deg",
            PaletteSize = context.PaletteSize,
            AffectedColorIndexes = changed
        };
    }

    public BatchColorOperationResult QuantizeColors(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        PaletteTarget paletteType,
        int targetCount,
        IProgress<BatchColorOperationProgress>? progress = null)
    {
        var context = ResolvePaletteContext(file, blocks, paletteType);
        if (targetCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetCount), "Target color count must be > 0.");
        targetCount = Math.Min(targetCount, context.PaletteSize);

        var colors = Enumerable.Range(0, context.PaletteSize)
            .Select(i => (Index: i, Color: ReadColor(file.Bytes, context.Range.Start, i)))
            .ToList();

        var unique = colors.Select(c => c.Color).Distinct().ToList();
        if (unique.Count <= targetCount)
            return BatchColorOperationResult.Empty($"QuantizeColors target={targetCount}", context.PaletteSize);

        var centroids = InitializeCentroids(unique, targetCount);
        var assignments = new int[colors.Count];
        for (int iteration = 0; iteration < 8; iteration++)
        {
            progress?.Report(new BatchColorOperationProgress("QuantizeColors", iteration + 1, 8, $"Iteration {iteration + 1}/8"));

            for (int i = 0; i < colors.Count; i++)
                assignments[i] = FindNearestCentroid(colors[i].Color, centroids);

            for (int c = 0; c < centroids.Count; c++)
            {
                var cluster = colors.Where((_, idx) => assignments[idx] == c).Select(x => x.Color).ToList();
                if (cluster.Count == 0)
                    continue;
                centroids[c] = Average(cluster);
            }
        }

        var changed = new List<int>();
        for (int i = 0; i < colors.Count; i++)
        {
            var mapped = centroids[assignments[i]];
            if (mapped == colors[i].Color)
                continue;
            WriteColor(file.Bytes, context.Range.Start, colors[i].Index, mapped);
            changed.Add(colors[i].Index);
        }

        return new BatchColorOperationResult
        {
            Operation = $"QuantizeColors target={targetCount}",
            PaletteSize = context.PaletteSize,
            AffectedColorIndexes = changed
        };
    }

    public IReadOnlyList<int> DetectUnusedColors(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        PaletteTarget paletteType,
        IProgress<BatchColorOperationProgress>? progress = null)
    {
        var context = ResolvePaletteContext(file, blocks, paletteType);
        var used = new bool[context.PaletteSize];
        var frames = BuildFrameInfos(file, blocks);

        if (paletteType.PaletteType == PaletteTargetType.GlobalColorTable)
        {
            var gctFrames = frames.Where(f => f.LocalColorTable is null).ToList();
            for (int i = 0; i < gctFrames.Count; i++)
            {
                var frame = gctFrames[i];
                progress?.Report(new BatchColorOperationProgress("DetectUnusedColors", i + 1, Math.Max(gctFrames.Count, 1), $"Analyzing GCT frame {frame.FrameIndex}"));
                MarkUsedFromFrame(file.Bytes, frame, used);
            }
        }
        else
        {
            if (!paletteType.FrameIndex.HasValue)
                throw new ArgumentException("FrameIndex is required for LocalColorTable target.", nameof(paletteType));
            var frame = frames.FirstOrDefault(f => f.FrameIndex == paletteType.FrameIndex.Value && f.LocalColorTable is not null);
            if (frame is null)
                throw new InvalidOperationException($"LCT frame {paletteType.FrameIndex.Value} was not found.");
            progress?.Report(new BatchColorOperationProgress("DetectUnusedColors", 1, 1, $"Analyzing LCT frame {frame.FrameIndex}"));
            MarkUsedFromFrame(file.Bytes, frame, used);
        }

        return Enumerable.Range(0, used.Length).Where(i => !used[i]).ToArray();
    }

    private static void MarkUsedFromFrame(byte[] bytes, FramePaletteInfo frame, bool[] used)
    {
        if (frame.ImageData is null || frame.ImageData.Length <= 1)
            return;

        int expectedPixels = Math.Max(1, frame.Width * frame.Height);
        var indices = DecodeImageIndices(bytes, frame.ImageData, expectedPixels);
        foreach (int index in indices)
        {
            if (index < 0 || index >= used.Length)
                continue;
            used[index] = true;
        }
    }

    private static IReadOnlyList<int> DecodeImageIndices(byte[] bytes, GifByteRange imageData, int maxOutput)
    {
        int start = imageData.Start;
        int end = Math.Min(imageData.Start + imageData.Length, bytes.Length);
        if (start < 0 || start >= end || start + 1 >= end)
            return Array.Empty<int>();

        int lzwMinCodeSize = bytes[start];
        var compressed = new List<byte>();
        int pos = start + 1;
        while (pos < end)
        {
            int blockSize = bytes[pos++];
            if (blockSize == 0)
                break;
            int take = Math.Min(blockSize, end - pos);
            if (take <= 0)
                break;
            compressed.AddRange(bytes.AsSpan(pos, take).ToArray());
            pos += take;
        }

        return LzwDecode(compressed.ToArray(), lzwMinCodeSize, maxOutput);
    }

    private static IReadOnlyList<int> LzwDecode(byte[] data, int minCodeSize, int maxOutput)
    {
        if (data.Length == 0 || minCodeSize is < 2 or > 8)
            return Array.Empty<int>();

        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;
        int nextCode = endCode + 1;
        int codeSize = minCodeSize + 1;

        var dict = new List<int[]>(4096);
        InitializeDictionary(dict, clearCode);

        var output = new List<int>(Math.Min(maxOutput, 4096));
        int bitPos = 0;
        int[]? prev = null;

        while (TryReadCode(data, ref bitPos, codeSize, out int code))
        {
            if (code == clearCode)
            {
                InitializeDictionary(dict, clearCode);
                nextCode = endCode + 1;
                codeSize = minCodeSize + 1;
                prev = null;
                continue;
            }

            if (code == endCode)
                break;

            int[] entry;
            if (code < dict.Count && dict[code].Length > 0)
            {
                entry = dict[code];
            }
            else if (code == nextCode && prev is not null && prev.Length > 0)
            {
                entry = Append(prev, prev[0]);
            }
            else
            {
                break;
            }

            foreach (int value in entry)
            {
                output.Add(value);
                if (output.Count >= maxOutput)
                    return output;
            }

            if (prev is not null && prev.Length > 0 && nextCode < 4096)
            {
                var combined = Append(prev, entry[0]);
                if (dict.Count <= nextCode)
                    dict.Add(combined);
                else
                    dict[nextCode] = combined;
                nextCode++;
                if (nextCode == (1 << codeSize) && codeSize < 12)
                    codeSize++;
            }

            prev = entry;
        }

        return output;
    }

    private static void InitializeDictionary(List<int[]> dict, int clearCode)
    {
        dict.Clear();
        for (int i = 0; i < clearCode; i++)
            dict.Add([i]);
        dict.Add([]); // clear code placeholder
        dict.Add([]); // end code placeholder
    }

    private static bool TryReadCode(byte[] data, ref int bitPos, int codeSize, out int code)
    {
        code = 0;
        int totalBits = data.Length * 8;
        if (bitPos + codeSize > totalBits)
            return false;

        for (int i = 0; i < codeSize; i++)
        {
            int absolute = bitPos + i;
            int b = data[absolute / 8];
            int bit = (b >> (absolute % 8)) & 1;
            code |= (bit << i);
        }

        bitPos += codeSize;
        return true;
    }

    private static int[] Append(int[] source, int value)
    {
        var result = new int[source.Length + 1];
        Array.Copy(source, result, source.Length);
        result[^1] = value;
        return result;
    }

    private static byte ApplyContrast(byte channel, double factor)
    {
        double centered = ((channel / 255.0) - 0.5) * factor + 0.5;
        return (byte)Math.Clamp((int)Math.Round(centered * 255), 0, 255);
    }

    private static RgbColor AdjustBrightnessPreserveHue(RgbColor color, double factor)
    {
        var media = MediaColor.FromRgb(color.R, color.G, color.B);
        RgbToHsv(media, out double h, out double s, out double v);
        v = Math.Clamp(v * factor, 0.0, 1.0);
        var adjusted = HsvToRgb(h, s, v);
        return new RgbColor(adjusted.R, adjusted.G, adjusted.B);
    }

    private static RgbColor ShiftHue(RgbColor color, double degrees)
    {
        var media = MediaColor.FromRgb(color.R, color.G, color.B);
        RgbToHsv(media, out double h, out double s, out double v);
        h = (h + degrees + 360.0) % 360.0;
        var shifted = HsvToRgb(h, s, v);
        return new RgbColor(shifted.R, shifted.G, shifted.B);
    }

    private static void RgbToHsv(MediaColor rgb, out double h, out double s, out double v)
    {
        double r = rgb.R / 255.0;
        double g = rgb.G / 255.0;
        double b = rgb.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        h = 0;
        if (delta > 0)
        {
            if (Math.Abs(max - r) < double.Epsilon)
                h = 60 * (((g - b) / delta) % 6);
            else if (Math.Abs(max - g) < double.Epsilon)
                h = 60 * (((b - r) / delta) + 2);
            else
                h = 60 * (((r - g) / delta) + 4);
        }
        if (h < 0)
            h += 360;

        s = max == 0 ? 0 : delta / max;
        v = max;
    }

    private static MediaColor HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        double m = v - c;
        (double r1, double g1, double b1) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return MediaColor.FromRgb(
            (byte)Math.Clamp((int)Math.Round((r1 + m) * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round((g1 + m) * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round((b1 + m) * 255), 0, 255));
    }

    private static List<RgbColor> InitializeCentroids(IReadOnlyList<RgbColor> points, int k)
    {
        var centroids = new List<RgbColor>(k) { points[0] };
        while (centroids.Count < k)
        {
            double bestDistance = -1;
            RgbColor bestPoint = points[0];
            foreach (var point in points)
            {
                double dist = centroids.Min(c => DistanceSq(point, c));
                if (dist <= bestDistance)
                    continue;
                bestDistance = dist;
                bestPoint = point;
            }

            centroids.Add(bestPoint);
        }

        return centroids;
    }

    private static int FindNearestCentroid(RgbColor color, IReadOnlyList<RgbColor> centroids)
    {
        int bestIndex = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < centroids.Count; i++)
        {
            double distance = DistanceSq(color, centroids[i]);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static RgbColor Average(IReadOnlyList<RgbColor> colors)
    {
        int r = 0;
        int g = 0;
        int b = 0;
        foreach (var color in colors)
        {
            r += color.R;
            g += color.G;
            b += color.B;
        }

        int count = colors.Count;
        return new RgbColor((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    private static double DistanceSq(RgbColor a, RgbColor b)
    {
        int dr = a.R - b.R;
        int dg = a.G - b.G;
        int db = a.B - b.B;
        return (dr * dr) + (dg * dg) + (db * db);
    }

    private static RgbColor ReadColor(byte[] bytes, int paletteStart, int colorIndex)
    {
        int offset = paletteStart + (colorIndex * 3);
        return new RgbColor(bytes[offset], bytes[offset + 1], bytes[offset + 2]);
    }

    private static void WriteColor(byte[] bytes, int paletteStart, int colorIndex, RgbColor color)
    {
        int offset = paletteStart + (colorIndex * 3);
        bytes[offset] = color.R;
        bytes[offset + 1] = color.G;
        bytes[offset + 2] = color.B;
    }

    private static List<int> NormalizeIndexes(IReadOnlyCollection<int> colorIndexes, int paletteSize)
    {
        if (colorIndexes.Count == 0)
            return Enumerable.Range(0, paletteSize).ToList();

        var normalized = colorIndexes.Distinct().OrderBy(i => i).ToList();
        if (normalized.Any(i => i < 0 || i >= paletteSize))
            throw new ArgumentOutOfRangeException(nameof(colorIndexes), $"Color index must be in range 0..{paletteSize - 1}.");
        return normalized;
    }

    private (GifByteRange Range, int PaletteSize) ResolvePaletteContext(GifFile file, IReadOnlyList<GifByteRange> blocks, PaletteTarget paletteType)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(blocks);

        GifByteRange? range = paletteType.PaletteType switch
        {
            PaletteTargetType.GlobalColorTable => ResolveGlobalColorTableRange(file, blocks),
            PaletteTargetType.LocalColorTable => ResolveLocalColorTableRange(file, blocks, paletteType.FrameIndex),
            _ => null
        };

        if (range is null)
            throw new InvalidOperationException("Palette range could not be resolved.");
        if (range.Length < 3)
            throw new InvalidOperationException("Palette is empty.");
        if (range.Start < 0 || range.Start + range.Length > file.Bytes.Length)
            throw new InvalidOperationException("Palette range is outside of GIF bytes.");

        return (range, range.Length / 3);
    }

    private static GifByteRange? ResolveGlobalColorTableRange(GifFile file, IReadOnlyList<GifByteRange> blocks)
    {
        var gct = blocks.FirstOrDefault(b => b.Kind == GifBlockKind.GlobalColorTable);
        if (gct is not null)
            return gct;

        if (!file.Screen.GlobalColorTableFlag)
            return null;

        const int gctStart = 13;
        int expected = file.Screen.GlobalColorTableSize * 3;
        int safeLength = Math.Min(expected, Math.Max(0, file.Bytes.Length - gctStart));
        return safeLength >= 3 ? new GifByteRange(GifBlockKind.GlobalColorTable, "Derived GCT", gctStart, safeLength) : null;
    }

    private GifByteRange? ResolveLocalColorTableRange(GifFile file, IReadOnlyList<GifByteRange> blocks, int? frameIndex)
    {
        if (!frameIndex.HasValue)
            throw new ArgumentException("FrameIndex is required for LocalColorTable target.", nameof(frameIndex));

        var frames = BuildFrameInfos(file, blocks);
        var frame = frames.FirstOrDefault(f => f.FrameIndex == frameIndex.Value);
        if (frame is null)
            throw new InvalidOperationException($"Frame {frameIndex.Value} was not found.");
        if (frame.LocalColorTable is null)
            throw new InvalidOperationException($"Frame {frameIndex.Value} does not have Local Color Table.");
        return frame.LocalColorTable;
    }

    private List<FramePaletteInfo> BuildFrameInfos(GifFile file, IReadOnlyList<GifByteRange> blocks)
    {
        var ordered = blocks.OrderBy(b => b.Start).ToList();
        var frames = new List<FramePaletteInfo>();
        int frameIndex = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            var block = ordered[i];
            if (block.Kind != GifBlockKind.ImageDescriptor)
                continue;

            if (!TryReadImageDescriptorSize(file.Bytes, block, out int width, out int height))
                continue;

            GifByteRange? lct = null;
            GifByteRange? imageData = null;
            int next = i + 1;
            if (next < ordered.Count && ordered[next].Kind == GifBlockKind.LocalColorTable)
            {
                lct = ordered[next];
                next++;
            }

            if (next < ordered.Count && ordered[next].Kind == GifBlockKind.ImageData)
                imageData = ordered[next];

            frames.Add(new FramePaletteInfo(frameIndex, block, lct, imageData, width, height));
            frameIndex++;
        }

        return frames;
    }

    private static bool TryReadImageDescriptorSize(byte[] bytes, GifByteRange descriptor, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (descriptor.Start < 0 || descriptor.Start + 9 >= bytes.Length || descriptor.Length < 10)
            return false;

        int s = descriptor.Start;
        width = bytes[s + 5] | (bytes[s + 6] << 8);
        height = bytes[s + 7] | (bytes[s + 8] << 8);
        return width > 0 && height > 0;
    }
}
