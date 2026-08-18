using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FolderStructureCreator.Models;
using System.Linq;
using System.Collections.Generic;

namespace FolderStructureCreator.Views;

/// <summary>
/// A horizontal org-chart / dendrogram style visualization of a folder structure -
/// colored boxes per depth level, connected by right-angle elbow lines, laid out left to right.
/// This is a lightweight custom-drawn control (Canvas + code-behind layout) rather than a
/// TreeView, since WPF has nothing built in for this diagram shape.
/// </summary>
public partial class OrgChartView : UserControl
{
    /// <summary>Raised when a node box is single-clicked (used to drive selection in the toolbar).</summary>
    public event Action<FolderNode>? NodeClicked;

    /// <summary>Raised after an inline rename (via double-click) is committed to the model.</summary>
    public event Action? StructureEdited;

    private const double BoxWidth = 172;
    private const double BoxHeight = 34;
    private const double ColumnGap = 56;   // horizontal room for connector routing between columns
    private const double RowHeight = 46;   // vertical spacing between sibling rows
    private const double ChartPadding = 24;

    // Depth-based palette, cycling if the tree goes deeper than the list - loosely matches the
    // reference org-chart style (root=blue, then salmon, gray, amber, repeating).
    private static readonly (Color Fill, Color Border)[] Palette =
    {
        (Color.FromRgb(0xAF, 0xC2, 0xE8), Color.FromRgb(0x6C, 0x86, 0xC2)), // depth 0 - blue
        (Color.FromRgb(0xF3, 0xB3, 0x9B), Color.FromRgb(0xD9, 0x7A, 0x5A)), // depth 1 - salmon
        (Color.FromRgb(0xD3, 0xD6, 0xDC), Color.FromRgb(0x9A, 0x9F, 0xA8)), // depth 2 - gray
        (Color.FromRgb(0xF7, 0xCE, 0x8A), Color.FromRgb(0xE0, 0xA5, 0x3A)), // depth 3 - amber
    };

    private static readonly SolidColorBrush SelectedBrush = new(Color.FromRgb(0x0F, 0x76, 0x6E));

    private List<FolderNode> _lastRoots = new();
    private FolderNode? _lastSelected;
    private const double MinZoom = 0.35;
    private const double MaxZoom = 2.0;

    public OrgChartView()
    {
        InitializeComponent();
    }

    /// <summary>Redraws the whole chart for the given roots, highlighting the selected node if any.</summary>
    public void Render(IEnumerable<FolderNode> roots, FolderNode? selected)
    {
        _lastRoots = roots.ToList();
        _lastSelected = selected;
        RenderInternal();
    }

    public void ZoomIn() => SetZoom(ChartScale.ScaleX + 0.15);

    public void ZoomOut() => SetZoom(ChartScale.ScaleX - 0.15);

    public void FitToView()
    {
        if (RootCanvas.Width <= 0 || RootCanvas.Height <= 0 ||
            ChartScrollViewer.ViewportWidth <= 0 || ChartScrollViewer.ViewportHeight <= 0)
            return;

        var widthScale = (ChartScrollViewer.ViewportWidth - 20) / RootCanvas.Width;
        var heightScale = (ChartScrollViewer.ViewportHeight - 20) / RootCanvas.Height;
        SetZoom(Math.Min(widthScale, heightScale));
        ChartScrollViewer.ScrollToHome();
    }

    private void SetZoom(double zoom)
    {
        var clamped = Math.Clamp(zoom, MinZoom, MaxZoom);
        ChartScale.ScaleX = clamped;
        ChartScale.ScaleY = clamped;
    }

