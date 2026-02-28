using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DecodingGif.UI.ViewModels;

public sealed class EditableFrameSettings : INotifyPropertyChanged
{
    private bool _isSelected;
    private int _delayMs;
    private int _disposalMethod;
    private bool _hasTransparency;
    private int _transparentColorIndex;
    private double _timelineBarWidth;

    public int FrameIndex { get; init; }
    public bool HasGraphicControlExtension { get; init; }

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

    public int DelayMs
    {
        get => _delayMs;
        set
        {
            int normalized = Math.Clamp(value, 0, 655350);
            if (_delayMs == normalized)
                return;
            _delayMs = normalized;
            OnPropertyChanged();
        }
    }

    public int DisposalMethod
    {
        get => _disposalMethod;
        set
        {
            int normalized = Math.Clamp(value, 0, 7);
            if (_disposalMethod == normalized)
                return;
            _disposalMethod = normalized;
            OnPropertyChanged();
        }
    }

    public bool HasTransparency
    {
        get => _hasTransparency;
        set
        {
            if (_hasTransparency == value)
                return;
            _hasTransparency = value;
            OnPropertyChanged();
        }
    }

    public int TransparentColorIndex
    {
        get => _transparentColorIndex;
        set
        {
            int normalized = Math.Clamp(value, 0, 255);
            if (_transparentColorIndex == normalized)
                return;
            _transparentColorIndex = normalized;
            OnPropertyChanged();
        }
    }

    public double TimelineBarWidth
    {
        get => _timelineBarWidth;
        set
        {
            if (Math.Abs(_timelineBarWidth - value) < double.Epsilon)
                return;
            _timelineBarWidth = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
