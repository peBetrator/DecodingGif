using System;
using System.Collections.Generic;
using System.Linq;

namespace DecodingGif.UI.UndoRedo.Commands;

public sealed class BatchCommand : IUndoableCommand
{
    private readonly IReadOnlyList<IUndoableCommand> _commands;

    public string Description { get; }
    public bool CanUndo => _commands.Any(c => c.CanUndo);

    public BatchCommand(string description, IEnumerable<IUndoableCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        Description = description;
        _commands = commands.ToList();
    }

    public void Execute()
    {
        foreach (var command in _commands)
            command.Execute();
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }
}
