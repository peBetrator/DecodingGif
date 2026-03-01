namespace DecodingGif.UI.Tutorial;

public sealed record TutorialScenario(
    string Id,
    string Name,
    IReadOnlyList<TutorialStep> Steps);
