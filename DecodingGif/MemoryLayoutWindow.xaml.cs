using System.Windows;

namespace DecodingGif;

public partial class MemoryLayoutWindow : Window
{
    public MemoryLayoutWindow()
    {
        InitializeComponent();
        MemoryLayoutControlInWindow.NavigateToOffset += OnNavigateToOffset;
    }

    public event EventHandler<int>? NavigateToOffset;

    private void OnNavigateToOffset(object? sender, int offset)
    {
        NavigateToOffset?.Invoke(this, offset);
    }
}
