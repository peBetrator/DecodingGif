using DecodingGif.Core.Models;
using DecodingGif.Core.Parsing;

namespace DecodingGif.Core.Services;

public sealed class FrameManagerService
{
    private readonly GifStructureService _structureService = new();
    private readonly GifParser _parser = new();
    private readonly GifOptimizationAnalyzer _optimizationAnalyzer = new();

    private sealed class FrameSegment
    {
        public required int FrameIndex { get; init; }
        public required int Start { get; init; }
        public required int EndExclusive { get; init; }
        public required byte[] Bytes { get; init; }
    }

    public FrameEditResult InsertFrame(GifFile file, int insertAtFrameIndex)
    {
        ArgumentNullException.ThrowIfNull(file);
        var ranges = _structureService.BuildRanges(file);
        var segments = BuildFrameSegments(file, ranges);
        if (segments.Count == 0)
            throw new InvalidOperationException("Cannot insert frame: GIF has no frames.");

        int insertIndex = Math.Clamp(insertAtFrameIndex, 0, segments.Count);
        int templateIndex = insertIndex > 0 ? insertIndex - 1 : 0;
        var template = segments[templateIndex];

        // Insert copies previous frame settings (GCE/disposal/delay/LCT) and payload to keep stream valid.
        var inserted = new FrameSegment
        {
            FrameIndex = insertIndex,
            Start = 0,
            EndExclusive = template.Bytes.Length,
            Bytes = (byte[])template.Bytes.Clone()
        };

        segments.Insert(insertIndex, inserted);
        return RebuildAndValidate(file, ranges, segments, $"InsertFrame at {insertIndex}");
    }

    public FrameEditResult DuplicateFrame(GifFile file, int frameIndex)
    {
        ArgumentNullException.ThrowIfNull(file);
        var ranges = _structureService.BuildRanges(file);
        var segments = BuildFrameSegments(file, ranges);
        if (segments.Count == 0)
            throw new InvalidOperationException("Cannot duplicate frame: GIF has no frames.");
        if (frameIndex < 0 || frameIndex >= segments.Count)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        var source = segments[frameIndex];
        var duplicated = new FrameSegment
        {
            FrameIndex = frameIndex + 1,
            Start = 0,
            EndExclusive = source.Bytes.Length,
            Bytes = (byte[])source.Bytes.Clone()
        };

        segments.Insert(frameIndex + 1, duplicated);
        return RebuildAndValidate(file, ranges, segments, $"DuplicateFrame {frameIndex}");
    }

    public FrameEditResult DeleteFrame(GifFile file, int frameIndex)
    {
        ArgumentNullException.ThrowIfNull(file);
        var ranges = _structureService.BuildRanges(file);
        var segments = BuildFrameSegments(file, ranges);
        if (segments.Count <= 1)
            throw new InvalidOperationException("Cannot delete frame: GIF must contain at least one frame.");
        if (frameIndex < 0 || frameIndex >= segments.Count)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        segments.RemoveAt(frameIndex);
        return RebuildAndValidate(file, ranges, segments, $"DeleteFrame {frameIndex}");
    }

    public FrameEditResult MoveFrameUp(GifFile file, int frameIndex)
    {
        ArgumentNullException.ThrowIfNull(file);
        var ranges = _structureService.BuildRanges(file);
        var segments = BuildFrameSegments(file, ranges);
        if (segments.Count < 2)
            throw new InvalidOperationException("Cannot reorder frames: GIF has fewer than 2 frames.");
        if (frameIndex <= 0 || frameIndex >= segments.Count)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        (segments[frameIndex - 1], segments[frameIndex]) = (segments[frameIndex], segments[frameIndex - 1]);
        return RebuildAndValidate(file, ranges, segments, $"MoveFrameUp {frameIndex}");
    }

