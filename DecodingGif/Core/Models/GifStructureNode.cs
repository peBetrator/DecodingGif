using System.Collections.ObjectModel;

namespace DecodingGif.Core.Models;

public sealed class GifStructureNode
{
    public string Title { get; }
    public GifByteRange? Range { get; }
    public int? FrameIndex { get; }
    public ObservableCollection<GifStructureNode> Children { get; } = new();

    public bool HasRange => Range is not null;

    public GifStructureNode(string title, GifByteRange? range = null, int? frameIndex = null)
    {
        Title = title;
        Range = range;
        FrameIndex = frameIndex;
    }

    public override string ToString() => Title;
}
