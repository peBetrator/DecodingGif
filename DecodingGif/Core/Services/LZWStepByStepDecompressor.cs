using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class LZWStepByStepDecompressor
{
    private const int MaxGifCode = 4095;
    private byte[] _compressedData = [];
    private int _lzwMinCodeSize;

    public LZWDecompressionState? CurrentState { get; private set; }
    public LZWStepHistory StepHistory { get; } = new();
    public string? LastErrorMessage { get; private set; }
    public string? LastWarningMessage { get; private set; }

    public LZWDecompressionState Initialize(byte[] compressedData, int lzwMinCodeSize)
    {
        ArgumentNullException.ThrowIfNull(compressedData);

        if (lzwMinCodeSize is < 2 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(lzwMinCodeSize), "LZW minimum code size must be in range 2..8.");
        }

        _compressedData = compressedData;
        _lzwMinCodeSize = lzwMinCodeSize;
        LastErrorMessage = null;
        LastWarningMessage = null;

        int clearCode = 1 << lzwMinCodeSize;
        int endOfInfoCode = clearCode + 1;
        int nextAvailableCode = endOfInfoCode + 1;
        int baseSymbolCount = clearCode;

        var state = new LZWDecompressionState(
            codeSize: lzwMinCodeSize + 1,
            clearCode: clearCode,
            endOfInfoCode: endOfInfoCode,
            nextAvailableCode: nextAvailableCode,
            initialDictionarySize: baseSymbolCount,
            bitPosition: 0,
            step: 0,
            stepDescription: "LZW decompressor initialized.",
            currentAction: LZWAction.Initialize);

        InitializeBaseCodeTable(state.CodeTable, baseSymbolCount);

        CurrentState = state;
        StepHistory.Reset();
        StepHistory.SaveStep(state);
        ValidateData();

        return state;
    }

    public void ValidateData()
    {
        if (_compressedData.Length == 0)
        {
            LastErrorMessage = "Compressed data is empty.";
            throw new InvalidOperationException("Compressed data is empty. Call Initialize with non-empty data.");
        }

        if (_lzwMinCodeSize is < 2 or > 8)
        {
            LastErrorMessage = $"Invalid LZW minimum code size: {_lzwMinCodeSize}.";
            throw new InvalidOperationException("Invalid LZW minimum code size. Call Initialize with a valid value in range 2..8.");
        }

        if (CurrentState is null)
        {
            LastErrorMessage = "Decompressor is not initialized.";
            throw new InvalidOperationException("Decompressor is not initialized.");
        }

        int availableBits = GetTotalBits();
        if (availableBits < CurrentState.CodeSize)
        {
            LastErrorMessage = "Compressed data is too short for first code.";
            throw new InvalidOperationException("Compressed data is too short for the first LZW code.");
        }
    }

    public bool CanStepForward()
    {
        if (CurrentState is null)
        {
            return false;
        }

        if (StepHistory.CurrentStepIndex >= 0 && StepHistory.CurrentStepIndex < StepHistory.Steps.Count - 1)
        {
            return true;
        }

        if (IsDecompressionComplete())
        {
            return false;
        }

        if (CurrentState.CurrentAction is not LZWAction.ReadCode)
        {
            return true;
        }

        return HasEnoughBits(CurrentState.BitPosition, CurrentState.CodeSize);
    }

    public bool CanStepBackward() =>
        StepHistory.CurrentStepIndex > 0;

    public bool IsDecompressionComplete()
    {
        if (CurrentState is null)
        {
            return false;
        }

        if (CurrentState.IsComplete || CurrentState.CurrentAction == LZWAction.Complete)
        {
            return true;
        }

        if (CurrentState.CurrentAction == LZWAction.ReadCode && !HasEnoughBits(CurrentState.BitPosition, CurrentState.CodeSize))
        {
            return true;
        }

        return false;
    }

    public LZWDecompressionStatistics GetDecompressionStatistics()
    {
        if (CurrentState is null)
        {
            throw new InvalidOperationException("Decompressor is not initialized.");
        }

        int totalBits = GetTotalBits();
        int processedBits = Math.Min(CurrentState.BitPosition, totalBits);
        int outputBytes = CurrentState.OutputBuffer.Count;
        int processedBytes = (processedBits + 7) / 8;
        double compressionRatio = outputBytes == 0 ? 0d : _compressedData.Length / (double)outputBytes;

        return new LZWDecompressionStatistics
        {
            TotalInputBytes = _compressedData.Length,
            TotalInputBits = totalBits,
            ProcessedBits = processedBits,
            ProcessedBytes = processedBytes,
            OutputBytes = outputBytes,
            DictionarySize = CurrentState.CodeTable.Count,
            CurrentCodeSize = CurrentState.CodeSize,
            StepCount = CurrentState.Step,
            IsComplete = IsDecompressionComplete(),
            ProgressPercent = CalculateProgressPercent(processedBits, totalBits),
            CompressionRatio = compressionRatio
        };
    }

    public LZWDecompressionState ExecuteNextStep(LZWDecompressionState currentState, byte[] compressedData)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(compressedData);

        _compressedData = compressedData;
        CurrentState = currentState;

        if (_lzwMinCodeSize == 0)
        {
            _lzwMinCodeSize = InferMinCodeSizeFromState(currentState);
        }

        currentState.Step++;

        if (currentState.IsComplete)
        {
            currentState.CurrentAction = LZWAction.Complete;
            currentState.StepDescription = "Decompression already completed.";
            StepHistory.SaveStep(currentState);
            return currentState;
        }

        try
        {
            switch (currentState.CurrentAction)
            {
                case LZWAction.Initialize:
                    ResetToInitialDictionary(currentState);
                    currentState.CurrentAction = LZWAction.ReadCode;
                    currentState.StepDescription = "Initialized code table and decoder parameters.";
                    break;

                case LZWAction.ReadCode:
                    HandleReadCode(currentState);
                    break;

                case LZWAction.ProcessClearCode:
                    ResetToInitialDictionary(currentState);
                    currentState.CurrentAction = LZWAction.ReadCode;
                    currentState.StepDescription = "Processed CLEAR code: dictionary reset.";
                    break;

                case LZWAction.ProcessExistingCode:
                    HandleExistingCode(currentState);
                    break;

                case LZWAction.ProcessNewCode:
                    HandleNewCode(currentState);
                    break;

                case LZWAction.AddToCodeTable:
                case LZWAction.OutputData:
                    currentState.CurrentAction = LZWAction.ReadCode;
                    currentState.StepDescription = "Intermediate action completed. Reading next code.";
                    break;

                case LZWAction.Complete:
                    currentState.IsComplete = true;
                    currentState.StepDescription = "Reached end of decompression.";
                    break;

                default:
                    throw new InvalidOperationException($"Unknown LZW action: {currentState.CurrentAction}.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            LastErrorMessage = ex.Message;
            currentState.IsComplete = true;
            currentState.CurrentAction = LZWAction.Complete;
            currentState.StepDescription = $"Decompression stopped due to invalid data: {ex.Message}";
        }

        CurrentState = currentState;
        StepHistory.SaveStep(currentState);
        return currentState;
    }

    private void HandleReadCode(LZWDecompressionState state)
    {
        if (!HasEnoughBits(state.BitPosition, state.CodeSize))
        {
            int remainingBits = Math.Max(0, GetTotalBits() - state.BitPosition);
            if (remainingBits > 0 && remainingBits < state.CodeSize)
            {
                LastWarningMessage = $"Premature end of data: {remainingBits} bit(s) remaining, {state.CodeSize} required.";
                state.StepDescription = $"Premature end of data: {remainingBits} bit(s) left, need {state.CodeSize}.";
            }
            else
            {
                state.StepDescription = "No more complete codes available in the bit stream.";
            }

            state.IsComplete = true;
            state.CurrentAction = LZWAction.Complete;
            return;
        }

        int code = ExtractBitsFromData(state.BitPosition, state.CodeSize);
        state.CurrentCode = code;
        state.BitPosition += state.CodeSize;

        if (code == state.ClearCode)
        {
            state.CurrentAction = LZWAction.ProcessClearCode;
            state.StepDescription = $"Read CLEAR code ({code}).";
            return;
        }

        if (code == state.EndOfInfoCode)
        {
            state.IsComplete = true;
            state.CurrentAction = LZWAction.Complete;
            state.StepDescription = $"Read End-Of-Information code ({code}).";
            return;
        }

        if (state.CodeTable.ContainsKey(code))
        {
            state.CurrentAction = LZWAction.ProcessExistingCode;
            state.StepDescription = $"Read existing dictionary code {code}.";
            return;
        }

        if (code == state.NextAvailableCode)
        {
            state.CurrentAction = LZWAction.ProcessNewCode;
            state.StepDescription = $"Read new-code case {code} (KwKwK pattern).";
            return;
        }

        throw new InvalidOperationException($"Invalid LZW code {code} at bit position {state.BitPosition - state.CodeSize}.");
    }

    private void HandleExistingCode(LZWDecompressionState state)
    {
        if (!state.CodeTable.TryGetValue(state.CurrentCode, out var sequence))
        {
            throw new InvalidOperationException($"Current code {state.CurrentCode} not found in dictionary.");
        }

        state.OutputBuffer.AddRange(sequence);

        if (state.PreviousCode >= 0 && state.CodeTable.TryGetValue(state.PreviousCode, out var previousSequence))
        {
            var newEntry = new List<byte>(previousSequence.Count + 1);
            newEntry.AddRange(previousSequence);
            newEntry.Add(sequence[0]);
            AddCodeToTable(state, newEntry);
        }

        state.PreviousCode = state.CurrentCode;
        state.CurrentAction = LZWAction.ReadCode;
        state.StepDescription = $"Output {sequence.Count} byte(s) for code {state.CurrentCode}.";
    }

    private void HandleNewCode(LZWDecompressionState state)
    {
        if (state.PreviousCode < 0 || !state.CodeTable.TryGetValue(state.PreviousCode, out var previousSequence) || previousSequence.Count == 0)
        {
            throw new InvalidOperationException("Special new-code case requires a valid previous sequence.");
        }

        var generatedSequence = new List<byte>(previousSequence.Count + 1);
        generatedSequence.AddRange(previousSequence);
        generatedSequence.Add(previousSequence[0]);

        state.OutputBuffer.AddRange(generatedSequence);
        AddCodeToTable(state, generatedSequence);

        state.PreviousCode = state.CurrentCode;
        state.CurrentAction = LZWAction.ReadCode;
        state.StepDescription = $"Processed new-code case {state.CurrentCode}: output/generated {generatedSequence.Count} byte(s).";
    }

    private void AddCodeToTable(LZWDecompressionState state, List<byte> entry)
    {
        if (state.NextAvailableCode > MaxGifCode)
        {
            return;
        }

        state.CodeTable[state.NextAvailableCode] = entry;
        state.NextAvailableCode++;

        int nextCodeSizeThreshold = 1 << state.CodeSize;
        if (state.NextAvailableCode == nextCodeSizeThreshold && state.CodeSize < 12)
        {
            state.CodeSize++;
        }
    }

    private void ResetToInitialDictionary(LZWDecompressionState state)
    {
        InitializeBaseCodeTable(state.CodeTable, state.InitialDictionarySize);

        state.CodeSize = _lzwMinCodeSize + 1;
        state.NextAvailableCode = state.EndOfInfoCode + 1;
        state.PreviousCode = -1;
    }

    private static int InferMinCodeSizeFromState(LZWDecompressionState state)
    {
        if (state.ClearCode <= 0 || (state.ClearCode & (state.ClearCode - 1)) != 0)
        {
            throw new InvalidOperationException("Cannot infer LZW minimum code size from state: clear code must be a power of two.");
        }

        int size = 0;
        int code = state.ClearCode;
        while (code > 1)
        {
            code >>= 1;
            size++;
        }

        if (size is < 2 or > 8)
        {
            throw new InvalidOperationException($"Inferred LZW minimum code size {size} is out of supported range 2..8.");
        }

        return size;
    }

    private int ExtractBitsFromData(int startBit, int codeSize)
    {
        if (startBit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startBit), "Start bit must be non-negative.");
        }

        if (codeSize <= 0 || codeSize > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(codeSize), "Code size must be in range 1..12.");
        }

        if (!HasEnoughBits(startBit, codeSize))
        {
            throw new ArgumentOutOfRangeException(nameof(startBit), "Requested bit range exceeds compressed data length.");
        }

        int value = 0;
        for (int i = 0; i < codeSize; i++)
        {
            int bitIndex = startBit + i;
            value |= ReadBit(bitIndex) << i;
        }

        return value;
    }

    private static void InitializeBaseCodeTable(Dictionary<int, List<byte>> codeTable, int symbolCount)
    {
        codeTable.Clear();

        int safeCount = Math.Clamp(symbolCount, 1, 256);
        for (int code = 0; code < safeCount; code++)
        {
            codeTable[code] = [unchecked((byte)code)];
        }
    }

    private int GetTotalBits() => _compressedData.Length * 8;

    private bool HasEnoughBits(int startBit, int codeSize) =>
        startBit >= 0 && codeSize > 0 && startBit + codeSize <= GetTotalBits();

    private int ReadBit(int bitIndex)
    {
        int byteIndex = bitIndex / 8;
        int bitOffset = bitIndex % 8;
        return (_compressedData[byteIndex] >> bitOffset) & 1;
    }

    private static double CalculateProgressPercent(int processedBits, int totalBits)
    {
        if (totalBits <= 0)
        {
            return 0d;
        }

        return (processedBits * 100d) / totalBits;
    }
}
