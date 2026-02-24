using System.ComponentModel;
using DecodingGif.Core.Models;

namespace DecodingGif.UI.ViewModels;

public sealed class PaletteEditorViewModel : INotifyPropertyChanged
{
    public ColorPaletteViewModel ColorEditor { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<ColorChangeEventArgs>? ColorChanged;
    public event EventHandler<BatchOperationEventArgs>? BatchOperationRequested;

    public PaletteEditorViewModel()
    {
        ColorEditor.PropertyChanged += (_, e) => PropertyChanged?.Invoke(this, e);
        ColorEditor.ColorChanged += (_, e) => ColorChanged?.Invoke(this, e);
        ColorEditor.BatchOperationRequested += (_, e) => BatchOperationRequested?.Invoke(this, e);
    }

    public void LoadFromCurrentFile(GifFile? file, IEnumerable<GifByteRange> blocks) =>
        ColorEditor.LoadFromCurrentFile(file, blocks);

    public void SetSelectedFrameIndex(int frameIndex) => ColorEditor.SetSelectedFrameIndex(frameIndex);

    public bool HasUnsavedChanges => ColorEditor.HasUnsavedChanges;
    public bool CanUndo => ColorEditor.CanUndo;
    public bool CanRedo => ColorEditor.CanRedo;
    public string UndoDescription => ColorEditor.UndoDescription;
    public string RedoDescription => ColorEditor.RedoDescription;

    public void Undo() => ColorEditor.UndoCommand.Execute(null);
    public void Redo() => ColorEditor.RedoCommand.Execute(null);
    public void SaveChanges() => ColorEditor.SaveChangesCommand.Execute(null);
    public void ResetChanges() => ColorEditor.ResetChangesCommand.Execute(null);
}
