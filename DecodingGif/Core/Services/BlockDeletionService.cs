using DecodingGif.Core.Models;
using DecodingGif.Core.Parsing;

namespace DecodingGif.Core.Services;

public sealed class BlockDeletionService
{
    private readonly GifParser _parser = new();
    private readonly GifStructureService _structureService = new();

    public GifFile DeleteBlock(GifFile file, GifByteRange block)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(block);

        byte[] originalBytes = file.Bytes;
        byte[] newBytes = new byte[originalBytes.Length - block.Length];

        Array.Copy(originalBytes, 0, newBytes, 0, block.Start);
        Array.Copy(
            originalBytes,
            block.Start + block.Length,
            newBytes,
            block.Start,
            originalBytes.Length - block.Start - block.Length);

        return _parser.Parse(file.FilePath, newBytes);
    }

    public List<GifByteRange> UpdateBlockPositions(List<GifByteRange> blocks, int deletedStart, int deletedLength)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        return blocks
            .Where(block => block.Start != deletedStart)
            .Select(block => block.Start > deletedStart
                ? block with { Start = block.Start - deletedLength }
                : block)
            .ToList();
    }

    public IReadOnlyList<GifByteRange> RegenerateStructure(GifFile file) =>
        _structureService.BuildRanges(file);

    public GifFile RestoreBlock(GifFile file, int insertStart, byte[] deletedBytes)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(deletedBytes);

        byte[] originalBytes = file.Bytes;
        byte[] restoredBytes = new byte[originalBytes.Length + deletedBytes.Length];

        Array.Copy(originalBytes, 0, restoredBytes, 0, insertStart);
        Array.Copy(deletedBytes, 0, restoredBytes, insertStart, deletedBytes.Length);
        Array.Copy(
            originalBytes,
            insertStart,
            restoredBytes,
            insertStart + deletedBytes.Length,
            originalBytes.Length - insertStart);

        return _parser.Parse(file.FilePath, restoredBytes);
    }
}
