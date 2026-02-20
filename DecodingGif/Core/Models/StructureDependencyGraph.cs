using System.Collections.ObjectModel;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace DecodingGif.Core.Models;

public enum NodeCategory
{
    Header,
    ColorData,
    FrameControl,
    ImageData,
    Extension
}

public enum EdgeType
{
    Sequential,
    Dependency,
    SharedResource,
    Temporal,
    DataFlow
}

public enum GraphLayoutMode
{
    Hierarchical,
    Circular,
    ForceDirected
}

public sealed class GraphNode
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public GifBlockKind BlockType { get; init; }
    public GifByteRange? ByteRange { get; init; }
    public WpfPoint Position { get; set; }
    public WpfSize Size { get; set; } = new(130, 48);
    public NodeCategory Category { get; init; }
    public Dictionary<string, object> Properties { get; init; } = new();
}

public sealed class GraphEdge
{
    public string FromNodeId { get; init; } = string.Empty;
    public string ToNodeId { get; init; } = string.Empty;
    public EdgeType Type { get; init; }
    public string Label { get; init; } = string.Empty;
    public object? Metadata { get; init; }
}

public sealed class StructureDependencyGraph
{
    public ObservableCollection<GraphNode> Nodes { get; } = new();
    public ObservableCollection<GraphEdge> Edges { get; } = new();
    public GraphLayoutMode Layout { get; set; } = GraphLayoutMode.Hierarchical;
    public WpfSize CanvasSize { get; set; } = new(1200, 800);
}
