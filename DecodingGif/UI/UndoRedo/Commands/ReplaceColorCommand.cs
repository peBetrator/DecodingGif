using System;
using System.Collections.Generic;

namespace DecodingGif.UI.UndoRedo.Commands;

public sealed class ReplaceColorCommand : IUndoableCommand
{
    private readonly byte[] _bytes;
    private readonly int _start;
    private readonly int _length;
    private readonly ColorRgb _fromColor;
    private readonly ColorRgb _toColor;
    private readonly string _description;
    private readonly List<(int Offset, ColorRgb Before)> _changes = [];

    private bool _initialized;
    private bool _executed;

    public string Description => _description;
    public bool CanUndo => _executed && _changes.Count > 0;

    public ReplaceColorCommand(byte[] bytes, int start, int length, ColorRgb fromColor, ColorRgb toColor, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (start < 0 || length < 0 || start + length > bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(length));

        _bytes = bytes;
        _start = start;
        _length = length;
        _fromColor = fromColor;
        _toColor = toColor;
        _description = description ?? $"Replace color {fromColor} -> {toColor}";
    }

    public void Execute()
    {
        if (!_initialized)
        {
            BuildChanges();
            _initialized = true;
        }

        foreach (var change in _changes)
        {
            _bytes[change.Offset] = _toColor.R;
            _bytes[change.Offset + 1] = _toColor.G;
            _bytes[change.Offset + 2] = _toColor.B;
        }

        _executed = _changes.Count > 0;
    }

    public void Undo()
    {
        if (!CanUndo)
            return;

        foreach (var change in _changes)
        {
            _bytes[change.Offset] = change.Before.R;
            _bytes[change.Offset + 1] = change.Before.G;
            _bytes[change.Offset + 2] = change.Before.B;
        }

        _executed = false;
    }

    private void BuildChanges()
    {
        int end = _start + _length;
        for (int i = _start; i + 2 < end; i += 3)
        {
            var current = new ColorRgb(_bytes[i], _bytes[i + 1], _bytes[i + 2]);
            if (current != _fromColor)
                continue;

            _changes.Add((i, current));
        }
    }
}
