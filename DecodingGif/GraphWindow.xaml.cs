using System.Windows;
using DecodingGif.Core.Models;

namespace DecodingGif;

public partial class GraphWindow : Window
{
    public GraphWindow()
    {
        InitializeComponent();
        StructureGraphControlInWindow.NavigateToByteRange += OnNavigateToByteRange;
    }

    public event EventHandler<GifByteRange>? NavigateToByteRange;

    private void OnNavigateToByteRange(object? sender, GifByteRange range)
    {
        NavigateToByteRange?.Invoke(this, range);
    }
}
