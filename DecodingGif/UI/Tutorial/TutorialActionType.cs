namespace DecodingGif.UI.Tutorial;

public enum TutorialActionType
{
    EnsureFileLoadedHint = 0,
    SelectFirstFrame = 1,
    NavigateToGlobalColorTable = 2,
    NavigateToFirstGraphicControlExtension = 3,
    NavigateToFirstLocalColorTable = 4,
    SwitchPaletteToGlobalMode = 5,
    SwitchPaletteToLocalMode = 6,
    StartLzwVisualization = 7,
    AdvanceLzwStep = 8,
    CompleteLzwDecompression = 9,
    NavigateToFirstImageData = 10
}