    public FrameEditResult MoveFrameDown(GifFile file, int frameIndex)
    {
        ArgumentNullException.ThrowIfNull(file);
        var ranges = _structureService.BuildRanges(file);
        var segments = BuildFrameSegments(file, ranges);
        if (segments.Count < 2)
            throw new InvalidOperationException("Cannot reorder frames: GIF has fewer than 2 frames.");
        if (frameIndex < 0 || frameIndex >= segments.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        (segments[frameIndex], segments[frameIndex + 1]) = (segments[frameIndex + 1], segments[frameIndex]);
        return RebuildAndValidate(file, ranges, segments, $"MoveFrameDown {frameIndex}");
    }

    private FrameEditResult RebuildAndValidate(
        GifFile sourceFile,
        IReadOnlyList<GifByteRange> sourceRanges,
        IReadOnlyList<FrameSegment> reorderedSegments,
        string operation)
    {
        byte[] rebuiltBytes = RebuildBytes(sourceFile.Bytes, sourceRanges, reorderedSegments);
        GifFile updated = _parser.Parse(sourceFile.FilePath, rebuiltBytes);
        var updatedRanges = _structureService.BuildRanges(updated);
        var validation = ValidateAnimationSequence(updated, updatedRanges);

        if (!validation.IsValid)
        {
            string errors = string.Join("; ", validation.Errors);
            throw new InvalidOperationException($"Frame operation '{operation}' produced invalid GIF: {errors}");
        }

        return new FrameEditResult
        {
            UpdatedFile = updated,
            UpdatedRanges = updatedRanges,
            Validation = validation,
            OperationDescription = operation
        };
    }

    private static byte[] RebuildBytes(
        byte[] originalBytes,
        IReadOnlyList<GifByteRange> ranges,
        IReadOnlyList<FrameSegment> segments)
    {
        if (segments.Count == 0)
            throw new InvalidOperationException("Cannot rebuild GIF: no frame segments.");

        int firstFrameStart = segments.Min(s => s.Start);
        int lastFrameEnd = segments.Max(s => s.EndExclusive);
        int trailerStart = ranges.FirstOrDefault(r => r.Kind == GifBlockKind.Trailer)?.Start ?? originalBytes.Length;
        trailerStart = Math.Max(trailerStart, lastFrameEnd);

        var output = new List<byte>(originalBytes.Length + segments.Sum(s => s.Bytes.Length));

        output.AddRange(originalBytes.AsSpan(0, firstFrameStart).ToArray());
        foreach (var segment in segments)
            output.AddRange(segment.Bytes);

        if (trailerStart < originalBytes.Length)
            output.AddRange(originalBytes.AsSpan(trailerStart).ToArray());

        return output.ToArray();
    }

    private List<FrameSegment> BuildFrameSegments(GifFile file, IReadOnlyList<GifByteRange> ranges)
    {
        var ordered = ranges.OrderBy(r => r.Start).ToList();
        var imageDescriptors = ordered
            .Select((range, idx) => (range, idx))
            .Where(x => x.range.Kind == GifBlockKind.ImageDescriptor)
            .ToList();

        if (imageDescriptors.Count == 0)
            return [];

        int trailerStart = ordered.FirstOrDefault(r => r.Kind == GifBlockKind.Trailer)?.Start ?? file.Bytes.Length;
        var segments = new List<FrameSegment>(imageDescriptors.Count);
        for (int i = 0; i < imageDescriptors.Count; i++)
        {
            int descriptorListIndex = imageDescriptors[i].idx;
            int descriptorStart = imageDescriptors[i].range.Start;
            int frameStart = descriptorStart;

            if (descriptorListIndex > 0 && ordered[descriptorListIndex - 1].Kind == GifBlockKind.GraphicControlExtension)
                frameStart = ordered[descriptorListIndex - 1].Start;

            int nextDescriptorStart = i < imageDescriptors.Count - 1
                ? imageDescriptors[i + 1].range.Start
                : trailerStart;

            // If next descriptor has GCE directly before it, next frame starts there.
            if (i < imageDescriptors.Count - 1)
            {
                int nextDescriptorListIndex = imageDescriptors[i + 1].idx;
                if (nextDescriptorListIndex > 0 && ordered[nextDescriptorListIndex - 1].Kind == GifBlockKind.GraphicControlExtension)
                    nextDescriptorStart = ordered[nextDescriptorListIndex - 1].Start;
            }

            int endExclusive = Math.Clamp(nextDescriptorStart, frameStart + 1, trailerStart);
            var bytes = new byte[endExclusive - frameStart];
            Array.Copy(file.Bytes, frameStart, bytes, 0, bytes.Length);
            segments.Add(new FrameSegment
            {
                FrameIndex = i,
                Start = frameStart,
                EndExclusive = endExclusive,
                Bytes = bytes
            });
        }

        return segments;
    }

    private FrameEditValidationResult ValidateAnimationSequence(GifFile file, IReadOnlyList<GifByteRange> ranges)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        int frameCount = ranges.Count(r => r.Kind == GifBlockKind.ImageDescriptor);
        if (frameCount == 0)
            errors.Add("No image frames found after edit.");

        var trailer = ranges.FirstOrDefault(r => r.Kind == GifBlockKind.Trailer);
        if (trailer is null)
            errors.Add("GIF trailer is missing.");
        else if (trailer.Start != file.Bytes.Length - 1)
            warnings.Add("Trailer is not the last byte of file.");

        foreach (var gce in ranges.Where(r => r.Kind == GifBlockKind.GraphicControlExtension && r.Length >= 8))
        {
            if (gce.Start + 3 >= file.Bytes.Length)
            {
                errors.Add($"Truncated GCE at 0x{gce.Start:X8}.");
                continue;
            }

            int disposal = (file.Bytes[gce.Start + 3] >> 2) & 0b111;
            if (disposal > 3)
                warnings.Add($"GCE at 0x{gce.Start:X8} uses reserved disposal method {disposal}.");
        }

        // Reuse existing analyzer signal for disposal consistency guidance.
        var optimization = _optimizationAnalyzer.AnalyzeFile(file, ranges);
        foreach (var suggestion in optimization.Suggestions.Where(s => s.Type == OptimizationType.DisposalMethod))
            warnings.Add(suggestion.Recommendation);

        return new FrameEditValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }
}
