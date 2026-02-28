using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DecodingGif.UI.UndoRedo;

public sealed class UndoRedoManager : INotifyPropertyChanged
{
    private readonly List<IUndoableCommand> _undoHistory = [];
    private readonly List<IUndoableCommand> _redoHistory = [];

    public int HistoryLimit { get; }

    private string? _lastCommandDescription;
    public string? LastCommandDescription
    {
        get => _lastCommandDescription;
        private set
        {
            if (_lastCommandDescription == value)
                return;
            _lastCommandDescription = value;
            OnPropertyChanged();
        }
    }

    public bool CanUndo => _undoHistory.Count > 0;
    public bool CanRedo => _redoHistory.Count > 0;
    public string? UndoDescription => CanUndo ? _undoHistory[^1].Description : null;
    public string? RedoDescription => CanRedo ? _redoHistory[^1].Description : null;

    public UndoRedoManager(int historyLimit = 10)
    {
        if (historyLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(historyLimit));

        HistoryLimit = historyLimit;
    }

    public void Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Execute();
        LastCommandDescription = command.Description;

        if (command.CanUndo)
        {
            _undoHistory.Add(command);
            TrimUndoHistoryToLimit();
        }

        _redoHistory.Clear();
        RaiseStateChanged();
    }

    public bool Undo()
    {
        if (!CanUndo)
            return false;

        var command = _undoHistory[^1];
        _undoHistory.RemoveAt(_undoHistory.Count - 1);
        command.Undo();
        _redoHistory.Add(command);
        LastCommandDescription = $"Undo: {command.Description}";
        RaiseStateChanged();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
            return false;

        var command = _redoHistory[^1];
        _redoHistory.RemoveAt(_redoHistory.Count - 1);
        command.Execute();
        if (command.CanUndo)
            _undoHistory.Add(command);
        TrimUndoHistoryToLimit();
        LastCommandDescription = $"Redo: {command.Description}";
        RaiseStateChanged();
        return true;
    }

    public void Clear()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        LastCommandDescription = null;
        RaiseStateChanged();
    }

    private void TrimUndoHistoryToLimit()
    {
        while (_undoHistory.Count > HistoryLimit)
            _undoHistory.RemoveAt(0);
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoDescription));
        OnPropertyChanged(nameof(RedoDescription));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
