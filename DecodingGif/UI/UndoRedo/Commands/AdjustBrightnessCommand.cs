using System;

namespace DecodingGif.UI.UndoRedo.Commands;

public sealed class AdjustBrightnessCommand : IUndoableCommand
{
    private readonly byte[] _bytes;
    private readonly int _start;
    private readonly int _length;
    private readonly int _delta;
    private readonly string _description;

    private byte[]? _before;
    private byte[]? _after;
    private bool _executed;

    public string Description => _description;
    public bool CanUndo => _executed && _before is not null;

    public AdjustBrightnessCommand(byte[] bytes, int start, int length, int delta, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (start < 0 || length < 0 || start + length > bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(length));

        _bytes = bytes;
        _start = start;
        _length = length;
        _delta = delta;
        _description = description ?? $"Adjust brightness by {_delta:+#;-#;0}";
    }

    public void Execute()
    {
        EnsureBuffers();
        if (_after is null)
            return;

        Array.Copy(_after, 0, _bytes, _start, _after.Length);
        _executed = true;
    }

    public void Undo()
    {
        if (!CanUndo || _before is null)
            return;

        Array.Copy(_before, 0, _bytes, _start, _before.Length);
        _executed = false;
    }

    private void EnsureBuffers()
    {
        if (_before is not null && _after is not null)
            return;

        _before = new byte[_length];
        _after = new byte[_length];
        Array.Copy(_bytes, _start, _before, 0, _length);
        for (int i = 0; i < _length; i++)
        {
            int adjusted = _before[i] + _delta;
            _after[i] = (byte)Math.Clamp(adjusted, 0, 255);
        }
    }
}
