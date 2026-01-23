using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DecodingGif.Core.Editing;
using DecodingGif.Core.Models;
using DecodingGif.Core.Parsing;
using DecodingGif.Core.Services;
using Microsoft.Win32;

namespace DecodingGif.UI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FileLoader _fileLoader = new();
    private readonly GifParser _parser = new();
    private readonly HexRowsBuilder _hexBuilder = new();
    private readonly GifStructureService _structure = new();
    private readonly IByteEditPolicy _editPolicy;

    private ObservableCollection<HexRow> _hexRows = new();
    public ObservableCollection<HexRow> HexRows
    {
        get => _hexRows;
        private set { _hexRows = value; OnPropertyChanged(); }
    }

    private ByteSelectionInfo? _selectedByte;
    public ByteSelectionInfo? SelectedByte
    {
        get => _selectedByte;
        private set { _selectedByte = value; OnPropertyChanged(); }
    }

    private string? _selectedByteMeaning;
    public string? SelectedByteMeaning
    {
        get => _selectedByteMeaning;
        private set { _selectedByteMeaning = value; OnPropertyChanged(); }
    }

    private ObservableCollection<GifStructureNode> _structureRoots = new();
    public ObservableCollection<GifStructureNode> StructureRoots
    {
        get => _structureRoots;
        private set { _structureRoots = value; OnPropertyChanged(); }
    }

    private GifFile? _currentFile;
    public GifFile? CurrentFile
    {
        get => _currentFile;
        private set { _currentFile = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    private ObservableCollection<GifByteRange> _blocks = new();
    public ObservableCollection<GifByteRange> Blocks
    {
        get => _blocks;
        private set { _blocks = value; OnPropertyChanged(); }
    }

    private string? _errorText;
    public string? ErrorText
    {
        get => _errorText;
        private set { _errorText = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    public string StatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ErrorText))
                return ErrorText;

            if (CurrentFile is null)
                return "No file loaded.";

            var s = CurrentFile.Screen;
            return $"Loaded: {CurrentFile.Header.Signature}{CurrentFile.Header.Version} | {s.Width}x{s.Height} | GCT={(s.GlobalColorTableFlag ? "Yes" : "No")} | GCT Size={(s.GlobalColorTableFlag ? s.GlobalColorTableSize : 0)}";
        }
    }

    private bool _isSafeMode = true;
    public bool IsSafeMode
    {
        get => _isSafeMode;
        set { _isSafeMode = value; OnPropertyChanged(); }
    }

    private bool _allowSelectedLctEdit;
    public bool AllowSelectedLctEdit
    {
        get => _allowSelectedLctEdit;
        set { _allowSelectedLctEdit = value; OnPropertyChanged(); }
    }

    private GifByteRange? _gctRange;
    public GifByteRange? GctRange
    {
        get => _gctRange;
        private set { _gctRange = value; OnPropertyChanged(); }
    }

    private GifByteRange? _selectedLctRange;
    public GifByteRange? SelectedLctRange
    {
        get => _selectedLctRange;
        private set { _selectedLctRange = value; OnPropertyChanged(); }
    }

    private int _selectedFrameIndex;
    public int SelectedFrameIndex
    {
        get => _selectedFrameIndex;
        private set
        {
            if (_selectedFrameIndex == value)
                return;
            _selectedFrameIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FrameLabel));
            UpdatePreview();
        }
    }

    private int _frameCount;
    public int FrameCount
    {
        get => _frameCount;
        private set
        {
            if (_frameCount == value)
                return;
            _frameCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FrameLabel));
        }
    }

    public string FrameLabel => FrameCount > 0 ? $"Frame {SelectedFrameIndex + 1}/{FrameCount}" : "Frame -";

    private BitmapSource? _previewImage;
    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set { _previewImage = value; OnPropertyChanged(); }
    }

    public ICommand OpenFileCommand { get; }
    public ICommand PrevFrameCommand { get; }
    public ICommand NextFrameCommand { get; }

    public MainViewModel()
    {
        OpenFileCommand = new RelayCommand(OpenFile);
        PrevFrameCommand = new RelayCommand(SelectPrevFrame);
        NextFrameCommand = new RelayCommand(SelectNextFrame);
        _editPolicy = new VmByteEditPolicy(this);
    }

    private void OpenFile()
    {
        ErrorText = null;
        SelectedByte = null;
        SelectedByteMeaning = null;

        var dlg = new OpenFileDialog
        {
            Filter = "GIF images (*.gif)|*.gif|All files (*.*)|*.*",
            Title = "Open GIF"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var bytes = _fileLoader.LoadAllBytes(dlg.FileName);
            CurrentFile = _parser.Parse(dlg.FileName, bytes);

            var tree = _structure.BuildStructureTree(CurrentFile);
            var ranges = _structure.BuildRanges(CurrentFile).ToList();
            Blocks = new ObservableCollection<GifByteRange>(ranges);
            StructureRoots = new ObservableCollection<GifStructureNode>(tree);
            GctRange = ranges.FirstOrDefault(r => r.Kind == GifBlockKind.GlobalColorTable);
            SelectedLctRange = null;
            _selectedFrameIndex = 0;
            OnPropertyChanged(nameof(SelectedFrameIndex));
            OnPropertyChanged(nameof(FrameLabel));

            HexRows = _hexBuilder.Build(bytes, _editPolicy);
            UpdatePreview();
        }
        catch (Exception ex)
        {
            CurrentFile = null;
            HexRows = new ObservableCollection<HexRow>();
            ErrorText = ex.Message;
            StructureRoots = new ObservableCollection<GifStructureNode>();
            SelectedByteMeaning = null;
            GctRange = null;
            SelectedLctRange = null;
            PreviewImage = null;
            FrameCount = 0;
            _selectedFrameIndex = 0;
            OnPropertyChanged(nameof(SelectedFrameIndex));
            OnPropertyChanged(nameof(FrameLabel));
        }
    }

    public void SelectByte(int offset)
    {
        if (CurrentFile is null)
        {
            SelectedByte = null;
            return;
        }

        var bytes = CurrentFile.Bytes;

        if (offset < 0 || offset >= bytes.Length)
        {
            SelectedByte = null;
            return;
        }

        byte value = bytes[offset];
        string ascii = value is >= 0x20 and <= 0x7E ? ((char)value).ToString() : ".";

        SelectedByte = new ByteSelectionInfo(
            Offset: offset,
            OffsetHex: offset.ToString("X8"),
            Value: value,
            ValueHex: value.ToString("X2"),
            ValueDec: value,
            Ascii: ascii
        );
        SelectedByteMeaning = _structure.DescribeOffset(CurrentFile, offset);
    }

    public void SetSelectedLctRange(GifByteRange? range)
    {
        SelectedLctRange = range?.Kind == GifBlockKind.LocalColorTable ? range : null;
    }

    public void SetSelectedFrameIndex(int index)
    {
        if (FrameCount > 0)
            index = Math.Clamp(index, 0, FrameCount - 1);

        SelectedFrameIndex = index;
    }

    private void SelectPrevFrame() => SetSelectedFrameIndex(SelectedFrameIndex - 1);
    private void SelectNextFrame() => SetSelectedFrameIndex(SelectedFrameIndex + 1);

    public void UpdatePreview()
    {
        var file = CurrentFile;
        if (file is null)
        {
            PreviewImage = null;
            FrameCount = 0;
            return;
        }

        try
        {
            using var ms = new MemoryStream(file.Bytes, writable: false);
            var decoder = new GifBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int count = decoder.Frames.Count;
            FrameCount = count;
            if (count == 0)
            {
                PreviewImage = null;
                return;
            }

            int index = SelectedFrameIndex;
            if (index < 0 || index >= count)
                index = 0;

            if (_selectedFrameIndex != index)
            {
                _selectedFrameIndex = index;
                OnPropertyChanged(nameof(SelectedFrameIndex));
                OnPropertyChanged(nameof(FrameLabel));
            }

            var frame = decoder.Frames[index];
            if (frame is null)
            {
                PreviewImage = null;
                return;
            }

            frame.Freeze();
            PreviewImage = frame;
        }
        catch
        {
            PreviewImage = null;
            FrameCount = 0;
        }
    }

    private sealed class VmByteEditPolicy : IByteEditPolicy
    {
        private readonly MainViewModel _vm;

        public VmByteEditPolicy(MainViewModel vm)
        {
            _vm = vm;
        }

        public bool CanEdit(int offset)
        {
            var file = _vm.CurrentFile;
            if (file is null)
                return false;

            if (offset < 0 || offset >= file.Bytes.Length)
                return false;

            if (!_vm.IsSafeMode)
                return true;

            if (_vm.GctRange is not null && _vm.GctRange.Contains(offset))
                return true;

            if (_vm.AllowSelectedLctEdit && _vm.SelectedLctRange is not null && _vm.SelectedLctRange.Contains(offset))
                return true;

            return false;
        }

        public void SetByte(int offset, byte value)
        {
            var file = _vm.CurrentFile;
            if (file is null)
                return;

            if (offset < 0 || offset >= file.Bytes.Length)
                return;

            file.Bytes[offset] = value;
            _vm.UpdatePreview();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
