namespace DecodingGif.Core.Models;

public enum DisposalMethod
{
    None = 0,
    DoNotDispose = 1,
    RestoreBackground = 2,
    RestorePrevious = 3
}

public sealed class GifFrameInfo
{
    public int Index { get; init; }
    public int DelayMs { get; init; }
    public int CumulativeTimeMs { get; init; }
    public DisposalMethod Disposal { get; init; }
    public bool HasTransparency { get; init; }
    public byte TransparentIndex { get; init; }
    public bool UserInputRequired { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public bool HasLocalColorTable { get; init; }
    public int LocalColorTableSize { get; init; }

    public string DelayText => DelayMs > 0 ? $"{DelayMs}ms" : "0ms (fast)";

    public string DisposalText => Disposal switch
    {
        DisposalMethod.DoNotDispose => "Keep",
        DisposalMethod.RestoreBackground => "Clear",
        DisposalMethod.RestorePrevious => "Restore",
        _ => "None"
    };

    public string FrameDetails => $"Frame {Index + 1}: {Width}x{Height} at ({Left},{Top})";
    public string TimingInfo => $"{DelayText} | {DisposalText} | Transparency: {(HasTransparency ? "Yes" : "No")}";
}
