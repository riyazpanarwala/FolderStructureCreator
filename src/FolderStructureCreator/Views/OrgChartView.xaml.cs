using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FolderStructureCreator.Models;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Security;

namespace FolderStructureCreator.Views;

public enum OrgChartLayoutDirection
{
    Horizontal,
    Vertical
}

/// <summary>
/// A horizontal or vertical org-chart / dendrogram style visualization of a folder structure -
/// colored boxes per depth level, connected by right-angle elbow lines, laid out left-to-right or top-to-bottom.
/// This is a lightweight custom-drawn control (Canvas + code-behind layout) rather than a
/// TreeView, since WPF has nothing built in for this diagram shape.
/// </summary>
public partial class OrgChartView : UserControl
{
    public static readonly DependencyProperty LayoutDirectionProperty =
        DependencyProperty.Register(
            nameof(LayoutDirection),
            typeof(OrgChartLayoutDirection),
            typeof(OrgChartView),
            new FrameworkPropertyMetadata(OrgChartLayoutDirection.Horizontal, OnLayoutDirectionChanged));

    public OrgChartLayoutDirection LayoutDirection
    {
        get => (OrgChartLayoutDirection)GetValue(LayoutDirectionProperty);
        set => SetValue(LayoutDirectionProperty, value);
    }

