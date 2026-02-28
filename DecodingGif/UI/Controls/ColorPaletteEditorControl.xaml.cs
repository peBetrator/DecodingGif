using System;
using System.Windows;
using DecodingGif.UI.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace DecodingGif.UI.Controls;

public partial class ColorPaletteEditorControl : WpfUserControl
{
    public static readonly DependencyProperty PaletteProperty =
        DependencyProperty.Register(
            nameof(Palette),
            typeof(ColorPaletteViewModel),
            typeof(ColorPaletteEditorControl),
            new PropertyMetadata(null, OnPaletteChanged));

    public static readonly DependencyProperty EditModeProperty =
        DependencyProperty.Register(
            nameof(EditMode),
            typeof(PaletteEditMode),
            typeof(ColorPaletteEditorControl),
            new PropertyMetadata(PaletteEditMode.GlobalColorTable, OnEditModeChanged));

    public ColorPaletteViewModel? Palette
    {
        get => (ColorPaletteViewModel?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    public PaletteEditMode EditMode
    {
        get => (PaletteEditMode)GetValue(EditModeProperty);
        set => SetValue(EditModeProperty, value);
    }

    public event Action<ColorChangeEventArgs>? ColorChanged;
    public event Action<BatchOperationEventArgs>? BatchOperationRequested;

    public ColorPaletteEditorControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private static void OnPaletteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ColorPaletteEditorControl)d;
        if (e.OldValue is ColorPaletteViewModel oldVm)
        {
            oldVm.ColorChanged -= control.OnPaletteColorChanged;
            oldVm.BatchOperationRequested -= control.OnBatchOperationRequested;
        }

        if (e.NewValue is ColorPaletteViewModel newVm)
        {
            newVm.ColorChanged += control.OnPaletteColorChanged;
            newVm.BatchOperationRequested += control.OnBatchOperationRequested;
            control.EditMode = newVm.CurrentMode;
        }

        control.DataContext = e.NewValue;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Palette is not null)
            return;

        if (e.NewValue is ColorPaletteViewModel vm)
            Palette = vm;
    }

    private static void OnEditModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ColorPaletteEditorControl)d;
        if (control.Palette is null)
            return;

        var mode = (PaletteEditMode)e.NewValue;
        if (control.Palette.CurrentMode != mode)
            control.Palette.CurrentMode = mode;
    }

    private void OnPaletteColorChanged(object? sender, ColorChangeEventArgs e) => ColorChanged?.Invoke(e);
    private void OnBatchOperationRequested(object? sender, BatchOperationEventArgs e) => BatchOperationRequested?.Invoke(e);
}
