using System;

namespace DecodingGif.UI.UndoRedo.Commands;

public sealed class SetColorCommand : IUndoableCommand
{
    private readonly byte[] _bytes;
    private readonly int _baseOffset;
    private readonly ColorRgb _newColor;
    private readonly string _description;

    private ColorRgb _oldColor;
    private bool _capturedOriginal;
    private bool _executed;

    public string Description => _description;
    public bool CanUndo => _executed;

    public SetColorCommand(byte[] bytes, int baseOffset, ColorRgb newColor, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (baseOffset < 0 || baseOffset + 2 >= bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(baseOffset));

        _bytes = bytes;
        _baseOffset = baseOffset;
        _newColor = newColor;
        _description = description ?? $"Set color @0x{baseOffset:X8} to {newColor}";
    }

    public void Execute()
    {
        if (!_capturedOriginal)
        {
            _oldColor = new ColorRgb(_bytes[_baseOffset], _bytes[_baseOffset + 1], _bytes[_baseOffset + 2]);
            _capturedOriginal = true;
        }

        _bytes[_baseOffset] = _newColor.R;
        _bytes[_baseOffset + 1] = _newColor.G;
        _bytes[_baseOffset + 2] = _newColor.B;
        _executed = true;
    }

    public void Undo()
    {
        if (!CanUndo)
            return;

        _bytes[_baseOffset] = _oldColor.R;
        _bytes[_baseOffset + 1] = _oldColor.G;
        _bytes[_baseOffset + 2] = _oldColor.B;
        _executed = false;
    }
}