    private static void OnLayoutDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OrgChartView chart)
        {
            chart.RenderInternal();
        }
    }

    /// <summary>Raised when a node box is single-clicked (used to drive selection in the toolbar).</summary>
    public event Action<FolderNode>? NodeClicked;

    /// <summary>Raised after an inline rename is committed to the model.</summary>
    public event Action? StructureEdited;

    /// <summary>Raised when an inline rename is committed, passing the node and requested new name.</summary>
    public event Action<FolderNode, string>? NodeRenamed;

    /// <summary>Raised when a node box is dragged and dropped onto another node box (moving/re-parenting) or onto empty canvas (null target, moving to root).</summary>
    public event Action<FolderNode, FolderNode?>? NodeMoved;

    /// <summary>Raised when Open in Explorer is clicked in the node context menu.</summary>
    public event Action<FolderNode>? OpenInExplorerRequested;

    /// <summary>Raised when Add Child is clicked in the node context menu.</summary>
    public event Action<FolderNode>? AddChildRequested;

    /// <summary>Raised when Add Sibling is clicked in the node context menu.</summary>
    public event Action<FolderNode>? AddSiblingRequested;

    /// <summary>Raised when Delete is clicked in the node context menu.</summary>
    public event Action<FolderNode>? DeleteRequested;

    /// <summary>Raised when the zoom level changes.</summary>
    public event Action<double>? ZoomLevelChanged;

    /// <summary>Current zoom level scale.</summary>
    public double ZoomLevel => ChartScale.ScaleX;

    private const double BoxWidth = 172;
    private const double BoxHeight = 34;
    private const double ColumnGap = 56;   // horizontal room for connector routing between columns (Horizontal mode)
    private const double RowHeight = 46;   // vertical spacing between sibling rows (Horizontal mode)
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
    private static readonly SolidColorBrush SearchMatchBorderBrush = new(Color.FromRgb(0xD9, 0x77, 0x06)); // Gold/Amber border for search match
    private static readonly SolidColorBrush SearchMatchBackgroundBrush = new(Color.FromRgb(0xFE, 0xF0, 0x8A)); // Bright yellow fill for search match

    private List<FolderNode> _lastRoots = new();
    private FolderNode? _lastSelected;
    private Dictionary<Border, FolderNode> _boxMap = new();
    private FolderNode? _draggedNode;
    private Point _dragStartPoint;
    private bool _isDragging;
    private FolderNode? _dragTargetNode;
    private Border? _dragTargetBox;
    private Border? _dragGhostBorder;
    private bool _isHoveringRootDropZone;

    // Expanded zoom limits (10% to 400%)
    private const double MinZoom = 0.1;
    private const double MaxZoom = 4.0;

    // Canvas panning fields
    private bool _isPanning;
    private Point _panStartMousePos;
    private double _panStartHOffset;
    private double _panStartVOffset;

    public static readonly DependencyProperty IsMiniMapVisibleProperty =
        DependencyProperty.Register(
            nameof(IsMiniMapVisible),
            typeof(bool),
            typeof(OrgChartView),
            new FrameworkPropertyMetadata(true, OnIsMiniMapVisibleChanged));

    public bool IsMiniMapVisible
    {
        get => (bool)GetValue(IsMiniMapVisibleProperty);
        set => SetValue(IsMiniMapVisibleProperty, value);
    }

    private static void OnIsMiniMapVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OrgChartView chart)
        {
            chart.MiniMapBorder.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            chart.UpdateMiniMapViewport();
        }
    }

    private bool _isMiniMapDragging;

    public OrgChartView()
    {
        InitializeComponent();

        ChartScrollViewer.PreviewMouseWheel += ChartScrollViewer_PreviewMouseWheel;
        ChartScrollViewer.PreviewMouseDown += ChartScrollViewer_PreviewMouseDown;
        ChartScrollViewer.PreviewMouseMove += ChartScrollViewer_PreviewMouseMove;
        ChartScrollViewer.PreviewMouseUp += ChartScrollViewer_PreviewMouseUp;
        ChartScrollViewer.ScrollChanged += (_, _) => UpdateMiniMapViewport();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && _isDragging)
        {
            _isDragging = false;
            _draggedNode = null;
            _dragTargetNode = null;
            _isHoveringRootDropZone = false;
            ClearDragTargetHighlight();
            if (_dragGhostBorder != null) _dragGhostBorder.Visibility = Visibility.Collapsed;
            RootDropBanner.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void ChartScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            Point mousePos = e.GetPosition(ChartScrollViewer);
            ZoomAtPoint(ChartScale.ScaleX * factor, mousePos);
            e.Handled = true;
        }
    }

    private void ChartScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle ||
           (e.ChangedButton == MouseButton.Right && (e.OriginalSource is Canvas or ScrollViewer)))
        {
            _isPanning = true;
            _panStartMousePos = e.GetPosition(ChartScrollViewer);
            _panStartHOffset = ChartScrollViewer.HorizontalOffset;
            _panStartVOffset = ChartScrollViewer.VerticalOffset;
            ChartScrollViewer.CaptureMouse();
            ChartScrollViewer.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    private void ChartScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            Point currentPos = e.GetPosition(ChartScrollViewer);
            Vector delta = currentPos - _panStartMousePos;

            ChartScrollViewer.ScrollToHorizontalOffset(_panStartHOffset - delta.X);
            ChartScrollViewer.ScrollToVerticalOffset(_panStartVOffset - delta.Y);
            e.Handled = true;
        }
    }

    private void ChartScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right))
        {
            _isPanning = false;
            ChartScrollViewer.ReleaseMouseCapture();
            ChartScrollViewer.Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    /// <summary>Redraws the whole chart for the given roots, highlighting the selected node if any.</summary>
    public void Render(IEnumerable<FolderNode> roots, FolderNode? selected)
    {
        _lastRoots = roots.ToList();
        _lastSelected = selected;
        RenderInternal();
    }

    /// <summary>Scrolls/centers the ScrollViewer viewport on the selected node box if present.</summary>
    public void BringSelectedIntoView()
    {
        if (_lastSelected == null) return;

        void PerformScroll()
        {
            var selectedEntry = _boxMap.FirstOrDefault(kvp => ReferenceEquals(kvp.Value, _lastSelected));
            if (selectedEntry.Key is not Border box) return;

            double scale = ChartScale.ScaleX;
            double left = Canvas.GetLeft(box) * scale;
            double top = Canvas.GetTop(box) * scale;

            double boxWidthScaled = BoxWidth * scale;
            double boxHeightScaled = BoxHeight * scale;

            double viewportWidth = ChartScrollViewer.ViewportWidth;
            double viewportHeight = ChartScrollViewer.ViewportHeight;

            if (viewportWidth <= 0 || viewportHeight <= 0) return;

            double targetX = left + (boxWidthScaled / 2.0) - (viewportWidth / 2.0);
            double targetY = top + (boxHeightScaled / 2.0) - (viewportHeight / 2.0);

            ChartScrollViewer.ScrollToHorizontalOffset(Math.Max(0, targetX));
            ChartScrollViewer.ScrollToVerticalOffset(Math.Max(0, targetY));
        }

        if (ChartScrollViewer.ViewportWidth > 0 && ChartScrollViewer.ViewportHeight > 0)
        {
            PerformScroll();
        }
        else
        {
            Dispatcher.BeginInvoke(PerformScroll, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    public void ZoomIn() => SetZoom(ChartScale.ScaleX * 1.15);

    public void ZoomOut() => SetZoom(ChartScale.ScaleX / 1.15);

    public void ResetZoom() => SetZoom(1.0);

    public void ZoomAtPoint(double newZoom, Point viewportPoint)
    {
        var oldZoom = ChartScale.ScaleX;
        var clampedZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (Math.Abs(clampedZoom - oldZoom) < 0.001) return;

        double mouseOffsetX = viewportPoint.X + ChartScrollViewer.HorizontalOffset;
        double mouseOffsetY = viewportPoint.Y + ChartScrollViewer.VerticalOffset;

        double canvasX = mouseOffsetX / oldZoom;
        double canvasY = mouseOffsetY / oldZoom;

        ChartScale.ScaleX = clampedZoom;
        ChartScale.ScaleY = clampedZoom;
        ZoomLevelChanged?.Invoke(clampedZoom);

        double newMouseOffsetX = canvasX * clampedZoom;
        double newMouseOffsetY = canvasY * clampedZoom;

        ChartScrollViewer.ScrollToHorizontalOffset(newMouseOffsetX - viewportPoint.X);
        ChartScrollViewer.ScrollToVerticalOffset(newMouseOffsetY - viewportPoint.Y);
    }

    public void FitToView()
    {
        void PerformFit()
        {
            if (RootCanvas.Width <= 0 || RootCanvas.Height <= 0 ||
                ChartScrollViewer.ViewportWidth <= 0 || ChartScrollViewer.ViewportHeight <= 0)
                return;

            double padding = 32;
            var widthScale = (ChartScrollViewer.ViewportWidth - padding) / RootCanvas.Width;
            var heightScale = (ChartScrollViewer.ViewportHeight - padding) / RootCanvas.Height;
            double fitZoom = Math.Min(widthScale, heightScale);

            SetZoom(fitZoom);

            double scaledWidth = RootCanvas.Width * ChartScale.ScaleX;
            double scaledHeight = RootCanvas.Height * ChartScale.ScaleY;

            double targetX = Math.Max(0, (scaledWidth - ChartScrollViewer.ViewportWidth) / 2.0);
            double targetY = Math.Max(0, (scaledHeight - ChartScrollViewer.ViewportHeight) / 2.0);

            ChartScrollViewer.ScrollToHorizontalOffset(targetX);
            ChartScrollViewer.ScrollToVerticalOffset(targetY);
        }

        if (ChartScrollViewer.ViewportWidth > 0 && ChartScrollViewer.ViewportHeight > 0)
        {
            PerformFit();
        }
        else
        {
            Dispatcher.BeginInvoke(PerformFit, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    public void FitSelectedToView()
    {
        if (_lastSelected == null)
        {
            FitToView();
            return;
        }

        if (ChartScale.ScaleX < 0.8)
        {
            SetZoom(1.0);
        }

        BringSelectedIntoView();
    }

    private void SetZoom(double zoom)
    {
        var clamped = Math.Clamp(zoom, MinZoom, MaxZoom);
        ChartScale.ScaleX = clamped;
        ChartScale.ScaleY = clamped;
        ZoomLevelChanged?.Invoke(clamped);
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
        // ends up centered over its children (classic dendrogram layout). ----
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

        bool isVertical = LayoutDirection == OrgChartLayoutDirection.Vertical;
        const double siblingWidth = BoxWidth + 20; // X spacing per sibling column in vertical mode
        const double levelHeight = BoxHeight + 46; // Y spacing per depth level in vertical mode

        if (isVertical)
        {
            RootCanvas.Width = Math.Max(ChartPadding * 2 + nextRow * siblingWidth, 100);
            RootCanvas.Height = Math.Max(ChartPadding * 2 + (maxDepth + 1) * levelHeight, 100);
        }
        else
        {
            RootCanvas.Width = Math.Max(ChartPadding * 2 + (maxDepth + 1) * (BoxWidth + ColumnGap), 100);
            RootCanvas.Height = Math.Max(ChartPadding * 2 + nextRow * RowHeight, 100);
        }

        (double X, double Y) GetParentConnectionPoint(double row, int depth)
        {
            if (isVertical)
            {
                double x = ChartPadding + row * siblingWidth + siblingWidth / 2.0;
                double y = ChartPadding + depth * levelHeight + BoxHeight;
                return (x, y);
            }
            else
            {
                double x = ChartPadding + depth * (BoxWidth + ColumnGap) + BoxWidth;
                double y = ChartPadding + row * RowHeight + RowHeight / 2.0;
                return (x, y);
            }
        }

        (double X, double Y) GetChildConnectionPoint(double row, int depth)
        {
            if (isVertical)
            {
                double x = ChartPadding + row * siblingWidth + siblingWidth / 2.0;
                double y = ChartPadding + depth * levelHeight;
                return (x, y);
            }
            else
            {
                double x = ChartPadding + depth * (BoxWidth + ColumnGap);
                double y = ChartPadding + row * RowHeight + RowHeight / 2.0;
                return (x, y);
            }
        }

        // ---- Connectors first, so node boxes visually sit on top of the lines. ----
        void DrawConnectors(FolderNode node)
        {
            if (!positions.TryGetValue(node, out var parentPos)) return;

            foreach (var child in node.Children)
            {
                if (!positions.TryGetValue(child, out var childPos)) continue;

                var (px, py) = GetParentConnectionPoint(parentPos.Row, parentPos.Depth);
                var (cx, cy) = GetChildConnectionPoint(childPos.Row, childPos.Depth);

                var figure = new PathFigure { StartPoint = new Point(px, py) };

                if (isVertical)
                {
                    double midY = (py + cy) / 2.0;
                    figure.Segments.Add(new LineSegment(new Point(px, midY), true));
                    figure.Segments.Add(new LineSegment(new Point(cx, midY), true));
                    figure.Segments.Add(new LineSegment(new Point(cx, cy), true));
                }
                else
                {
                    double midX = (px + cx) / 2.0;
                    figure.Segments.Add(new LineSegment(new Point(midX, py), true));
                    figure.Segments.Add(new LineSegment(new Point(midX, cy), true));
                    figure.Segments.Add(new LineSegment(new Point(cx, cy), true));
                }

                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);

                RootCanvas.Children.Add(new System.Windows.Shapes.Path
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
            bool isMatch = node.IsMatchingSearch;

            var box = new Border
            {
                Width = BoxWidth,
                Height = BoxHeight,
                Background = isMatch ? SearchMatchBackgroundBrush : new SolidColorBrush(fill),
                BorderBrush = isSelected ? SelectedBrush : (isMatch ? SearchMatchBorderBrush : new SolidColorBrush(border)),
                BorderThickness = new Thickness((isSelected || isMatch) ? 2.5 : 1),
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

            double boxX, boxY;
            if (isVertical)
            {
                boxX = ChartPadding + pos.Row * siblingWidth + siblingWidth / 2.0 - BoxWidth / 2.0;
                boxY = ChartPadding + pos.Depth * levelHeight;
            }
            else
            {
                boxX = ChartPadding + pos.Depth * (BoxWidth + ColumnGap);
                boxY = ChartPadding + pos.Row * RowHeight + RowHeight / 2.0 - BoxHeight / 2.0;
            }

            Canvas.SetLeft(box, boxX);
            Canvas.SetTop(box, boxY);

            _boxMap[box] = node;

            box.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    _draggedNode = null;
                    _isDragging = false;
                    OpenInExplorerRequested?.Invoke(node);
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
                    EnsureDragGhostCreated();
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

                if (_dragGhostBorder != null)
                    _dragGhostBorder.Visibility = Visibility.Collapsed;

                RootDropBanner.Visibility = Visibility.Collapsed;

                if (_isDragging && _draggedNode != null)
                {
                    var source = _draggedNode;
                    var target = _dragTargetNode;
                    bool wasRootHover = _isHoveringRootDropZone;

                    _draggedNode = null;
                    _isDragging = false;
                    _isHoveringRootDropZone = false;
                    ClearDragTargetHighlight();

                    if (target != null)
                    {
                        NodeMoved?.Invoke(source, target);
                    }
                    else if (wasRootHover)
                    {
                        NodeMoved?.Invoke(source, null); // Dropped explicitly onto Root Drop Banner
                    }
                    // Else: dropped on empty canvas space -> drag is safely CANCELLED (node remains unchanged)

                    e.Handled = true;
                    return;
                }

                _draggedNode = null;
                _isDragging = false;
                _isHoveringRootDropZone = false;
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

            MenuItem? moveToRootItem = null;
            if (node.Parent != null)
            {
                moveToRootItem = new MenuItem { Header = "Move to Root" };
                moveToRootItem.Click += (_, _) =>
                {
                    NodeClicked?.Invoke(node);
                    NodeMoved?.Invoke(node, null);
                };
            }

            var renameItem = new MenuItem { Header = "Rename" };
            renameItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                BeginRename(node, box);
            };

            var focusItem = new MenuItem { Header = "Focus folder (Fit selection)" };
            focusItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                FitSelectedToView();
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
            menu.Items.Add(focusItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(addChildItem);
            menu.Items.Add(addSiblingItem);
            if (moveToRootItem != null)
            {
                menu.Items.Add(moveToRootItem);
            }
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

        UpdateMiniMapViewport();
    }

    private void EnsureDragGhostCreated()
    {
        if (_draggedNode == null) return;

        if (_dragGhostBorder == null)
        {
            _dragGhostBorder = new Border
            {
                Width = BoxWidth,
                Height = BoxHeight,
                Background = new SolidColorBrush(Color.FromArgb(0xD8, 0xE2, 0xE8, 0xF0)),
                BorderBrush = DragHoverBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                IsHitTestVisible = false,
                Opacity = 0.85,
                Child = new TextBlock
                {
                    Text = _draggedNode.Name,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0)
                }
            };
            Canvas.SetZIndex(_dragGhostBorder, 9999);
        }
        else
        {
            if (_dragGhostBorder.Child is TextBlock tb)
                tb.Text = _draggedNode.Name;
        }

        if (!RootCanvas.Children.Contains(_dragGhostBorder))
            RootCanvas.Children.Add(_dragGhostBorder);

        _dragGhostBorder.Visibility = Visibility.Visible;

        // Show top banner to drop to root only if the node actually has a parent (i.e. is not already a root folder)
        if (_draggedNode.Parent != null)
        {
            RootDropBanner.Visibility = Visibility.Visible;
        }
    }

    private void UpdateDragHoverTarget(Point canvasPos)
    {
        if (_dragGhostBorder != null && _dragGhostBorder.Visibility == Visibility.Visible)
        {
            Canvas.SetLeft(_dragGhostBorder, canvasPos.X + 12);
            Canvas.SetTop(_dragGhostBorder, canvasPos.Y + 12);
        }

        // Check if cursor is over the top RootDropBanner
        if (RootDropBanner.Visibility == Visibility.Visible)
        {
            Point gridPos = Mouse.GetPosition(MainGrid);
            double bannerLeft = (MainGrid.ActualWidth / 2.0) - 180;
            double bannerRight = (MainGrid.ActualWidth / 2.0) + 180;
            _isHoveringRootDropZone = (gridPos.Y >= 0 && gridPos.Y <= 75 && gridPos.X >= bannerLeft && gridPos.X <= bannerRight);

            if (_isHoveringRootDropZone)
            {
                RootDropBanner.Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88));
                RootDropBanner.BorderBrush = Brushes.White;
                RootDropBanner.BorderThickness = new Thickness(2.5);
            }
            else
            {
                RootDropBanner.Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E));
                RootDropBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88));
                RootDropBanner.BorderThickness = new Thickness(1.5);
            }
        }
        else
        {
            _isHoveringRootDropZone = false;
        }

        FolderNode? hitNode = null;
        Border? hitBox = null;

        // VisualTreeHelper.HitTest for fast O(1) hit testing of diagram boxes
        VisualTreeHelper.HitTest(RootCanvas, null, result =>
        {
            if (result.VisualHit is DependencyObject hitObj)
            {
                DependencyObject? curr = hitObj;
                while (curr != null && curr != RootCanvas)
                {
                    if (curr is Border b && _boxMap.TryGetValue(b, out var node))
                    {
                        if (!ReferenceEquals(node, _draggedNode))
                        {
                            hitBox = b;
                            hitNode = node;
                            return HitTestResultBehavior.Stop;
                        }
                    }
                    curr = VisualTreeHelper.GetParent(curr);
                }
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(canvasPos));

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

    private static (Color Fill, Color Border) GetPalette(int depth) => Palette[depth % Palette.Length];

    public void UpdateMiniMapViewport()
    {
        if (MiniMapBorder == null || MiniMapViewportRect == null) return;
        if (MiniMapBorder.Visibility != Visibility.Visible || RootCanvas.Width <= 0 || RootCanvas.Height <= 0)
        {
            MiniMapViewportRect.Visibility = Visibility.Collapsed;
            return;
        }

        MiniMapViewportRect.Visibility = Visibility.Visible;

        double canvasW = RootCanvas.Width;
        double canvasH = RootCanvas.Height;
        double mapW = 160.0;
        double mapH = 110.0;

        double scale = Math.Min(mapW / canvasW, mapH / canvasH);
        double previewW = canvasW * scale;
        double previewH = canvasH * scale;

        double offsetX = (mapW - previewW) / 2.0;
        double offsetY = (mapH - previewH) / 2.0;

        double zoom = ChartScale.ScaleX;
        double viewportW = ChartScrollViewer.ViewportWidth;
        double viewportH = ChartScrollViewer.ViewportHeight;

        double unscaledLeft = ChartScrollViewer.HorizontalOffset / zoom;
        double unscaledTop = ChartScrollViewer.VerticalOffset / zoom;
        double unscaledWidth = viewportW / zoom;
        double unscaledHeight = viewportH / zoom;

        double rectLeft = offsetX + unscaledLeft * scale;
        double rectTop = offsetY + unscaledTop * scale;
        double rectWidth = unscaledWidth * scale;
        double rectHeight = unscaledHeight * scale;

        rectLeft = Math.Clamp(rectLeft, offsetX, offsetX + previewW);
        rectTop = Math.Clamp(rectTop, offsetY, offsetY + previewH);
        rectWidth = Math.Clamp(rectWidth, 8, mapW);
        rectHeight = Math.Clamp(rectHeight, 8, mapH);

        Canvas.SetLeft(MiniMapViewportRect, rectLeft);
        Canvas.SetTop(MiniMapViewportRect, rectTop);
        MiniMapViewportRect.Width = rectWidth;
        MiniMapViewportRect.Height = rectHeight;
    }

    private void MiniMapCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isMiniMapDragging = true;
        MiniMapCanvas.CaptureMouse();
        ScrollFromMiniMapPos(e.GetPosition(MiniMapCanvas));
        e.Handled = true;
    }

    private void MiniMapCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isMiniMapDragging)
        {
            ScrollFromMiniMapPos(e.GetPosition(MiniMapCanvas));
            e.Handled = true;
        }
    }

    private void MiniMapCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isMiniMapDragging)
        {
            _isMiniMapDragging = false;
            MiniMapCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ScrollFromMiniMapPos(Point mapPos)
    {
        if (RootCanvas.Width <= 0 || RootCanvas.Height <= 0) return;

        double canvasW = RootCanvas.Width;
        double canvasH = RootCanvas.Height;
        double mapW = 160.0;
        double mapH = 110.0;

        double scale = Math.Min(mapW / canvasW, mapH / canvasH);
        double previewW = canvasW * scale;
        double previewH = canvasH * scale;

        double offsetX = (mapW - previewW) / 2.0;
        double offsetY = (mapH - previewH) / 2.0;

        double clickX = mapPos.X - offsetX;
        double clickY = mapPos.Y - offsetY;

        double unscaledTargetX = clickX / scale;
        double unscaledTargetY = clickY / scale;

        double zoom = ChartScale.ScaleX;
        double targetHOffset = (unscaledTargetX * zoom) - (ChartScrollViewer.ViewportWidth / 2.0);
        double targetVOffset = (unscaledTargetY * zoom) - (ChartScrollViewer.ViewportHeight / 2.0);

        ChartScrollViewer.ScrollToHorizontalOffset(Math.Max(0, targetHOffset));
        ChartScrollViewer.ScrollToVerticalOffset(Math.Max(0, targetVOffset));
    }

    private void CloseMiniMap_Click(object sender, RoutedEventArgs e)
    {
        IsMiniMapVisible = false;
    }

    #region Export Diagram Features (PNG / SVG / PDF)

    private static string ToHexColor(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public RenderTargetBitmap RenderDiagramToBitmap(double dpiScale = 2.0)
    {
        if (RootCanvas.Width <= 0 || RootCanvas.Height <= 0)
            throw new InvalidOperationException("Diagram canvas is empty.");

        if (_dragGhostBorder != null)
            _dragGhostBorder.Visibility = Visibility.Collapsed;

        double oldScaleX = ChartScale.ScaleX;
        double oldScaleY = ChartScale.ScaleY;

        try
        {
            ChartScale.ScaleX = 1.0;
            ChartScale.ScaleY = 1.0;
            RootCanvas.UpdateLayout();

            double width = RootCanvas.Width;
            double height = RootCanvas.Height;

            int pixelWidth = (int)Math.Ceiling(width * dpiScale);
            int pixelHeight = (int)Math.Ceiling(height * dpiScale);

            var drawingVisual = new DrawingVisual();
            using (DrawingContext dc = drawingVisual.RenderOpen())
            {
                var bgBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
                dc.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));

                var visualBrush = new VisualBrush(RootCanvas)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                };
                dc.DrawRectangle(visualBrush, null, new Rect(0, 0, width, height));
            }

            var rtb = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                96 * dpiScale,
                96 * dpiScale,
                PixelFormats.Pbgra32);

            rtb.Render(drawingVisual);
            return rtb;
        }
        finally
        {
            ChartScale.ScaleX = oldScaleX;
            ChartScale.ScaleY = oldScaleY;
            RootCanvas.UpdateLayout();
        }
    }

    public void ExportToPng(string filePath)
    {
        var rtb = RenderDiagramToBitmap(2.0);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }

    public void ExportToSvg(string filePath)
    {
        if (_lastRoots.Count == 0 || RootCanvas.Width <= 0 || RootCanvas.Height <= 0)
            throw new InvalidOperationException("Diagram canvas is empty.");

        double width = RootCanvas.Width;
        double height = RootCanvas.Height;
        bool isVertical = LayoutDirection == OrgChartLayoutDirection.Vertical;

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

        const double siblingWidth = BoxWidth + 20;
        const double levelHeight = BoxHeight + 46;

        (double X, double Y) GetParentConnectionPoint(double row, int depth)
        {
            if (isVertical)
            {
                double x = ChartPadding + row * siblingWidth + siblingWidth / 2.0;
                double y = ChartPadding + depth * levelHeight + BoxHeight;
                return (x, y);
            }
            else
            {
                double x = ChartPadding + depth * (BoxWidth + ColumnGap) + BoxWidth;
                double y = ChartPadding + row * RowHeight + RowHeight / 2.0;
                return (x, y);
            }
        }

        (double X, double Y) GetChildConnectionPoint(double row, int depth)
        {
            if (isVertical)
            {
                double x = ChartPadding + row * siblingWidth + siblingWidth / 2.0;
                double y = ChartPadding + depth * levelHeight;
                return (x, y);
            }
            else
            {
                double x = ChartPadding + depth * (BoxWidth + ColumnGap);
                double y = ChartPadding + row * RowHeight + RowHeight / 2.0;
                return (x, y);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width:F1}\" height=\"{height:F1}\" viewBox=\"0 0 {width:F1} {height:F1}\">");
        sb.AppendLine("  <!-- Background -->");
        sb.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"#0F172A\"/>");
        sb.AppendLine("  <!-- Connectors -->");

        void DrawSvgConnectors(FolderNode node)
        {
            if (!positions.TryGetValue(node, out var parentPos)) return;

            foreach (var child in node.Children)
            {
                if (!positions.TryGetValue(child, out var childPos)) continue;

                var (px, py) = GetParentConnectionPoint(parentPos.Row, parentPos.Depth);
                var (cx, cy) = GetChildConnectionPoint(childPos.Row, childPos.Depth);

                string strokeColor = ToHexColor(GetPalette(childPos.Depth).Border);

                if (isVertical)
                {
                    double midY = (py + cy) / 2.0;
                    sb.AppendLine($"  <path d=\"M {px:F1},{py:F1} L {px:F1},{midY:F1} L {cx:F1},{midY:F1} L {cx:F1},{cy:F1}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                }
                else
                {
                    double midX = (px + cx) / 2.0;
                    sb.AppendLine($"  <path d=\"M {px:F1},{py:F1} L {midX:F1},{py:F1} L {midX:F1},{cy:F1} L {cx:F1},{cy:F1}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                }

                DrawSvgConnectors(child);
            }
        }

        foreach (var root in _lastRoots)
            DrawSvgConnectors(root);

        sb.AppendLine("  <!-- Nodes -->");
        foreach (var (node, pos) in positions)
        {
            var (fill, border) = GetPalette(pos.Depth);
            bool isSelected = ReferenceEquals(node, _lastSelected);
            bool isMatch = node.IsMatchingSearch;

            Color fillColor = isMatch ? Color.FromRgb(0xFE, 0xF0, 0x8A) : fill;
            Color borderColor = isSelected ? Color.FromRgb(0x0F, 0x76, 0x6E) : (isMatch ? Color.FromRgb(0xD9, 0x77, 0x06) : border);
            double borderWidth = (isSelected || isMatch) ? 2.5 : 1.0;

            double boxX, boxY;
            if (isVertical)
            {
                boxX = ChartPadding + pos.Row * siblingWidth + siblingWidth / 2.0 - BoxWidth / 2.0;
                boxY = ChartPadding + pos.Depth * levelHeight;
            }
            else
            {
                boxX = ChartPadding + pos.Depth * (BoxWidth + ColumnGap);
                boxY = ChartPadding + pos.Row * RowHeight + RowHeight / 2.0 - BoxHeight / 2.0;
            }

            string fillHex = ToHexColor(fillColor);
            string borderHex = ToHexColor(borderColor);
            string escapedName = SecurityElement.Escape(node.Name) ?? string.Empty;

            sb.AppendLine("  <g>");
            sb.AppendLine($"    <rect x=\"{boxX:F1}\" y=\"{boxY:F1}\" width=\"{BoxWidth}\" height=\"{BoxHeight}\" rx=\"6\" ry=\"6\" fill=\"{fillHex}\" stroke=\"{borderHex}\" stroke-width=\"{borderWidth:F1}\"/>");
            sb.AppendLine($"    <text x=\"{boxX + BoxWidth / 2.0:F1}\" y=\"{boxY + BoxHeight / 2.0 + 4:F1}\" fill=\"#000000\" font-family=\"Segoe UI, system-ui, sans-serif\" font-size=\"11.5\" font-weight=\"600\" text-anchor=\"middle\">{escapedName}</text>");
            sb.AppendLine("  </g>");
        }

        sb.AppendLine("</svg>");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    public void ExportToPdf(string filePath)
    {
        var rtb = RenderDiagramToBitmap(2.0);

        using var ms = new MemoryStream();
        var jpegEncoder = new JpegBitmapEncoder { QualityLevel = 95 };
        jpegEncoder.Frames.Add(BitmapFrame.Create(rtb));
        jpegEncoder.Save(ms);
        byte[] jpegBytes = ms.ToArray();

        double widthPt = RootCanvas.Width * 72.0 / 96.0;
        double heightPt = RootCanvas.Height * 72.0 / 96.0;

        WritePdfWithJpegImage(filePath, jpegBytes, rtb.PixelWidth, rtb.PixelHeight, widthPt, heightPt);
    }

    private static void WritePdfWithJpegImage(string filePath, byte[] jpegBytes, int pixelWidth, int pixelHeight, double widthPt, double heightPt)
    {
        using var fileStream = File.Create(filePath);
        using var writer = new StreamWriter(fileStream, Encoding.ASCII);

        var offsets = new List<long>();

        void WriteHeader()
        {
            writer.Write("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
            writer.Flush();
        }

        void RecordObj()
        {
            fileStream.Flush();
            offsets.Add(fileStream.Position);
        }

        WriteHeader();

        // Obj 1: Catalog
        RecordObj();
        writer.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        writer.Flush();

        // Obj 2: Pages
        RecordObj();
        writer.Write("2 0 obj\n<< /Type /Pages /Count 1 /Kids [ 3 0 R ] >>\nendobj\n");
        writer.Flush();

        // Obj 3: Page
        RecordObj();
        writer.Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPt:F2} {heightPt:F2}] /Resources << /XObject << /Img1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");
        writer.Flush();

        // Obj 4: Image XObject
        RecordObj();
        writer.Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pixelWidth} /Height {pixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
        writer.Flush();
        fileStream.Write(jpegBytes, 0, jpegBytes.Length);
        writer.Write("\nendstream\nendobj\n");
        writer.Flush();

        // Obj 5: Page Content Stream
        string contentStream = $"q\n{widthPt:F2} 0 0 {heightPt:F2} 0 0 cm\n/Img1 Do\nQ\n";
        byte[] contentBytes = Encoding.ASCII.GetBytes(contentStream);

        RecordObj();
        writer.Write($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n{contentStream}endstream\nendobj\n");
        writer.Flush();

        // XRef table
        long xrefPos = fileStream.Position;
        writer.Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            writer.Write($"{offset:D10} 00000 n \n");
        }

        // Trailer
        writer.Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
        writer.Flush();
    }

    #endregion
}

