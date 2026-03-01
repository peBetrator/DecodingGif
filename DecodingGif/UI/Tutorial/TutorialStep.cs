using DecodingGif.Core.Models;

namespace DecodingGif.UI.Tutorial;

public sealed record TutorialStep(
    string Title,
    string Description,
    GifByteRange? HighlightRange,
    int? TabToShow,
    IReadOnlyList<TutorialActionType> Actions);
