using System.ComponentModel;
using System.Runtime.CompilerServices;
using DecodingGif.Core.Models;
using DecodingGif.Core.Parsing;
using DecodingGif.Core.Services;
using Microsoft.Win32;
using System.Windows.Input;
using System.Collections.ObjectModel;

namespace DecodingGif.UI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FileLoader _fileLoader = new();
    private readonly GifParser _parser = new();
    private readonly HexRowsBuilder _hexBuilder = new();
    private readonly GifStructureService _structure = new();

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

    public ICommand OpenFileCommand { get; }

    public MainViewModel()
    {
        OpenFileCommand = new RelayCommand(OpenFile);
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

            var ranges = _structure.BuildRanges(CurrentFile);
            Blocks = new ObservableCollection<GifByteRange>(ranges);

            HexRows = _hexBuilder.Build(bytes);
        }
        catch (Exception ex)
        {
            CurrentFile = null;
            HexRows = new ObservableCollection<HexRow>();
            ErrorText = ex.Message;
            Blocks = new ObservableCollection<GifByteRange>();
            SelectedByteMeaning = null;
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