using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using DecodingGif.Core.Editing;
using DecodingGif.Core.Models;
using DecodingGif.Core.Parsing;
using DecodingGif.Core.Services;
using Win32OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace DecodingGif.UI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FileLoader _fileLoader = new();
    private readonly GifParser _parser = new();
    private readonly HexRowsBuilder _hexBuilder = new();
    private readonly GifStructureService _structure = new();
    private readonly GifAnimationService _animation = new();
    private readonly StructureDependencyGraphBuilder _graphBuilder = new();
    private readonly IByteEditPolicy _editPolicy;
    private readonly DispatcherTimer _playbackTimer;

    private readonly RelayCommand _prevFrameRelayCommand;
    private readonly RelayCommand _nextFrameRelayCommand;
    private readonly RelayCommand _playRelayCommand;
    private readonly RelayCommand _pauseRelayCommand;
    private readonly RelayCommand _stopRelayCommand;
    private readonly RelayCommand _stepBackwardRelayCommand;
    private readonly RelayCommand _stepForwardRelayCommand;
    private readonly RelayCommand _restartRelayCommand;
    private readonly RelayCommand _resetGraphLayoutRelayCommand;

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
        private set
        {
            _selectedByte = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedByteOffset));
        }
    }

    public int? SelectedByteOffset => SelectedByte?.Offset;

    private int? _hoveredByteOffset;
    public int? HoveredByteOffset
    {
        get => _hoveredByteOffset;
        private set { _hoveredByteOffset = value; OnPropertyChanged(); }
    }

    private string? _selectedByteMeaning;
    public string? SelectedByteMeaning
    {
        get => _selectedByteMeaning;
        private set
        {
            _selectedByteMeaning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedByteMeaning));
        }
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
        private set
        {
            _currentFile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(FileLength));
        }
    }

    public int FileLength => CurrentFile?.Bytes.Length ?? 0;

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
        set { _isSafeMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedColorCanEdit)); }
    }

    private bool _allowSelectedLctEdit;
    public bool AllowSelectedLctEdit
    {
        get => _allowSelectedLctEdit;
        set { _allowSelectedLctEdit = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedColorCanEdit)); }
    }

    private GifByteRange? _gctRange;
    public GifByteRange? GctRange
    {
        get => _gctRange;
        private set { _gctRange = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedColorCanEdit)); }
    }

    private GifByteRange? _selectedLctRange;
    public GifByteRange? SelectedLctRange
    {
        get => _selectedLctRange;
        private set { _selectedLctRange = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedColorCanEdit)); }
    }

    private int _selectedFrameIndex;
    public int SelectedFrameIndex
    {
        get => _selectedFrameIndex;
        set
        {
            int normalized = value;
            if (FrameCount > 0)
                normalized = Math.Clamp(normalized, 0, FrameCount - 1);
            else if (normalized < 0)
                normalized = 0;

            if (_selectedFrameIndex == normalized)
                return;

            _selectedFrameIndex = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FrameLabel));
            UpdatePreview();
            ResetPlaybackTimerForCurrentFrame();
            RaisePlaybackCanExecuteChanged();
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
            RaisePlaybackCanExecuteChanged();
        }
    }

    public string FrameLabel => FrameCount > 0 ? $"Frame {SelectedFrameIndex + 1}/{FrameCount}" : "Frame -";

    private StructureDependencyGraph _structureGraph = new();
    public StructureDependencyGraph StructureGraph
    {
        get => _structureGraph;
        private set { _structureGraph = value; OnPropertyChanged(); }
    }

    private StructureDependencyGraph _fullStructureGraph = new();

    private GraphLayoutMode _graphLayoutMode = GraphLayoutMode.Hierarchical;
    public GraphLayoutMode GraphLayoutMode
    {
        get => _graphLayoutMode;
        set
        {
            if (_graphLayoutMode == value)
                return;
            _graphLayoutMode = value;
            OnPropertyChanged();
            RebuildGraph();
        }
    }

    public Array GraphLayoutModes => Enum.GetValues(typeof(GraphLayoutMode));

    private bool _showSequentialEdges = true;
    public bool ShowSequentialEdges
    {
        get => _showSequentialEdges;
        set
        {
            if (_showSequentialEdges == value)
                return;
            _showSequentialEdges = value;
            OnPropertyChanged();
            ApplyGraphFilters();
        }
    }

    private bool _showDependencyEdges = true;
    public bool ShowDependencyEdges
    {
        get => _showDependencyEdges;
        set
        {
            if (_showDependencyEdges == value)
                return;
            _showDependencyEdges = value;
            OnPropertyChanged();
            ApplyGraphFilters();
        }
    }

    private bool _showSharedResourceEdges = true;
    public bool ShowSharedResourceEdges
    {
        get => _showSharedResourceEdges;
        set
        {
            if (_showSharedResourceEdges == value)
                return;
            _showSharedResourceEdges = value;
            OnPropertyChanged();
            ApplyGraphFilters();
        }
    }

    private bool _showTemporalEdges = true;
    public bool ShowTemporalEdges
    {
        get => _showTemporalEdges;
        set
        {
            if (_showTemporalEdges == value)
                return;
            _showTemporalEdges = value;
            OnPropertyChanged();
            ApplyGraphFilters();
        }
    }

    private bool _showDataFlowEdges = true;
    public bool ShowDataFlowEdges
    {
        get => _showDataFlowEdges;
        set
        {
            if (_showDataFlowEdges == value)
                return;
            _showDataFlowEdges = value;
            OnPropertyChanged();
            ApplyGraphFilters();
        }
    }

    private bool _showEdgeLabels = true;
    public bool ShowEdgeLabels
    {
        get => _showEdgeLabels;
        set { _showEdgeLabels = value; OnPropertyChanged(); }
    }

    private ObservableCollection<GifFrameInfo> _frameTimeline = new();
    public ObservableCollection<GifFrameInfo> FrameTimeline
    {
        get => _frameTimeline;
        private set
        {
            _frameTimeline = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalAnimationText));
            ResetPlaybackTimerForCurrentFrame();
            RaisePlaybackCanExecuteChanged();
        }
    }

    private double _playbackSpeed = 1.0;
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            double clamped = Math.Clamp(value, 0.1, 5.0);
            if (Math.Abs(_playbackSpeed - clamped) < 0.0001)
                return;

            _playbackSpeed = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaybackSpeedText));
            ResetPlaybackTimerForCurrentFrame();
        }
    }

    public string PlaybackSpeedText => $"{PlaybackSpeed:0.0}x";

    private bool _isLooping = true;
    public bool IsLooping
    {
        get => _isLooping;
        set { _isLooping = value; OnPropertyChanged(); }
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value)
                return;

            _isPlaying = value;
            OnPropertyChanged();
            RaisePlaybackCanExecuteChanged();
        }
    }

    private bool _isInfiniteLoopInFile = true;
    public bool IsInfiniteLoopInFile
    {
        get => _isInfiniteLoopInFile;
        private set
        {
            _isInfiniteLoopInFile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalAnimationText));
        }
    }

    public string TotalAnimationText
    {
        get
        {
            if (FrameTimeline.Count == 0)
                return "Animation: no frames";

            int duration = _animation.CalculateTotalDuration(FrameTimeline);
            string loop = IsInfiniteLoopInFile ? "infinite" : "finite";
            return $"Duration: {duration} ms | Frames: {FrameTimeline.Count} | File loop: {loop}";
        }
    }

    private BitmapSource? _previewImage;
    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set { _previewImage = value; OnPropertyChanged(); }
    }

    private string? _selectedColorTableLabel;
    public string? SelectedColorTableLabel
    {
        get => _selectedColorTableLabel;
        private set
        {
            _selectedColorTableLabel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedColorInfo));
        }
    }

    private int? _selectedColorIndex;
    public int? SelectedColorIndex
    {
        get => _selectedColorIndex;
        private set
        {
            _selectedColorIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedColorInfo));
        }
    }

    private string? _selectedColorChannel;
    public string? SelectedColorChannel
    {
        get => _selectedColorChannel;
        private set
        {
            _selectedColorChannel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedColorInfo));
        }
    }

    private string? _selectedColorRgbText;
    public string? SelectedColorRgbText
    {
        get => _selectedColorRgbText;
        private set
        {
            _selectedColorRgbText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedColorInfo));
        }
    }

    private SolidColorBrush? _selectedColorBrush;
    public SolidColorBrush? SelectedColorBrush
    {
        get => _selectedColorBrush;
        private set
        {
            _selectedColorBrush = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedColorInfo));
        }
    }

    public bool HasSelectedColorInfo =>
        !string.IsNullOrWhiteSpace(SelectedColorTableLabel)
        || SelectedColorIndex.HasValue
        || !string.IsNullOrWhiteSpace(SelectedColorRgbText);

    public bool HasSelectedByteMeaning =>
        !string.IsNullOrWhiteSpace(SelectedByteMeaning);

    private string? _selectedGceLabel;
    public string? SelectedGceLabel
    {
        get => _selectedGceLabel;
        private set
        {
            _selectedGceLabel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedGceInfo));
        }
    }

    private string? _selectedGceDelayText;
    public string? SelectedGceDelayText
    {
        get => _selectedGceDelayText;
        private set
        {
            _selectedGceDelayText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedGceInfo));
        }
    }

    private string? _selectedGceDisposalText;
    public string? SelectedGceDisposalText
    {
        get => _selectedGceDisposalText;
        private set
        {
            _selectedGceDisposalText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedGceInfo));
        }
    }

    private string? _selectedGceTransparencyText;
    public string? SelectedGceTransparencyText
    {
        get => _selectedGceTransparencyText;
        private set
        {
            _selectedGceTransparencyText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedGceInfo));
        }
    }

    public bool HasSelectedGceInfo =>
        !string.IsNullOrWhiteSpace(SelectedGceLabel)
        || !string.IsNullOrWhiteSpace(SelectedGceDelayText)
        || !string.IsNullOrWhiteSpace(SelectedGceDisposalText)
        || !string.IsNullOrWhiteSpace(SelectedGceTransparencyText);

    private string? _selectedLsdLabel;
    public string? SelectedLsdLabel
    {
        get => _selectedLsdLabel;
        private set
        {
            _selectedLsdLabel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLsdInfo));
        }
    }

    private string? _selectedLsdDimensions;
    public string? SelectedLsdDimensions
    {
        get => _selectedLsdDimensions;
        private set
        {
            _selectedLsdDimensions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLsdInfo));
        }
    }

    private string? _selectedLsdGctPresent;
    public string? SelectedLsdGctPresent
    {
        get => _selectedLsdGctPresent;
        private set
        {
            _selectedLsdGctPresent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLsdInfo));
        }
    }

    private string? _selectedLsdGctSize;
    public string? SelectedLsdGctSize
    {
        get => _selectedLsdGctSize;
        private set
        {
            _selectedLsdGctSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLsdInfo));
        }
    }

    private string? _selectedLsdColorResolution;
    public string? SelectedLsdColorResolution
    {
        get => _selectedLsdColorResolution;
        private set
        {
            _selectedLsdColorResolution = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLsdInfo));
        }
    }

    private string? _selectedLsdSortFlag;
    public string? SelectedLsdSortFlag
    {
        get => _selectedLsdSortFlag;
        private set
        {
            _selectedLsdSortFlag = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLsdInfo));
        }
    }

    private string? _selectedLsdBackgroundIndex;
    public string? SelectedLsdBackgroundIndex
    {
        get => _selectedLsdBackgroundIndex;
        private set
        {
            _selectedLsdBackgroundIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLsdInfo));
        }
    }

    private string? _selectedLsdPixelAspect;
    public string? SelectedLsdPixelAspect
    {
        get => _selectedLsdPixelAspect;
        private set
        {
            _selectedLsdPixelAspect = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLsdInfo));
        }
    }

    private string? _selectedLsdBackgroundRgb;
    public string? SelectedLsdBackgroundRgb
    {
        get => _selectedLsdBackgroundRgb;
        private set
        {
            _selectedLsdBackgroundRgb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLsdInfo));
        }
    }

    private SolidColorBrush? _selectedLsdBackgroundBrush;
    public SolidColorBrush? SelectedLsdBackgroundBrush
    {
        get => _selectedLsdBackgroundBrush;
        private set
        {
            _selectedLsdBackgroundBrush = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLsdInfo));
        }
    }

    public bool HasSelectedLsdInfo =>
        !string.IsNullOrWhiteSpace(SelectedLsdLabel)
        || !string.IsNullOrWhiteSpace(SelectedLsdDimensions)
        || !string.IsNullOrWhiteSpace(SelectedLsdGctPresent)
        || !string.IsNullOrWhiteSpace(SelectedLsdGctSize)
        || !string.IsNullOrWhiteSpace(SelectedLsdColorResolution)
        || !string.IsNullOrWhiteSpace(SelectedLsdSortFlag)
        || !string.IsNullOrWhiteSpace(SelectedLsdBackgroundIndex)
        || !string.IsNullOrWhiteSpace(SelectedLsdPixelAspect)
        || !string.IsNullOrWhiteSpace(SelectedLsdBackgroundRgb);

    public bool SelectedColorCanEdit
    {
        get
        {
            if (_selectedColorBaseOffset is null)
                return false;

            int baseOffset = _selectedColorBaseOffset.Value;
            return _editPolicy.CanEdit(baseOffset)
                && _editPolicy.CanEdit(baseOffset + 1)
                && _editPolicy.CanEdit(baseOffset + 2);
        }
    }

    private GifByteRange? _selectedColorTableRange;
    private int? _selectedColorBaseOffset;

    public ICommand OpenFileCommand { get; }
    public ICommand PrevFrameCommand { get; }
    public ICommand NextFrameCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand StepBackwardCommand { get; }
    public ICommand StepForwardCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand ResetGraphLayoutCommand { get; }
    public ICommand PickColorCommand { get; }

    public MainViewModel()
    {
        _playbackTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _playbackTimer.Tick += OnPlaybackTick;

        OpenFileCommand = new RelayCommand(OpenFile);
        _prevFrameRelayCommand = new RelayCommand(SelectPrevFrame, CanStepBackward);
        _nextFrameRelayCommand = new RelayCommand(SelectNextFrame, CanStepForward);
        _playRelayCommand = new RelayCommand(PlayAnimation, CanPlay);
        _pauseRelayCommand = new RelayCommand(PauseAnimation, CanPause);
        _stopRelayCommand = new RelayCommand(StopAnimation, CanStop);
        _stepBackwardRelayCommand = new RelayCommand(StepBackward, CanStepBackward);
        _stepForwardRelayCommand = new RelayCommand(StepForward, CanStepForward);
        _restartRelayCommand = new RelayCommand(RestartAnimation, CanRestart);
        _resetGraphLayoutRelayCommand = new RelayCommand(ResetGraphLayout);

        PrevFrameCommand = _prevFrameRelayCommand;
        NextFrameCommand = _nextFrameRelayCommand;
        PlayCommand = _playRelayCommand;
        PauseCommand = _pauseRelayCommand;
        StopCommand = _stopRelayCommand;
        StepBackwardCommand = _stepBackwardRelayCommand;
        StepForwardCommand = _stepForwardRelayCommand;
        RestartCommand = _restartRelayCommand;
        ResetGraphLayoutCommand = _resetGraphLayoutRelayCommand;

        PickColorCommand = new RelayCommand(PickColorForSelectedPalette);
        _editPolicy = new VmByteEditPolicy(this);
        RaisePlaybackCanExecuteChanged();
    }

    private void OpenFile()
    {
        PauseAnimation();
        ErrorText = null;
        SelectedByte = null;
        HoveredByteOffset = null;
        SelectedByteMeaning = null;
        ClearSelectedColorInfo();
        ClearSelectedGceInfo();
        ClearSelectedLsdInfo();

        var dlg = new Win32OpenFileDialog
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
            FrameTimeline = _animation.BuildFrameTimeline(CurrentFile, ranges);
            IsInfiniteLoopInFile = _animation.IsInfiniteLoop(CurrentFile, ranges);
            IsLooping = IsInfiniteLoopInFile;
            _fullStructureGraph = _graphBuilder.BuildGraph(CurrentFile, ranges, GraphLayoutMode);
            ApplyGraphFilters();
            OnPropertyChanged(nameof(TotalAnimationText));
            _selectedFrameIndex = 0;
            OnPropertyChanged(nameof(SelectedFrameIndex));
            OnPropertyChanged(nameof(FrameLabel));

            HexRows = _hexBuilder.Build(bytes, _editPolicy);
            UpdatePreview();
            RaisePlaybackCanExecuteChanged();
        }
        catch (Exception ex)
        {
            CurrentFile = null;
            HoveredByteOffset = null;
            HexRows = new ObservableCollection<HexRow>();
            ErrorText = ex.Message;
            StructureRoots = new ObservableCollection<GifStructureNode>();
            SelectedByteMeaning = null;
            GctRange = null;
            SelectedLctRange = null;
            PreviewImage = null;
            FrameTimeline = new ObservableCollection<GifFrameInfo>();
            IsInfiniteLoopInFile = true;
            IsLooping = true;
            _fullStructureGraph = new StructureDependencyGraph();
            StructureGraph = new StructureDependencyGraph();
            IsPlaying = false;
            _playbackTimer.Stop();
            FrameCount = 0;
            _selectedFrameIndex = 0;
            OnPropertyChanged(nameof(SelectedFrameIndex));
            OnPropertyChanged(nameof(FrameLabel));
            OnPropertyChanged(nameof(TotalAnimationText));
            ClearSelectedColorInfo();
            ClearSelectedGceInfo();
            ClearSelectedLsdInfo();
            RaisePlaybackCanExecuteChanged();
        }
    }

    public void SelectByte(int offset)
    {
        if (CurrentFile is null)
        {
            SelectedByte = null;
            ClearSelectedColorInfo();
            ClearSelectedGceInfo();
            ClearSelectedLsdInfo();
            return;
        }

        var bytes = CurrentFile.Bytes;

        if (offset < 0 || offset >= bytes.Length)
        {
            SelectedByte = null;
            ClearSelectedColorInfo();
            ClearSelectedGceInfo();
            ClearSelectedLsdInfo();
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
        UpdateSelectedColorInfo(offset);
        UpdateSelectedGceInfo(offset);
        UpdateSelectedLsdInfo(offset);
    }

    public void SetSelectedLctRange(GifByteRange? range)
    {
        SelectedLctRange = range?.Kind == GifBlockKind.LocalColorTable ? range : null;
    }

    public void SetHoveredByteOffset(int? offset)
    {
        if (offset.HasValue && (offset.Value < 0 || offset.Value >= FileLength))
            offset = null;

        if (HoveredByteOffset == offset)
            return;

        HoveredByteOffset = offset;
    }

    public void SetSelectedFrameIndex(int index)
    {
        SelectedFrameIndex = index;
    }

    public void NavigateToByteRange(GifByteRange range)
    {
        SetHoveredByteOffset(range.Start);
        SelectByte(range.Start);
    }

    private void ResetGraphLayout()
    {
        RebuildGraph();
    }

    private void RebuildGraph()
    {
        var file = CurrentFile;
        if (file is null || Blocks.Count == 0)
        {
            _fullStructureGraph = new StructureDependencyGraph();
            StructureGraph = new StructureDependencyGraph();
            return;
        }

        _fullStructureGraph = _graphBuilder.BuildGraph(file, Blocks, GraphLayoutMode);
        ApplyGraphFilters();
    }

    private void ApplyGraphFilters()
    {
        var source = _fullStructureGraph;
        if (source.Nodes.Count == 0)
        {
            StructureGraph = new StructureDependencyGraph { Layout = GraphLayoutMode };
            return;
        }

        var filtered = new StructureDependencyGraph
        {
            Layout = source.Layout,
            CanvasSize = source.CanvasSize
        };

        foreach (var node in source.Nodes)
        {
            filtered.Nodes.Add(new GraphNode
            {
                Id = node.Id,
                Title = node.Title,
                BlockType = node.BlockType,
                ByteRange = node.ByteRange,
                Position = node.Position,
                Size = node.Size,
                Category = node.Category,
                Properties = new Dictionary<string, object>(node.Properties)
            });
        }

        foreach (var edge in source.Edges)
        {
            if (!IsEdgeVisible(edge.Type))
                continue;
            filtered.Edges.Add(edge);
        }

        StructureGraph = filtered;
    }

    private bool IsEdgeVisible(EdgeType type) =>
        type switch
        {
            EdgeType.Sequential => ShowSequentialEdges,
            EdgeType.Dependency => ShowDependencyEdges,
            EdgeType.SharedResource => ShowSharedResourceEdges,
            EdgeType.Temporal => ShowTemporalEdges,
            EdgeType.DataFlow => ShowDataFlowEdges,
            _ => true
        };

    private void SelectPrevFrame()
    {
        PauseAnimation();
        SetSelectedFrameIndex(SelectedFrameIndex - 1);
    }

    private void SelectNextFrame()
    {
        PauseAnimation();
        SetSelectedFrameIndex(SelectedFrameIndex + 1);
    }

    private void StepBackward()
    {
        PauseAnimation();
        SetSelectedFrameIndex(SelectedFrameIndex - 1);
    }

    private void StepForward()
    {
        PauseAnimation();
        SetSelectedFrameIndex(SelectedFrameIndex + 1);
    }

    private void RestartAnimation()
    {
        SetSelectedFrameIndex(0);
        if (IsPlaying)
            ResetPlaybackTimerForCurrentFrame();
    }

    private void PlayAnimation()
    {
        if (FrameCount == 0)
            return;

        IsPlaying = true;
        ResetPlaybackTimerForCurrentFrame();
        _playbackTimer.Start();
    }

    private void PauseAnimation()
    {
        _playbackTimer.Stop();
        IsPlaying = false;
    }

    private void StopAnimation()
    {
        PauseAnimation();
        SetSelectedFrameIndex(0);
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        if (!IsPlaying || FrameCount == 0)
        {
            PauseAnimation();
            return;
        }

        int nextIndex = SelectedFrameIndex + 1;
        if (nextIndex < FrameCount)
        {
            SetSelectedFrameIndex(nextIndex);
            return;
        }

        if (IsLooping)
        {
            SetSelectedFrameIndex(0);
            return;
        }

        StopAnimation();
    }

    private void ResetPlaybackTimerForCurrentFrame()
    {
        int delayMs = 100;
        if (SelectedFrameIndex >= 0 && SelectedFrameIndex < FrameTimeline.Count)
            delayMs = Math.Max(FrameTimeline[SelectedFrameIndex].DelayMs, 10);

        double adjustedDelay = delayMs / PlaybackSpeed;
        int safeDelay = Math.Max((int)Math.Round(adjustedDelay), 1);
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(safeDelay);
    }

    private void RaisePlaybackCanExecuteChanged()
    {
        _prevFrameRelayCommand.RaiseCanExecuteChanged();
        _nextFrameRelayCommand.RaiseCanExecuteChanged();
        _playRelayCommand.RaiseCanExecuteChanged();
        _pauseRelayCommand.RaiseCanExecuteChanged();
        _stopRelayCommand.RaiseCanExecuteChanged();
        _stepBackwardRelayCommand.RaiseCanExecuteChanged();
        _stepForwardRelayCommand.RaiseCanExecuteChanged();
        _restartRelayCommand.RaiseCanExecuteChanged();
    }

    private bool CanPlay() => FrameCount > 0 && !IsPlaying;
    private bool CanPause() => IsPlaying;
    private bool CanStop() => FrameCount > 0 && (IsPlaying || SelectedFrameIndex != 0);
    private bool CanRestart() => FrameCount > 0;
    private bool CanStepBackward() => FrameCount > 0 && SelectedFrameIndex > 0;
    private bool CanStepForward() => FrameCount > 0 && SelectedFrameIndex < FrameCount - 1;

    private void PickColorForSelectedPalette()
    {
        var file = CurrentFile;
        if (file is null)
            return;

        if (_selectedColorBaseOffset is null)
            return;

        int baseOffset = _selectedColorBaseOffset.Value;
        if (baseOffset < 0 || baseOffset + 2 >= file.Bytes.Length)
            return;

        if (!SelectedColorCanEdit)
            return;

        var dialog = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(file.Bytes[baseOffset], file.Bytes[baseOffset + 1], file.Bytes[baseOffset + 2])
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
            return;

        var c = dialog.Color;
        _editPolicy.SetByte(baseOffset, c.R);
        _editPolicy.SetByte(baseOffset + 1, c.G);
        _editPolicy.SetByte(baseOffset + 2, c.B);

        int refreshOffset = SelectedByte?.Offset ?? baseOffset;
        SelectByte(refreshOffset);
    }

    public void UpdatePreview()
    {
        var file = CurrentFile;
        if (file is null)
        {
            PauseAnimation();
            PreviewImage = null;
            FrameCount = 0;
            ClearSelectedColorInfo();
            ClearSelectedGceInfo();
            ClearSelectedLsdInfo();
            RaisePlaybackCanExecuteChanged();
            return;
        }

        try
        {
            using var ms = new MemoryStream(file.Bytes, writable: false);
            var decoder = new GifBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int count = decoder.Frames.Count;
            FrameCount = count;
            OnPropertyChanged(nameof(TotalAnimationText));
            if (count == 0)
            {
                PauseAnimation();
                PreviewImage = null;
                RaisePlaybackCanExecuteChanged();
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
            ResetPlaybackTimerForCurrentFrame();
            RaisePlaybackCanExecuteChanged();

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
            PauseAnimation();
            PreviewImage = null;
            FrameCount = 0;
            ClearSelectedGceInfo();
            ClearSelectedLsdInfo();
            RaisePlaybackCanExecuteChanged();
        }
    }

    private void UpdateSelectedColorInfo(int offset)
    {
        var file = CurrentFile;
        if (file is null)
        {
            ClearSelectedColorInfo();
            return;
        }

        GifByteRange? tableRange = null;
        string? tableLabel = null;

        if (GctRange is not null && GctRange.Contains(offset))
        {
            tableRange = GctRange;
            tableLabel = "Color table: GCT";
        }
        else
        {
            var lctRange = Blocks.FirstOrDefault(r => r.Kind == GifBlockKind.LocalColorTable && r.Contains(offset));
            if (lctRange is not null)
            {
                tableRange = lctRange;
                int? frameIndex = FindFrameIndexForRange(lctRange);
                if (frameIndex.HasValue)
                    tableLabel = $"Color table: LCT (Frame {frameIndex.Value + 1})";
                else
                    tableLabel = "Color table: LCT";
            }
        }

        if (tableRange is null)
        {
            ClearSelectedColorInfo();
            return;
        }

        int rel = offset - tableRange.Start;
        int colorIndex = rel / 3;
        int channel = rel % 3;

        int baseOffset = tableRange.Start + (colorIndex * 3);
        if (baseOffset < 0 || baseOffset + 2 >= file.Bytes.Length)
        {
            ClearSelectedColorInfo();
            return;
        }

        byte r = file.Bytes[baseOffset + 0];
        byte g = file.Bytes[baseOffset + 1];
        byte b = file.Bytes[baseOffset + 2];

        SelectedColorTableLabel = tableLabel;
        SelectedColorIndex = colorIndex;
        SelectedColorChannel = channel switch
        {
            0 => "Channel: R",
            1 => "Channel: G",
            2 => "Channel: B",
            _ => null
        };
        SelectedColorRgbText = $"RGB: ({r},{g},{b})";
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        SelectedColorBrush = brush;
        _selectedColorTableRange = tableRange;
        _selectedColorBaseOffset = baseOffset;
        OnPropertyChanged(nameof(SelectedColorCanEdit));
    }

    private int? FindFrameIndexForRange(GifByteRange range)
    {
        foreach (var root in StructureRoots)
        {
            var found = FindFrameIndexForRange(root, range);
            if (found.HasValue)
                return found;
        }

        return null;
    }

    private static int? FindFrameIndexForRange(GifStructureNode node, GifByteRange range)
    {
        if (node.Range == range && node.FrameIndex.HasValue)
            return node.FrameIndex.Value;

        foreach (var child in node.Children)
        {
            var found = FindFrameIndexForRange(child, range);
            if (found.HasValue)
                return found;
        }

        return null;
    }

    private void ClearSelectedColorInfo()
    {
        SelectedColorTableLabel = null;
        SelectedColorIndex = null;
        SelectedColorChannel = null;
        SelectedColorRgbText = null;
        SelectedColorBrush = null;
        _selectedColorTableRange = null;
        _selectedColorBaseOffset = null;
        OnPropertyChanged(nameof(SelectedColorCanEdit));
    }

    private void UpdateSelectedGceInfo(int offset)
    {
        var file = CurrentFile;
        if (file is null)
        {
            ClearSelectedGceInfo();
            return;
        }

        var gceRange = Blocks.FirstOrDefault(r => r.Kind == GifBlockKind.GraphicControlExtension && r.Contains(offset));
        if (gceRange is null || gceRange.Length < 8)
        {
            ClearSelectedGceInfo();
            return;
        }

        int start = gceRange.Start;
        if (start < 0 || start + 7 >= file.Bytes.Length)
        {
            ClearSelectedGceInfo();
            return;
        }

        byte packed = file.Bytes[start + 3];
        ushort delay = (ushort)(file.Bytes[start + 4] | (file.Bytes[start + 5] << 8));
        byte transparentIndex = file.Bytes[start + 6];

        int disposal = (packed >> 2) & 0b0000_0111;
        bool userInput = (packed & 0b0000_0010) != 0;
        bool transparency = (packed & 0b0000_0001) != 0;

        string disposalText = disposal switch
        {
            0 => "Disposal: 0 (No disposal specified)",
            1 => "Disposal: 1 (Do not dispose)",
            2 => "Disposal: 2 (Restore to background)",
            3 => "Disposal: 3 (Restore to previous)",
            _ => $"Disposal: {disposal} (Reserved)"
        };

        int delayMs = delay * 10;

        SelectedGceLabel = "Graphic Control Extension (GCE)";
        SelectedGceDelayText = $"Delay: {delay} cs ({delayMs} ms)";
        SelectedGceDisposalText = disposalText;
        SelectedGceTransparencyText = $"Transparency: {(transparency ? "Yes" : "No")}, Index: {transparentIndex}, User Input: {(userInput ? "Yes" : "No")}";
    }

    private void ClearSelectedGceInfo()
    {
        SelectedGceLabel = null;
        SelectedGceDelayText = null;
        SelectedGceDisposalText = null;
        SelectedGceTransparencyText = null;
    }

    private void UpdateSelectedLsdInfo(int offset)
    {
        var file = CurrentFile;
        if (file is null)
        {
            ClearSelectedLsdInfo();
            return;
        }

        var lsdRange = Blocks.FirstOrDefault(r => r.Kind == GifBlockKind.LogicalScreenDescriptor && r.Contains(offset));
        if (lsdRange is null || lsdRange.Length < 7)
        {
            ClearSelectedLsdInfo();
            return;
        }

        int start = lsdRange.Start;
        if (start < 0 || start + 6 >= file.Bytes.Length)
        {
            ClearSelectedLsdInfo();
            return;
        }

        ushort width = (ushort)(file.Bytes[start + 0] | (file.Bytes[start + 1] << 8));
        ushort height = (ushort)(file.Bytes[start + 2] | (file.Bytes[start + 3] << 8));
        byte packed = file.Bytes[start + 4];
        byte bgIndex = file.Bytes[start + 5];
        byte pixelAspect = file.Bytes[start + 6];

        bool gctPresent = (packed & 0b1000_0000) != 0;
        int colorResolutionBits = ((packed >> 4) & 0b0000_0111) + 1;
        bool sortFlag = (packed & 0b0000_1000) != 0;
        int gctSize = 1 << ((packed & 0b0000_0111) + 1);

        SelectedLsdLabel = "Logical Screen Descriptor (LSD)";
        SelectedLsdDimensions = $"Logical Screen: {width}×{height}";
        SelectedLsdGctPresent = $"GCT present: {(gctPresent ? "Yes" : "No")}";
        SelectedLsdGctSize = gctPresent ? $"GCT size: {gctSize}" : null;
        SelectedLsdColorResolution = $"Color resolution: {colorResolutionBits} bits";
        SelectedLsdSortFlag = $"Sort flag: {(sortFlag ? "Yes" : "No")}";
        SelectedLsdBackgroundIndex = $"Background Color Index: {bgIndex}";
        SelectedLsdPixelAspect = $"Pixel Aspect Ratio: {pixelAspect}";

        SelectedLsdBackgroundRgb = null;
        SelectedLsdBackgroundBrush = null;

        if (gctPresent && GctRange is not null)
        {
            int gctStart = GctRange.Start;
            int colorOffset = gctStart + (bgIndex * 3);
            if (colorOffset >= gctStart && colorOffset + 2 < file.Bytes.Length)
            {
                byte r = file.Bytes[colorOffset + 0];
                byte g = file.Bytes[colorOffset + 1];
                byte b = file.Bytes[colorOffset + 2];
                SelectedLsdBackgroundRgb = $"Background RGB: ({r},{g},{b})";
                var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
                brush.Freeze();
                SelectedLsdBackgroundBrush = brush;
            }
        }
    }

    private void ClearSelectedLsdInfo()
    {
        SelectedLsdLabel = null;
        SelectedLsdDimensions = null;
        SelectedLsdGctPresent = null;
        SelectedLsdGctSize = null;
        SelectedLsdColorResolution = null;
        SelectedLsdSortFlag = null;
        SelectedLsdBackgroundIndex = null;
        SelectedLsdPixelAspect = null;
        SelectedLsdBackgroundRgb = null;
        SelectedLsdBackgroundBrush = null;
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
