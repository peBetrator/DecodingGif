using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DecodingGif.Core.Models;
using DecodingGif.Core.Services;
using WinForms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace DecodingGif.UI.ViewModels;

public sealed class AnimationPropertiesEditorViewModel : INotifyPropertyChanged
{
    private sealed record DisposalOption(int Value, string Name, string Description);

    private readonly GifOptimizationAnalyzer _optimizationAnalyzer = new();
    private readonly AnimationFPSAnalyzer _fpsAnalyzer = new();
    private readonly GifStructureService _structureService = new();
    private readonly FrameManagerService _frameManagerService = new();
    private static readonly WpfBrush SmoothBrush = CreateFrozenBrush(0x15, 0x65, 0x3A);
    private static readonly WpfBrush AcceptableBrush = CreateFrozenBrush(0x1D, 0x4E, 0x89);
    private static readonly WpfBrush ChoppyBrush = CreateFrozenBrush(0xB4, 0x53, 0x09);
    private static readonly WpfBrush VeryChoppyBrush = CreateFrozenBrush(0x99, 0x1B, 0x1B);
    private static readonly WpfBrush ExcellentBrush = CreateFrozenBrush(0x15, 0x65, 0x3A);
    private static readonly WpfBrush GoodBrush = CreateFrozenBrush(0x1D, 0x4E, 0x89);
    private static readonly WpfBrush FairBrush = CreateFrozenBrush(0xB4, 0x53, 0x09);
    private static readonly WpfBrush PoorBrush = CreateFrozenBrush(0x99, 0x1B, 0x1B);
    private readonly ObservableCollection<DisposalOption> _disposalOptions =
    [
        new(0, "0 - None", "No disposal specified. Renderer-dependent behavior."),
        new(1, "1 - Keep", "Do not dispose. Keep previous frame pixels."),
        new(2, "2 - Clear", "Restore affected area to background color."),
        new(3, "3 - Restore", "Restore affected area to previous frame state.")
    ];

    private GifFile? _file;
    private IReadOnlyList<GifByteRange> _blocks = Array.Empty<GifByteRange>();
    private readonly Dictionary<int, GifByteRange> _gceByFrameIndex = [];
    private bool _isApplying;
    private int _selectedFrameIndex;
    private int _bulkDelayMs = 100;
    private int _bulkDisposalMethod = 1;
    private bool _bulkHasTransparency;
    private int _bulkTransparentColorIndex;
    private FPSAnalysisResult _fpsAnalysis = new();

