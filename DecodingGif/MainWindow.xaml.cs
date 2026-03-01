using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DecodingGif.Core.Models;
using DecodingGif.UI.Tutorial;
using DecodingGif.UI.ViewModels;

namespace DecodingGif;

public partial class MainWindow
{
    private readonly DispatcherTimer _hoverTimer;
    private MainViewModel? _hoverVm;
    private int? _pendingHoverOffset;
    private DateTime _lastHoverInputUtc;
    private GraphWindow? _graphWindow;
    private MemoryLayoutWindow? _memoryLayoutWindow;
    private LZWWindow? _lzwWindow;
    private PaletteWindow? _paletteWindow;
    private AnimationPropertiesWindow? _animationPropertiesWindow;
    private MainViewModel? _subscribedVm;

    public MainWindow()
    {
        InitializeComponent();
        FileOverview.OffsetClicked += FileOverview_OffsetClicked;
        StructureGraphControl.NavigateToByteRange += StructureGraphControl_NavigateToByteRange;
        MemoryLayoutControl.NavigateToOffset += MemoryLayoutControl_NavigateToOffset;
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _hoverTimer.Tick += HoverTimer_Tick;
        DataContextChanged += MainWindow_DataContextChanged;
        SubscribeToTutorialWindowRequests(DataContext as MainViewModel);
    }

    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        SubscribeToTutorialWindowRequests(e.NewValue as MainViewModel);

    private void SubscribeToTutorialWindowRequests(MainViewModel? vm)
    {
        if (_subscribedVm is not null)
            _subscribedVm.TutorialDetachedWindowRequested -= OnTutorialDetachedWindowRequested;

        _subscribedVm = vm;

        if (_subscribedVm is not null)
            _subscribedVm.TutorialDetachedWindowRequested += OnTutorialDetachedWindowRequested;
    }

