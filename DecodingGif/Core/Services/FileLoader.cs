using System.IO;

namespace DecodingGif.Core.Services;

public sealed class FileLoader
{
    public byte[] LoadAllBytes(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is empty.", nameof(filePath));

        return File.ReadAllBytes(filePath);
    }
}