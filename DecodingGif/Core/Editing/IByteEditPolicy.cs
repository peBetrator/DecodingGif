namespace DecodingGif.Core.Editing;

public interface IByteEditPolicy
{
    bool CanEdit(int offset);
    void SetByte(int offset, byte value);
}
