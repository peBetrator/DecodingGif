using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DecodingGif.Core.Models;
using DecodingGif.Core.Services;
using WinForms = System.Windows.Forms;

namespace DecodingGif.UI.ViewModels;

public sealed class FrameEditorViewModel : INotifyPropertyChanged
{
    private readonly FrameManagerService _frameManagerService = new();

    private GifFile? _file;
    private IReadOnlyList<GifByteRange> _blocks = Array.Empty<GifByteRange>();
    private int _selectedFrameIndex;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<FrameEditResult>? FrameEdited;

    public int SelectedFrameIndex
    {
        get => _selectedFrameIndex;
        set
        {
            int normalized = Math.Max(0, value);
            if (_selectedFrameIndex == normalized)
                return;
            _selectedFrameIndex = normalized;
            OnPropertyChanged();
            RaiseCanExecuteChanged();
        }
    }

    public int FrameCount { get; private set; }

    public ICommand InsertFrameCommand { get; }
    public ICommand DuplicateFrameCommand { get; }
    public ICommand DeleteFrameCommand { get; }
    public ICommand MoveFrameUpCommand { get; }
    public ICommand MoveFrameDownCommand { get; }

    public FrameEditorViewModel()
    {
        InsertFrameCommand = new RelayCommand(InsertFrame, CanEditFrames);
        DuplicateFrameCommand = new RelayCommand(DuplicateFrame, CanEditFrames);
        DeleteFrameCommand = new RelayCommand(DeleteFrame, CanDeleteFrame);
        MoveFrameUpCommand = new RelayCommand(MoveFrameUp, CanMoveFrameUp);
        MoveFrameDownCommand = new RelayCommand(MoveFrameDown, CanMoveFrameDown);
    }

    public void Load(GifFile? file, IReadOnlyList<GifByteRange> blocks)
    {
        _file = file;
        _blocks = blocks;
        FrameCount = blocks.Count(b => b.Kind == GifBlockKind.ImageDescriptor);
        OnPropertyChanged(nameof(FrameCount));
        RaiseCanExecuteChanged();
    }

    public void SetSelectedFrameIndex(int index) => SelectedFrameIndex = index;

    private void InsertFrame()
    {
        if (_file is null)
            return;
        try
        {
            int desiredIndex = SelectedFrameIndex;
            var result = _frameManagerService.InsertFrame(_file, SelectedFrameIndex);
            SelectedFrameIndex = desiredIndex;
            FrameEdited?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                $"Insert frame failed: {ex.Message}",
                "Frame Operation Error",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
    }

    private void DuplicateFrame()
    {
        if (_file is null)
            return;
        try
        {
            int desiredIndex = SelectedFrameIndex + 1;
            var result = _frameManagerService.DuplicateFrame(_file, SelectedFrameIndex);
            SelectedFrameIndex = Math.Clamp(desiredIndex, 0, Math.Max(0, FrameCount));
            FrameEdited?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                $"Duplicate frame failed: {ex.Message}",
                "Frame Operation Error",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
    }

    private void DeleteFrame()
    {
        if (_file is null)
            return;

        if (WinForms.MessageBox.Show(
                $"Delete frame {SelectedFrameIndex + 1}? This action is destructive.",
                "Delete Frame",
                WinForms.MessageBoxButtons.YesNo,
                WinForms.MessageBoxIcon.Warning) != WinForms.DialogResult.Yes)
            return;

        try
        {
            int desiredIndex = Math.Max(0, SelectedFrameIndex - 1);
            var result = _frameManagerService.DeleteFrame(_file, SelectedFrameIndex);
            SelectedFrameIndex = desiredIndex;
            FrameEdited?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                $"Delete frame failed: {ex.Message}",
                "Frame Operation Error",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
    }

    private void MoveFrameUp()
    {
        if (_file is null)
            return;
        try
        {
            int desiredIndex = Math.Max(0, SelectedFrameIndex - 1);
            var result = _frameManagerService.MoveFrameUp(_file, SelectedFrameIndex);
            SelectedFrameIndex = desiredIndex;
            FrameEdited?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                $"Move frame up failed: {ex.Message}",
                "Frame Operation Error",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
    }

    private void MoveFrameDown()
    {
        if (_file is null)
            return;
        try
        {
            int desiredIndex = SelectedFrameIndex + 1;
            var result = _frameManagerService.MoveFrameDown(_file, SelectedFrameIndex);
            SelectedFrameIndex = Math.Clamp(desiredIndex, 0, Math.Max(0, FrameCount));
            FrameEdited?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                $"Move frame down failed: {ex.Message}",
                "Frame Operation Error",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
    }

    private bool CanEditFrames() => _file is not null && FrameCount > 0;
    private bool CanDeleteFrame() => _file is not null && FrameCount > 1;
    private bool CanMoveFrameUp() => _file is not null && FrameCount > 1 && SelectedFrameIndex > 0;
    private bool CanMoveFrameDown() => _file is not null && FrameCount > 1 && SelectedFrameIndex < FrameCount - 1;

    private void RaiseCanExecuteChanged()
    {
        (InsertFrameCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DuplicateFrameCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteFrameCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveFrameUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveFrameDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
