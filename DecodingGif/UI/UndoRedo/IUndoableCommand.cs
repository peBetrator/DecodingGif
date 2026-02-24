namespace DecodingGif.UI.UndoRedo;

public interface IUndoableCommand
{
    string Description { get; }
    bool CanUndo { get; }
    void Execute();
    void Undo();
}
