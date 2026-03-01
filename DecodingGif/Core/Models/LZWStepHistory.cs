namespace DecodingGif.Core.Models;

public sealed class LZWStepHistory
{
    public List<LZWDecompressionState> Steps { get; } = new();

    public int CurrentStepIndex { get; private set; } = -1;

    public void SaveStep(LZWDecompressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (CurrentStepIndex < Steps.Count - 1)
        {
            Steps.RemoveRange(CurrentStepIndex + 1, Steps.Count - CurrentStepIndex - 1);
        }

        Steps.Add(DeepClone(state));
        CurrentStepIndex = Steps.Count - 1;
    }

    public LZWDecompressionState? GetPreviousStep()
    {
        if (CurrentStepIndex <= 0 || Steps.Count == 0)
        {
            return null;
        }

        CurrentStepIndex--;
        return DeepClone(Steps[CurrentStepIndex]);
    }

    public LZWDecompressionState? GetNextStep()
    {
        if (CurrentStepIndex < 0 || CurrentStepIndex >= Steps.Count - 1)
        {
            return null;
        }

        CurrentStepIndex++;
        return DeepClone(Steps[CurrentStepIndex]);
    }

    public void Reset()
    {
        Steps.Clear();
        CurrentStepIndex = -1;
    }

    private static LZWDecompressionState DeepClone(LZWDecompressionState state)
    {
        var clone = new LZWDecompressionState(
            state.CodeSize,
            state.ClearCode,
            state.EndOfInfoCode,
            state.NextAvailableCode,
            state.InitialDictionarySize,
            state.BitPosition,
            state.Step,
            state.StepDescription,
            state.CurrentAction)
        {
            CurrentCode = state.CurrentCode,
            PreviousCode = state.PreviousCode,
            IsComplete = state.IsComplete
        };

        clone.OutputBuffer.AddRange(state.OutputBuffer);

        foreach (var pair in state.CodeTable)
        {
            clone.CodeTable[pair.Key] = new List<byte>(pair.Value);
        }

        return clone;
    }
}
