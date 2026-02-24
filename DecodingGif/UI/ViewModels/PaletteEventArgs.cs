using System;

namespace DecodingGif.UI.ViewModels;

public sealed class ColorChangeEventArgs : EventArgs
{
    public ColorChangeEventArgs(int index, byte oldR, byte oldG, byte oldB, byte newR, byte newG, byte newB, string description)
    {
        Index = index;
        OldR = oldR;
        OldG = oldG;
        OldB = oldB;
        NewR = newR;
        NewG = newG;
        NewB = newB;
        Description = description;
    }

    public int Index { get; }
    public byte OldR { get; }
    public byte OldG { get; }
    public byte OldB { get; }
    public byte NewR { get; }
    public byte NewG { get; }
    public byte NewB { get; }
    public string Description { get; }
}

public sealed class BatchOperationEventArgs : EventArgs
{
    public BatchOperationEventArgs(string operationName, int affectedCount)
    {
        OperationName = operationName;
        AffectedCount = affectedCount;
    }

    public string OperationName { get; }
    public int AffectedCount { get; }
}
