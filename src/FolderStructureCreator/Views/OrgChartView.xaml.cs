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

    /// <summary>Raised when an inline rename is committed, passing the node and requested new name.</summary>
    public event Action<FolderNode, string>? NodeRenamed;

    /// <summary>Raised when a node box is dragged and dropped onto another node box (moving/re-parenting).</summary>
    public event Action<FolderNode, FolderNode>? NodeMoved;

    /// <summary>Raised when Open in Explorer is clicked in the node context menu.</summary>
    public event Action<FolderNode>? OpenInExplorerRequested;

    /// <summary>Raised when Add Child is clicked in the node context menu.</summary>
    public event Action<FolderNode>? AddChildRequested;

    /// <summary>Raised when Add Sibling is clicked in the node context menu.</summary>
    public event Action<FolderNode>? AddSiblingRequested;

    /// <summary>Raised when Delete is clicked in the node context menu.</summary>
    public event Action<FolderNode>? DeleteRequested;

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
    private static readonly SolidColorBrush DragHoverBrush = new(Color.FromRgb(0x02, 0x84, 0xC7)); // Sky blue highlight for drag target

    private List<FolderNode> _lastRoots = new();
    private FolderNode? _lastSelected;
    private Dictionary<Border, FolderNode> _boxMap = new();
    private FolderNode? _draggedNode;
    private Point _dragStartPoint;
    private bool _isDragging;
    private FolderNode? _dragTargetNode;
    private Border? _dragTargetBox;
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
        _boxMap.Clear();

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

            _boxMap[box] = node;

            box.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    _draggedNode = null;
                    _isDragging = false;
                    BeginRename(node, box);
                    e.Handled = true;
                    return;
                }

                _draggedNode = node;
                _dragStartPoint = e.GetPosition(RootCanvas);
                _isDragging = false;
                NodeClicked?.Invoke(node);
            };

            box.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _draggedNode == null) return;

                var posCanvas = e.GetPosition(RootCanvas);
                var diff = _dragStartPoint - posCanvas;

                if (!_isDragging && (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
                {
                    _isDragging = true;
                    box.CaptureMouse();
                }

                if (_isDragging)
                {
                    UpdateDragHoverTarget(posCanvas);
                }
            };

            box.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (box.IsMouseCaptured)
                    box.ReleaseMouseCapture();

                if (_isDragging && _draggedNode != null && _dragTargetNode != null)
                {
                    var source = _draggedNode;
                    var target = _dragTargetNode;
                    _draggedNode = null;
                    _isDragging = false;
                    ClearDragTargetHighlight();

                    NodeMoved?.Invoke(source, target);
                    e.Handled = true;
                    return;
                }

                _draggedNode = null;
                _isDragging = false;
                ClearDragTargetHighlight();
            };

            box.PreviewMouseRightButtonDown += (s, e) =>
            {
                NodeClicked?.Invoke(node);
            };

            var menu = new ContextMenu();
            var openInExplorerItem = new MenuItem { Header = "Open in Explorer" };
            openInExplorerItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                OpenInExplorerRequested?.Invoke(node);
            };

            var addChildItem = new MenuItem { Header = "Add child" };
            addChildItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                AddChildRequested?.Invoke(node);
            };

            var addSiblingItem = new MenuItem { Header = "Add sibling" };
            addSiblingItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                AddSiblingRequested?.Invoke(node);
            };

            var renameItem = new MenuItem { Header = "Rename" };
            renameItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                BeginRename(node, box);
            };

            var deleteItem = new MenuItem
            {
                Header = "Delete",
                Foreground = new SolidColorBrush(Color.FromRgb(0xBE, 0x12, 0x3C))
            };
            deleteItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                DeleteRequested?.Invoke(node);
            };

            menu.Items.Add(openInExplorerItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(addChildItem);
            menu.Items.Add(addSiblingItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(renameItem);
            menu.Items.Add(deleteItem);

            box.ContextMenu = menu;

            RootCanvas.Children.Add(box);

            if (node.IsEditing)
            {
                BeginRename(node, box);
            }
        }
    }

    private void UpdateDragHoverTarget(Point canvasPos)
    {
        FolderNode? hitNode = null;
        Border? hitBox = null;

        foreach (var (box, node) in _boxMap)
        {
            if (ReferenceEquals(node, _draggedNode)) continue;

            double left = Canvas.GetLeft(box);
            double top = Canvas.GetTop(box);

            if (canvasPos.X >= left && canvasPos.X <= left + BoxWidth &&
                canvasPos.Y >= top && canvasPos.Y <= top + BoxHeight)
            {
                hitNode = node;
                hitBox = box;
                break;
            }
        }

        if (ReferenceEquals(_dragTargetBox, hitBox)) return;

        ClearDragTargetHighlight();

        if (hitBox != null && hitNode != null)
        {
            _dragTargetBox = hitBox;
            _dragTargetNode = hitNode;
            hitBox.BorderBrush = DragHoverBrush;
            hitBox.BorderThickness = new Thickness(3);
        }
    }

    private void ClearDragTargetHighlight()
    {
        if (_dragTargetBox != null && _dragTargetNode != null)
        {
            bool isSelected = ReferenceEquals(_dragTargetNode, _lastSelected);
            var (_, border) = GetPalette(0);
            _dragTargetBox.BorderBrush = isSelected ? SelectedBrush : new SolidColorBrush(border);
            _dragTargetBox.BorderThickness = new Thickness(isSelected ? 2.5 : 1);
        }

        _dragTargetBox = null;
        _dragTargetNode = null;
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
            node.IsEditing = false;
            var newName = editBox.Text;
            if (NodeRenamed != null)
                NodeRenamed.Invoke(node, newName);
            else
                node.Name = newName;

            RenderInternal();
            StructureEdited?.Invoke();
        }

        editBox.LostFocus += (_, _) => Commit();
        editBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { node.IsEditing = false; RenderInternal(); e.Handled = true; }
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