    public ObservableCollection<EditableFrameSettings> Frames { get; } = [];
    public ObservableCollection<string> ValidationWarnings { get; } = [];
    public ObservableCollection<object> DisposalMethods { get; } = [];
    public ObservableCollection<string> FPSRecommendations { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsApplied;
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
        }
    }

    public int BulkDelayMs
    {
        get => _bulkDelayMs;
        set
        {
            int normalized = Math.Clamp(value, 0, 655350);
            if (_bulkDelayMs == normalized)
                return;
            _bulkDelayMs = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BulkDelayFpsPreviewText));
        }
    }

    public int BulkDisposalMethod
    {
        get => _bulkDisposalMethod;
        set
        {
            int normalized = Math.Clamp(value, 0, 7);
            if (_bulkDisposalMethod == normalized)
                return;
            _bulkDisposalMethod = normalized;
            OnPropertyChanged();
        }
    }

    public bool BulkHasTransparency
    {
        get => _bulkHasTransparency;
        set
        {
            if (_bulkHasTransparency == value)
                return;
            _bulkHasTransparency = value;
            OnPropertyChanged();
        }
    }

    public int BulkTransparentColorIndex
    {
        get => _bulkTransparentColorIndex;
        set
        {
            int normalized = Math.Clamp(value, 0, 255);
            if (_bulkTransparentColorIndex == normalized)
                return;
            _bulkTransparentColorIndex = normalized;
            OnPropertyChanged();
        }
    }

    public int SelectedFramesCount => Frames.Count(f => f.IsSelected);
    public bool HasFrames => Frames.Count > 0;
    public FPSAnalysisResult FPSAnalysis
    {
        get => _fpsAnalysis;
        private set
        {
            _fpsAnalysis = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasFPSAnalysis));
            OnPropertyChanged(nameof(AverageFPSText));
            OnPropertyChanged(nameof(FPSRangeText));
            OnPropertyChanged(nameof(FPSVarianceText));
            OnPropertyChanged(nameof(FPSPerformanceText));
            OnPropertyChanged(nameof(FPSConsistencyText));
            OnPropertyChanged(nameof(FPSConsistencyPercent));
            OnPropertyChanged(nameof(FPSPerformanceBrush));
            OnPropertyChanged(nameof(FPSConsistencyBrush));
        }
    }

    public bool HasFPSAnalysis => HasFrames;
    public string AverageFPSText => HasFrames ? $"{FPSAnalysis.AverageFPS:0.0} FPS" : "—";
    public string FPSRangeText => HasFrames ? $"{FPSAnalysis.MinFPS:0.0} - {FPSAnalysis.MaxFPS:0.0} FPS" : "—";
    public string FPSVarianceText => HasFrames ? $"{FPSAnalysis.FPSVariance:0.00}" : "—";
    public string FPSPerformanceText => GetPerformanceLabel(FPSAnalysis.PerformanceRating);
    public string FPSConsistencyText => HasFrames ? $"{GetConsistencyLabel(FPSAnalysis.ConsistencyRating)} ({FPSConsistencyPercent:0}%)" : "—";
    public double FPSConsistencyPercent => Math.Clamp(100.0 - (FPSAnalysis.FPSVariance * 20.0), 0.0, 100.0);
    public WpfBrush FPSPerformanceBrush => GetPerformanceBrush(FPSAnalysis.PerformanceRating);
    public WpfBrush FPSConsistencyBrush => GetConsistencyBrush(FPSAnalysis.ConsistencyRating);
    public string BulkDelayFpsPreviewText
    {
        get
        {
            double fps = 1000.0 / Math.Max(10.0, BulkDelayMs);
            return $"Массовая задержка {BulkDelayMs} мс даст примерно {fps:0.0} FPS на выбранных кадрах.";
        }
    }

    public ICommand ApplyBulkCommand { get; }
    public ICommand SelectAllFramesCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand DeleteSelectedFramesCommand { get; }

    public AnimationPropertiesEditorViewModel()
    {
        foreach (var option in _disposalOptions)
            DisposalMethods.Add(option);

        Frames.CollectionChanged += Frames_CollectionChanged;
        ApplyBulkCommand = new RelayCommand(ApplyBulkChanges, () => Frames.Count > 0);
        SelectAllFramesCommand = new RelayCommand(SelectAllFrames, () => Frames.Count > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => Frames.Count > 0);
        DeleteSelectedFramesCommand = new RelayCommand(DeleteSelectedFrames, CanDeleteSelectedFrames);
    }

    public IEnumerable<object> GetDisposalOptions() => DisposalMethods;

    public string GetDisposalTooltip(object? option)
    {
        if (option is not DisposalOption disposal)
            return string.Empty;
        return disposal.Description;
    }

    public int GetDisposalValue(object? option)
    {
        if (option is not DisposalOption disposal)
            return 0;
        return disposal.Value;
    }

    public string GetDisposalName(int disposalValue)
    {
        var item = _disposalOptions.FirstOrDefault(d => d.Value == disposalValue);
        return item?.Name ?? $"{disposalValue} - Custom";
    }

    public void Load(GifFile? file, IReadOnlyList<GifByteRange> blocks)
    {
        _file = file;
        _blocks = _file is null ? Array.Empty<GifByteRange>() : _structureService.BuildRanges(_file);
        Frames.Clear();
        _gceByFrameIndex.Clear();
        ValidationWarnings.Clear();

        if (_file is null || _blocks.Count == 0)
        {
            RaiseComputedProperties();
            return;
        }

        BuildFrameSettings();
        UpdateTimelineBars();
        UpdateFPSAnalysis();
        RefreshValidationWarnings();
        RaiseComputedProperties();
    }

    public void SetSelectedFrameIndex(int frameIndex) => SelectedFrameIndex = frameIndex;

    private void BuildFrameSettings()
    {
        if (_file is null)
            return;

        var ordered = _blocks.OrderBy(b => b.Start).ToList();
        GifByteRange? pendingGce = null;
        int frameIndex = 0;
        foreach (var block in ordered)
        {
            if (block.Kind == GifBlockKind.GraphicControlExtension)
            {
                pendingGce = block;
                continue;
            }

            if (block.Kind != GifBlockKind.ImageDescriptor)
                continue;

            int delayMs = 100;
            int disposal = 0;
            bool transparency = false;
            int transparentIndex = 0;
            bool hasGce = false;

            if (pendingGce is not null && TryReadGceSettings(_file.Bytes, pendingGce, out var settings))
            {
                hasGce = true;
                delayMs = settings.DelayMs;
                disposal = settings.DisposalMethod;
                transparency = settings.HasTransparency;
                transparentIndex = settings.TransparentColorIndex;
                _gceByFrameIndex[frameIndex] = pendingGce;
            }

            var frame = new EditableFrameSettings
            {
                FrameIndex = frameIndex,
                HasGraphicControlExtension = hasGce,
                DelayMs = delayMs,
                DisposalMethod = disposal,
                HasTransparency = transparency,
                TransparentColorIndex = transparentIndex
            };

            Frames.Add(frame);
            frameIndex++;
            pendingGce = null;
        }
    }

    private void ApplyBulkChanges()
    {
        if (_file is null || _isApplying)
            return;

        _isApplying = true;
        try
        {
            var targets = Frames.Where(f => f.IsSelected).ToList();
            if (targets.Count == 0)
                targets = Frames.ToList();

            foreach (var frame in targets)
            {
                frame.DelayMs = BulkDelayMs;
                frame.DisposalMethod = BulkDisposalMethod;
                frame.HasTransparency = BulkHasTransparency;
                frame.TransparentColorIndex = BulkTransparentColorIndex;
                WriteFrameToGce(frame);
            }

            UpdateTimelineBars();
            UpdateFPSAnalysis();
            RefreshValidationWarnings();
            RaiseSettingsApplied();
        }
        catch (Exception ex)
        {
            ValidationWarnings.Add($"Bulk apply failed: {ex.Message}");
        }
        finally
        {
            _isApplying = false;
        }
    }

    private void Frame_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not EditableFrameSettings frame)
            return;

        if (e.PropertyName == nameof(EditableFrameSettings.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedFramesCount));
            (DeleteSelectedFramesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            return;
        }

        if (_isApplying || _file is null)
            return;

        if (e.PropertyName is nameof(EditableFrameSettings.DelayMs)
            or nameof(EditableFrameSettings.DisposalMethod)
            or nameof(EditableFrameSettings.HasTransparency)
            or nameof(EditableFrameSettings.TransparentColorIndex))
        {
            try
            {
                WriteFrameToGce(frame);
                UpdateTimelineBars();
                UpdateFPSAnalysis();
                RefreshValidationWarnings();
                RaiseSettingsApplied();
            }
            catch (Exception ex)
            {
                ValidationWarnings.Add($"Frame {frame.FrameIndex + 1}: failed to apply change ({ex.Message}).");
            }
        }
    }

    private void WriteFrameToGce(EditableFrameSettings frame)
    {
        if (_file is null || !_gceByFrameIndex.TryGetValue(frame.FrameIndex, out var gce))
            return;
        if (gce.Start < 0 || gce.Start + 7 >= _file.Bytes.Length)
            return;

        int start = gce.Start;
        byte packed = _file.Bytes[start + 3];
        byte userInputBit = (byte)(packed & 0b0000_0010);
        byte disposalBits = (byte)((frame.DisposalMethod & 0b111) << 2);
        byte transparencyBit = (byte)(frame.HasTransparency ? 0b0000_0001 : 0);
        _file.Bytes[start + 3] = (byte)(userInputBit | disposalBits | transparencyBit);

        int delayCentiseconds = Math.Clamp((int)Math.Round(frame.DelayMs / 10.0), 0, ushort.MaxValue);
        _file.Bytes[start + 4] = (byte)(delayCentiseconds & 0xFF);
        _file.Bytes[start + 5] = (byte)((delayCentiseconds >> 8) & 0xFF);
        _file.Bytes[start + 6] = (byte)frame.TransparentColorIndex;
    }

    private void RefreshValidationWarnings()
    {
        ValidationWarnings.Clear();
        if (_file is null || _blocks.Count == 0)
            return;

        foreach (var frame in Frames)
        {
            if (!frame.HasGraphicControlExtension)
                ValidationWarnings.Add($"Frame {frame.FrameIndex + 1}: no GCE block; timing/disposal edits are not applied.");
            if (frame.DelayMs is > 0 and < 20)
                ValidationWarnings.Add($"Frame {frame.FrameIndex + 1}: delay {frame.DelayMs}ms may be too fast.");
            if (frame.DisposalMethod > 3)
                ValidationWarnings.Add($"Frame {frame.FrameIndex + 1}: disposal {frame.DisposalMethod} is reserved.");
        }

        if (FPSAnalysis.AverageFPS is > 0 and < 6)
            ValidationWarnings.Add($"FPS: средняя частота {FPSAnalysis.AverageFPS:0.0} FPS даёт очень дёрганое воспроизведение.");
        else if (FPSAnalysis.AverageFPS is >= 6 and < 12)
            ValidationWarnings.Add($"FPS: средняя частота {FPSAnalysis.AverageFPS:0.0} FPS всё ещё выглядит рвано.");

        if (FPSAnalysis.FPSVariance > 4.0)
            ValidationWarnings.Add($"FPS: нестабильный тайминг кадров (отклонение {FPSAnalysis.FPSVariance:0.00}).");

        var suggestions = _optimizationAnalyzer.AnalyzeFile(_file, _blocks).Suggestions
            .Where(s => s.Type is OptimizationType.AnimationTiming or OptimizationType.DisposalMethod)
            .Select(s => $"{s.Title}: {s.Recommendation}");

        foreach (var warning in suggestions)
            ValidationWarnings.Add(warning);
    }

    private void UpdateTimelineBars()
    {
        int maxDelay = Math.Max(Frames.Count > 0 ? Frames.Max(f => f.DelayMs) : 1, 1);
        foreach (var frame in Frames)
        {
            double factor = frame.DelayMs / (double)maxDelay;
            frame.TimelineBarWidth = 24 + (factor * 180);
        }
    }

    private void UpdateFPSAnalysis()
    {
        var analysisFrames = Frames
            .OrderBy(frame => frame.FrameIndex)
            .Select(frame => new GifByteRange(
                GifBlockKind.ImageDescriptor,
                $"Frame {frame.FrameIndex + 1}",
                0,
                0,
                frame.FrameIndex,
                frame.DelayMs))
            .ToList();

        FPSAnalysis = _fpsAnalyzer.Analyze(analysisFrames);
        FPSRecommendations.Clear();
        foreach (string recommendation in FPSAnalysis.Recommendations)
            FPSRecommendations.Add(recommendation);
    }

    private void SelectAllFrames()
    {
        foreach (var frame in Frames)
            frame.IsSelected = true;
        OnPropertyChanged(nameof(SelectedFramesCount));
    }

    private void ClearSelection()
    {
        foreach (var frame in Frames)
            frame.IsSelected = false;
        OnPropertyChanged(nameof(SelectedFramesCount));
        (DeleteSelectedFramesCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public void MoveFrameUpAt(int frameIndex)
    {
        if (_file is null || frameIndex <= 0 || frameIndex >= Frames.Count)
            return;

        try
        {
            var result = _frameManagerService.MoveFrameUp(_file, frameIndex);
            SelectedFrameIndex = frameIndex - 1;
            FrameEdited?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            ValidationWarnings.Add($"Move up failed for frame {frameIndex + 1}: {ex.Message}");
        }
    }

    public void MoveFrameDownAt(int frameIndex)
    {
        if (_file is null || frameIndex < 0 || frameIndex >= Frames.Count - 1)
            return;

        try
        {
            var result = _frameManagerService.MoveFrameDown(_file, frameIndex);
            SelectedFrameIndex = frameIndex + 1;
            FrameEdited?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            ValidationWarnings.Add($"Move down failed for frame {frameIndex + 1}: {ex.Message}");
        }
    }

    public void DeleteFrameAt(int frameIndex)
    {
        if (_file is null || Frames.Count <= 1 || frameIndex < 0 || frameIndex >= Frames.Count)
            return;

        if (WinForms.MessageBox.Show(
                $"Delete frame {frameIndex + 1}? This action is destructive.",
                "Delete Frame",
                WinForms.MessageBoxButtons.YesNo,
                WinForms.MessageBoxIcon.Warning) != WinForms.DialogResult.Yes)
            return;

        try
        {
            int desiredIndex = Math.Max(0, frameIndex - 1);
            var result = _frameManagerService.DeleteFrame(_file, frameIndex);
            SelectedFrameIndex = desiredIndex;
            FrameEdited?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            ValidationWarnings.Add($"Delete failed for frame {frameIndex + 1}: {ex.Message}");
        }
    }

    private void DeleteSelectedFrames()
    {
        if (_file is null)
            return;

        var selectedIndexes = Frames
            .Where(f => f.IsSelected)
            .Select(f => f.FrameIndex)
            .Distinct()
            .OrderByDescending(i => i)
            .ToList();

        if (selectedIndexes.Count == 0)
            return;

        if (Frames.Count - selectedIndexes.Count < 1)
        {
            ValidationWarnings.Add("Batch delete cancelled: at least one frame must remain.");
            return;
        }

        if (WinForms.MessageBox.Show(
                $"Delete {selectedIndexes.Count} selected frame(s)? This action is destructive.",
                "Delete Selected Frames",
                WinForms.MessageBoxButtons.YesNo,
                WinForms.MessageBoxIcon.Warning) != WinForms.DialogResult.Yes)
            return;

        try
        {
            GifFile currentFile = _file;
            FrameEditResult? lastResult = null;
            foreach (int index in selectedIndexes)
            {
                lastResult = _frameManagerService.DeleteFrame(currentFile, index);
                currentFile = lastResult.UpdatedFile;
            }

            if (lastResult is null)
                return;

            int desiredIndex = Math.Clamp(SelectedFrameIndex, 0, Math.Max(0, Frames.Count - selectedIndexes.Count - 1));
            SelectedFrameIndex = desiredIndex;
            FrameEdited?.Invoke(this, lastResult);
        }
        catch (Exception ex)
        {
            ValidationWarnings.Add($"Batch delete failed: {ex.Message}");
        }
    }

    private bool CanDeleteSelectedFrames()
    {
        int selected = Frames.Count(f => f.IsSelected);
        return _file is not null && selected > 0 && Frames.Count - selected >= 1;
    }

    private void Frames_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<EditableFrameSettings>())
            {
                item.PropertyChanged -= Frame_PropertyChanged;
                item.PropertyChanged += Frame_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<EditableFrameSettings>())
                item.PropertyChanged -= Frame_PropertyChanged;
        }

        UpdateFPSAnalysis();
        if (_file is not null)
            RefreshValidationWarnings();
        RaiseComputedProperties();
    }

    private void RaiseComputedProperties()
    {
        OnPropertyChanged(nameof(HasFrames));
        OnPropertyChanged(nameof(HasFPSAnalysis));
        OnPropertyChanged(nameof(SelectedFramesCount));
        (ApplyBulkCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SelectAllFramesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteSelectedFramesCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RaiseSettingsApplied()
    {
        try
        {
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ValidationWarnings.Add($"Preview refresh failed: {ex.Message}");
        }
    }

    private static bool TryReadGceSettings(byte[] bytes, GifByteRange gce, out (int DelayMs, int DisposalMethod, bool HasTransparency, int TransparentColorIndex) settings)
    {
        settings = default;
        if (gce.Start < 0 || gce.Start + 7 >= bytes.Length || gce.Length < 8)
            return false;

        int start = gce.Start;
        byte packed = bytes[start + 3];
        ushort delayCs = (ushort)(bytes[start + 4] | (bytes[start + 5] << 8));
        byte transparentIndex = bytes[start + 6];

        settings = (
            DelayMs: delayCs * 10,
            DisposalMethod: (packed >> 2) & 0b111,
            HasTransparency: (packed & 0b0000_0001) != 0,
            TransparentColorIndex: transparentIndex);
        return true;
    }

    private static string GetPerformanceLabel(FPSPerformanceRating rating) =>
        rating switch
        {
            FPSPerformanceRating.Smooth => "Плавно",
            FPSPerformanceRating.Acceptable => "Приемлемо",
            FPSPerformanceRating.Choppy => "Рвано",
            FPSPerformanceRating.VeryChoppy => "Очень рвано",
            _ => "—"
        };

    private static string GetConsistencyLabel(FPSConsistencyRating rating) =>
        rating switch
        {
            FPSConsistencyRating.Excellent => "Отличная стабильность",
            FPSConsistencyRating.Good => "Хорошая стабильность",
            FPSConsistencyRating.Fair => "Средняя стабильность",
            FPSConsistencyRating.Poor => "Плохая стабильность",
            _ => "—"
        };

    private static WpfBrush GetPerformanceBrush(FPSPerformanceRating rating) =>
        rating switch
        {
            FPSPerformanceRating.Smooth => SmoothBrush,
            FPSPerformanceRating.Acceptable => AcceptableBrush,
            FPSPerformanceRating.Choppy => ChoppyBrush,
            FPSPerformanceRating.VeryChoppy => VeryChoppyBrush,
            _ => WpfBrushes.Gray
        };

    private static WpfBrush GetConsistencyBrush(FPSConsistencyRating rating) =>
        rating switch
        {
            FPSConsistencyRating.Excellent => ExcellentBrush,
            FPSConsistencyRating.Good => GoodBrush,
            FPSConsistencyRating.Fair => FairBrush,
            FPSConsistencyRating.Poor => PoorBrush,
            _ => WpfBrushes.Gray
        };

    private static WpfBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new WpfSolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
