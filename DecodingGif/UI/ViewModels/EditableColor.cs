using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace DecodingGif.UI.ViewModels;

public sealed class EditableColor : INotifyPropertyChanged
{
    private byte _r;
    private byte _g;
    private byte _b;
    private bool _isUsed;
    private bool _isSelected;
    private bool _isModified;

    public int Index { get; init; }

    public byte R
    {
        get => _r;
        set
        {
            if (_r == value)
                return;
            _r = value;
            NotifyColorChanged();
        }
    }

    public byte G
    {
        get => _g;
        set
        {
            if (_g == value)
                return;
            _g = value;
            NotifyColorChanged();
        }
    }

    public byte B
    {
        get => _b;
        set
        {
            if (_b == value)
                return;
            _b = value;
            NotifyColorChanged();
        }
    }

    public MediaColor WpfColor => MediaColor.FromRgb(R, G, B);

    public MediaBrush Brush
    {
        get
        {
            var brush = new SolidColorBrush(WpfColor);
            brush.Freeze();
            return brush;
        }
    }

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public bool IsUsed
    {
        get => _isUsed;
        set
        {
            if (_isUsed == value)
                return;
            _isUsed = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified == value)
                return;
            _isModified = value;
            OnPropertyChanged();
        }
    }

    public ICommand? EditColorCommand { get; set; }
    public ICommand? CopyColorCommand { get; set; }
    public ICommand? PasteColorCommand { get; set; }
    public ICommand? ReplaceAllCommand { get; set; }
    public ICommand? AdjustBrightnessCommand { get; set; }
    public ICommand? AdjustSaturationCommand { get; set; }

    private void NotifyColorChanged()
    {
        OnPropertyChanged(nameof(R));
        OnPropertyChanged(nameof(G));
        OnPropertyChanged(nameof(B));
        OnPropertyChanged(nameof(WpfColor));
        OnPropertyChanged(nameof(Brush));
        OnPropertyChanged(nameof(Hex));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
