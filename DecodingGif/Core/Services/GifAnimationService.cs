using System.Collections.ObjectModel;
using System.Text;
using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class GifAnimationService
{
    public ObservableCollection<GifFrameInfo> BuildFrameTimeline(GifFile file, IEnumerable<GifByteRange> blocks)
    {
        var timeline = new ObservableCollection<GifFrameInfo>();
        var orderedBlocks = blocks.OrderBy(b => b.Start);
        GifByteRange? pendingGce = null;
        int frameIndex = 0;
        int cumulativeTime = 0;

        foreach (var block in orderedBlocks)
        {
            if (block.Kind == GifBlockKind.GraphicControlExtension)
            {
                pendingGce = block;
                continue;
            }

            if (block.Kind != GifBlockKind.ImageDescriptor)
                continue;

            var frameInfo = ExtractFrameInfo(file, frameIndex, block, pendingGce, cumulativeTime);
            timeline.Add(frameInfo);

            cumulativeTime += frameInfo.DelayMs;
            frameIndex++;
            pendingGce = null;
        }

        return timeline;
    }

    public int CalculateTotalDuration(ObservableCollection<GifFrameInfo> frames) =>
        frames.Sum(f => f.DelayMs);

    public bool IsInfiniteLoop(GifFile file, IEnumerable<GifByteRange> blocks)
    {
        foreach (var appExt in blocks.Where(b => b.Kind == GifBlockKind.ApplicationExtension))
        {
            if (TryReadNetscapeLoopCount(file.Bytes, appExt, out ushort loopCount))
                return loopCount == 0;
        }

        return true;
    }

    private static GifFrameInfo ExtractFrameInfo(
        GifFile file,
        int frameIndex,
        GifByteRange imageBlock,
        GifByteRange? gceBlock,
        int cumulativeTime)
    {
        var bytes = file.Bytes;
        int idStart = imageBlock.Start;

        ushort left = ReadUInt16(bytes, idStart + 1);
        ushort top = ReadUInt16(bytes, idStart + 3);
        ushort width = ReadUInt16(bytes, idStart + 5);
        ushort height = ReadUInt16(bytes, idStart + 7);
        byte packed = bytes[idStart + 9];

        bool hasLct = (packed & 0x80) != 0;
        int lctSize = hasLct ? 1 << ((packed & 0x07) + 1) : 0;

        int delayMs = 100;
        DisposalMethod disposal = DisposalMethod.None;
        bool hasTransparency = false;
        byte transparentIndex = 0;
        bool userInput = false;

        if (gceBlock is not null && gceBlock.Start >= 0 && gceBlock.Start + 7 < bytes.Length)
        {
            int gceStart = gceBlock.Start;
            byte gcePacked = bytes[gceStart + 3];
            ushort delay = ReadUInt16(bytes, gceStart + 4);
            transparentIndex = bytes[gceStart + 6];

            int disposalRaw = (gcePacked >> 2) & 0x07;
            disposal = disposalRaw switch
            {
                1 => DisposalMethod.DoNotDispose,
                2 => DisposalMethod.RestoreBackground,
                3 => DisposalMethod.RestorePrevious,
                _ => DisposalMethod.None
            };

            userInput = (gcePacked & 0x02) != 0;
            hasTransparency = (gcePacked & 0x01) != 0;
            delayMs = Math.Max(delay * 10, 10);
        }

        return new GifFrameInfo
        {
            Index = frameIndex,
            DelayMs = delayMs,
            CumulativeTimeMs = cumulativeTime,
            Disposal = disposal,
            HasTransparency = hasTransparency,
            TransparentIndex = transparentIndex,
            UserInputRequired = userInput,
            Width = width,
            Height = height,
            Left = left,
            Top = top,
            HasLocalColorTable = hasLct,
            LocalColorTableSize = lctSize
        };
    }

    private static bool TryReadNetscapeLoopCount(byte[] bytes, GifByteRange appExt, out ushort loopCount)
    {
        loopCount = 0;
        int start = appExt.Start;
        int end = Math.Min(appExt.EndExclusive, bytes.Length);

        if (start < 0 || start + 14 >= end)
            return false;

        if (bytes[start] != 0x21 || bytes[start + 1] != 0xFF || bytes[start + 2] != 0x0B)
            return false;

        string identifier = Encoding.ASCII.GetString(bytes, start + 3, 8);
        string authCode = Encoding.ASCII.GetString(bytes, start + 11, 3);
        if (identifier != "NETSCAPE" || authCode != "2.0")
            return false;

        int pos = start + 14;
        if (pos + 4 >= end)
            return false;

        int subBlockSize = bytes[pos];
        if (subBlockSize < 3)
            return false;

        if (bytes[pos + 1] != 0x01)
            return false;

        loopCount = ReadUInt16(bytes, pos + 2);
        return true;
    }

    private static ushort ReadUInt16(byte[] bytes, int start) =>
        (ushort)(bytes[start] | (bytes[start + 1] << 8));
}
