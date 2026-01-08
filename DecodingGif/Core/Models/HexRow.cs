using System.Text;

namespace DecodingGif.Core.Models;

public sealed class HexRow
{
    public int Offset { get; }
    public string OffsetHex { get; }

    public byte[] Bytes { get; }

    public string B00 { get; }
    public string B01 { get; }
    public string B02 { get; }
    public string B03 { get; }
    public string B04 { get; }
    public string B05 { get; }
    public string B06 { get; }
    public string B07 { get; }
    public string B08 { get; }
    public string B09 { get; }
    public string B10 { get; }
    public string B11 { get; }
    public string B12 { get; }
    public string B13 { get; }
    public string B14 { get; }
    public string B15 { get; }

    public string Ascii { get; }

    public HexRow(int offset, ReadOnlySpan<byte> slice)
    {
        Offset = offset;
        OffsetHex = offset.ToString("X8");

        Bytes = slice.ToArray();

        B00 = HexOrEmpty(0);
        B01 = HexOrEmpty(1);
        B02 = HexOrEmpty(2);
        B03 = HexOrEmpty(3);
        B04 = HexOrEmpty(4);
        B05 = HexOrEmpty(5);
        B06 = HexOrEmpty(6);
        B07 = HexOrEmpty(7);
        B08 = HexOrEmpty(8);
        B09 = HexOrEmpty(9);
        B10 = HexOrEmpty(10);
        B11 = HexOrEmpty(11);
        B12 = HexOrEmpty(12);
        B13 = HexOrEmpty(13);
        B14 = HexOrEmpty(14);
        B15 = HexOrEmpty(15);

        Ascii = BuildAscii(Bytes);
    }

    private string HexOrEmpty(int i)
        => i < Bytes.Length ? Bytes[i].ToString("X2") : "";

    public bool TryGetByte(int columnIndex, out byte value)
    {
        if (columnIndex < 0 || columnIndex >= Bytes.Length)
        {
            value = 0;
            return false;
        }

        value = Bytes[columnIndex];
        return true;
    }

    private static string BuildAscii(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
            sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
        return sb.ToString();
    }
}
