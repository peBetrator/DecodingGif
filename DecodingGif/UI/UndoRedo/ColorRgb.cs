namespace DecodingGif.UI.UndoRedo;

public readonly record struct ColorRgb(byte R, byte G, byte B)
{
    public override string ToString() => $"({R},{G},{B})";
}
