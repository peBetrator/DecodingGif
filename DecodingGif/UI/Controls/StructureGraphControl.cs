using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DecodingGif.Core.Models;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace DecodingGif.UI.Controls;

public sealed class StructureGraphControl : FrameworkElement
{
    public static readonly DependencyProperty GraphProperty =
        DependencyProperty.Register(
            nameof(Graph),
            typeof(StructureDependencyGraph),
            typeof(StructureGraphControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGraphChanged));

    public static readonly DependencyProperty ShowEdgeLabelsProperty =
        DependencyProperty.Register(
            nameof(ShowEdgeLabels),
            typeof(bool),
            typeof(StructureGraphControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Dictionary<string, Rect> _nodeRects = new();

    public StructureDependencyGraph? Graph
    {
        get => (StructureDependencyGraph?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public bool ShowEdgeLabels
    {
        get => (bool)GetValue(ShowEdgeLabelsProperty);
        set => SetValue(ShowEdgeLabelsProperty, value);
    }

    public event EventHandler<GifByteRange>? NavigateToByteRange;

    private static void OnGraphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not StructureGraphControl c)
            return;

        c.InvalidateMeasure();
        c.InvalidateVisual();
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        var graph = Graph;
        if (graph is null)
            return new WpfSize(600, 400);

        return new WpfSize(
            Math.Max(600, graph.CanvasSize.Width),
            Math.Max(400, graph.CanvasSize.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _nodeRects.Clear();

        if (Graph is null)
            return;

        var map = Graph.Nodes.ToDictionary(n => n.Id, n => n);
        DrawEdges(dc, map);
        DrawNodes(dc);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Graph is null)
            return;

        var p = e.GetPosition(this);
        foreach (var node in Graph.Nodes)
        {
            if (!_nodeRects.TryGetValue(node.Id, out var rect))
                continue;

            if (!rect.Contains(p) || node.ByteRange is null)
                continue;

            NavigateToByteRange?.Invoke(this, node.ByteRange);
            e.Handled = true;
            return;
        }
    }

    private void DrawEdges(DrawingContext dc, Dictionary<string, GraphNode> map)
    {
        foreach (var edge in Graph!.Edges)
        {
            if (!map.TryGetValue(edge.FromNodeId, out var from) || !map.TryGetValue(edge.ToNodeId, out var to))
                continue;

            var start = NodeCenter(from);
            var end = NodeCenter(to);
            var pen = BuildEdgePen(edge.Type);
            dc.DrawLine(pen, start, end);
            DrawArrowHead(dc, pen.Brush, start, end);

            if (ShowEdgeLabels && !string.IsNullOrWhiteSpace(edge.Label))
            {
                var text = BuildText(edge.Label, 10, WpfBrushes.DimGray);
                var mid = new WpfPoint((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
                dc.DrawText(text, new WpfPoint(mid.X + 2, mid.Y + 2));
            }
        }
    }

    private void DrawNodes(DrawingContext dc)
    {
        foreach (var node in Graph!.Nodes)
        {
            var rect = new Rect(node.Position, node.Size);
            _nodeRects[node.Id] = rect;

            var fill = GetNodeBrush(node);
            var border = new WpfPen(WpfBrushes.SlateGray, 1.1);

            if (node.Category == NodeCategory.ColorData)
            {
                dc.DrawEllipse(fill, border, NodeCenter(node), node.Size.Width / 2.0, node.Size.Height / 2.0);
                if (node.BlockType == GifBlockKind.GlobalColorTable)
                {
                    dc.DrawEllipse(null, new WpfPen(WpfBrushes.DarkGreen, 1.0), NodeCenter(node), node.Size.Width / 2.0 - 4, node.Size.Height / 2.0 - 4);
                }
            }
            else if (node.Category == NodeCategory.FrameControl)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new WpfPoint(rect.Left + rect.Width / 2, rect.Top), true, true);
                    g.LineTo(new WpfPoint(rect.Right, rect.Top + rect.Height / 2), true, false);
                    g.LineTo(new WpfPoint(rect.Left + rect.Width / 2, rect.Bottom), true, false);
                    g.LineTo(new WpfPoint(rect.Left, rect.Top + rect.Height / 2), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(fill, border, geo);
            }
            else
            {
                dc.DrawRoundedRectangle(fill, border, rect, 8, 8);
            }

            var text = BuildText(node.Title, 10, WpfBrushes.Black);
            dc.DrawText(text, new WpfPoint(rect.Left + 6, rect.Top + 6));
        }
    }

    private static WpfPoint NodeCenter(GraphNode n) =>
        new(n.Position.X + (n.Size.Width / 2.0), n.Position.Y + (n.Size.Height / 2.0));

    private static WpfBrush GetNodeBrush(GraphNode node) =>
        node.Category switch
        {
            NodeCategory.Header => new SolidColorBrush(WpfColor.FromRgb(219, 234, 254)),
            NodeCategory.ColorData => new SolidColorBrush(WpfColor.FromRgb(220, 252, 231)),
            NodeCategory.FrameControl => new SolidColorBrush(WpfColor.FromRgb(237, 233, 254)),
            NodeCategory.ImageData => new SolidColorBrush(WpfColor.FromRgb(255, 237, 213)),
            _ => new SolidColorBrush(WpfColor.FromRgb(226, 232, 240))
        };

    private static WpfPen BuildEdgePen(EdgeType type)
    {
        var pen = type switch
        {
            EdgeType.Sequential => new WpfPen(WpfBrushes.SlateGray, 1.0),
            EdgeType.Dependency => new WpfPen(WpfBrushes.MediumPurple, 1.0) { DashStyle = DashStyles.Dash },
            EdgeType.SharedResource => new WpfPen(WpfBrushes.ForestGreen, 1.0) { DashStyle = DashStyles.Dot },
            EdgeType.Temporal => new WpfPen(WpfBrushes.OrangeRed, 2.1),
            EdgeType.DataFlow => new WpfPen(WpfBrushes.SteelBlue, 1.2) { DashStyle = DashStyles.DashDot },
            _ => new WpfPen(WpfBrushes.Gray, 1.0)
        };
        pen.Freeze();
        return pen;
    }

    private static void DrawArrowHead(DrawingContext dc, WpfBrush brush, WpfPoint start, WpfPoint end)
    {
        var dir = end - start;
        if (dir.Length < 1.0)
            return;
        dir.Normalize();
        var normal = new Vector(-dir.Y, dir.X);
        var tip = end;
        var p1 = end - (dir * 9) + (normal * 4);
        var p2 = end - (dir * 9) - (normal * 4);

        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(tip, true, true);
        g.LineTo(p1, true, false);
        g.LineTo(p2, true, false);
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);
    }

    private static FormattedText BuildText(string text, double size, WpfBrush color) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            WpfFlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            color,
            1.0);
}
