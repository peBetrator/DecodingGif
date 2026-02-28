namespace DecodingGif.Core.Models;

public sealed record BatchColorOperationProgress(
    string Operation,
    int Current,
    int Total,
    string Message);
