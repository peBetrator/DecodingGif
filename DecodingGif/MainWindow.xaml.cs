using System.Globalization;
using System.Windows.Controls;
using DecodingGif.UI.ViewModels;
using DecodingGif.Core.Models;

namespace DecodingGif;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void BlocksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (BlocksList.SelectedItem is not GifByteRange range)
            return;

        // Скроллим к началу диапазона
        int rowIndex = range.Start / 16;
        if (rowIndex < 0 || rowIndex >= vm.HexRows.Count)
            return;

        var rowItem = vm.HexRows[rowIndex];
        HexGrid.ScrollIntoView(rowItem);

        // Выбираем первый байт диапазона (для инфо панели)
        vm.SelectByte(range.Start);
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

        if (cell.Column?.Header is not string header)
            return;

        // Нас интересуют только колонки байтов "00".."0F"
        // Offset и ASCII игнорируем
        if (header.Length != 2)
            return;

        if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int index))
            return;

        if (index < 0 || index > 15)
            return;

        // Если в этой позиции байта нет (конец файла) – ничего не выбираем
        if (!row.TryGetByte(index, out _))
            return;

        int absoluteOffset = row.Offset + index;
        vm.SelectByte(absoluteOffset);
    }
}