    private void OnTutorialDetachedWindowRequested(TutorialDetachedWindowTarget target)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (target == TutorialDetachedWindowTarget.Lzw)
            EnsureLzwWindowOpen(vm);
    }

    private void StructureTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (e.NewValue is not GifStructureNode node)
            return;

        if (node.FrameIndex.HasValue)
            vm.SetSelectedFrameIndex(node.FrameIndex.Value);

        if (node.Range?.Kind == GifBlockKind.LocalColorTable)
            vm.SetSelectedLctRange(node.Range);
        else
            vm.SetSelectedLctRange(null);

        if (node.Range is null)
            return;

        int start = node.Range.Start;
        vm.SetHoveredByteOffset(start);

        int rowIndex = start / 16;
        if (rowIndex >= 0 && rowIndex < vm.HexRows.Count)
            HexGrid.ScrollIntoView(vm.HexRows[rowIndex]);

        vm.SelectByte(start);
    }

    private void HexGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (HexGrid.SelectedCells.Count == 0)
            return;

        var cell = HexGrid.SelectedCells[0];
        if (cell.Item is not HexRow row)
            return;

        if (cell.Column?.Header is not string header || header.Length != 2)
            return;

        if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int index))
            return;

        if (index is < 0 or > 15)
            return;

        if (!row.TryGetByte(index, out _))
            return;

        int absoluteOffset = row.Offset + index;
        vm.SelectByte(absoluteOffset);
    }

    private void HexGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (TryGetOffsetUnderMouse(e.OriginalSource as DependencyObject, out int offset))
            QueueHoverOffset(vm, offset);
        else
            QueueHoverOffset(vm, null);
    }

    private void HexGrid_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        QueueHoverOffset(vm, null);
    }

    private void FileOverview_OffsetClicked(object? sender, int offset)
    {
        if (DataContext is not MainViewModel vm)
            return;

        int rowIndex = offset / 16;
        if (rowIndex >= 0 && rowIndex < vm.HexRows.Count)
            HexGrid.ScrollIntoView(vm.HexRows[rowIndex]);

        vm.SelectByte(offset);
        QueueHoverOffset(vm, offset);
    }

    private void QueueHoverOffset(MainViewModel vm, int? offset)
    {
        _hoverVm = vm;
        _pendingHoverOffset = offset;
        _lastHoverInputUtc = DateTime.UtcNow;
        if (!_hoverTimer.IsEnabled)
            _hoverTimer.Start();
    }

    private void HoverTimer_Tick(object? sender, EventArgs e)
    {
        if (_hoverVm is null)
        {
            _hoverTimer.Stop();
            return;
        }

        _hoverVm.SetHoveredByteOffset(_pendingHoverOffset);
        if ((DateTime.UtcNow - _lastHoverInputUtc).TotalMilliseconds > 120
            && _hoverVm.HoveredByteOffset == _pendingHoverOffset)
        {
            _hoverTimer.Stop();
        }
    }

    private static bool TryGetOffsetUnderMouse(DependencyObject? source, out int offset)
    {
        offset = -1;
        if (source is null)
            return false;

        var cell = FindVisualParent<DataGridCell>(source);
        if (cell?.DataContext is not HexRow row)
            return false;

        if (cell.Column?.Header is not string header || header.Length != 2)
            return false;

        if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int index))
            return false;

        if (index is < 0 or > 15)
            return false;

        if (!row.TryGetByte(index, out _))
            return false;

        offset = row.Offset + index;
        return true;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parent = child;
        while (parent is not null)
        {
            if (parent is T typed)
                return typed;
            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private void StructureGraphControl_NavigateToByteRange(object? sender, GifByteRange range)
    {
        if (DataContext is not MainViewModel vm)
            return;

        int rowIndex = range.Start / 16;
        if (rowIndex >= 0 && rowIndex < vm.HexRows.Count)
            HexGrid.ScrollIntoView(vm.HexRows[rowIndex]);

        vm.NavigateToByteRange(range);
    }

    private void GraphTab_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (_graphWindow is null)
        {
            _graphWindow = new GraphWindow
            {
                Owner = this,
                DataContext = vm
            };
            _graphWindow.NavigateToByteRange += StructureGraphControl_NavigateToByteRange;
            _graphWindow.Closed += (_, _) =>
            {
                _graphWindow.NavigateToByteRange -= StructureGraphControl_NavigateToByteRange;
                _graphWindow = null;
            };
            _graphWindow.Show();
        }
        else
        {
            if (_graphWindow.WindowState == WindowState.Minimized)
                _graphWindow.WindowState = WindowState.Normal;
            _graphWindow.Activate();
        }

        e.Handled = true;
    }

    private void MemoryLayoutControl_NavigateToOffset(object? sender, int offset)
    {
        if (DataContext is not MainViewModel vm)
            return;

        int rowIndex = offset / 16;
        if (rowIndex >= 0 && rowIndex < vm.HexRows.Count)
            HexGrid.ScrollIntoView(vm.HexRows[rowIndex]);

        vm.SetHoveredByteOffset(offset);
        vm.SelectByte(offset);
    }

    private void MemoryLayoutTab_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (_memoryLayoutWindow is null)
        {
            _memoryLayoutWindow = new MemoryLayoutWindow
            {
                Owner = this,
                DataContext = vm
            };
            _memoryLayoutWindow.NavigateToOffset += MemoryLayoutControl_NavigateToOffset;
            _memoryLayoutWindow.Closed += (_, _) =>
            {
                _memoryLayoutWindow.NavigateToOffset -= MemoryLayoutControl_NavigateToOffset;
                _memoryLayoutWindow = null;
            };
            _memoryLayoutWindow.Show();
        }
        else
        {
            if (_memoryLayoutWindow.WindowState == WindowState.Minimized)
                _memoryLayoutWindow.WindowState = WindowState.Normal;
            _memoryLayoutWindow.Activate();
        }

        e.Handled = true;
    }

    private void LzwTab_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        EnsureLzwWindowOpen(vm);
        e.Handled = true;
    }

    private void EnsureLzwWindowOpen(MainViewModel vm)
    {
        if (_lzwWindow is null)
        {
            _lzwWindow = new LZWWindow
            {
                Owner = this,
                DataContext = vm
            };
            _lzwWindow.Closed += (_, _) => _lzwWindow = null;
            _lzwWindow.Show();
        }
        else
        {
            if (_lzwWindow.WindowState == WindowState.Minimized)
                _lzwWindow.WindowState = WindowState.Normal;
            _lzwWindow.Activate();
        }
    }

    private void PaletteTab_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (_paletteWindow is null)
        {
            _paletteWindow = new PaletteWindow
            {
                Owner = this,
                DataContext = vm
            };
            _paletteWindow.Closed += (_, _) => _paletteWindow = null;
            _paletteWindow.Show();
        }
        else
        {
            if (_paletteWindow.WindowState == WindowState.Minimized)
                _paletteWindow.WindowState = WindowState.Normal;
            _paletteWindow.Activate();
        }

        e.Handled = true;
    }

    private void AnimationPropertiesTab_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (_animationPropertiesWindow is null)
        {
            _animationPropertiesWindow = new AnimationPropertiesWindow
            {
                Owner = this,
                DataContext = vm
            };
            _animationPropertiesWindow.Closed += (_, _) => _animationPropertiesWindow = null;
            _animationPropertiesWindow.Show();
        }
        else
        {
            if (_animationPropertiesWindow.WindowState == WindowState.Minimized)
                _animationPropertiesWindow.WindowState = WindowState.Normal;
            _animationPropertiesWindow.Activate();
        }

        e.Handled = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (vm.TryHandleCloseRequest())
            return;

        e.Cancel = true;
    }

}
