namespace DecodingGif.Core.Models;

public enum LZWAction
{
    Initialize = 0,
    ReadCode = 1,
    ProcessClearCode = 2,
    ProcessExistingCode = 3,
    ProcessNewCode = 4,
    AddToCodeTable = 5,
    OutputData = 6,
    Complete = 7
}
