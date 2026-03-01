using System.Windows.Media;
using WpfMouse = System.Windows.Input.Mouse;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace DecodingGif.UI.Controls;

public partial class TutorialOverlayControl : WpfUserControl
{
    private bool _isDragging;
    private WpfPoint _dragStart;
    private double _originX;
    private double _originY;
    private readonly TranslateTransform _dialogTransform = new();

    public TutorialOverlayControl()
    {
        InitializeComponent();
        TutorialDialog.RenderTransform = _dialogTransform;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStart = e.GetPosition(this);
        _originX = _dialogTransform.X;
        _originY = _dialogTransform.Y;
        WpfMouse.Capture((System.Windows.IInputElement)sender);
    }

    private void DragHandle_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isDragging)
            return;

        WpfPoint now = e.GetPosition(this);
        double deltaX = now.X - _dragStart.X;
        double deltaY = now.Y - _dragStart.Y;
        _dialogTransform.X = _originX + deltaX;
        _dialogTransform.Y = _originY + deltaY;
    }

    private void DragHandle_MouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        if (WpfMouse.Captured is not null)
            WpfMouse.Capture(null);
    }
}
