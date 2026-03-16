using DecodingGif.Core.Models;
using DecodingGif.Core.Services;

namespace DecodingGif.UI.UndoRedo.Commands;

public sealed class DeleteBlockCommand : IUndoableCommand
{
    private readonly BlockDeletionService _deletionService;
    private readonly Action<GifFile, IReadOnlyList<GifByteRange>, int?> _applyState;
    private readonly GifFile _originalFile;
    private readonly GifByteRange _block;
    private readonly byte[] _deletedBlockData;
    private readonly string _description;

    private GifFile? _deletedFile;
    private IReadOnlyList<GifByteRange>? _deletedRanges;
    private IReadOnlyList<GifByteRange>? _restoredRanges;
    private bool _executed;

    public string Description => _description;
    public bool CanUndo => _executed;

    public DeleteBlockCommand(
        BlockDeletionService deletionService,
        GifFile file,
        GifByteRange block,
        Action<GifFile, IReadOnlyList<GifByteRange>, int?> applyState,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(deletionService);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(applyState);

        _deletionService = deletionService;
        _originalFile = file;
        _block = block;
        _applyState = applyState;
        _description = description ?? $"Delete {_block.Name}";
        _deletedBlockData = new byte[_block.Length];
        Array.Copy(file.Bytes, _block.Start, _deletedBlockData, 0, _block.Length);
    }

    public void Execute()
    {
        if (_deletedFile is null)
        {
            _deletedFile = _deletionService.DeleteBlock(_originalFile, _block);
            _deletedRanges = _deletionService.RegenerateStructure(_deletedFile);
        }

        _applyState(_deletedFile, _deletedRanges ?? Array.Empty<GifByteRange>(), ResolveFallbackOffset(_deletedFile.Bytes.Length));
        _executed = true;
    }

    public void Undo()
    {
        if (!CanUndo)
            return;

        GifFile restoredFile = _deletionService.RestoreBlock(
            _deletedFile ?? _originalFile,
            _block.Start,
            _deletedBlockData);

        _restoredRanges ??= _deletionService.RegenerateStructure(restoredFile);
        _applyState(restoredFile, _restoredRanges, _block.Start);
        _executed = false;
    }

    private int? ResolveFallbackOffset(int newLength)
    {
        if (newLength <= 0)
            return null;

        return Math.Clamp(_block.Start, 0, newLength - 1);
    }
}
