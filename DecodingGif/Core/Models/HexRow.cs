using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using DecodingGif.Core.Editing;

namespace DecodingGif.Core.Models;

public sealed class HexRow : INotifyPropertyChanged
{
    private readonly byte[] _bytes;
    private readonly IByteEditPolicy _policy;
    private string _ascii = string.Empty;

    public int Offset { get; }
    public string OffsetHex { get; }

    public string B00 { get => GetHex(0); set => SetHex(0, value, nameof(B00)); }
    public string B01 { get => GetHex(1); set => SetHex(1, value, nameof(B01)); }
    public string B02 { get => GetHex(2); set => SetHex(2, value, nameof(B02)); }
    public string B03 { get => GetHex(3); set => SetHex(3, value, nameof(B03)); }
    public string B04 { get => GetHex(4); set => SetHex(4, value, nameof(B04)); }
    public string B05 { get => GetHex(5); set => SetHex(5, value, nameof(B05)); }
    public string B06 { get => GetHex(6); set => SetHex(6, value, nameof(B06)); }
    public string B07 { get => GetHex(7); set => SetHex(7, value, nameof(B07)); }
    public string B08 { get => GetHex(8); set => SetHex(8, value, nameof(B08)); }
    public string B09 { get => GetHex(9); set => SetHex(9, value, nameof(B09)); }
    public string B10 { get => GetHex(10); set => SetHex(10, value, nameof(B10)); }
    public string B11 { get => GetHex(11); set => SetHex(11, value, nameof(B11)); }
    public string B12 { get => GetHex(12); set => SetHex(12, value, nameof(B12)); }
    public string B13 { get => GetHex(13); set => SetHex(13, value, nameof(B13)); }
    public string B14 { get => GetHex(14); set => SetHex(14, value, nameof(B14)); }
    public string B15 { get => GetHex(15); set => SetHex(15, value, nameof(B15)); }

    public string Ascii
    {
        get => _ascii;
        private set
        {
            if (_ascii == value)
                return;
            _ascii = value;
            OnPropertyChanged();
        }
    }

    public HexRow(int offset, byte[] bytes, IByteEditPolicy policy)
    {
        Offset = offset;
        OffsetHex = offset.ToString("X8");
        _bytes = bytes;
        _policy = policy;
        Ascii = BuildAsciiRow();
    }

    public bool TryGetByte(int index, out byte value)
    {
        value = 0;
        if (index < 0 || index >= 16)
            return false;

        if (!TryGetAbsoluteOffset(index, out int absoluteOffset))
            return false;

        value = _bytes[absoluteOffset];
        return true;
    }

    private string GetHex(int index)
    {
        if (!TryGetByte(index, out byte value))
            return string.Empty;

        return value.ToString("X2");
    }

    private void SetHex(int index, string? input, string propertyName)
    {
        if (!TryGetAbsoluteOffset(index, out int absoluteOffset))
        {
            OnPropertyChanged(propertyName);
            return;
        }

        if (!TryParseHexByte(input, out byte value))
        {
            OnPropertyChanged(propertyName);
            return;
        }

        if (!_policy.CanEdit(absoluteOffset))
        {
            OnPropertyChanged(propertyName);
            return;
        }

        _policy.SetByte(absoluteOffset, value);
        OnPropertyChanged(propertyName);
        Ascii = BuildAsciiRow();
    }

    private bool TryGetAbsoluteOffset(int index, out int absoluteOffset)
    {
        absoluteOffset = Offset + index;
        if (index < 0 || index >= 16)
            return false;

        if (absoluteOffset < 0 || absoluteOffset >= _bytes.Length)
            return false;

        return true;
    }

    private static bool TryParseHexByte(string? input, out byte value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string trimmed = input.Trim();
        if (trimmed.Length == 1)
            trimmed = "0" + trimmed;

        if (trimmed.Length != 2)
            return false;

        return byte.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private string BuildAsciiRow()
    {
        int length = Math.Min(16, _bytes.Length - Offset);
        if (length <= 0)
            return string.Empty;

        return BuildAscii(new ReadOnlySpan<byte>(_bytes, Offset, length));
    }

    private static string BuildAscii(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
        }
        return sb.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
