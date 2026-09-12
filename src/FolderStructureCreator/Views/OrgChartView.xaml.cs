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
using System.Globalization;

namespace FolderStructureCreator.Views;

public enum OrgChartLayoutDirection
{
    Horizontal,
    Vertical
}

public enum OrgChartConnectorStyle
{
    Orthogonal,
    Curved,
    Straight
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

    public static readonly DependencyProperty ConnectorStyleProperty =
        DependencyProperty.Register(
            nameof(ConnectorStyle),
            typeof(OrgChartConnectorStyle),
            typeof(OrgChartView),
            new FrameworkPropertyMetadata(OrgChartConnectorStyle.Orthogonal, OnConnectorStyleChanged));

    public OrgChartConnectorStyle ConnectorStyle
    {
        get => (OrgChartConnectorStyle)GetValue(ConnectorStyleProperty);
        set => SetValue(ConnectorStyleProperty, value);
    }

    private static void OnConnectorStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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
    private const double MinVerticalSpacing = 12; // vertical spacing between adjacent boxes
    private const double MinHorizontalSpacing = 16;
    private const double ChartPadding = 24;

    // Depth-based palettes harmonious with Dark, Light, and High Contrast themes
    private static readonly (Color Fill, Color Border, Color Text)[] DarkPalette =
    {
        (Color.FromRgb(0x13, 0x38, 0x48), Color.FromRgb(0x2D, 0xD4, 0xBF), Color.FromRgb(0xF8, 0xFA, 0xFC)), // depth 0 - teal dark slate
        (Color.FromRgb(0x1E, 0x2D, 0x4A), Color.FromRgb(0x60, 0xA5, 0xFA), Color.FromRgb(0xF8, 0xFA, 0xFC)), // depth 1 - soft navy slate
        (Color.FromRgb(0x15, 0x3E, 0x35), Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0xF8, 0xFA, 0xFC)), // depth 2 - emerald slate
        (Color.FromRgb(0x38, 0x2E, 0x1E), Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0xF8, 0xFA, 0xFC)), // depth 3 - amber slate
        (Color.FromRgb(0x2D, 0x23, 0x45), Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromRgb(0xF8, 0xFA, 0xFC)), // depth 4 - violet slate
    };

    private static readonly (Color Fill, Color Border, Color Text)[] LightPalette =
    {
        (Color.FromRgb(0xCC, 0xFB, 0xF1), Color.FromRgb(0x0D, 0x94, 0x88), Color.FromRgb(0x0F, 0x17, 0x2A)), // depth 0 - teal
        (Color.FromRgb(0xDB, 0xEA, 0xFE), Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x0F, 0x17, 0x2A)), // depth 1 - blue
        (Color.FromRgb(0xD1, 0xFA, 0xE5), Color.FromRgb(0x05, 0x96, 0x69), Color.FromRgb(0x0F, 0x17, 0x2A)), // depth 2 - emerald
        (Color.FromRgb(0xFE, 0xF3, 0xC7), Color.FromRgb(0xD9, 0x77, 0x06), Color.FromRgb(0x0F, 0x17, 0x2A)), // depth 3 - amber
        (Color.FromRgb(0xED, 0xE9, 0xFE), Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0x0F, 0x17, 0x2A)), // depth 4 - violet
    };

    private static readonly (Color Fill, Color Border, Color Text)[] HighContrastPalette =
    {
        (Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0x00, 0xFF, 0xFF), Color.FromRgb(0xFF, 0xFF, 0xFF)), // depth 0 - cyan
        (Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(0xFF, 0xFF, 0xFF)), // depth 1 - yellow
        (Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0x00, 0xFF, 0x00), Color.FromRgb(0xFF, 0xFF, 0xFF)), // depth 2 - green
        (Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xFF, 0x80, 0x00), Color.FromRgb(0xFF, 0xFF, 0xFF)), // depth 3 - orange
        (Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xFF, 0x00, 0xFF), Color.FromRgb(0xFF, 0xFF, 0xFF)), // depth 4 - magenta
    };

    private static bool IsDarkTheme()
    {
        var effective = FolderStructureCreator.Services.ThemeService.GetEffectiveTheme(FolderStructureCreator.Services.ThemeService.CurrentTheme);
        return effective == FolderStructureCreator.Services.AppTheme.Dark;
    }

    private static bool IsHighContrastTheme()
    {
        var effective = FolderStructureCreator.Services.ThemeService.GetEffectiveTheme(FolderStructureCreator.Services.ThemeService.CurrentTheme);
        return effective == FolderStructureCreator.Services.AppTheme.HighContrast;
    }

    private static (Color Fill, Color Border, Color Text) GetPalette(int depth)
    {
        if (IsHighContrastTheme())
            return HighContrastPalette[depth % HighContrastPalette.Length];
        return IsDarkTheme()
            ? DarkPalette[depth % DarkPalette.Length]
            : LightPalette[depth % LightPalette.Length];
    }

    private static Brush GetSelectedBorderBrush()
    {
        if (IsHighContrastTheme())
            return new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x00));
        return IsDarkTheme()
            ? new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF))
            : new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88));
    }

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

    public OrgChartView()
    {
        InitializeComponent();

        ChartScrollViewer.PreviewMouseWheel += ChartScrollViewer_PreviewMouseWheel;
        ChartScrollViewer.PreviewMouseDown += ChartScrollViewer_PreviewMouseDown;
        ChartScrollViewer.PreviewMouseMove += ChartScrollViewer_PreviewMouseMove;
        ChartScrollViewer.PreviewMouseUp += ChartScrollViewer_PreviewMouseUp;
        ChartScrollViewer.SizeChanged += (_, _) => UpdateContainerAlignment();

        FolderStructureCreator.Services.ThemeService.ThemeChanged += _ => RenderInternal();
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

            double boxWidthScaled = (box.Width > 0 ? box.Width : BoxWidth) * scale;
            double boxHeightScaled = (box.Height > 0 ? box.Height : BoxHeight) * scale;

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

    public (double Zoom, double Horizontal, double Vertical) CaptureViewport() =>
        (ChartScale.ScaleX, ChartScrollViewer.HorizontalOffset, ChartScrollViewer.VerticalOffset);

    public void RestoreViewport((double Zoom, double Horizontal, double Vertical) viewport)
    {
        SetZoom(viewport.Zoom);
        UpdateLayout();
        UpdateContainerAlignment();
        UpdateLayout();
        ChartScrollViewer.ScrollToHorizontalOffset(viewport.Horizontal);
        ChartScrollViewer.ScrollToVerticalOffset(viewport.Vertical);
    }

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

            double padding = 48;
            var widthScale = (ChartScrollViewer.ViewportWidth - padding) / RootCanvas.Width;
            var heightScale = (ChartScrollViewer.ViewportHeight - padding) / RootCanvas.Height;
            // Frame visible hierarchy: if tree is smaller than viewport, keep 100% scale (never artificially enlarge beyond 1.0)
            double fitZoom = Math.Clamp(Math.Min(widthScale, heightScale), MinZoom, 1.0);

            SetZoom(fitZoom);

            double scaledWidth = RootCanvas.Width * ChartScale.ScaleX;
            double scaledHeight = RootCanvas.Height * ChartScale.ScaleY;

            double targetX = Math.Max(0, (scaledWidth - ChartScrollViewer.ViewportWidth) / 2.0);
            double targetY = Math.Max(0, (scaledHeight - ChartScrollViewer.ViewportHeight) / 2.0);

            ChartScrollViewer.ScrollToHorizontalOffset(targetX);
            ChartScrollViewer.ScrollToVerticalOffset(targetY);
            UpdateContainerAlignment();
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

    /// <summary>Scales chart to comfortably fit the horizontal viewport width, eliminating right-side empty space.</summary>
    public void FitToWidth()
    {
        void PerformFitWidth()
        {
            if (RootCanvas.Width <= 0 || ChartScrollViewer.ViewportWidth <= 0)
                return;

            double padding = 48;
            var widthScale = (ChartScrollViewer.ViewportWidth - padding) / RootCanvas.Width;
            double fitZoom = Math.Clamp(widthScale, MinZoom, 1.0);

            SetZoom(fitZoom);

            double scaledWidth = RootCanvas.Width * ChartScale.ScaleX;
            double targetX = Math.Max(0, (scaledWidth - ChartScrollViewer.ViewportWidth) / 2.0);

            ChartScrollViewer.ScrollToHorizontalOffset(targetX);
            ChartScrollViewer.ScrollToVerticalOffset(0);
            UpdateContainerAlignment();
        }

        if (ChartScrollViewer.ViewportWidth > 0)
        {
            PerformFitWidth();
        }
        else
        {
            Dispatcher.BeginInvoke(PerformFitWidth, System.Windows.Threading.DispatcherPriority.Loaded);
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
        UpdateContainerAlignment();
    }

    private void UpdateContainerAlignment()
    {
        if (CanvasContainer == null) return;
        double scaledW = RootCanvas.Width * ChartScale.ScaleX;
        double scaledH = RootCanvas.Height * ChartScale.ScaleY;
        CanvasContainer.HorizontalAlignment = (ChartScrollViewer.ViewportWidth > 0 && scaledW < ChartScrollViewer.ViewportWidth)
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;
        CanvasContainer.VerticalAlignment = (ChartScrollViewer.ViewportHeight > 0 && scaledH < ChartScrollViewer.ViewportHeight)
            ? VerticalAlignment.Center
            : VerticalAlignment.Top;
    }

    private sealed class NodeLayoutInfo
    {
        public FolderNode Node { get; init; } = null!;
        public int Depth { get; init; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double CenterX => X + Width / 2.0;
        public double CenterY => Y + Height / 2.0;
    }

    private static (double Width, double Height) GetNodeDimensions(FolderNode node, int depth)
    {
        double height = node.HasDiffBadge ? 36.0 : BoxHeight;
        return (BoxWidth, height);
    }

    private static void ShiftSubtree(FolderNode node, double deltaX, double deltaY, Dictionary<FolderNode, NodeLayoutInfo> map)
    {
        if (map.TryGetValue(node, out var info))
        {
            info.X += deltaX;
            info.Y += deltaY;
        }
        if (node.IsExpanded)
        {
            foreach (var child in node.Children)
            {
                if (map.ContainsKey(child))
                {
                    ShiftSubtree(child, deltaX, deltaY, map);
                }
            }
        }
    }

    private static Dictionary<FolderNode, NodeLayoutInfo> ComputeLayout(IReadOnlyList<FolderNode> roots, bool isVertical)
    {
        var layoutMap = new Dictionary<FolderNode, NodeLayoutInfo>();
        if (roots.Count == 0) return layoutMap;

        if (!isVertical)
        {
            // HORIZONTAL LAYOUT (Left to right dendrogram)
            double currentY = ChartPadding;

            void LayoutNodeH(FolderNode node, int depth)
            {
                var (w, h) = GetNodeDimensions(node, depth);
                var info = new NodeLayoutInfo
                {
                    Node = node,
                    Depth = depth,
                    Width = w,
                    Height = h,
                    X = ChartPadding + depth * (BoxWidth + ColumnGap)
                };
                layoutMap[node] = info;

                var visibleChildren = (node.IsExpanded && node.Children.Count > 0)
                    ? node.Children
                    : (IReadOnlyList<FolderNode>)Array.Empty<FolderNode>();

                if (visibleChildren.Count == 0)
                {
                    info.Y = currentY;
                    currentY += h + MinVerticalSpacing;
                }
                else
                {
                    foreach (var child in visibleChildren)
                    {
                        LayoutNodeH(child, depth + 1);
                    }

                    double firstCenterY = layoutMap[visibleChildren[0]].CenterY;
                    double lastCenterY = layoutMap[visibleChildren[^1]].CenterY;
                    double desiredCenterY = (firstCenterY + lastCenterY) / 2.0;
                    info.Y = desiredCenterY - h / 2.0;
                }
            }

            foreach (var root in roots)
            {
                LayoutNodeH(root, 0);
            }

            // Guarantee no two boxes overlap at the same depth column
            bool adjusted = true;
            int passes = 0;
            while (adjusted && passes++ < 30)
            {
                adjusted = false;
                var byDepth = layoutMap.Values.GroupBy(x => x.Depth).OrderBy(g => g.Key);
                foreach (var group in byDepth)
                {
                    var sorted = group.OrderBy(x => x.Y).ToList();
                    for (int i = 0; i < sorted.Count - 1; i++)
                    {
                        var a = sorted[i];
                        var b = sorted[i + 1];
                        double requiredMinY = a.Y + a.Height + MinVerticalSpacing;
                        if (b.Y < requiredMinY)
                        {
                            double shift = requiredMinY - b.Y;
                            ShiftSubtree(b.Node, 0, shift, layoutMap);
                            adjusted = true;
                        }
                    }
                }

                // Re-center parents over visible children
                foreach (var info in layoutMap.Values)
                {
                    if (info.Node.IsExpanded && info.Node.Children.Count > 0)
                    {
                        var children = info.Node.Children.Where(c => layoutMap.ContainsKey(c)).ToList();
                        if (children.Count > 0)
                        {
                            double firstCenterY = layoutMap[children[0]].CenterY;
                            double lastCenterY = layoutMap[children[^1]].CenterY;
                            double desiredCenterY = (firstCenterY + lastCenterY) / 2.0;
                            double desiredY = desiredCenterY - info.Height / 2.0;
                            if (Math.Abs(desiredY - info.Y) > 0.01)
                            {
                                info.Y = desiredY;
                                adjusted = true;
                            }
                        }
                    }
                }
            }

            // Normalize so top-most node is anchored cleanly at ChartPadding
            if (layoutMap.Count > 0)
            {
                double minY = layoutMap.Values.Min(i => i.Y);
                double offsetY = ChartPadding - minY;
                if (Math.Abs(offsetY) > 0.01)
                {
                    foreach (var info in layoutMap.Values)
                        info.Y += offsetY;
                }
            }
        }
        else
        {
            // VERTICAL LAYOUT (Top to bottom tree)
            const double levelHeight = BoxHeight + 46;
            double currentX = ChartPadding;

            void LayoutNodeV(FolderNode node, int depth)
            {
                var (w, h) = GetNodeDimensions(node, depth);
                var info = new NodeLayoutInfo
                {
                    Node = node,
                    Depth = depth,
                    Width = w,
                    Height = h,
                    Y = ChartPadding + depth * levelHeight
                };
                layoutMap[node] = info;

                var visibleChildren = (node.IsExpanded && node.Children.Count > 0)
                    ? node.Children
                    : (IReadOnlyList<FolderNode>)Array.Empty<FolderNode>();

                if (visibleChildren.Count == 0)
                {
                    info.X = currentX;
                    currentX += w + MinHorizontalSpacing;
                }
                else
                {
                    foreach (var child in visibleChildren)
                    {
                        LayoutNodeV(child, depth + 1);
                    }

                    double firstCenterX = layoutMap[visibleChildren[0]].CenterX;
                    double lastCenterX = layoutMap[visibleChildren[^1]].CenterX;
                    double desiredCenterX = (firstCenterX + lastCenterX) / 2.0;
                    info.X = desiredCenterX - w / 2.0;
                }
            }

            foreach (var root in roots)
            {
                LayoutNodeV(root, 0);
            }

            // Guarantee no two boxes overlap at the same depth level horizontally
            bool adjusted = true;
            int passes = 0;
            while (adjusted && passes++ < 30)
            {
                adjusted = false;
                var byDepth = layoutMap.Values.GroupBy(x => x.Depth).OrderBy(g => g.Key);
                foreach (var group in byDepth)
                {
                    var sorted = group.OrderBy(x => x.X).ToList();
                    for (int i = 0; i < sorted.Count - 1; i++)
                    {
                        var a = sorted[i];
                        var b = sorted[i + 1];
                        double requiredMinX = a.X + a.Width + MinHorizontalSpacing;
                        if (b.X < requiredMinX)
                        {
                            double shift = requiredMinX - b.X;
                            ShiftSubtree(b.Node, shift, 0, layoutMap);
                            adjusted = true;
                        }
                    }
                }

                // Re-center parents horizontally over visible children
                foreach (var info in layoutMap.Values)
                {
                    if (info.Node.IsExpanded && info.Node.Children.Count > 0)
                    {
                        var children = info.Node.Children.Where(c => layoutMap.ContainsKey(c)).ToList();
                        if (children.Count > 0)
                        {
                            double firstCenterX = layoutMap[children[0]].CenterX;
                            double lastCenterX = layoutMap[children[^1]].CenterX;
                            double desiredCenterX = (firstCenterX + lastCenterX) / 2.0;
                            double desiredX = desiredCenterX - info.Width / 2.0;
                            if (Math.Abs(desiredX - info.X) > 0.01)
                            {
                                info.X = desiredX;
                                adjusted = true;
                            }
                        }
                    }
                }
            }

            // Normalize so left-most node is anchored cleanly at ChartPadding
            if (layoutMap.Count > 0)
            {
                double minX = layoutMap.Values.Min(i => i.X);
                double offsetX = ChartPadding - minX;
                if (Math.Abs(offsetX) > 0.01)
                {
                    foreach (var info in layoutMap.Values)
                        info.X += offsetX;
                }
            }
        }

        return layoutMap;
    }

    private void RenderInternal()
    {
        double prevHOffset = ChartScrollViewer.HorizontalOffset;
        double prevVOffset = ChartScrollViewer.VerticalOffset;

        RootCanvas.Children.Clear();
        _boxMap.Clear();

        if (_lastRoots.Count == 0)
        {
            RootCanvas.Width = 0;
            RootCanvas.Height = 0;
            return;
        }

        bool isVertical = LayoutDirection == OrgChartLayoutDirection.Vertical;
        var layoutMap = ComputeLayout(_lastRoots, isVertical);

        double maxX = layoutMap.Values.Max(info => info.X + info.Width);
        double maxY = layoutMap.Values.Max(info => info.Y + info.Height);

        RootCanvas.Width = Math.Max(maxX + ChartPadding, 100);
        RootCanvas.Height = Math.Max(maxY + ChartPadding, 100);

        // ---- Connectors first, so node boxes visually sit on top of the lines. ----
        void DrawConnectors(FolderNode node)
        {
            if (!node.IsExpanded || !layoutMap.TryGetValue(node, out var parentLayout)) return;

            foreach (var child in node.Children)
            {
                if (!layoutMap.TryGetValue(child, out var childLayout)) continue;

                Point startPoint = isVertical
                    ? new Point(parentLayout.CenterX, parentLayout.Y + parentLayout.Height)
                    : new Point(parentLayout.X + parentLayout.Width, parentLayout.CenterY);

                Point endPoint = isVertical
                    ? new Point(childLayout.CenterX, childLayout.Y)
                    : new Point(childLayout.X, childLayout.CenterY);

                var figure = new PathFigure { StartPoint = startPoint };

                switch (ConnectorStyle)
                {
                    case OrgChartConnectorStyle.Straight:
                        figure.Segments.Add(new LineSegment(endPoint, true));
                        break;

                    case OrgChartConnectorStyle.Curved:
                        if (isVertical)
                        {
                            double dy = (endPoint.Y - startPoint.Y) * 0.5;
                            var c1 = new Point(startPoint.X, startPoint.Y + dy);
                            var c2 = new Point(endPoint.X, endPoint.Y - dy);
                            figure.Segments.Add(new BezierSegment(c1, c2, endPoint, true));
                        }
                        else
                        {
                            double dx = (endPoint.X - startPoint.X) * 0.5;
                            var c1 = new Point(startPoint.X + dx, startPoint.Y);
                            var c2 = new Point(endPoint.X - dx, endPoint.Y);
                            figure.Segments.Add(new BezierSegment(c1, c2, endPoint, true));
                        }
                        break;

                    case OrgChartConnectorStyle.Orthogonal:
                    default:
                        if (isVertical)
                        {
                            double midY = (startPoint.Y + endPoint.Y) / 2.0;
                            figure.Segments.Add(new LineSegment(new Point(startPoint.X, midY), true));
                            figure.Segments.Add(new LineSegment(new Point(endPoint.X, midY), true));
                            figure.Segments.Add(new LineSegment(endPoint, true));
                        }
                        else
                        {
                            double midX = (startPoint.X + endPoint.X) / 2.0;
                            figure.Segments.Add(new LineSegment(new Point(midX, startPoint.Y), true));
                            figure.Segments.Add(new LineSegment(new Point(midX, endPoint.Y), true));
                            figure.Segments.Add(new LineSegment(endPoint, true));
                        }
                        break;
                }

                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);

                RootCanvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = geometry,
                    Stroke = new SolidColorBrush(GetPalette(childLayout.Depth).Border),
                    StrokeThickness = 1.6
                });

                DrawConnectors(child);
            }
        }

        foreach (var root in _lastRoots)
            DrawConnectors(root);

        // ---- Node boxes. ----
        bool isDark = IsDarkTheme();

        foreach (var info in layoutMap.Values)
        {
            var node = info.Node;
            var (fill, border, textCol) = GetPalette(info.Depth);
            bool isSelected = ReferenceEquals(node, _lastSelected);
            bool isMatch = node.IsMatchingSearch;

            Brush boxBackground = isMatch ? SearchMatchBackgroundBrush : new SolidColorBrush(fill);
            Brush boxBorderBrush = isSelected ? GetSelectedBorderBrush() : (isMatch ? SearchMatchBorderBrush : new SolidColorBrush(border));
            Brush textForeground = new SolidColorBrush(textCol);
            Brush badgeForeground = new SolidColorBrush(isDark ? Color.FromRgb(0x94, 0xA3, 0xB8) : Color.FromRgb(0x47, 0x55, 0x69));

            if (node.DiffStatus == NodeDiffStatus.MissingOnDisk)
            {
                boxBorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Emerald Green
                boxBackground = isDark
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B))
                    : new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5));
                textForeground = isDark
                    ? new SolidColorBrush(Color.FromRgb(0xEC, 0xFD, 0xF5))
                    : new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46));
                badgeForeground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
            }
            else if (node.DiffStatus == NodeDiffStatus.MatchesDisk)
            {
                boxBorderBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)); // Slate Neutral
                boxBackground = isDark
                    ? new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B))
                    : new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
                textForeground = isDark
                    ? new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9))
                    : new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
                badgeForeground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            }
            else if (node.DiffStatus == NodeDiffStatus.ExtraOnDisk)
            {
                boxBorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // Amber
                boxBackground = isDark
                    ? new SolidColorBrush(Color.FromRgb(0x45, 0x1A, 0x03))
                    : new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
                textForeground = isDark
                    ? new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7))
                    : new SolidColorBrush(Color.FromRgb(0x78, 0x35, 0x0F));
                badgeForeground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            }
            else if (isMatch)
            {
                textForeground = new SolidColorBrush(isDark ? Color.FromRgb(0xFE, 0xF0, 0x8A) : Color.FromRgb(0x85, 0x4D, 0x0E));
                if (isDark)
                {
                    boxBackground = new SolidColorBrush(Color.FromRgb(0x42, 0x20, 0x06));
                }
            }

            var boxStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var namePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(
                    8,
                    0,
                    (node.Children.Count > 0 && !isVertical ? 16 : 8),
                    (node.Children.Count > 0 && isVertical ? 8 : 0))
            };

            if (isSelected)
            {
                namePanel.Children.Add(new TextBlock
                {
                    Text = "● ",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = isDark ? new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)) : new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 2, 0)
                });
            }

            namePanel.Children.Add(new TextBlock
            {
                Text = node.Name,
                FontSize = 12.0,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = textForeground,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center
            });

            boxStack.Children.Add(namePanel);

            if (node.HasDiffBadge)
            {
                boxStack.Children.Add(new TextBlock
                {
                    Text = node.DiffBadgeText,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = badgeForeground,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            var box = new Border
            {
                Width = info.Width,
                Height = info.Height,
                Background = boxBackground,
                BorderBrush = boxBorderBrush,
                BorderThickness = new Thickness(isSelected ? 2.8 : ((isMatch || node.HasDiffBadge) ? 2.0 : 1.2)),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                ToolTip = node.Name,
                Focusable = true,
                FocusVisualStyle = null,
                Child = boxStack
            };

            if (isSelected)
            {
                box.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = isDark ? Color.FromRgb(0x2D, 0xD4, 0xBF) : Color.FromRgb(0x0D, 0x94, 0x88),
                    BlurRadius = 6,
                    ShadowDepth = 0,
                    Opacity = 0.3
                };
            }

            Brush originalBorder = boxBorderBrush;
            box.MouseEnter += (s, e) =>
            {
                if (!ReferenceEquals(node, _lastSelected))
                {
                    box.BorderBrush = isDark ? new SolidColorBrush(Color.FromArgb(0xDD, 0x5E, 0xEA, 0xD4)) : new SolidColorBrush(Color.FromArgb(0xDD, 0x14, 0xB8, 0xA6));
                }
            };
            box.MouseLeave += (s, e) =>
            {
                if (!ReferenceEquals(node, _lastSelected))
                {
                    box.BorderBrush = originalBorder;
                }
            };
            box.GotFocus += (s, e) =>
            {
                if (!ReferenceEquals(node, _lastSelected))
                {
                    box.BorderThickness = new Thickness(2.2);
                    box.BorderBrush = isDark ? new SolidColorBrush(Color.FromRgb(0x5E, 0xEA, 0xD4)) : new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88));
                }
            };
            box.LostFocus += (s, e) =>
            {
                if (!ReferenceEquals(node, _lastSelected))
                {
                    box.BorderThickness = new Thickness((isMatch || node.HasDiffBadge) ? 2.0 : 1.2);
                    box.BorderBrush = originalBorder;
                }
            };
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    NodeClicked?.Invoke(node);
                    e.Handled = true;
                }
                else if (e.Key == Key.F2)
                {
                    BeginRename(node, box);
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete)
                {
                    DeleteRequested?.Invoke(node);
                    e.Handled = true;
                }
            };

            Canvas.SetLeft(box, info.X);
            Canvas.SetTop(box, info.Y);

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

            var menu = new ContextMenu { PlacementTarget = box };
            var openInExplorerItem = new MenuItem
            {
                Header = "Open in Explorer",
                Icon = CreateMenuItemIcon("ExplorerGeometry")
            };
            openInExplorerItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                OpenInExplorerRequested?.Invoke(node);
            };

            var focusItem = new MenuItem
            {
                Header = "Focus Folder (Fit Selection)",
                Icon = CreateMenuItemIcon("FitGeometry")
            };
            focusItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                FitSelectedToView();
            };

            var addChildItem = new MenuItem
            {
                Header = "Add Child Folder",
                Icon = CreateMenuItemIcon("PlusGeometry")
            };
            addChildItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                AddChildRequested?.Invoke(node);
            };

            var addSiblingItem = new MenuItem
            {
                Header = "Add Sibling Folder",
                Icon = CreateMenuItemIcon("FolderGeometry")
            };
            addSiblingItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                AddSiblingRequested?.Invoke(node);
            };

            MenuItem? moveToRootItem = null;
            if (node.Parent != null)
            {
                moveToRootItem = new MenuItem
                {
                    Header = "Move to Root",
                    Icon = CreateMenuItemIcon("ArrowUpGeometry")
                };
                moveToRootItem.Click += (_, _) =>
                {
                    NodeClicked?.Invoke(node);
                    NodeMoved?.Invoke(node, null);
                };
            }

            var renameItem = new MenuItem
            {
                Header = "Rename",
                Icon = CreateMenuItemIcon("EditGeometry")
            };
            renameItem.Click += (_, _) =>
            {
                NodeClicked?.Invoke(node);
                BeginRename(node, box);
            };

            var deleteItem = new MenuItem
            {
                Header = "Delete",
                Icon = CreateMenuItemIcon("TrashGeometry", "BrushDanger")
            };
            deleteItem.SetResourceReference(MenuItem.ForegroundProperty, "BrushDanger");
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

            if (node.Children.Count > 0)
            {
                var toggleExpandItem = new MenuItem
                {
                    Header = node.IsExpanded ? "Collapse Subfolders" : $"Expand ({node.Children.Count} Subfolders)",
                    Icon = CreateMenuItemIcon(node.IsExpanded ? "CollapseGeometry" : "ExpandGeometry")
                };
                toggleExpandItem.Click += (_, _) =>
                {
                    NodeClicked?.Invoke(node);
                    node.IsExpanded = !node.IsExpanded;
                    RenderInternal();
                    StructureEdited?.Invoke();
                };
                menu.Items.Add(new Separator());
                menu.Items.Add(toggleExpandItem);
            }

            box.ContextMenu = menu;

            RootCanvas.Children.Add(box);

            if (node.Children.Count > 0)
            {
                bool expanded = node.IsExpanded;
                double badgeHeight = 22;
                double badgeWidth = expanded ? 22 : Math.Max(30, 16 + node.Children.Count.ToString().Length * 8);

                var badgeText = new TextBlock
                {
                    Text = expanded ? "−" : $"+{node.Children.Count}",
                    FontSize = expanded ? 12.0 : 10.0,
                    FontWeight = FontWeights.Bold,
                    Foreground = expanded
                        ? (isDark ? new SolidColorBrush(Color.FromRgb(0xCF, 0xFA, 0xFE)) : new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)))
                        : Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, expanded ? -1.5 : 0, 0, 0)
                };

                var badge = new Border
                {
                    Width = badgeWidth,
                    Height = badgeHeight,
                    CornerRadius = new CornerRadius(badgeHeight / 2.0),
                    Background = expanded
                        ? (isDark ? new SolidColorBrush(Color.FromRgb(0x13, 0x2B, 0x39)) : new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF6)))
                        : (isDark ? new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E)) : new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88))),
                    BorderBrush = expanded
                        ? (isDark ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)) : new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7)))
                        : (isDark ? new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)) : new SolidColorBrush(Color.FromRgb(0x14, 0xB8, 0xA6))),
                    BorderThickness = new Thickness(1.4),
                    Cursor = Cursors.Hand,
                    ToolTip = expanded
                        ? $"Click to collapse ({node.Children.Count} subfolders)"
                        : $"Click to expand ({node.Children.Count} subfolders)",
                    Child = badgeText
                };

                badge.MouseEnter += (_, _) => badge.Opacity = 0.82;
                badge.MouseLeave += (_, _) => badge.Opacity = 1.0;

                badge.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    node.IsExpanded = !node.IsExpanded;
                    RenderInternal();
                    StructureEdited?.Invoke();
                };

                double badgeLeft, badgeTop;
                if (isVertical)
                {
                    badgeLeft = info.CenterX - badgeWidth / 2.0;
                    badgeTop = info.Y + info.Height - (badgeHeight / 2.0);
                }
                else
                {
                    badgeLeft = info.X + info.Width - (badgeWidth / 2.0);
                    badgeTop = info.CenterY - (badgeHeight / 2.0);
                }

                Canvas.SetLeft(badge, badgeLeft);
                Canvas.SetTop(badge, badgeTop);
                Canvas.SetZIndex(badge, 50);

                RootCanvas.Children.Add(badge);
            }

            if (node.IsEditing)
            {
                BeginRename(node, box);
            }
        }

        if (prevHOffset > 0 || prevVOffset > 0)
        {
            ChartScrollViewer.ScrollToHorizontalOffset(prevHOffset);
            ChartScrollViewer.ScrollToVerticalOffset(prevVOffset);
        }

        UpdateContainerAlignment();
    }

    private void EnsureDragGhostCreated()
    {
        if (_draggedNode == null) return;

        int depth = 0;
        var p = _draggedNode.Parent;
        while (p != null) { depth++; p = p.Parent; }
        var (gw, gh) = GetNodeDimensions(_draggedNode, depth);

        if (_dragGhostBorder == null)
        {
            _dragGhostBorder = new Border
            {
                Width = gw,
                Height = gh,
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
            _dragGhostBorder.Width = gw;
            _dragGhostBorder.Height = gh;
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
            var (_, border, _) = GetPalette(0);
            _dragTargetBox.BorderBrush = isSelected ? GetSelectedBorderBrush() : new SolidColorBrush(border);
            _dragTargetBox.BorderThickness = new Thickness(isSelected ? 2.5 : 1.2);
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
            Width = box.Width,
            Height = box.Height,
            Text = node.Name,
            FontSize = 12.0,
            FontWeight = FontWeights.SemiBold,
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
                var bgBrush = (Application.Current.TryFindResource("BrushBg") as SolidColorBrush) ?? new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
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

        bool isVertical = LayoutDirection == OrgChartLayoutDirection.Vertical;
        var layoutMap = ComputeLayout(_lastRoots, isVertical);
        if (layoutMap.Count == 0)
            throw new InvalidOperationException("Diagram canvas is empty.");

        double width = RootCanvas.Width;
        double height = RootCanvas.Height;

        bool isDark = IsDarkTheme();
        string bgHex = isDark ? "#0B1E28" : "#FFFFFF";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width.ToString("F1", CultureInfo.InvariantCulture)}\" height=\"{height.ToString("F1", CultureInfo.InvariantCulture)}\" viewBox=\"0 0 {width.ToString("F1", CultureInfo.InvariantCulture)} {height.ToString("F1", CultureInfo.InvariantCulture)}\">");
        sb.AppendLine("  <!-- Background -->");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{bgHex}\"/>");
        sb.AppendLine("  <!-- Connectors -->");

        void DrawSvgConnectors(FolderNode node)
        {
            if (!node.IsExpanded || !layoutMap.TryGetValue(node, out var parentLayout)) return;

            foreach (var child in node.Children)
            {
                if (!layoutMap.TryGetValue(child, out var childLayout)) continue;

                Point startPoint = isVertical
                    ? new Point(parentLayout.CenterX, parentLayout.Y + parentLayout.Height)
                    : new Point(parentLayout.X + parentLayout.Width, parentLayout.CenterY);

                Point endPoint = isVertical
                    ? new Point(childLayout.CenterX, childLayout.Y)
                    : new Point(childLayout.X, childLayout.CenterY);

                string strokeColor = ToHexColor(GetPalette(childLayout.Depth).Border);

                switch (ConnectorStyle)
                {
                    case OrgChartConnectorStyle.Straight:
                        sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} L {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        break;

                    case OrgChartConnectorStyle.Curved:
                        if (isVertical)
                        {
                            double dy = (endPoint.Y - startPoint.Y) * 0.5;
                            Point c1 = new Point(startPoint.X, startPoint.Y + dy);
                            Point c2 = new Point(endPoint.X, endPoint.Y - dy);
                            sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} C {c1.X.ToString("F1", CultureInfo.InvariantCulture)},{c1.Y.ToString("F1", CultureInfo.InvariantCulture)} {c2.X.ToString("F1", CultureInfo.InvariantCulture)},{c2.Y.ToString("F1", CultureInfo.InvariantCulture)} {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        }
                        else
                        {
                            double dx = (endPoint.X - startPoint.X) * 0.5;
                            Point c1 = new Point(startPoint.X + dx, startPoint.Y);
                            Point c2 = new Point(endPoint.X - dx, endPoint.Y);
                            sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} C {c1.X.ToString("F1", CultureInfo.InvariantCulture)},{c1.Y.ToString("F1", CultureInfo.InvariantCulture)} {c2.X.ToString("F1", CultureInfo.InvariantCulture)},{c2.Y.ToString("F1", CultureInfo.InvariantCulture)} {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        }
                        break;

                    case OrgChartConnectorStyle.Orthogonal:
                    default:
                        if (isVertical)
                        {
                            double midY = (startPoint.Y + endPoint.Y) / 2.0;
                            sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} L {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{midY.ToString("F1", CultureInfo.InvariantCulture)} L {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{midY.ToString("F1", CultureInfo.InvariantCulture)} L {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        }
                        else
                        {
                            double midX = (startPoint.X + endPoint.X) / 2.0;
                            sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} L {midX.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} L {midX.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} L {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        }
                        break;
                }

                DrawSvgConnectors(child);
            }
        }

        foreach (var root in _lastRoots)
            DrawSvgConnectors(root);

        sb.AppendLine("  <!-- Nodes -->");
        foreach (var info in layoutMap.Values)
        {
            var node = info.Node;
            var (fill, border, textCol) = GetPalette(info.Depth);
            bool isSelected = ReferenceEquals(node, _lastSelected);
            bool isMatch = node.IsMatchingSearch;

            Color fillColor = isMatch ? (isDark ? Color.FromRgb(0x42, 0x20, 0x06) : Color.FromRgb(0xFE, 0xF0, 0x8A)) : fill;
            Color borderColor = isSelected
                ? (isDark ? Color.FromRgb(0x2D, 0xD4, 0xBF) : Color.FromRgb(0x0D, 0x94, 0x88))
                : (isMatch ? Color.FromRgb(0xD9, 0x77, 0x06) : border);
            Color textColor = isMatch ? (isDark ? Color.FromRgb(0xFE, 0xF0, 0x8A) : Color.FromRgb(0x85, 0x4D, 0x0E)) : textCol;
            double borderWidth = (isSelected || isMatch) ? 2.5 : 1.2;

            string fillHex = ToHexColor(fillColor);
            string borderHex = ToHexColor(borderColor);
            string textHex = ToHexColor(textColor);
            string escapedName = SecurityElement.Escape(node.Name) ?? string.Empty;

            sb.AppendLine("  <g>");
            sb.AppendLine($"    <rect x=\"{info.X.ToString("F1", CultureInfo.InvariantCulture)}\" y=\"{info.Y.ToString("F1", CultureInfo.InvariantCulture)}\" width=\"{info.Width.ToString("F1", CultureInfo.InvariantCulture)}\" height=\"{info.Height.ToString("F1", CultureInfo.InvariantCulture)}\" rx=\"6\" ry=\"6\" fill=\"{fillHex}\" stroke=\"{borderHex}\" stroke-width=\"{borderWidth.ToString("F1", CultureInfo.InvariantCulture)}\"/>");
            sb.AppendLine($"    <text x=\"{info.CenterX.ToString("F1", CultureInfo.InvariantCulture)}\" y=\"{(info.CenterY + 4).ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"{textHex}\" font-family=\"Segoe UI, system-ui, sans-serif\" font-size=\"12\" font-weight=\"600\" text-anchor=\"middle\">{escapedName}</text>");

            if (node.Children.Count > 0)
            {
                bool expanded = node.IsExpanded;
                double badgeHeight = 20;
                double badgeWidth = expanded ? 20 : Math.Max(28, 16 + node.Children.Count.ToString().Length * 7.5);
                double badgeX = isVertical ? info.CenterX - badgeWidth / 2.0 : info.X + info.Width - (badgeWidth / 2.0);
                double badgeY = isVertical ? info.Y + info.Height - (badgeHeight / 2.0) : info.CenterY - (badgeHeight / 2.0);
                string badgeBg = expanded ? (isDark ? "#132B39" : "#EEF2F6") : (isDark ? "#0F766E" : "#0D9488");
                string badgeStroke = expanded ? (isDark ? "#38BDF8" : "#0284C7") : (isDark ? "#2DD4BF" : "#14B8A6");
                string badgeFg = expanded ? (isDark ? "#CFFAFE" : "#0F172A") : "#FFFFFF";
                string badgeLabel = expanded ? "−" : $"+{node.Children.Count}";

                sb.AppendLine($"    <rect x=\"{badgeX.ToString("F1", CultureInfo.InvariantCulture)}\" y=\"{badgeY.ToString("F1", CultureInfo.InvariantCulture)}\" width=\"{badgeWidth.ToString("F1", CultureInfo.InvariantCulture)}\" height=\"{badgeHeight}\" rx=\"{badgeHeight / 2.0}\" ry=\"{badgeHeight / 2.0}\" fill=\"{badgeBg}\" stroke=\"{badgeStroke}\" stroke-width=\"1.4\"/>");
                sb.AppendLine($"    <text x=\"{(badgeX + badgeWidth / 2.0).ToString("F1", CultureInfo.InvariantCulture)}\" y=\"{(badgeY + badgeHeight / 2.0 + 3.5).ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"{badgeFg}\" font-family=\"Segoe UI, system-ui, sans-serif\" font-size=\"10\" font-weight=\"bold\" text-anchor=\"middle\">{badgeLabel}</text>");
            }

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
            writer.Flush();
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
        writer.Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPt.ToString("F2", CultureInfo.InvariantCulture)} {heightPt.ToString("F2", CultureInfo.InvariantCulture)}] /Resources << /XObject << /Img1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");
        writer.Flush();

        // Obj 4: Image XObject
        RecordObj();
        writer.Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pixelWidth} /Height {pixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
        writer.Flush();
        fileStream.Write(jpegBytes, 0, jpegBytes.Length);
        writer.Write("\nendstream\nendobj\n");
        writer.Flush();

        // Obj 5: Page Content Stream
        string contentStream = $"q\n{widthPt.ToString("F2", CultureInfo.InvariantCulture)} 0 0 {heightPt.ToString("F2", CultureInfo.InvariantCulture)} 0 0 cm\n/Img1 Do\nQ\n";
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

    private static System.Windows.Shapes.Path CreateMenuItemIcon(string geometryResourceKey, string? brushResourceKey = null)
    {
        var path = new System.Windows.Shapes.Path
        {
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (Application.Current?.TryFindResource(geometryResourceKey) is Geometry geom)
        {
            path.Data = geom;
        }

        path.SetResourceReference(Shape.StrokeProperty, brushResourceKey ?? "BrushTextSecondary");
        return path;
    }
}

