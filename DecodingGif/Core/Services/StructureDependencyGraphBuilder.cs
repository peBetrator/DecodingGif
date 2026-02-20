using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class StructureDependencyGraphBuilder
{
    public StructureDependencyGraph BuildGraph(
        GifFile file,
        IEnumerable<GifByteRange> blocks,
        GraphLayoutMode layoutMode = GraphLayoutMode.Hierarchical)
    {
        var graph = new StructureDependencyGraph { Layout = layoutMode };
        var ordered = blocks.OrderBy(b => b.Start).ToList();
        if (ordered.Count == 0)
            return graph;

        CreateStructuralNodes(graph, ordered);
        CreateSequentialEdges(graph, ordered);
        CreateDependencyEdges(graph, ordered);
        CreateSharedResourceEdges(graph, ordered);
        CreateTemporalEdges(graph, ordered);
        CreateDataFlowEdges(graph, ordered);
        ApplyLayout(graph, layoutMode);
        return graph;
    }

    private static void CreateStructuralNodes(StructureDependencyGraph graph, IReadOnlyList<GifByteRange> blocks)
    {
        foreach (var block in blocks)
        {
            graph.Nodes.Add(new GraphNode
            {
                Id = NodeId(block.Start),
                Title = BuildNodeTitle(block),
                BlockType = block.Kind,
                ByteRange = block,
                Category = GetNodeCategory(block.Kind),
                Properties = new Dictionary<string, object>
                {
                    ["Start"] = block.Start,
                    ["Length"] = block.Length,
                    ["Name"] = block.Name
                }
            });
        }
    }

    private static void CreateSequentialEdges(StructureDependencyGraph graph, IReadOnlyList<GifByteRange> blocks)
    {
        for (int i = 0; i < blocks.Count - 1; i++)
        {
            graph.Edges.Add(new GraphEdge
            {
                FromNodeId = NodeId(blocks[i].Start),
                ToNodeId = NodeId(blocks[i + 1].Start),
                Type = EdgeType.Sequential,
                Label = "Next"
            });
        }
    }

    private static void CreateDependencyEdges(StructureDependencyGraph graph, IReadOnlyList<GifByteRange> blocks)
    {
        var gceBlocks = blocks.Where(b => b.Kind == GifBlockKind.GraphicControlExtension).ToList();
        var imageBlocks = blocks.Where(b => b.Kind == GifBlockKind.ImageDescriptor).ToList();

        foreach (var image in imageBlocks)
        {
            var precedingGce = gceBlocks.LastOrDefault(g => g.Start < image.Start);
            if (precedingGce is not null)
            {
                graph.Edges.Add(new GraphEdge
                {
                    FromNodeId = NodeId(precedingGce.Start),
                    ToNodeId = NodeId(image.Start),
                    Type = EdgeType.Dependency,
                    Label = "Controls"
                });
            }

            var trailing = blocks.Where(b => b.Start > image.Start).OrderBy(b => b.Start).ToList();
            foreach (var next in trailing)
            {
                if (next.Kind == GifBlockKind.LocalColorTable)
                {
                    graph.Edges.Add(new GraphEdge
                    {
                        FromNodeId = NodeId(image.Start),
                        ToNodeId = NodeId(next.Start),
                        Type = EdgeType.Dependency,
                        Label = "Has LCT"
                    });
                    continue;
                }

                if (next.Kind == GifBlockKind.ImageData)
                {
                    graph.Edges.Add(new GraphEdge
                    {
                        FromNodeId = NodeId(image.Start),
                        ToNodeId = NodeId(next.Start),
                        Type = EdgeType.Dependency,
                        Label = "Pixel Data"
                    });
                    break;
                }

                if (next.Kind is GifBlockKind.ImageDescriptor or GifBlockKind.GraphicControlExtension or GifBlockKind.Trailer)
                    break;
            }
        }
    }

    private static void CreateSharedResourceEdges(StructureDependencyGraph graph, IReadOnlyList<GifByteRange> blocks)
    {
        var gct = blocks.FirstOrDefault(b => b.Kind == GifBlockKind.GlobalColorTable);
        if (gct is null)
            return;

        foreach (var image in blocks.Where(b => b.Kind == GifBlockKind.ImageDescriptor))
        {
            graph.Edges.Add(new GraphEdge
            {
                FromNodeId = NodeId(gct.Start),
                ToNodeId = NodeId(image.Start),
                Type = EdgeType.SharedResource,
                Label = "Palette"
            });
        }
    }

    private static void CreateTemporalEdges(StructureDependencyGraph graph, IReadOnlyList<GifByteRange> blocks)
    {
        var images = blocks.Where(b => b.Kind == GifBlockKind.ImageDescriptor).OrderBy(b => b.Start).ToList();
        for (int i = 0; i < images.Count - 1; i++)
        {
            graph.Edges.Add(new GraphEdge
            {
                FromNodeId = NodeId(images[i].Start),
                ToNodeId = NodeId(images[i + 1].Start),
                Type = EdgeType.Temporal,
                Label = "Frame Seq"
            });
        }
    }

    private static void CreateDataFlowEdges(StructureDependencyGraph graph, IReadOnlyList<GifByteRange> blocks)
    {
        var header = blocks.FirstOrDefault(b => b.Kind == GifBlockKind.Header);
        var lsd = blocks.FirstOrDefault(b => b.Kind == GifBlockKind.LogicalScreenDescriptor);
        var gct = blocks.FirstOrDefault(b => b.Kind == GifBlockKind.GlobalColorTable);

        if (header is not null && lsd is not null)
        {
            graph.Edges.Add(new GraphEdge
            {
                FromNodeId = NodeId(header.Start),
                ToNodeId = NodeId(lsd.Start),
                Type = EdgeType.DataFlow,
                Label = "Format"
            });
        }

        if (lsd is not null && gct is not null)
        {
            graph.Edges.Add(new GraphEdge
            {
                FromNodeId = NodeId(lsd.Start),
                ToNodeId = NodeId(gct.Start),
                Type = EdgeType.DataFlow,
                Label = "LSD->GCT"
            });
        }
    }

    private static void ApplyLayout(StructureDependencyGraph graph, GraphLayoutMode mode)
    {
        switch (mode)
        {
            case GraphLayoutMode.Circular:
                ApplyCircularLayout(graph);
                break;
            case GraphLayoutMode.ForceDirected:
                ApplyCircularLayout(graph);
                break;
            default:
                ApplyHierarchicalLayout(graph);
                break;
        }
    }

    private static void ApplyHierarchicalLayout(StructureDependencyGraph graph)
    {
        var rowMap = new Dictionary<NodeCategory, int>
        {
            [NodeCategory.Header] = 0,
            [NodeCategory.ColorData] = 1,
            [NodeCategory.FrameControl] = 2,
            [NodeCategory.ImageData] = 3,
            [NodeCategory.Extension] = 4
        };

        const double left = 40;
        const double top = 40;
        const double xStep = 170;
        const double yStep = 120;

        foreach (var grp in graph.Nodes.GroupBy(n => n.Category))
        {
            int row = rowMap.TryGetValue(grp.Key, out int mapped) ? mapped : 5;
            int col = 0;
            foreach (var node in grp.OrderBy(n => n.ByteRange?.Start ?? int.MaxValue))
            {
                node.Position = new System.Windows.Point(left + (col * xStep), top + (row * yStep));
                col++;
            }
        }

        graph.CanvasSize = new System.Windows.Size(
            Math.Max(1200, left + (graph.Nodes.Count * xStep * 0.5)),
            top + (6 * yStep));
    }

    private static void ApplyCircularLayout(StructureDependencyGraph graph)
    {
        int count = graph.Nodes.Count;
        if (count == 0)
            return;

        double radius = Math.Max(260, count * 14);
        var center = new System.Windows.Point(radius + 120, radius + 120);

        var ordered = graph.Nodes.OrderBy(n => n.ByteRange?.Start ?? int.MaxValue).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            double angle = (Math.PI * 2.0 * i) / count;
            ordered[i].Position = new System.Windows.Point(
                center.X + (Math.Cos(angle) * radius),
                center.Y + (Math.Sin(angle) * radius));
        }

        graph.CanvasSize = new System.Windows.Size(center.X * 2 + 120, center.Y * 2 + 120);
    }

    private static string NodeId(int start) => $"block_{start}";

    private static string BuildNodeTitle(GifByteRange block) =>
        $"{block.Kind}\n0x{block.Start:X6} ({block.Length}b)";

    private static NodeCategory GetNodeCategory(GifBlockKind kind) =>
        kind switch
        {
            GifBlockKind.Header or GifBlockKind.LogicalScreenDescriptor => NodeCategory.Header,
            GifBlockKind.GlobalColorTable or GifBlockKind.LocalColorTable => NodeCategory.ColorData,
            GifBlockKind.GraphicControlExtension => NodeCategory.FrameControl,
            GifBlockKind.ImageDescriptor or GifBlockKind.ImageData => NodeCategory.ImageData,
            _ => NodeCategory.Extension
        };
}
