namespace DecodingGif.Core.Models;

public sealed class LZWDecompressionState
{
    public Dictionary<int, List<byte>> CodeTable { get; }
    public List<byte> OutputBuffer { get; }

    public int CurrentCode { get; set; }
    public int PreviousCode { get; set; }
    public int CodeSize { get; set; }
    public int ClearCode { get; set; }
    public int EndOfInfoCode { get; set; }
    public int NextAvailableCode { get; set; }
    public int BitPosition { get; set; }
    public int Step { get; set; }
    public int InitialDictionarySize { get; set; }
    public string StepDescription { get; set; }
    public LZWAction CurrentAction { get; set; }
    public bool IsComplete { get; set; }

    public LZWDecompressionState(
        int codeSize,
        int clearCode,
        int endOfInfoCode,
        int nextAvailableCode,
        int initialDictionarySize,
        int bitPosition = 0,
        int step = 0,
        string? stepDescription = null,
        LZWAction currentAction = LZWAction.Initialize)
    {
        if (codeSize <= 0 || codeSize > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(codeSize), "Code size must be in range 1..12.");
        }

        if (clearCode < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clearCode), "Clear code must be non-negative.");
        }

        if (endOfInfoCode <= clearCode)
        {
            throw new ArgumentOutOfRangeException(nameof(endOfInfoCode), "End-of-information code must be greater than clear code.");
        }

        if (nextAvailableCode <= endOfInfoCode)
        {
            throw new ArgumentOutOfRangeException(nameof(nextAvailableCode), "Next available code must be greater than end-of-information code.");
        }

        if (bitPosition < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitPosition), "Bit position must be non-negative.");
        }

        if (step < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "Step must be non-negative.");
        }

        if (initialDictionarySize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDictionarySize), "Initial dictionary size must be positive.");
        }

        CodeTable = new Dictionary<int, List<byte>>();
        OutputBuffer = new List<byte>();
        CurrentCode = -1;
        PreviousCode = -1;
        CodeSize = codeSize;
        ClearCode = clearCode;
        EndOfInfoCode = endOfInfoCode;
        NextAvailableCode = nextAvailableCode;
        InitialDictionarySize = initialDictionarySize;
        BitPosition = bitPosition;
        Step = step;
        StepDescription = stepDescription ?? string.Empty;
        CurrentAction = currentAction;
        IsComplete = false;
    }
}
