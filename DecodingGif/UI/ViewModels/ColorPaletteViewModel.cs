using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WinForms = System.Windows.Forms;
using DecodingGif.Core.Models;
using DecodingGif.Core.Services;
using DecodingGif.UI.UndoRedo;
using DecodingGif.UI.UndoRedo.Commands;
using MediaColor = System.Windows.Media.Color;

namespace DecodingGif.UI.ViewModels;

public sealed class ColorPaletteViewModel : INotifyPropertyChanged
{
    private sealed record LocalPaletteInfo(int FrameIndex, GifByteRange Range);

    private readonly UndoRedoManager _undoRedoManager = new(10);
    private readonly BatchColorOperationService _batchColorOperationService = new();
    private readonly List<EditableColor> _allColors = [];
    private readonly List<LocalPaletteInfo> _localPalettes = [];
    private IReadOnlyList<GifByteRange> _blocks = Array.Empty<GifByteRange>();

    private GifFile? _file;
    private GifByteRange? _gctRange;
    private GifByteRange? _derivedGctRange;
    private byte[]? _originalBytes;
    private byte[]? _savedBytes;
    private string _searchText = string.Empty;
    private bool _showOnlyUsed;
    private bool _showSimilar;
    private PaletteEditMode _currentMode = PaletteEditMode.GlobalColorTable;
    private int _selectedFrameIndex;
    private bool _hasUnsavedChanges;
    private double _batchBrightnessFactor = 1.10;
    private double _batchContrastFactor = 1.10;
    private double _batchHueShift = 15;
    private string _replaceHexColor = "#FFFFFF";
    private ColorRgb? _clipboardColor;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<ColorChangeEventArgs>? ColorChanged;
    public event EventHandler<BatchOperationEventArgs>? BatchOperationRequested;

    public ObservableCollection<EditableColor> Colors { get; } = [];
    public ObservableCollection<int> AvailableFrames { get; } = [];
    public ObservableCollection<PaletteOperationEntry> RecentOperations { get; } = [];