    private void RenderInternal()
    {
        RootCanvas.Children.Clear();

        if (_lastRoots.Count == 0)
        {
            RootCanvas.Width = 0;
            RootCanvas.Height = 0;
            return;
        }

        // ---- Layout pass: assign every node a fractional "row" via post-order DFS so a parent
        // ends up vertically centered over its children (classic dendrogram layout). ----
        double nextRow = 0;
        var positions = new Dictionary<FolderNode, (double Row, int Depth)>();

        double LayoutNode(FolderNode node, int depth)
        {
            if (node.Children.Count == 0)
            {
                double row = nextRow;
                nextRow += 1;
                positions[node] = (row, depth);
                return row;
            }

            double first = -1, last = -1;
            foreach (var child in node.Children)
            {
                var r = LayoutNode(child, depth + 1);
                if (first < 0) first = r;
                last = r;
            }

            double center = (first + last) / 2.0;
            positions[node] = (center, depth);
            return center;
        }

        foreach (var root in _lastRoots)
            LayoutNode(root, 0);

        int maxDepth = positions.Count > 0 ? positions.Values.Max(p => p.Depth) : 0;
        RootCanvas.Width = Math.Max(ChartPadding * 2 + (maxDepth + 1) * (BoxWidth + ColumnGap), 100);
        RootCanvas.Height = Math.Max(ChartPadding * 2 + nextRow * RowHeight, 100);

        // ---- Connectors first, so node boxes visually sit on top of the lines. ----
        void DrawConnectors(FolderNode node)
        {
            if (!positions.TryGetValue(node, out var parentPos)) return;

            foreach (var child in node.Children)
            {
                if (!positions.TryGetValue(child, out var childPos)) continue;

                var (px, py) = ToPixel(parentPos.Row, parentPos.Depth, rightEdge: true);
                var (cx, cy) = ToPixel(childPos.Row, childPos.Depth, rightEdge: false);
                double midX = (px + cx) / 2.0;

                var figure = new PathFigure { StartPoint = new Point(px, py) };
                figure.Segments.Add(new LineSegment(new Point(midX, py), true));
                figure.Segments.Add(new LineSegment(new Point(midX, cy), true));
                figure.Segments.Add(new LineSegment(new Point(cx, cy), true));
                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);

                RootCanvas.Children.Add(new Path
                {
                    Data = geometry,
                    Stroke = new SolidColorBrush(GetPalette(childPos.Depth).Border),
                    StrokeThickness = 1.6
                });

                DrawConnectors(child);
            }
        }

        foreach (var root in _lastRoots)
            DrawConnectors(root);

        // ---- Node boxes. ----
        foreach (var (node, pos) in positions)
        {
            var (fill, border) = GetPalette(pos.Depth);
            bool isSelected = ReferenceEquals(node, _lastSelected);

            var box = new Border
            {
                Width = BoxWidth,
                Height = BoxHeight,
                Background = new SolidColorBrush(fill),
                BorderBrush = isSelected ? SelectedBrush : new SolidColorBrush(border),
                BorderThickness = new Thickness(isSelected ? 2.5 : 1),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                ToolTip = node.Name
            };

            box.Child = new TextBlock
            {
                Text = node.Name,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0)
            };

            var (x, y) = ToPixel(pos.Row, pos.Depth, rightEdge: false);
            Canvas.SetLeft(box, x);
            Canvas.SetTop(box, y - BoxHeight / 2);

            box.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                    BeginRename(node, box);
                else
                    NodeClicked?.Invoke(node);
                e.Handled = true;
            };

            RootCanvas.Children.Add(box);
        }
    }

    /// <summary>Swaps a node's box for an inline TextBox so its name can be edited in place.</summary>
    private void BeginRename(FolderNode node, Border box)
    {
        NodeClicked?.Invoke(node); // renaming also selects it, matching the tree view's behavior

        var editBox = new TextBox
        {
            Width = BoxWidth,
            Height = BoxHeight,
            Text = node.Name,
            FontSize = 11.5,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        Canvas.SetLeft(editBox, Canvas.GetLeft(box));
        Canvas.SetTop(editBox, Canvas.GetTop(box));
        RootCanvas.Children.Remove(box);
        RootCanvas.Children.Add(editBox);

        editBox.Loaded += (_, _) =>
        {
            editBox.Focus();
            editBox.SelectAll();
        };

        void Commit()
        {
            node.Name = editBox.Text; // FolderNode.Name setter already falls back to "New Folder" if blank
            RenderInternal();
            StructureEdited?.Invoke();
        }

        editBox.LostFocus += (_, _) => Commit();
        editBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { RenderInternal(); e.Handled = true; }
        };
    }

    private (double X, double Y) ToPixel(double row, int depth, bool rightEdge)
    {
        double x = ChartPadding + depth * (BoxWidth + ColumnGap) + (rightEdge ? BoxWidth : 0);
        double y = ChartPadding + row * RowHeight + RowHeight / 2.0;
        return (x, y);
    }

    private static (Color Fill, Color Border) GetPalette(int depth) => Palette[depth % Palette.Length];
}
