using System.Windows;
using DecodingGif.UI.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace DecodingGif.UI.Controls;

public partial class AnimationPropertiesEditorControl : WpfUserControl
{
    public static readonly DependencyProperty EditorProperty =
        DependencyProperty.Register(
            nameof(Editor),
            typeof(AnimationPropertiesEditorViewModel),
            typeof(AnimationPropertiesEditorControl),
            new PropertyMetadata(null, OnEditorChanged));

    public AnimationPropertiesEditorViewModel? Editor
    {
        get => (AnimationPropertiesEditorViewModel?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public AnimationPropertiesEditorControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private static void OnEditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AnimationPropertiesEditorControl)d;
        control.DataContext = e.NewValue;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Editor is not null)
            return;

        if (e.NewValue is AnimationPropertiesEditorViewModel vm)
            Editor = vm;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EditableFrameSettings frame)
            return;

        Editor?.MoveFrameUpAt(frame.FrameIndex);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EditableFrameSettings frame)
            return;

        Editor?.MoveFrameDownAt(frame.FrameIndex);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EditableFrameSettings frame)
            return;

        Editor?.DeleteFrameAt(frame.FrameIndex);
    }
}