    public PaletteEditMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode == value)
                return;
            _currentMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGCTMode));
            OnPropertyChanged(nameof(IsLCTMode));
            LoadActivePalette();
        }
    }

    public bool IsGCTMode
    {
        get => CurrentMode == PaletteEditMode.GlobalColorTable;
        set
        {
            if (value)
                CurrentMode = PaletteEditMode.GlobalColorTable;
        }
    }

    public bool IsLCTMode
    {
        get => CurrentMode == PaletteEditMode.LocalColorTable;
        set
        {
            if (value)
                CurrentMode = PaletteEditMode.LocalColorTable;
        }
    }

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
            if (CurrentMode == PaletteEditMode.LocalColorTable)
                LoadActivePalette();
        }
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (_hasUnsavedChanges == value)
                return;
            _hasUnsavedChanges = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
                return;
            _searchText = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public bool ShowOnlyUsed
    {
        get => _showOnlyUsed;
        set
        {
            if (_showOnlyUsed == value)
                return;
            _showOnlyUsed = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public bool ShowSimilar
    {
        get => _showSimilar;
        set
        {
            if (_showSimilar == value)
                return;
            _showSimilar = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public int GridColumns => Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, Colors.Count))));
    public int LoadedColorsCount => _allColors.Count;
    public int SelectedColorsCount => _allColors.Count(c => c.IsSelected);
    public int TotalColorsCount => _allColors.Count;
    public int UnusedColorsCount => _allColors.Count(c => !c.IsUsed);
    public bool HasColors => _allColors.Count > 0;
    public bool HasLocalColorTables => _localPalettes.Count > 0;
    public bool HasGlobalColorTable => EffectiveGctRange is not null;
    public bool ShowNoPaletteMessage => !HasColors;
    public bool CanSwitchToGlobalMode => CurrentMode == PaletteEditMode.LocalColorTable && !HasLocalColorTables && HasGlobalColorTable;
    public string NoPaletteMessage
    {
        get
        {
            if (_file is null)
                return "No file loaded. Open a GIF first.";

            if (CurrentMode == PaletteEditMode.GlobalColorTable)
                return EffectiveGctRange is null
                    ? "Global Color Table was not found in this GIF."
                    : "Global Color Table has no color entries.";

            if (!HasLocalColorTables)
                return HasGlobalColorTable
                    ? "No Local Color Tables found in this GIF. Switch to GCT mode."
                    : "No Local Color Tables found in this GIF.";

            return $"No Local Color Table for frame {SelectedFrameIndex}.";
        }
    }
    public bool CanUndo => _undoRedoManager.CanUndo;
    public bool CanRedo => _undoRedoManager.CanRedo;
    public string UndoDescription => _undoRedoManager.UndoDescription is null ? "Undo" : $"Undo {_undoRedoManager.UndoDescription}";
    public string RedoDescription => _undoRedoManager.RedoDescription is null ? "Redo" : $"Redo {_undoRedoManager.RedoDescription}";

    public double BatchBrightnessFactor
    {
        get => _batchBrightnessFactor;
        set
        {
            double clamped = Math.Clamp(value, 0.1, 3.0);
            if (Math.Abs(_batchBrightnessFactor - clamped) < double.Epsilon)
                return;
            _batchBrightnessFactor = clamped;
            OnPropertyChanged();
        }
    }

    public double BatchContrastFactor
    {
        get => _batchContrastFactor;
        set
        {
            double clamped = Math.Clamp(value, 0.1, 3.0);
            if (Math.Abs(_batchContrastFactor - clamped) < double.Epsilon)
                return;
            _batchContrastFactor = clamped;
            OnPropertyChanged();
        }
    }

    public double BatchHueShift
    {
        get => _batchHueShift;
        set
        {
            double clamped = Math.Clamp(value, -180, 180);
            if (Math.Abs(_batchHueShift - clamped) < double.Epsilon)
                return;
            _batchHueShift = clamped;
            OnPropertyChanged();
        }
    }

    public string ReplaceHexColor
    {
        get => _replaceHexColor;
        set
        {
            if (_replaceHexColor == value)
                return;
            _replaceHexColor = value;
            OnPropertyChanged();
        }
    }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand BatchBrightnessCommand { get; }
    public ICommand BatchContrastCommand { get; }
    public ICommand BatchHueCommand { get; }
    public ICommand BatchReplaceCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectUnusedCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand SaveChangesCommand { get; }
    public ICommand ResetChangesCommand { get; }
    public ICommand SwitchToGlobalModeCommand { get; }

    public ColorPaletteViewModel()
    {
        _undoRedoManager.PropertyChanged += (_, _) => RaiseHistoryChanged();
        Colors.CollectionChanged += Colors_CollectionChanged;
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
        BatchBrightnessCommand = new RelayCommand(ApplyBatchBrightness, () => _allColors.Count > 0);
        BatchContrastCommand = new RelayCommand(ApplyBatchContrast, () => _allColors.Count > 0);
        BatchHueCommand = new RelayCommand(ApplyBatchHue, () => _allColors.Count > 0);
        BatchReplaceCommand = new RelayCommand(ApplyBatchReplace, () => _allColors.Count > 0);
        SelectAllCommand = new RelayCommand(SelectAll, () => _allColors.Count > 0);
        SelectUnusedCommand = new RelayCommand(SelectUnused, () => _allColors.Count > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => _allColors.Count > 0);
        SaveChangesCommand = new RelayCommand(SaveChanges, () => HasUnsavedChanges);
        ResetChangesCommand = new RelayCommand(ResetChanges, () => HasUnsavedChanges && _originalBytes is not null);
        SwitchToGlobalModeCommand = new RelayCommand(() => CurrentMode = PaletteEditMode.GlobalColorTable, () => CanSwitchToGlobalMode);
    }

    public void LoadFromCurrentFile(GifFile? file, IEnumerable<GifByteRange> blocks)
    {
        _file = file;
        _blocks = blocks.ToList();
        _allColors.Clear();
        Colors.Clear();
        AvailableFrames.Clear();
        RecentOperations.Clear();
        _localPalettes.Clear();
        _undoRedoManager.Clear();

        if (_file is null)
        {
            _gctRange = null;
            _derivedGctRange = null;
            _originalBytes = null;
            _savedBytes = null;
            HasUnsavedChanges = false;
            RaiseMetricsChanged();
            return;
        }

        _gctRange = blocks.FirstOrDefault(b => b.Kind == GifBlockKind.GlobalColorTable);
        _derivedGctRange = BuildDerivedGctRange(_file);
        int frameIndex = 0;
        foreach (var lct in blocks.Where(b => b.Kind == GifBlockKind.LocalColorTable))
        {
            _localPalettes.Add(new LocalPaletteInfo(frameIndex, lct));
            AvailableFrames.Add(frameIndex);
            frameIndex++;
        }

        _originalBytes = (byte[])_file.Bytes.Clone();
        _savedBytes = (byte[])_file.Bytes.Clone();
        HasUnsavedChanges = false;

        if (EffectiveGctRange is not null)
            CurrentMode = PaletteEditMode.GlobalColorTable;
        else if (_localPalettes.Count > 0)
            CurrentMode = PaletteEditMode.LocalColorTable;

        SelectedFrameIndex = AvailableFrames.Count > 0 ? AvailableFrames[0] : 0;
        LoadActivePalette();
        RaiseHistoryChanged();
    }

    public void SetSelectedFrameIndex(int frameIndex)
    {
        SelectedFrameIndex = frameIndex;
    }

    private void LoadActivePalette()
    {
        _allColors.Clear();
        var range = ResolveActiveRange();
        if (_file is null || range is null)
        {
            Colors.Clear();
            RaiseMetricsChanged();
            return;
        }

        int colorCount = range.Length / 3;
        for (int i = 0; i < colorCount; i++)
        {
            int offset = range.Start + (i * 3);
            if (offset + 2 >= _file.Bytes.Length)
                break;

            var color = new EditableColor
            {
                Index = i,
                R = _file.Bytes[offset],
                G = _file.Bytes[offset + 1],
                B = _file.Bytes[offset + 2]
            };
            AttachColorCommands(color);
            color.PropertyChanged += EditableColor_PropertyChanged;
            _allColors.Add(color);
        }

        RefreshUsedFlags();
        UpdateModifiedFlags();
        ApplyFilters();
        RaiseMetricsChanged();
    }

    private void AttachColorCommands(EditableColor color)
    {
        color.EditColorCommand = new RelayCommand(() => EditColor(color));
        color.CopyColorCommand = new RelayCommand(() => CopyColor(color));
        color.PasteColorCommand = new RelayCommand(() => PasteColor(color), () => _clipboardColor.HasValue);
        color.ReplaceAllCommand = new RelayCommand(() => ReplaceAllInstances(color));
        color.AdjustBrightnessCommand = new RelayCommand(() => AdjustBrightness(color, 1.1));
        color.AdjustSaturationCommand = new RelayCommand(() => AdjustSaturation(color, 1.1));
    }

    private void EditColor(EditableColor color)
    {
        var dialog = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B)
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
            return;

        var c = dialog.Color;
        SetPaletteColor(color.Index, new ColorRgb(c.R, c.G, c.B), $"Set color #{color.Index} to ({c.R},{c.G},{c.B})");
    }

    private void CopyColor(EditableColor color)
    {
        _clipboardColor = new ColorRgb(color.R, color.G, color.B);
        RefreshItemCommandStates();
    }

    private void PasteColor(EditableColor color)
    {
        if (!_clipboardColor.HasValue)
            return;
        SetPaletteColor(color.Index, _clipboardColor.Value, $"Paste color to #{color.Index}");
    }

    private void ReplaceAllInstances(EditableColor color)
    {
        var dialog = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B)
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
            return;

        var c = dialog.Color;
        var range = ResolveActiveRange();
        if (_file is null || range is null)
            return;

        var command = new ReplaceColorCommand(
            _file.Bytes,
            range.Start,
            range.Length,
            new ColorRgb(color.R, color.G, color.B),
            new ColorRgb(c.R, c.G, c.B),
            $"Replace all {color.Hex} -> #{c.R:X2}{c.G:X2}{c.B:X2}");
        ExecuteUndoableCommand(command, true);
    }

    private void AdjustBrightness(EditableColor color, double factor)
    {
        var updated = ScaleColor(new ColorRgb(color.R, color.G, color.B), factor);
        SetPaletteColor(color.Index, updated, $"Adjust brightness of #{color.Index} by {factor:P0}");
    }

    private void AdjustSaturation(EditableColor color, double factor)
    {
        var updated = ShiftSaturation(new ColorRgb(color.R, color.G, color.B), factor);
        SetPaletteColor(color.Index, updated, $"Adjust saturation of #{color.Index} by {factor:P0}");
    }

    private void SetPaletteColor(int colorIndex, ColorRgb newColor, string description)
    {
        var range = ResolveActiveRange();
        if (_file is null || range is null)
            return;

        int baseOffset = range.Start + (colorIndex * 3);
        if (baseOffset < 0 || baseOffset + 2 >= _file.Bytes.Length)
            return;

        byte oldR = _file.Bytes[baseOffset];
        byte oldG = _file.Bytes[baseOffset + 1];
        byte oldB = _file.Bytes[baseOffset + 2];
        var command = new SetColorCommand(_file.Bytes, baseOffset, newColor, description);
        ExecuteUndoableCommand(command, false);
        ColorChanged?.Invoke(this, new ColorChangeEventArgs(colorIndex, oldR, oldG, oldB, newColor.R, newColor.G, newColor.B, description));
    }

    private void ExecuteUndoableCommand(IUndoableCommand command, bool isBatch)
    {
        _undoRedoManager.Execute(command);
        UpdateFromBytes();
        AddRecentOperation(command.Description);
        UpdateUnsavedState();
        if (isBatch)
            BatchOperationRequested?.Invoke(this, new BatchOperationEventArgs(command.Description, SelectedColorsCount));
        RaiseHistoryChanged();
    }

    private void Undo()
    {
        if (!_undoRedoManager.Undo())
            return;
        UpdateFromBytes();
        UpdateUnsavedState();
        RaiseHistoryChanged();
    }

    private void Redo()
    {
        if (!_undoRedoManager.Redo())
            return;
        UpdateFromBytes();
        UpdateUnsavedState();
        RaiseHistoryChanged();
    }

    private void ApplyBatchBrightness()
    {
        var indexes = GetTargetIndexes().ToList();
        if (indexes.Count == 0)
            return;

        ExecuteBatchSet(
            indexes,
            color => ScaleColor(color, BatchBrightnessFactor),
            $"Adjust brightness by {BatchBrightnessFactor:P0}");
    }

    private void ApplyBatchContrast()
    {
        var indexes = GetTargetIndexes().ToList();
        if (indexes.Count == 0)
            return;

        ExecuteBatchSet(
            indexes,
            color => AdjustContrast(color, BatchContrastFactor),
            $"Adjust contrast by {BatchContrastFactor:P0}");
    }

    private void ApplyBatchHue()
    {
        var indexes = GetTargetIndexes().ToList();
        if (indexes.Count == 0)
            return;

        ExecuteBatchSet(
            indexes,
            color => ShiftHue(color, BatchHueShift),
            $"Shift hue by {BatchHueShift:0} deg");
    }

    private void ApplyBatchReplace()
    {
        if (!TryParseHexColor(ReplaceHexColor, out var target))
            return;

        var indexes = GetTargetIndexes().ToList();
        if (indexes.Count == 0)
            return;

        ExecuteBatchSet(indexes, _ => target, $"Replace selected colors with {ReplaceHexColor}");
    }

    private void ExecuteBatchSet(IReadOnlyList<int> indexes, Func<ColorRgb, ColorRgb> transform, string description)
    {
        var range = ResolveActiveRange();
        if (_file is null || range is null)
            return;

        var commands = new List<IUndoableCommand>(indexes.Count);
        foreach (int index in indexes.Distinct().OrderBy(i => i))
        {
            int offset = range.Start + (index * 3);
            if (offset < 0 || offset + 2 >= _file.Bytes.Length)
                continue;

            var old = new ColorRgb(_file.Bytes[offset], _file.Bytes[offset + 1], _file.Bytes[offset + 2]);
            var @new = transform(old);
            commands.Add(new SetColorCommand(_file.Bytes, offset, @new, $"Set color #{index}"));
        }

        if (commands.Count == 0)
            return;

        ExecuteUndoableCommand(new BatchCommand(description, commands), true);
    }

    private void SaveChanges()
    {
        if (_file is null)
            return;
        _savedBytes = (byte[])_file.Bytes.Clone();
        HasUnsavedChanges = false;
        AddRecentOperation("Saved palette changes");
        RaiseHistoryChanged();
    }

    private void ResetChanges()
    {
        if (_file is null || _originalBytes is null)
            return;

        foreach (var range in GetTrackedRanges())
        {
            if (range.Start < 0 || range.Start + range.Length > _file.Bytes.Length || range.Start + range.Length > _originalBytes.Length)
                continue;
            Array.Copy(_originalBytes, range.Start, _file.Bytes, range.Start, range.Length);
        }

        _savedBytes = (byte[])_originalBytes.Clone();
        _undoRedoManager.Clear();
        RecentOperations.Clear();
        HasUnsavedChanges = false;
        UpdateFromBytes();
        RaiseHistoryChanged();
    }

    private IEnumerable<int> GetTargetIndexes()
    {
        var selected = _allColors.Where(c => c.IsSelected).Select(c => c.Index).ToList();
        if (selected.Count > 0)
            return selected;
        return _allColors.Select(c => c.Index);
    }

    private void SelectAll()
    {
        foreach (var color in _allColors)
            color.IsSelected = true;
        RaiseMetricsChanged();
    }

    private void SelectUnused()
    {
        foreach (var color in _allColors)
            color.IsSelected = !color.IsUsed;
        RaiseMetricsChanged();
    }

    private void ClearSelection()
    {
        foreach (var color in _allColors)
            color.IsSelected = false;
        RaiseMetricsChanged();
    }

    private void UpdateFromBytes()
    {
        var range = ResolveActiveRange();
        if (_file is null || range is null)
            return;

        if ((_allColors.Count * 3) > range.Length)
        {
            LoadActivePalette();
            return;
        }

        foreach (var color in _allColors)
        {
            int offset = range.Start + (color.Index * 3);
            if (offset < 0 || offset + 2 >= _file.Bytes.Length)
                continue;
            color.R = _file.Bytes[offset];
            color.G = _file.Bytes[offset + 1];
            color.B = _file.Bytes[offset + 2];
        }

        RefreshUsedFlags();
        UpdateModifiedFlags();
        ApplyFilters();
        RaiseMetricsChanged();
    }

    private void UpdateModifiedFlags()
    {
        if (_file is null || _originalBytes is null)
            return;

        var range = ResolveActiveRange();
        if (range is null)
            return;

        foreach (var color in _allColors)
        {
            int offset = range.Start + (color.Index * 3);
            if (offset < 0 || offset + 2 >= _file.Bytes.Length || offset + 2 >= _originalBytes.Length)
            {
                color.IsModified = false;
                continue;
            }

            color.IsModified =
                _file.Bytes[offset] != _originalBytes[offset]
                || _file.Bytes[offset + 1] != _originalBytes[offset + 1]
                || _file.Bytes[offset + 2] != _originalBytes[offset + 2];
        }
    }

    private void ApplyFilters()
    {
        IEnumerable<EditableColor> filtered = _allColors;

        if (ShowOnlyUsed)
            filtered = filtered.Where(c => c.IsUsed);

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(MatchesSearch);

        if (ShowSimilar)
        {
            var anchor = _allColors.FirstOrDefault(c => c.IsSelected);
            if (anchor is not null)
                filtered = filtered.Where(c => ColorDistance(anchor, c) <= 32);
        }

        Colors.Clear();
        foreach (var color in filtered)
            Colors.Add(color);

        OnPropertyChanged(nameof(GridColumns));
        RaiseMetricsChanged();
    }

    private bool MatchesSearch(EditableColor color)
    {
        string query = SearchText.Trim();
        if (query.StartsWith('#') && query.Length >= 2)
        {
            string hex = query.TrimStart('#').ToUpperInvariant();
            return color.Hex.Contains(hex, StringComparison.OrdinalIgnoreCase);
        }

        return color.R.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase)
            || color.G.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase)
            || color.B.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase)
            || color.Index.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateUnsavedState()
    {
        if (_file is null || _savedBytes is null)
        {
            HasUnsavedChanges = false;
            return;
        }

        foreach (var range in GetTrackedRanges())
        {
            int start = range.Start;
            int end = Math.Min(start + range.Length, Math.Min(_file.Bytes.Length, _savedBytes.Length));
            for (int i = start; i < end; i++)
            {
                if (_file.Bytes[i] == _savedBytes[i])
                    continue;
                HasUnsavedChanges = true;
                return;
            }
        }

        HasUnsavedChanges = false;
    }

    private IEnumerable<GifByteRange> GetTrackedRanges()
    {
        if (EffectiveGctRange is not null)
            yield return EffectiveGctRange;

        foreach (var lct in _localPalettes)
            yield return lct.Range;
    }

    private GifByteRange? ResolveActiveRange()
    {
        if (CurrentMode == PaletteEditMode.GlobalColorTable)
            return EffectiveGctRange;

        var selected = _localPalettes.FirstOrDefault(p => p.FrameIndex == SelectedFrameIndex);
        return selected?.Range ?? _localPalettes.FirstOrDefault()?.Range;
    }

    private GifByteRange? EffectiveGctRange => _gctRange ?? _derivedGctRange;

    private void RefreshUsedFlags()
    {
        if (_file is null || _allColors.Count == 0)
            return;

        var target = CurrentMode == PaletteEditMode.GlobalColorTable
            ? PaletteTarget.Global()
            : PaletteTarget.Local(SelectedFrameIndex);

        try
        {
            var unused = _batchColorOperationService.DetectUnusedColors(_file, _blocks, target);
            var unusedSet = new HashSet<int>(unused);
            foreach (var color in _allColors)
                color.IsUsed = !unusedSet.Contains(color.Index);
        }
        catch
        {
            foreach (var color in _allColors)
                color.IsUsed = true;
        }
    }

    private static GifByteRange? BuildDerivedGctRange(GifFile file)
    {
        if (!file.Screen.GlobalColorTableFlag)
            return null;

        const int gctStart = 13; // Header(6) + LSD(7)
        if (file.Bytes.Length <= gctStart)
            return null;

        int expectedLength = file.Screen.GlobalColorTableSize * 3;
        int safeLength = Math.Min(expectedLength, Math.Max(0, file.Bytes.Length - gctStart));
        if (safeLength < 3)
            return null;

        return new GifByteRange(GifBlockKind.GlobalColorTable, "Derived Global Color Table (GCT)", gctStart, safeLength);
    }

    private void AddRecentOperation(string description)
    {
        RecentOperations.Insert(0, new PaletteOperationEntry(description, DateTime.Now));
        while (RecentOperations.Count > 10)
            RecentOperations.RemoveAt(RecentOperations.Count - 1);
    }

    private void EditableColor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditableColor.IsSelected))
            return;
        RaiseMetricsChanged();
    }

    private void Colors_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<EditableColor>())
                item.PropertyChanged += EditableColor_PropertyChanged;
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<EditableColor>())
                item.PropertyChanged -= EditableColor_PropertyChanged;
        }
    }

    private void RaiseMetricsChanged()
    {
        OnPropertyChanged(nameof(LoadedColorsCount));
        OnPropertyChanged(nameof(SelectedColorsCount));
        OnPropertyChanged(nameof(TotalColorsCount));
        OnPropertyChanged(nameof(UnusedColorsCount));
        OnPropertyChanged(nameof(GridColumns));
        OnPropertyChanged(nameof(HasColors));
        OnPropertyChanged(nameof(HasLocalColorTables));
        OnPropertyChanged(nameof(HasGlobalColorTable));
        OnPropertyChanged(nameof(ShowNoPaletteMessage));
        OnPropertyChanged(nameof(CanSwitchToGlobalMode));
        OnPropertyChanged(nameof(NoPaletteMessage));
    }

    private void RaiseHistoryChanged()
    {
        (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RedoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveChangesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ResetChangesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SwitchToGlobalModeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        RefreshItemCommandStates();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoDescription));
        OnPropertyChanged(nameof(RedoDescription));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void RefreshItemCommandStates()
    {
        foreach (var color in _allColors)
            (color.PasteColorCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private static bool TryParseHexColor(string input, out ColorRgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var hex = input.Trim().TrimStart('#');
        if (hex.Length != 6)
            return false;

        if (!byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r))
            return false;
        if (!byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g))
            return false;
        if (!byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            return false;

        color = new ColorRgb(r, g, b);
        return true;
    }

    private static ColorRgb ScaleColor(ColorRgb color, double factor) =>
        new(
            (byte)Math.Clamp((int)Math.Round(color.R * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.G * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.B * factor), 0, 255));

    private static ColorRgb AdjustContrast(ColorRgb color, double factor)
    {
        byte Apply(byte channel)
        {
            double centered = ((channel / 255.0) - 0.5) * factor + 0.5;
            return (byte)Math.Clamp((int)Math.Round(centered * 255), 0, 255);
        }

        return new ColorRgb(Apply(color.R), Apply(color.G), Apply(color.B));
    }

    private static ColorRgb ShiftHue(ColorRgb color, double degrees)
    {
        var media = MediaColor.FromRgb(color.R, color.G, color.B);
        RgbToHsv(media, out double h, out double s, out double v);
        h = (h + degrees + 360) % 360;
        var shifted = HsvToRgb(h, s, v);
        return new ColorRgb(shifted.R, shifted.G, shifted.B);
    }

    private static ColorRgb ShiftSaturation(ColorRgb color, double factor)
    {
        var media = MediaColor.FromRgb(color.R, color.G, color.B);
        RgbToHsv(media, out double h, out double s, out double v);
        s = Math.Clamp(s * factor, 0.0, 1.0);
        var shifted = HsvToRgb(h, s, v);
        return new ColorRgb(shifted.R, shifted.G, shifted.B);
    }

    private static double ColorDistance(EditableColor a, EditableColor b)
    {
        int dr = a.R - b.R;
        int dg = a.G - b.G;
        int db = a.B - b.B;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private static void RgbToHsv(MediaColor rgb, out double h, out double s, out double v)
    {
        double r = rgb.R / 255.0;
        double g = rgb.G / 255.0;
        double b = rgb.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        h = 0;
        if (delta > 0)
        {
            if (Math.Abs(max - r) < double.Epsilon)
                h = 60 * (((g - b) / delta) % 6);
            else if (Math.Abs(max - g) < double.Epsilon)
                h = 60 * (((b - r) / delta) + 2);
            else
                h = 60 * (((r - g) / delta) + 4);
        }

        if (h < 0)
            h += 360;

        s = max == 0 ? 0 : delta / max;
        v = max;
    }

    private static MediaColor HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        double m = v - c;

        (double r1, double g1, double b1) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        byte r = (byte)Math.Clamp((int)Math.Round((r1 + m) * 255), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round((g1 + m) * 255), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round((b1 + m) * 255), 0, 255);
        return MediaColor.FromRgb(r, g, b);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class PaletteOperationEntry
{
    public PaletteOperationEntry(string description, DateTime timestamp)
    {
        Description = description;
        Timestamp = timestamp;
    }

    public string Description { get; }
    public DateTime Timestamp { get; }
}
