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
    Vertical,
    Mindmap,
    Radial,
    Sunburst
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
            chart.RequestRender();
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
            chart.RequestRender();
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

    private const double StandardParentBoxWidth = 172;
    private const double BoxWidth = StandardParentBoxWidth;
    private const double MinLeafBoxWidth = 64;
    private const double LeafHorizontalPadding = 24;
    private const double BoxHeight = 26;
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

    private static readonly Dictionary<(byte A, byte R, byte G, byte B), SolidColorBrush> BrushCache = new();
    private static SolidColorBrush GetCachedBrush(Color color)
    {
        var key = (color.A, color.R, color.G, color.B);
        if (!BrushCache.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            BrushCache[key] = brush;
        }
        return brush;
    }

    private static readonly SolidColorBrush SelectedBorderHighContrastBrush = GetCachedBrush(Color.FromRgb(0xFF, 0xFF, 0x00));
    private static readonly SolidColorBrush SelectedBorderDarkBrush = GetCachedBrush(Color.FromRgb(0x2D, 0xD4, 0xBF));
    private static readonly SolidColorBrush SelectedBorderLightBrush = GetCachedBrush(Color.FromRgb(0x0D, 0x94, 0x88));

    private static Brush GetSelectedBorderBrush()
    {
        if (IsHighContrastTheme())
            return SelectedBorderHighContrastBrush;
        return IsDarkTheme()
            ? SelectedBorderDarkBrush
            : SelectedBorderLightBrush;
    }

    private static readonly SolidColorBrush DragHoverBrush = GetCachedBrush(Color.FromRgb(0x02, 0x84, 0xC7)); // Sky blue highlight for drag target
    private static readonly SolidColorBrush SearchMatchBorderBrush = GetCachedBrush(Color.FromRgb(0xD9, 0x77, 0x06)); // Gold/Amber border for search match
    private static readonly SolidColorBrush SearchMatchBackgroundBrush = GetCachedBrush(Color.FromRgb(0xFE, 0xF0, 0x8A)); // Bright yellow fill for search match

    private static readonly Dictionary<(string Text, int SizeKey), double> TextWidthCache = new();

    private List<FolderNode> _lastRoots = new();
    private FolderNode? _lastSelected;
    private Dictionary<FrameworkElement, FolderNode> _boxMap = new();
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

        FolderStructureCreator.Services.ThemeService.ThemeChanged += _ => RequestRender();
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

    private bool _isRenderScheduled;

    /// <summary>
    /// Schedules a render pass on the UI dispatcher at Render priority.
    /// Debounces consecutive render requests during tab transitions or layout property changes.
    /// </summary>
    public void RequestRender()
    {
        if (_isRenderScheduled) return;
        _isRenderScheduled = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _isRenderScheduled = false;
            RenderInternal();
        }));
    }

    /// <summary>Redraws the whole chart for the given roots, highlighting the selected node if any.</summary>
    public void Render(IEnumerable<FolderNode> roots, FolderNode? selected)
    {
        _lastRoots = roots.ToList();
        _lastSelected = selected;
        _isRenderScheduled = false;
        RenderInternal();
    }

    private sealed record NodeVisualMeta(
        int Depth,
        Brush NormalBorder,
        Brush NormalBackground,
        Brush TextForeground,
        bool IsMatch,
        bool HasDiffBadge,
        StackPanel? NamePanel,
        TextBlock? NameTextBlock);

    /// <summary>
    /// Updates the selected node visually in-place without tearing down or rebuilding the canvas.
    /// If the chart has not been rendered yet or the node is outside the current visual tree,
    /// a full render is performed.
    /// </summary>
    public void SelectNode(FolderNode? node)
    {
        if (ReferenceEquals(_lastSelected, node)) return;

        // If chart is empty or not rendered yet, trigger full render
        if (_boxMap.Count == 0 || _lastRoots.Count == 0)
        {
            _lastSelected = node;
            RequestRender();
            return;
        }

        // If node is non-null and not found in current visual map (e.g. collapsed ancestor), full render to reveal it
        if (node != null && !_boxMap.Values.Any(n => ReferenceEquals(n, node)))
        {
            _lastSelected = node;
            RequestRender();
            return;
        }

        // Unselect previous
        if (_lastSelected != null)
        {
            var prevEntry = _boxMap.FirstOrDefault(kvp => ReferenceEquals(kvp.Value, _lastSelected));
            if (prevEntry.Key != null)
            {
                ApplySelectionVisuals(prevEntry.Key, _lastSelected, false);
            }
        }

        _lastSelected = node;

        // Select new
        if (_lastSelected != null)
        {
            var newEntry = _boxMap.FirstOrDefault(kvp => ReferenceEquals(kvp.Value, _lastSelected));
            if (newEntry.Key != null)
            {
                ApplySelectionVisuals(newEntry.Key, _lastSelected, true);
            }
        }
    }

    private void ApplySelectionVisuals(FrameworkElement element, FolderNode node, bool isSelected)
    {
        bool isDark = IsDarkTheme();
        if (element is Border box && box.Tag is NodeVisualMeta meta)
        {
            if (isSelected)
            {
                box.BorderBrush = GetSelectedBorderBrush();
                box.BorderThickness = new Thickness(2.8);
                box.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = isDark ? Color.FromRgb(0x2D, 0xD4, 0xBF) : Color.FromRgb(0x0D, 0x94, 0x88),
                    BlurRadius = 6,
                    ShadowDepth = 0,
                    Opacity = 0.3
                };
                if (meta.NameTextBlock != null)
                {
                    meta.NameTextBlock.FontWeight = FontWeights.Bold;
                }
                if (meta.NamePanel != null && (meta.NamePanel.Children.Count == 0 || !(meta.NamePanel.Children[0] is TextBlock tb && tb.Text == "● ")))
                {
                    meta.NamePanel.Children.Insert(0, new TextBlock
                    {
                        Text = "● ",
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = isDark ? GetCachedBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)) : GetCachedBrush(Color.FromRgb(0x0D, 0x94, 0x88)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 2, 0)
                    });
                }
            }
            else
            {
                box.BorderBrush = meta.NormalBorder;
                box.BorderThickness = new Thickness((meta.IsMatch || meta.HasDiffBadge) ? 2.0 : 1.2);
                box.Effect = null;
                if (meta.NameTextBlock != null)
                {
                    meta.NameTextBlock.FontWeight = FontWeights.SemiBold;
                }
                if (meta.NamePanel != null && meta.NamePanel.Children.Count > 1 && meta.NamePanel.Children[0] is TextBlock tb && tb.Text == "● ")
                {
                    meta.NamePanel.Children.RemoveAt(0);
                }
            }
        }
        else if (element is System.Windows.Shapes.Path path && path.Tag is NodeVisualMeta pathMeta)
        {
            if (isSelected)
            {
                path.Stroke = GetSelectedBorderBrush();
                path.StrokeThickness = 2.8;
                path.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = isDark ? Color.FromRgb(0x2D, 0xD4, 0xBF) : Color.FromRgb(0x0D, 0x94, 0x88),
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
            }
            else
            {
                path.Stroke = pathMeta.NormalBorder;
                path.StrokeThickness = pathMeta.IsMatch ? 2.0 : 1.2;
                path.Effect = null;
            }
        }
    }

    /// <summary>Scrolls/centers the ScrollViewer viewport on the selected node box if present.</summary>
    public void BringSelectedIntoView()
    {
        if (_lastSelected == null) return;

        void PerformScroll()
        {
            var selectedEntry = _boxMap.FirstOrDefault(kvp => ReferenceEquals(kvp.Value, _lastSelected));
            if (selectedEntry.Key == null) return;

            double scale = ChartScale.ScaleX;
            Rect bounds;
            if (selectedEntry.Key is Border box)
            {
                double left = Canvas.GetLeft(box);
                double top = Canvas.GetTop(box);
                double boxWidthScaled = (box.Width > 0 ? box.Width : BoxWidth);
                double boxHeightScaled = (box.Height > 0 ? box.Height : BoxHeight);
                bounds = new Rect(left, top, boxWidthScaled, boxHeightScaled);
            }
            else if (selectedEntry.Key is System.Windows.Shapes.Path path && path.Data != null)
            {
                bounds = path.Data.Bounds;
            }
            else
            {
                return;
            }

            double viewportWidth = ChartScrollViewer.ViewportWidth;
            double viewportHeight = ChartScrollViewer.ViewportHeight;

            if (viewportWidth <= 0 || viewportHeight <= 0) return;

            double targetX = (bounds.Left + bounds.Width / 2.0) * scale - (viewportWidth / 2.0);
            double targetY = (bounds.Top + bounds.Height / 2.0) * scale - (viewportHeight / 2.0);

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
        public bool IsLeftBranch { get; set; }
        public double Angle { get; set; }
        public double Radius { get; set; }
    }

    private static readonly Typeface NodeTextTypeface = new Typeface(
        new FontFamily("Segoe UI, system-ui, sans-serif"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);

    private static readonly GlyphTypeface? NodeGlyphTypeface = NodeTextTypeface.TryGetGlyphTypeface(out var gtf) ? gtf : null;

    private static double MeasureTextWidth(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int sizeKey = (int)Math.Round(fontSize * 10);
        var cacheKey = (text, sizeKey);
        if (TextWidthCache.TryGetValue(cacheKey, out double cachedWidth))
            return cachedWidth;

        double measured;
        if (NodeGlyphTypeface != null)
        {
            double total = 0;
            var cmap = NodeGlyphTypeface.CharacterToGlyphMap;
            var advances = NodeGlyphTypeface.AdvanceWidths;
            for (int i = 0; i < text.Length; i++)
            {
                if (cmap.TryGetValue(text[i], out ushort glyphIndex))
                    total += advances[glyphIndex] * fontSize;
                else
                    total += 0.6 * fontSize;
            }
            measured = total;
        }
        else
        {
            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeTextTypeface,
                fontSize,
                Brushes.Black,
                1.0);
            measured = formattedText.WidthIncludingTrailingWhitespace;
        }

        if (TextWidthCache.Count >= 5000)
        {
            TextWidthCache.Clear();
        }

        TextWidthCache[cacheKey] = measured;
        return measured;
    }

    private static (double Width, double Height) GetNodeDimensions(FolderNode node, int depth, bool isVertical = false)
    {
        double height = node.HasDiffBadge ? 36.0 : BoxHeight;
        double textWidth = MeasureTextWidth(node.Name, 12.0);

        double width;
        if (node.Children.Count > 0)
        {
            // Parent box with children: keep enough standard width to clearly represent hierarchy and connector lines
            double extraPadding = !isVertical ? 48.0 : 38.0;
            double requiredWidth = textWidth + extraPadding;
            width = Math.Max(StandardParentBoxWidth, Math.Ceiling(requiredWidth));
        }
        else
        {
            // Leaf box with no children: dynamic width based on content with reasonable minimum padding
            double requiredWidth = textWidth + LeafHorizontalPadding;
            if (node.HasDiffBadge && !string.IsNullOrEmpty(node.DiffBadgeText))
            {
                double badgeWidth = MeasureTextWidth(node.DiffBadgeText, 9.5) + LeafHorizontalPadding;
                requiredWidth = Math.Max(requiredWidth, badgeWidth);
            }
            width = Math.Max(MinLeafBoxWidth, Math.Ceiling(requiredWidth));
        }

        return (width, height);
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

    private static (Dictionary<FolderNode, NodeLayoutInfo> Map, List<NodeLayoutInfo> AllNodes) LayoutHorizontalForest(
        IReadOnlyList<FolderNode> branchRoots,
        int baseDepth,
        double startX)
    {
        var layoutMap = new Dictionary<FolderNode, NodeLayoutInfo>();
        var allForestNodes = new List<NodeLayoutInfo>();
        if (branchRoots.Count == 0) return (layoutMap, allForestNodes);

        var depthMaxWidths = new Dictionary<int, double>();
        void MeasureVisibleDepths(FolderNode node, int depth)
        {
            var (w, _) = GetNodeDimensions(node, depth, isVertical: false);
            if (!depthMaxWidths.TryGetValue(depth, out double maxW) || w > maxW)
            {
                depthMaxWidths[depth] = w;
            }

            if (node.IsExpanded && node.Children.Count > 0)
            {
                foreach (var child in node.Children)
                {
                    MeasureVisibleDepths(child, depth + 1);
                }
            }
        }

        foreach (var root in branchRoots)
        {
            MeasureVisibleDepths(root, baseDepth);
        }

        int minD = depthMaxWidths.Keys.Count > 0 ? depthMaxWidths.Keys.Min() : baseDepth;
        int maxD = depthMaxWidths.Keys.Count > 0 ? depthMaxWidths.Keys.Max() : baseDepth;
        var colX = new Dictionary<int, double>();
        double runningX = startX;
        for (int d = minD; d <= maxD; d++)
        {
            colX[d] = runningX;
            double cw = depthMaxWidths.TryGetValue(d, out var w) ? w : StandardParentBoxWidth;
            runningX += cw + ColumnGap;
        }

        Dictionary<int, (double MinY, double MaxY)> GetDepthProfileY(List<NodeLayoutInfo> nodes)
        {
            var profile = new Dictionary<int, (double MinY, double MaxY)>();
            foreach (var n in nodes)
            {
                double top = n.Y;
                double bottom = n.Y + n.Height;
                if (!profile.TryGetValue(n.Depth, out var span))
                {
                    profile[n.Depth] = (top, bottom);
                }
                else
                {
                    profile[n.Depth] = (Math.Min(span.MinY, top), Math.Max(span.MaxY, bottom));
                }
            }
            return profile;
        }

        double ComputeShiftY(Dictionary<int, (double MinY, double MaxY)> prevProfile, Dictionary<int, (double MinY, double MaxY)> currProfile)
        {
            double maxShift = 0;
            foreach (var kvp in currProfile)
            {
                int depth = kvp.Key;
                if (prevProfile.TryGetValue(depth, out var prevSpan))
                {
                    double requiredY = prevSpan.MaxY + MinVerticalSpacing;
                    double overlap = requiredY - kvp.Value.MinY;
                    if (overlap > maxShift)
                    {
                        maxShift = overlap;
                    }
                }
            }
            return maxShift;
        }

        void ShiftNodesY(List<NodeLayoutInfo> nodes, double deltaY)
        {
            if (Math.Abs(deltaY) < 0.0001) return;
            foreach (var n in nodes)
            {
                n.Y += deltaY;
            }
        }

        List<NodeLayoutInfo> LayoutSubtreeH(FolderNode node, int depth)
        {
            var (w, h) = GetNodeDimensions(node, depth, isVertical: false);
            var nodeInfo = new NodeLayoutInfo
            {
                Node = node,
                Depth = depth,
                Width = w,
                Height = h,
                X = colX.TryGetValue(depth, out double cx) ? cx : (startX + (depth - baseDepth) * (StandardParentBoxWidth + ColumnGap)),
                Y = 0
            };
            layoutMap[node] = nodeInfo;

            var visibleChildren = (node.IsExpanded && node.Children.Count > 0)
                ? node.Children
                : (IReadOnlyList<FolderNode>)Array.Empty<FolderNode>();

            var allSubtreeNodes = new List<NodeLayoutInfo> { nodeInfo };

            if (visibleChildren.Count == 0)
            {
                return allSubtreeNodes;
            }

            var cumulativeChildrenNodes = new List<NodeLayoutInfo>();
            Dictionary<int, (double MinY, double MaxY)> cumulativeProfile = new();

            foreach (var child in visibleChildren)
            {
                var childSubtreeNodes = LayoutSubtreeH(child, depth + 1);
                var childProfile = GetDepthProfileY(childSubtreeNodes);

                if (cumulativeChildrenNodes.Count > 0)
                {
                    double shift = ComputeShiftY(cumulativeProfile, childProfile);
                    if (shift > 0)
                    {
                        ShiftNodesY(childSubtreeNodes, shift);
                        childProfile = GetDepthProfileY(childSubtreeNodes);
                    }
                }

                cumulativeChildrenNodes.AddRange(childSubtreeNodes);

                foreach (var kvp in childProfile)
                {
                    if (!cumulativeProfile.TryGetValue(kvp.Key, out var span))
                    {
                        cumulativeProfile[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        cumulativeProfile[kvp.Key] = (Math.Min(span.MinY, kvp.Value.MinY), Math.Max(span.MaxY, kvp.Value.MaxY));
                    }
                }
            }

            // Center parent over its first and last visible children
            double firstChildCenter = layoutMap[visibleChildren[0]].CenterY;
            double lastChildCenter = layoutMap[visibleChildren[^1]].CenterY;
            double desiredCenter = (firstChildCenter + lastChildCenter) / 2.0;
            nodeInfo.Y = desiredCenter - h / 2.0;

            allSubtreeNodes.AddRange(cumulativeChildrenNodes);
            return allSubtreeNodes;
        }

        var cumulativeRootNodes = new List<NodeLayoutInfo>();
        Dictionary<int, (double MinY, double MaxY)> cumulativeRootProfile = new();

        foreach (var root in branchRoots)
        {
            var rootSubtreeNodes = LayoutSubtreeH(root, baseDepth);
            var rootProfile = GetDepthProfileY(rootSubtreeNodes);

            if (cumulativeRootNodes.Count > 0)
            {
                double shift = ComputeShiftY(cumulativeRootProfile, rootProfile);
                if (shift > 0)
                {
                    ShiftNodesY(rootSubtreeNodes, shift);
                    rootProfile = GetDepthProfileY(rootSubtreeNodes);
                }
            }

            cumulativeRootNodes.AddRange(rootSubtreeNodes);

            foreach (var kvp in rootProfile)
            {
                if (!cumulativeRootProfile.TryGetValue(kvp.Key, out var span))
                {
                    cumulativeRootProfile[kvp.Key] = kvp.Value;
                }
                else
                {
                    cumulativeRootProfile[kvp.Key] = (Math.Min(span.MinY, kvp.Value.MinY), Math.Max(span.MaxY, kvp.Value.MaxY));
                }
            }
        }

        allForestNodes.AddRange(cumulativeRootNodes);
        return (layoutMap, allForestNodes);
    }

    private static Dictionary<FolderNode, NodeLayoutInfo> ComputeHorizontalLayout(IReadOnlyList<FolderNode> roots)
    {
        var (layoutMap, _) = LayoutHorizontalForest(roots, baseDepth: 0, startX: ChartPadding);
        if (layoutMap.Count > 0)
        {
            double minY = layoutMap.Values.Min(i => i.Y);
            double offsetY = ChartPadding - minY;
            if (Math.Abs(offsetY) > 0.001)
            {
                foreach (var info in layoutMap.Values)
                    info.Y += offsetY;
            }
        }
        return layoutMap;
    }

    private static Dictionary<FolderNode, NodeLayoutInfo> ComputeVerticalLayout(IReadOnlyList<FolderNode> roots)
    {
        var layoutMap = new Dictionary<FolderNode, NodeLayoutInfo>();
        if (roots.Count == 0) return layoutMap;

        const double LevelVerticalGap = 44.0;
        var depthMaxHeights = new Dictionary<int, double>();
        void MeasureVisibleHeights(FolderNode node, int depth)
        {
            var (_, h) = GetNodeDimensions(node, depth, isVertical: true);
            if (!depthMaxHeights.TryGetValue(depth, out double maxH) || h > maxH)
            {
                depthMaxHeights[depth] = h;
            }

            if (node.IsExpanded && node.Children.Count > 0)
            {
                foreach (var child in node.Children)
                {
                    MeasureVisibleHeights(child, depth + 1);
                }
            }
        }

        foreach (var root in roots)
        {
            MeasureVisibleHeights(root, 0);
        }

        int maxDepthV = depthMaxHeights.Keys.Count > 0 ? depthMaxHeights.Keys.Max() : 0;
        var levelY = new Dictionary<int, double>();
        double runningY = ChartPadding;
        for (int d = 0; d <= maxDepthV; d++)
        {
            levelY[d] = runningY;
            double ch = depthMaxHeights.TryGetValue(d, out var h) ? h : BoxHeight;
            runningY += ch + LevelVerticalGap;
        }

        Dictionary<int, (double MinX, double MaxX)> GetDepthProfileX(List<NodeLayoutInfo> nodes)
        {
            var profile = new Dictionary<int, (double MinX, double MaxX)>();
            foreach (var n in nodes)
            {
                double left = n.X;
                double right = n.X + n.Width;
                if (!profile.TryGetValue(n.Depth, out var span))
                {
                    profile[n.Depth] = (left, right);
                }
                else
                {
                    profile[n.Depth] = (Math.Min(span.MinX, left), Math.Max(span.MaxX, right));
                }
            }
            return profile;
        }

        double ComputeShiftX(Dictionary<int, (double MinX, double MaxX)> prevProfile, Dictionary<int, (double MinX, double MaxX)> currProfile)
        {
            double maxShift = 0;
            foreach (var kvp in currProfile)
            {
                int depth = kvp.Key;
                if (prevProfile.TryGetValue(depth, out var prevSpan))
                {
                    double requiredX = prevSpan.MaxX + MinHorizontalSpacing;
                    double overlap = requiredX - kvp.Value.MinX;
                    if (overlap > maxShift)
                    {
                        maxShift = overlap;
                    }
                }
            }
            return maxShift;
        }

        void ShiftNodesX(List<NodeLayoutInfo> nodes, double deltaX)
        {
            if (Math.Abs(deltaX) < 0.0001) return;
            foreach (var n in nodes)
            {
                n.X += deltaX;
            }
        }

        List<NodeLayoutInfo> LayoutSubtreeV(FolderNode node, int depth)
        {
            var (w, h) = GetNodeDimensions(node, depth, isVertical: true);
            var nodeInfo = new NodeLayoutInfo
            {
                Node = node,
                Depth = depth,
                Width = w,
                Height = h,
                X = 0,
                Y = levelY.TryGetValue(depth, out double cy) ? cy : (ChartPadding + depth * (BoxHeight + LevelVerticalGap))
            };
            layoutMap[node] = nodeInfo;

            var visibleChildren = (node.IsExpanded && node.Children.Count > 0)
                ? node.Children
                : (IReadOnlyList<FolderNode>)Array.Empty<FolderNode>();

            var allSubtreeNodes = new List<NodeLayoutInfo> { nodeInfo };

            if (visibleChildren.Count == 0)
            {
                return allSubtreeNodes;
            }

            var cumulativeChildrenNodes = new List<NodeLayoutInfo>();
            Dictionary<int, (double MinX, double MaxX)> cumulativeProfile = new();

            foreach (var child in visibleChildren)
            {
                var childSubtreeNodes = LayoutSubtreeV(child, depth + 1);
                var childProfile = GetDepthProfileX(childSubtreeNodes);

                if (cumulativeChildrenNodes.Count > 0)
                {
                    double shift = ComputeShiftX(cumulativeProfile, childProfile);
                    if (shift > 0)
                    {
                        ShiftNodesX(childSubtreeNodes, shift);
                        childProfile = GetDepthProfileX(childSubtreeNodes);
                    }
                }

                cumulativeChildrenNodes.AddRange(childSubtreeNodes);

                foreach (var kvp in childProfile)
                {
                    if (!cumulativeProfile.TryGetValue(kvp.Key, out var span))
                    {
                        cumulativeProfile[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        cumulativeProfile[kvp.Key] = (Math.Min(span.MinX, kvp.Value.MinX), Math.Max(span.MaxX, kvp.Value.MaxX));
                    }
                }
            }

            // Center parent horizontally over its first and last visible children
            double firstChildCenter = layoutMap[visibleChildren[0]].CenterX;
            double lastChildCenter = layoutMap[visibleChildren[^1]].CenterX;
            double desiredCenter = (firstChildCenter + lastChildCenter) / 2.0;
            nodeInfo.X = desiredCenter - w / 2.0;

            allSubtreeNodes.AddRange(cumulativeChildrenNodes);
            return allSubtreeNodes;
        }

        var cumulativeRootNodes = new List<NodeLayoutInfo>();
        Dictionary<int, (double MinX, double MaxX)> cumulativeRootProfile = new();

        foreach (var root in roots)
        {
            var rootSubtreeNodes = LayoutSubtreeV(root, 0);
            var rootProfile = GetDepthProfileX(rootSubtreeNodes);

            if (cumulativeRootNodes.Count > 0)
            {
                double shift = ComputeShiftX(cumulativeRootProfile, rootProfile);
                if (shift > 0)
                {
                    ShiftNodesX(rootSubtreeNodes, shift);
                    rootProfile = GetDepthProfileX(rootSubtreeNodes);
                }
            }

            cumulativeRootNodes.AddRange(rootSubtreeNodes);

            foreach (var kvp in rootProfile)
            {
                if (!cumulativeRootProfile.TryGetValue(kvp.Key, out var span))
                {
                    cumulativeRootProfile[kvp.Key] = kvp.Value;
                }
                else
                {
                    cumulativeRootProfile[kvp.Key] = (Math.Min(span.MinX, kvp.Value.MinX), Math.Max(span.MaxX, kvp.Value.MaxX));
                }
            }
        }

        // Normalize so left-most node is anchored cleanly at ChartPadding
        if (layoutMap.Count > 0)
        {
            double minX = layoutMap.Values.Min(i => i.X);
            double offsetX = ChartPadding - minX;
            if (Math.Abs(offsetX) > 0.001)
            {
                foreach (var info in layoutMap.Values)
                    info.X += offsetX;
            }
        }

        return layoutMap;
    }

    private static Dictionary<FolderNode, NodeLayoutInfo> ComputeMindmapLayout(IReadOnlyList<FolderNode> roots)
    {
        var layoutMap = new Dictionary<FolderNode, NodeLayoutInfo>();
        if (roots.Count == 0) return layoutMap;

        double currentBaseY = 0;

        foreach (var root in roots)
        {
            var (rw, rh) = GetNodeDimensions(root, 0, isVertical: false);
            var rootInfo = new NodeLayoutInfo
            {
                Node = root,
                Depth = 0,
                Width = rw,
                Height = rh,
                X = -rw / 2.0,
                Y = currentBaseY - rh / 2.0,
                IsLeftBranch = false
            };
            layoutMap[root] = rootInfo;

            var visibleChildren = (root.IsExpanded && root.Children.Count > 0)
                ? root.Children
                : (IReadOnlyList<FolderNode>)Array.Empty<FolderNode>();

            if (visibleChildren.Count > 0)
            {
                // Balance children: right half and left half
                int rightCount = (visibleChildren.Count + 1) / 2;
                var rightChildren = visibleChildren.Take(rightCount).ToList();
                var leftChildren = visibleChildren.Skip(rightCount).ToList();

                List<NodeLayoutInfo> rightNodesList = new();
                List<NodeLayoutInfo> leftNodesList = new();

                if (rightChildren.Count > 0)
                {
                    double rightStartX = rw / 2.0 + ColumnGap;
                    var (rMap, rList) = LayoutHorizontalForest(rightChildren, baseDepth: 1, startX: rightStartX);
                    foreach (var kvp in rMap)
                    {
                        kvp.Value.IsLeftBranch = false;
                        layoutMap[kvp.Key] = kvp.Value;
                    }
                    rightNodesList = rList;
                }

                if (leftChildren.Count > 0)
                {
                    double leftStartX = rw / 2.0 + ColumnGap;
                    var (lMap, lList) = LayoutHorizontalForest(leftChildren, baseDepth: 1, startX: leftStartX);
                    foreach (var kvp in lMap)
                    {
                        kvp.Value.IsLeftBranch = true;
                        // Mirror horizontally to the left
                        kvp.Value.X = -(kvp.Value.X + kvp.Value.Width);
                        layoutMap[kvp.Key] = kvp.Value;
                    }
                    leftNodesList = lList;
                }

                // Center right side vertically around currentBaseY
                if (rightNodesList.Count > 0)
                {
                    double rMinY = rightNodesList.Min(n => n.Y);
                    double rMaxY = rightNodesList.Max(n => n.Y + n.Height);
                    double rCenterY = (rMinY + rMaxY) / 2.0;
                    double rShift = currentBaseY - rCenterY;
                    foreach (var n in rightNodesList) n.Y += rShift;
                }

                // Center left side vertically around currentBaseY
                if (leftNodesList.Count > 0)
                {
                    double lMinY = leftNodesList.Min(n => n.Y);
                    double lMaxY = leftNodesList.Max(n => n.Y + n.Height);
                    double lCenterY = (lMinY + lMaxY) / 2.0;
                    double lShift = currentBaseY - lCenterY;
                    foreach (var n in leftNodesList) n.Y += lShift;
                }
            }

            // Advance currentBaseY for next root if any
            double clusterMaxY = layoutMap.Values.Max(n => n.Y + n.Height);
            currentBaseY = clusterMaxY + MinVerticalSpacing * 4;
        }

        // Normalize so all coordinates start from ChartPadding
        double minX = layoutMap.Values.Min(i => i.X);
        double minY = layoutMap.Values.Min(i => i.Y);
        double shiftX = ChartPadding - minX;
        double shiftY = ChartPadding - minY;
        foreach (var info in layoutMap.Values)
        {
            info.X += shiftX;
            info.Y += shiftY;
        }

        return layoutMap;
    }

    private static Dictionary<FolderNode, NodeLayoutInfo> ComputeRadialLayout(IReadOnlyList<FolderNode> roots)
    {
        var layoutMap = new Dictionary<FolderNode, NodeLayoutInfo>();
        if (roots.Count == 0) return layoutMap;

        int GetSubtreeLeafCount(FolderNode node)
        {
            if (!node.IsExpanded || node.Children.Count == 0) return 1;
            int count = 0;
            foreach (var c in node.Children) count += GetSubtreeLeafCount(c);
            return Math.Max(1, count);
        }

        const double LayerRadiusDelta = 175.0;

        void LayoutRadialSubtree(FolderNode node, int depth, double startAngle, double endAngle)
        {
            double angle = (startAngle + endAngle) / 2.0;
            double radius = depth * LayerRadiusDelta;
            double cx = radius * Math.Cos(angle);
            double cy = radius * Math.Sin(angle);

            var (w, h) = GetNodeDimensions(node, depth, isVertical: false);
            var info = new NodeLayoutInfo
            {
                Node = node,
                Depth = depth,
                Width = w,
                Height = h,
                X = cx - w / 2.0,
                Y = cy - h / 2.0,
                Angle = angle,
                Radius = radius
            };
            layoutMap[node] = info;

            if (node.IsExpanded && node.Children.Count > 0)
            {
                int totalChildLeaves = node.Children.Sum(GetSubtreeLeafCount);
                double curAngle = startAngle;
                double span = endAngle - startAngle;

                foreach (var child in node.Children)
                {
                    int childLeaves = GetSubtreeLeafCount(child);
                    double childSpan = (double)childLeaves / totalChildLeaves * span;
                    LayoutRadialSubtree(child, depth + 1, curAngle, curAngle + childSpan);
                    curAngle += childSpan;
                }
            }
        }

        if (roots.Count == 1)
        {
            var root = roots[0];
            var (rw, rh) = GetNodeDimensions(root, 0, isVertical: false);
            var rootInfo = new NodeLayoutInfo
            {
                Node = root,
                Depth = 0,
                Width = rw,
                Height = rh,
                X = -rw / 2.0,
                Y = -rh / 2.0,
                Angle = 0,
                Radius = 0
            };
            layoutMap[root] = rootInfo;

            if (root.IsExpanded && root.Children.Count > 0)
            {
                int totalLeaves = root.Children.Sum(GetSubtreeLeafCount);
                double curAngle = 0.0;
                foreach (var child in root.Children)
                {
                    int cLeaves = GetSubtreeLeafCount(child);
                    double cSpan = (double)cLeaves / totalLeaves * (2.0 * Math.PI);
                    LayoutRadialSubtree(child, 1, curAngle, curAngle + cSpan);
                    curAngle += cSpan;
                }
            }
        }
        else
        {
            int totalLeaves = roots.Sum(GetSubtreeLeafCount);
            double curAngle = 0.0;
            foreach (var root in roots)
            {
                int rLeaves = GetSubtreeLeafCount(root);
                double rSpan = (double)rLeaves / totalLeaves * (2.0 * Math.PI);
                LayoutRadialSubtree(root, 0, curAngle, curAngle + rSpan);
                curAngle += rSpan;
            }
        }

        // Normalize so bounds are positive with ChartPadding
        double minX = layoutMap.Values.Min(i => i.X);
        double minY = layoutMap.Values.Min(i => i.Y);
        double shiftX = ChartPadding - minX;
        double shiftY = ChartPadding - minY;
        foreach (var info in layoutMap.Values)
        {
            info.X += shiftX;
            info.Y += shiftY;
        }

        return layoutMap;
    }

    private static Dictionary<FolderNode, NodeLayoutInfo> ComputeLayout(IReadOnlyList<FolderNode> roots, OrgChartLayoutDirection layoutDirection)
    {
        return layoutDirection switch
        {
            OrgChartLayoutDirection.Vertical => ComputeVerticalLayout(roots),
            OrgChartLayoutDirection.Mindmap => ComputeMindmapLayout(roots),
            OrgChartLayoutDirection.Radial => ComputeRadialLayout(roots),
            _ => ComputeHorizontalLayout(roots)
        };
    }

    private static Dictionary<FolderNode, NodeLayoutInfo> ComputeLayout(IReadOnlyList<FolderNode> roots, bool isVertical)
    {
        return ComputeLayout(roots, isVertical ? OrgChartLayoutDirection.Vertical : OrgChartLayoutDirection.Horizontal);
    }

    private static Geometry CreateAnnularSectorGeometry(Point center, double innerR, double outerR, double startAngle, double endAngle)
    {
        double angleSpan = endAngle - startAngle;
        double pad = Math.Min(0.015, angleSpan * 0.04);
        double a1 = startAngle + pad;
        double a2 = endAngle - pad;
        if (a2 <= a1) { a1 = startAngle; a2 = endAngle; }

        double pOut1X = center.X + outerR * Math.Cos(a1);
        double pOut1Y = center.Y + outerR * Math.Sin(a1);
        double pOut2X = center.X + outerR * Math.Cos(a2);
        double pOut2Y = center.Y + outerR * Math.Sin(a2);

        bool isLargeArc = (a2 - a1) > Math.PI;

        var figure = new PathFigure
        {
            StartPoint = new Point(pOut1X, pOut1Y),
            IsClosed = true,
            IsFilled = true
        };

        figure.Segments.Add(new ArcSegment(new Point(pOut2X, pOut2Y), new Size(outerR, outerR), 0, isLargeArc, SweepDirection.Clockwise, true));

        if (innerR > 0.001)
        {
            double pIn2X = center.X + innerR * Math.Cos(a2);
            double pIn2Y = center.Y + innerR * Math.Sin(a2);
            double pIn1X = center.X + innerR * Math.Cos(a1);
            double pIn1Y = center.Y + innerR * Math.Sin(a1);

            figure.Segments.Add(new LineSegment(new Point(pIn2X, pIn2Y), true));
            figure.Segments.Add(new ArcSegment(new Point(pIn1X, pIn1Y), new Size(innerR, innerR), 0, isLargeArc, SweepDirection.Counterclockwise, true));
        }
        else
        {
            figure.Segments.Add(new LineSegment(center, true));
        }

        var geom = new PathGeometry();
        geom.Figures.Add(figure);
        return geom;
    }

    private ContextMenu CreateNodeContextMenu(FolderNode node, FrameworkElement element)
    {
        var menu = new ContextMenu { PlacementTarget = element };
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
            if (element is Border box)
            {
                BeginRename(node, box);
            }
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

        return menu;
    }

    private void AttachNodeInteractions(FrameworkElement element, FolderNode node)
    {
        element.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                OpenInExplorerRequested?.Invoke(node);
                e.Handled = true;
                return;
            }
            NodeClicked?.Invoke(node);
        };

        element.PreviewMouseRightButtonDown += (s, e) =>
        {
            NodeClicked?.Invoke(node);
        };

        element.MouseRightButtonUp += (s, e) =>
        {
            element.ContextMenu = CreateNodeContextMenu(node, element);
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.IsOpen = true;
            e.Handled = true;
        };

        element.ContextMenuOpening += (s, e) =>
        {
            element.ContextMenu = CreateNodeContextMenu(node, element);
        };
    }

    private void RenderSunburstInternal(double prevHOffset = 0, double prevVOffset = 0)
    {
        if (_lastRoots.Count == 0)
        {
            RootCanvas.Width = 0;
            RootCanvas.Height = 0;
            return;
        }

        int maxDepth = 0;
        void MeasureMaxDepth(FolderNode node, int depth)
        {
            if (depth > maxDepth) maxDepth = depth;
            if (node.IsExpanded && node.Children.Count > 0)
            {
                foreach (var c in node.Children)
                    MeasureMaxDepth(c, depth + 1);
            }
        }
        foreach (var r in _lastRoots)
            MeasureMaxDepth(r, 0);

        const double RootRadius = 60.0;
        const double RingThickness = 65.0;
        const double RingGap = 6.0;

        double totalRadius = RootRadius + (maxDepth > 0 ? maxDepth * (RingThickness + RingGap) : 0);
        double cx = totalRadius + ChartPadding + 20.0;
        double cy = totalRadius + ChartPadding + 20.0;
        Point center = new Point(cx, cy);

        RootCanvas.Width = Math.Max(cx * 2, 100);
        RootCanvas.Height = Math.Max(cy * 2, 100);

        bool isDark = IsDarkTheme();

        int GetSubtreeLeafCount(FolderNode node)
        {
            if (!node.IsExpanded || node.Children.Count == 0) return 1;
            int sum = 0;
            foreach (var c in node.Children) sum += GetSubtreeLeafCount(c);
            return Math.Max(1, sum);
        }

        void RenderSector(FolderNode node, int depth, double innerR, double outerR, double startAngle, double endAngle)
        {
            var (fill, border, textCol) = GetPalette(depth);
            bool isSelected = ReferenceEquals(node, _lastSelected);
            bool isMatch = node.IsMatchingSearch;

            Brush fillBrush = isMatch ? SearchMatchBackgroundBrush : GetCachedBrush(fill);
            Brush normalBorderBrush = isMatch ? SearchMatchBorderBrush : GetCachedBrush(border);
            Brush textBrush = GetCachedBrush(textCol);

            if (node.DiffStatus == NodeDiffStatus.MissingOnDisk)
            {
                normalBorderBrush = GetCachedBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                fillBrush = isDark ? GetCachedBrush(Color.FromRgb(0x06, 0x4E, 0x3B)) : GetCachedBrush(Color.FromRgb(0xD1, 0xFA, 0xE5));
                textBrush = isDark ? GetCachedBrush(Color.FromRgb(0xEC, 0xFD, 0xF5)) : GetCachedBrush(Color.FromRgb(0x06, 0x5F, 0x46));
            }
            else if (node.DiffStatus == NodeDiffStatus.MatchesDisk)
            {
                normalBorderBrush = GetCachedBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                fillBrush = isDark ? GetCachedBrush(Color.FromRgb(0x1E, 0x29, 0x3B)) : GetCachedBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
                textBrush = GetCachedBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            }

            Brush borderBrush = isSelected ? GetSelectedBorderBrush() : normalBorderBrush;

            Geometry geom;
            if (innerR <= 0.001)
            {
                if (Math.Abs(endAngle - startAngle - 2 * Math.PI) < 0.05)
                {
                    geom = new EllipseGeometry(center, outerR, outerR);
                }
                else
                {
                    geom = CreateAnnularSectorGeometry(center, 0, outerR, startAngle, endAngle);
                }
            }
            else
            {
                geom = CreateAnnularSectorGeometry(center, innerR, outerR, startAngle, endAngle);
            }

            var path = new System.Windows.Shapes.Path
            {
                Data = geom,
                Fill = fillBrush,
                Stroke = borderBrush,
                StrokeThickness = isSelected ? 2.8 : (isMatch ? 2.0 : 1.2),
                Cursor = Cursors.Hand,
                ToolTip = $"{node.Name}{(node.Children.Count > 0 ? $" ({node.Children.Count} subfolders)" : "")}",
                Tag = new NodeVisualMeta(depth, normalBorderBrush, fillBrush, textBrush, isMatch, false, null, null)
            };

            if (isSelected)
            {
                path.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = isDark ? Color.FromRgb(0x2D, 0xD4, 0xBF) : Color.FromRgb(0x0D, 0x94, 0x88),
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
            }

            AttachNodeInteractions(path, node);
            RootCanvas.Children.Add(path);
            _boxMap[path] = node;

            double span = endAngle - startAngle;
            double midAngle = (startAngle + endAngle) / 2.0;
            double midRadius = (innerR + outerR) / 2.0;
            double arcLength = midRadius * span;

            if (innerR <= 0.001)
            {
                var label = new TextBlock
                {
                    Text = node.Name,
                    Foreground = textBrush,
                    FontSize = 12.0,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = outerR * 1.6,
                    IsHitTestVisible = false
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, cx - label.DesiredSize.Width / 2.0);
                Canvas.SetTop(label, cy - label.DesiredSize.Height / 2.0);
                RootCanvas.Children.Add(label);
            }
            else if (arcLength >= 22.0)
            {
                double textX = cx + midRadius * Math.Cos(midAngle);
                double textY = cy + midRadius * Math.Sin(midAngle);
                double maxLabelW = Math.Max(20, Math.Min(outerR - innerR - 8.0, arcLength - 4.0));

                var label = new TextBlock
                {
                    Text = node.Name,
                    Foreground = textBrush,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Medium,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = maxLabelW,
                    IsHitTestVisible = false
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                double deg = midAngle * 180.0 / Math.PI;
                if (deg > 90 && deg < 270)
                    deg += 180;

                label.RenderTransformOrigin = new Point(0.5, 0.5);
                label.RenderTransform = new RotateTransform(deg);

                Canvas.SetLeft(label, textX - label.DesiredSize.Width / 2.0);
                Canvas.SetTop(label, textY - label.DesiredSize.Height / 2.0);
                RootCanvas.Children.Add(label);
            }

            if (node.IsExpanded && node.Children.Count > 0)
            {
                int totalChildLeaves = node.Children.Sum(GetSubtreeLeafCount);
                double curAngle = startAngle;
                double nextInnerR = outerR + RingGap;
                double nextOuterR = nextInnerR + RingThickness;

                foreach (var child in node.Children)
                {
                    int childLeaves = GetSubtreeLeafCount(child);
                    double childSpan = (double)childLeaves / totalChildLeaves * span;
                    RenderSector(child, depth + 1, nextInnerR, nextOuterR, curAngle, curAngle + childSpan);
                    curAngle += childSpan;
                }
            }
        }

        if (_lastRoots.Count == 1)
        {
            var root = _lastRoots[0];
            RenderSector(root, 0, 0, RootRadius, 0, 2 * Math.PI);
        }
        else
        {
            int totalLeaves = _lastRoots.Sum(GetSubtreeLeafCount);
            double curAngle = 0.0;
            foreach (var root in _lastRoots)
            {
                int rLeaves = GetSubtreeLeafCount(root);
                double rSpan = (double)rLeaves / totalLeaves * (2 * Math.PI);
                RenderSector(root, 0, 0, RootRadius, curAngle, curAngle + rSpan);
                curAngle += rSpan;
            }
        }

        if (prevHOffset > 0 || prevVOffset > 0)
        {
            ChartScrollViewer.ScrollToHorizontalOffset(prevHOffset);
            ChartScrollViewer.ScrollToVerticalOffset(prevVOffset);
        }

        UpdateContainerAlignment();
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

        if (LayoutDirection == OrgChartLayoutDirection.Sunburst)
        {
            RenderSunburstInternal(prevHOffset, prevVOffset);
            return;
        }

        var layoutMap = ComputeLayout(_lastRoots, LayoutDirection);
        if (layoutMap.Count == 0)
        {
            RootCanvas.Width = 0;
            RootCanvas.Height = 0;
            return;
        }

        double maxX = layoutMap.Values.Max(info => info.X + info.Width);
        double maxY = layoutMap.Values.Max(info => info.Y + info.Height);

        RootCanvas.Width = Math.Max(maxX + ChartPadding, 100);
        RootCanvas.Height = Math.Max(maxY + ChartPadding, 100);

        // ---- Connectors first, so node boxes visually sit on top of the lines. ----
        // Group all connector figures by stroke color (depth palette) into frozen PathGeometries,
        // reducing hundreds of individual Path FrameworkElements down to ~4-6 composite Paths.
        var connectorGeometries = new Dictionary<int, PathGeometry>();

        void DrawConnectors(FolderNode node)
        {
            if (!node.IsExpanded || !layoutMap.TryGetValue(node, out var parentLayout)) return;

            foreach (var child in node.Children)
            {
                if (!layoutMap.TryGetValue(child, out var childLayout)) continue;

                Point startPoint, endPoint;
                if (LayoutDirection == OrgChartLayoutDirection.Vertical)
                {
                    startPoint = new Point(parentLayout.CenterX, parentLayout.Y + parentLayout.Height);
                    endPoint = new Point(childLayout.CenterX, childLayout.Y);
                }
                else if (LayoutDirection == OrgChartLayoutDirection.Mindmap)
                {
                    if (childLayout.IsLeftBranch)
                    {
                        startPoint = new Point(parentLayout.X, parentLayout.CenterY);
                        endPoint = new Point(childLayout.X + childLayout.Width, childLayout.CenterY);
                    }
                    else
                    {
                        startPoint = new Point(parentLayout.X + parentLayout.Width, parentLayout.CenterY);
                        endPoint = new Point(childLayout.X, childLayout.CenterY);
                    }
                }
                else if (LayoutDirection == OrgChartLayoutDirection.Radial)
                {
                    startPoint = new Point(parentLayout.CenterX, parentLayout.CenterY);
                    endPoint = new Point(childLayout.CenterX, childLayout.CenterY);
                }
                else // Horizontal
                {
                    startPoint = new Point(parentLayout.X + parentLayout.Width, parentLayout.CenterY);
                    endPoint = new Point(childLayout.X, childLayout.CenterY);
                }

                var figure = new PathFigure { StartPoint = startPoint, IsFilled = false };

                switch (ConnectorStyle)
                {
                    case OrgChartConnectorStyle.Straight:
                        figure.Segments.Add(new LineSegment(endPoint, true));
                        break;

                    case OrgChartConnectorStyle.Curved:
                        if (LayoutDirection == OrgChartLayoutDirection.Vertical)
                        {
                            double dy = (endPoint.Y - startPoint.Y) * 0.5;
                            var c1 = new Point(startPoint.X, startPoint.Y + dy);
                            var c2 = new Point(endPoint.X, endPoint.Y - dy);
                            figure.Segments.Add(new BezierSegment(c1, c2, endPoint, true));
                        }
                        else if (LayoutDirection == OrgChartLayoutDirection.Radial)
                        {
                            double pAngle = parentLayout.Angle;
                            double cAngle = childLayout.Angle;
                            double midR = (parentLayout.Radius + childLayout.Radius) / 2.0;
                            var rootInfo = layoutMap.Values.FirstOrDefault(i => i.Depth == 0);
                            double rcX = rootInfo?.CenterX ?? 0;
                            double rcY = rootInfo?.CenterY ?? 0;
                            Point c1 = new Point(rcX + midR * Math.Cos(pAngle), rcY + midR * Math.Sin(pAngle));
                            Point c2 = new Point(rcX + midR * Math.Cos(cAngle), rcY + midR * Math.Sin(cAngle));
                            figure.Segments.Add(new BezierSegment(c1, c2, endPoint, true));
                        }
                        else // Horizontal and Mindmap
                        {
                            double dx = (endPoint.X - startPoint.X) * 0.5;
                            var c1 = new Point(startPoint.X + dx, startPoint.Y);
                            var c2 = new Point(endPoint.X - dx, endPoint.Y);
                            figure.Segments.Add(new BezierSegment(c1, c2, endPoint, true));
                        }
                        break;

                    case OrgChartConnectorStyle.Orthogonal:
                    default:
                        if (LayoutDirection == OrgChartLayoutDirection.Vertical)
                        {
                            double midY = (startPoint.Y + endPoint.Y) / 2.0;
                            figure.Segments.Add(new LineSegment(new Point(startPoint.X, midY), true));
                            figure.Segments.Add(new LineSegment(new Point(endPoint.X, midY), true));
                            figure.Segments.Add(new LineSegment(endPoint, true));
                        }
                        else if (LayoutDirection == OrgChartLayoutDirection.Radial)
                        {
                            double pAngle = parentLayout.Angle;
                            double cAngle = childLayout.Angle;
                            double midR = (parentLayout.Radius + childLayout.Radius) / 2.0;
                            var rootInfo = layoutMap.Values.FirstOrDefault(i => i.Depth == 0);
                            double rcX = rootInfo?.CenterX ?? 0;
                            double rcY = rootInfo?.CenterY ?? 0;
                            Point c1 = new Point(rcX + midR * Math.Cos(pAngle), rcY + midR * Math.Sin(pAngle));
                            Point c2 = new Point(rcX + midR * Math.Cos(cAngle), rcY + midR * Math.Sin(cAngle));
                            figure.Segments.Add(new BezierSegment(c1, c2, endPoint, true));
                        }
                        else // Horizontal and Mindmap
                        {
                            double midX = (startPoint.X + endPoint.X) / 2.0;
                            figure.Segments.Add(new LineSegment(new Point(midX, startPoint.Y), true));
                            figure.Segments.Add(new LineSegment(new Point(midX, endPoint.Y), true));
                            figure.Segments.Add(new LineSegment(endPoint, true));
                        }
                        break;
                }

                if (!connectorGeometries.TryGetValue(childLayout.Depth, out var geometry))
                {
                    geometry = new PathGeometry();
                    connectorGeometries[childLayout.Depth] = geometry;
                }
                geometry.Figures.Add(figure);

                DrawConnectors(child);
            }
        }

        foreach (var root in _lastRoots)
            DrawConnectors(root);

        foreach (var (depth, geometry) in connectorGeometries)
        {
            geometry.Freeze();
            RootCanvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = GetCachedBrush(GetPalette(depth).Border),
                StrokeThickness = 1.6,
                IsHitTestVisible = false
            });
        }

        // ---- Node boxes. ----
        bool isDark = IsDarkTheme();

        foreach (var info in layoutMap.Values)
        {
            var node = info.Node;
            var (fill, border, textCol) = GetPalette(info.Depth);
            bool isSelected = ReferenceEquals(node, _lastSelected);
            bool isMatch = node.IsMatchingSearch;

            Brush boxBackground = isMatch ? SearchMatchBackgroundBrush : GetCachedBrush(fill);
            Brush normalBorderBrush = isMatch ? SearchMatchBorderBrush : GetCachedBrush(border);
            Brush textForeground = GetCachedBrush(textCol);
            Brush badgeForeground = isDark ? GetCachedBrush(Color.FromRgb(0x94, 0xA3, 0xB8)) : GetCachedBrush(Color.FromRgb(0x47, 0x55, 0x69));

            if (node.DiffStatus == NodeDiffStatus.MissingOnDisk)
            {
                normalBorderBrush = GetCachedBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Emerald Green
                boxBackground = isDark
                    ? GetCachedBrush(Color.FromRgb(0x06, 0x4E, 0x3B))
                    : GetCachedBrush(Color.FromRgb(0xD1, 0xFA, 0xE5));
                textForeground = isDark
                    ? GetCachedBrush(Color.FromRgb(0xEC, 0xFD, 0xF5))
                    : GetCachedBrush(Color.FromRgb(0x06, 0x5F, 0x46));
                badgeForeground = GetCachedBrush(Color.FromRgb(0x34, 0xD3, 0x99));
            }
            else if (node.DiffStatus == NodeDiffStatus.MatchesDisk)
            {
                normalBorderBrush = GetCachedBrush(Color.FromRgb(0x64, 0x74, 0x8B)); // Slate Neutral
                boxBackground = isDark
                    ? GetCachedBrush(Color.FromRgb(0x1E, 0x29, 0x3B))
                    : GetCachedBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
                textForeground = isDark
                    ? GetCachedBrush(Color.FromRgb(0xF1, 0xF5, 0xF9))
                    : GetCachedBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
                badgeForeground = GetCachedBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            }
            else if (node.DiffStatus == NodeDiffStatus.ExtraOnDisk)
            {
                normalBorderBrush = GetCachedBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // Amber
                boxBackground = isDark
                    ? GetCachedBrush(Color.FromRgb(0x45, 0x1A, 0x03))
                    : GetCachedBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
                textForeground = isDark
                    ? GetCachedBrush(Color.FromRgb(0xFE, 0xF3, 0xC7))
                    : GetCachedBrush(Color.FromRgb(0x78, 0x35, 0x0F));
                badgeForeground = GetCachedBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            }
            else if (isMatch)
            {
                textForeground = isDark ? GetCachedBrush(Color.FromRgb(0xFE, 0xF0, 0x8A)) : GetCachedBrush(Color.FromRgb(0x85, 0x4D, 0x0E));
                if (isDark)
                {
                    boxBackground = GetCachedBrush(Color.FromRgb(0x42, 0x20, 0x06));
                }
            }

            Brush boxBorderBrush = isSelected ? GetSelectedBorderBrush() : normalBorderBrush;

            var namePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };

            if (isSelected)
            {
                namePanel.Children.Add(new TextBlock
                {
                    Text = "● ",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = isDark ? GetCachedBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)) : GetCachedBrush(Color.FromRgb(0x0D, 0x94, 0x88)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 2, 0)
                });
            }

            var nameTextBlock = new TextBlock
            {
                Text = node.Name,
                FontSize = 12.0,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = textForeground,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center
            };
            namePanel.Children.Add(nameTextBlock);

            UIElement boxContent;
            if (node.HasDiffBadge)
            {
                var boxStack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                boxStack.Children.Add(namePanel);
                boxStack.Children.Add(new TextBlock
                {
                    Text = node.DiffBadgeText,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = badgeForeground,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                boxContent = boxStack;
            }
            else
            {
                boxContent = namePanel;
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
                Child = boxContent,
                Tag = new NodeVisualMeta(info.Depth, normalBorderBrush, boxBackground, textForeground, isMatch, node.HasDiffBadge, namePanel, nameTextBlock)
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

            Brush originalBorder = normalBorderBrush;
            box.MouseEnter += (s, e) =>
            {
                if (!ReferenceEquals(node, _lastSelected))
                {
                    box.BorderBrush = isDark ? GetCachedBrush(Color.FromArgb(0xDD, 0x5E, 0xEA, 0xD4)) : GetCachedBrush(Color.FromArgb(0xDD, 0x14, 0xB8, 0xA6));
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
                    box.BorderBrush = isDark ? GetCachedBrush(Color.FromRgb(0x5E, 0xEA, 0xD4)) : GetCachedBrush(Color.FromRgb(0x0D, 0x94, 0x88));
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

            box.MouseRightButtonUp += (s, e) =>
            {
                box.ContextMenu = CreateNodeContextMenu(node, box);
                box.ContextMenu.PlacementTarget = box;
                box.ContextMenu.IsOpen = true;
                e.Handled = true;
            };

            box.ContextMenuOpening += (s, e) =>
            {
                box.ContextMenu = CreateNodeContextMenu(node, box);
            };

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
                        ? (isDark ? GetCachedBrush(Color.FromRgb(0xCF, 0xFA, 0xFE)) : GetCachedBrush(Color.FromRgb(0x0F, 0x17, 0x2A)))
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
                        ? (isDark ? GetCachedBrush(Color.FromRgb(0x13, 0x2B, 0x39)) : GetCachedBrush(Color.FromRgb(0xEE, 0xF2, 0xF6)))
                        : (isDark ? GetCachedBrush(Color.FromRgb(0x0F, 0x76, 0x6E)) : GetCachedBrush(Color.FromRgb(0x0D, 0x94, 0x88))),
                    BorderBrush = expanded
                        ? (isDark ? GetCachedBrush(Color.FromRgb(0x38, 0xBD, 0xF8)) : GetCachedBrush(Color.FromRgb(0x02, 0x84, 0xC7)))
                        : (isDark ? GetCachedBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)) : GetCachedBrush(Color.FromRgb(0x14, 0xB8, 0xA6))),
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
                if (LayoutDirection == OrgChartLayoutDirection.Vertical)
                {
                    badgeLeft = info.CenterX - badgeWidth / 2.0;
                    badgeTop = info.Y + info.Height - (badgeHeight / 2.0);
                }
                else if (LayoutDirection == OrgChartLayoutDirection.Mindmap)
                {
                    if (info.IsLeftBranch)
                    {
                        badgeLeft = info.X - badgeWidth / 2.0;
                        badgeTop = info.CenterY - (badgeHeight / 2.0);
                    }
                    else
                    {
                        badgeLeft = info.X + info.Width - (badgeWidth / 2.0);
                        badgeTop = info.CenterY - (badgeHeight / 2.0);
                    }
                }
                else if (LayoutDirection == OrgChartLayoutDirection.Radial)
                {
                    double cos = Math.Cos(info.Angle);
                    if (cos >= 0)
                    {
                        badgeLeft = info.X + info.Width - badgeWidth / 2.0;
                        badgeTop = info.CenterY - badgeHeight / 2.0;
                    }
                    else
                    {
                        badgeLeft = info.X - badgeWidth / 2.0;
                        badgeTop = info.CenterY - badgeHeight / 2.0;
                    }
                }
                else // Horizontal
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
        var (gw, gh) = GetNodeDimensions(_draggedNode, depth, LayoutDirection == OrgChartLayoutDirection.Vertical);

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
        if (double.IsNaN(RootCanvas.Width) || double.IsNaN(RootCanvas.Height) || RootCanvas.Width <= 0 || RootCanvas.Height <= 0)
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
        if (_lastRoots.Count == 0 || double.IsNaN(RootCanvas.Width) || double.IsNaN(RootCanvas.Height) || RootCanvas.Width <= 0 || RootCanvas.Height <= 0)
            throw new InvalidOperationException("Diagram canvas is empty.");

        if (LayoutDirection == OrgChartLayoutDirection.Sunburst)
        {
            var rtbSunburst = RenderDiagramToBitmap(2.0);
            using var ms = new MemoryStream();
            var pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(rtbSunburst));
            pngEncoder.Save(ms);
            string base64Png = Convert.ToBase64String(ms.ToArray());

            double w = RootCanvas.Width;
            double h = RootCanvas.Height;
            var sbSunburst = new StringBuilder();
            sbSunburst.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sbSunburst.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w.ToString("F1", CultureInfo.InvariantCulture)}\" height=\"{h.ToString("F1", CultureInfo.InvariantCulture)}\" viewBox=\"0 0 {w.ToString("F1", CultureInfo.InvariantCulture)} {h.ToString("F1", CultureInfo.InvariantCulture)}\">");
            sbSunburst.AppendLine($"  <image width=\"{w.ToString("F1", CultureInfo.InvariantCulture)}\" height=\"{h.ToString("F1", CultureInfo.InvariantCulture)}\" href=\"data:image/png;base64,{base64Png}\"/>");
            sbSunburst.AppendLine("</svg>");
            File.WriteAllText(filePath, sbSunburst.ToString(), Encoding.UTF8);
            return;
        }

        var layoutMap = ComputeLayout(_lastRoots, LayoutDirection);
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

                Point startPoint, endPoint;
                if (LayoutDirection == OrgChartLayoutDirection.Vertical)
                {
                    startPoint = new Point(parentLayout.CenterX, parentLayout.Y + parentLayout.Height);
                    endPoint = new Point(childLayout.CenterX, childLayout.Y);
                }
                else if (LayoutDirection == OrgChartLayoutDirection.Mindmap)
                {
                    if (childLayout.IsLeftBranch)
                    {
                        startPoint = new Point(parentLayout.X, parentLayout.CenterY);
                        endPoint = new Point(childLayout.X + childLayout.Width, childLayout.CenterY);
                    }
                    else
                    {
                        startPoint = new Point(parentLayout.X + parentLayout.Width, parentLayout.CenterY);
                        endPoint = new Point(childLayout.X, childLayout.CenterY);
                    }
                }
                else if (LayoutDirection == OrgChartLayoutDirection.Radial)
                {
                    startPoint = new Point(parentLayout.CenterX, parentLayout.CenterY);
                    endPoint = new Point(childLayout.CenterX, childLayout.CenterY);
                }
                else // Horizontal
                {
                    startPoint = new Point(parentLayout.X + parentLayout.Width, parentLayout.CenterY);
                    endPoint = new Point(childLayout.X, childLayout.CenterY);
                }

                string strokeColor = ToHexColor(GetPalette(childLayout.Depth).Border);

                switch (ConnectorStyle)
                {
                    case OrgChartConnectorStyle.Straight:
                        sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} L {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        break;

                    case OrgChartConnectorStyle.Curved:
                        if (LayoutDirection == OrgChartLayoutDirection.Vertical)
                        {
                            double dy = (endPoint.Y - startPoint.Y) * 0.5;
                            Point c1 = new Point(startPoint.X, startPoint.Y + dy);
                            Point c2 = new Point(endPoint.X, endPoint.Y - dy);
                            sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} C {c1.X.ToString("F1", CultureInfo.InvariantCulture)},{c1.Y.ToString("F1", CultureInfo.InvariantCulture)} {c2.X.ToString("F1", CultureInfo.InvariantCulture)},{c2.Y.ToString("F1", CultureInfo.InvariantCulture)} {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        }
                        else if (LayoutDirection == OrgChartLayoutDirection.Radial)
                        {
                            double pAngle = parentLayout.Angle;
                            double cAngle = childLayout.Angle;
                            double midR = (parentLayout.Radius + childLayout.Radius) / 2.0;
                            var rootInfo = layoutMap.Values.FirstOrDefault(i => i.Depth == 0);
                            double rcX = rootInfo?.CenterX ?? 0;
                            double rcY = rootInfo?.CenterY ?? 0;
                            Point c1 = new Point(rcX + midR * Math.Cos(pAngle), rcY + midR * Math.Sin(pAngle));
                            Point c2 = new Point(rcX + midR * Math.Cos(cAngle), rcY + midR * Math.Sin(cAngle));
                            sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} C {c1.X.ToString("F1", CultureInfo.InvariantCulture)},{c1.Y.ToString("F1", CultureInfo.InvariantCulture)} {c2.X.ToString("F1", CultureInfo.InvariantCulture)},{c2.Y.ToString("F1", CultureInfo.InvariantCulture)} {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        }
                        else // Horizontal and Mindmap
                        {
                            double dx = (endPoint.X - startPoint.X) * 0.5;
                            Point c1 = new Point(startPoint.X + dx, startPoint.Y);
                            Point c2 = new Point(endPoint.X - dx, endPoint.Y);
                            sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} C {c1.X.ToString("F1", CultureInfo.InvariantCulture)},{c1.Y.ToString("F1", CultureInfo.InvariantCulture)} {c2.X.ToString("F1", CultureInfo.InvariantCulture)},{c2.Y.ToString("F1", CultureInfo.InvariantCulture)} {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        }
                        break;

                    case OrgChartConnectorStyle.Orthogonal:
                    default:
                        if (LayoutDirection == OrgChartLayoutDirection.Vertical)
                        {
                            double midY = (startPoint.Y + endPoint.Y) / 2.0;
                            sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} L {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{midY.ToString("F1", CultureInfo.InvariantCulture)} L {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{midY.ToString("F1", CultureInfo.InvariantCulture)} L {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        }
                        else if (LayoutDirection == OrgChartLayoutDirection.Radial)
                        {
                            double pAngle = parentLayout.Angle;
                            double cAngle = childLayout.Angle;
                            double midR = (parentLayout.Radius + childLayout.Radius) / 2.0;
                            var rootInfo = layoutMap.Values.FirstOrDefault(i => i.Depth == 0);
                            double rcX = rootInfo?.CenterX ?? 0;
                            double rcY = rootInfo?.CenterY ?? 0;
                            Point c1 = new Point(rcX + midR * Math.Cos(pAngle), rcY + midR * Math.Sin(pAngle));
                            Point c2 = new Point(rcX + midR * Math.Cos(cAngle), rcY + midR * Math.Sin(cAngle));
                            sb.AppendLine($"  <path d=\"M {startPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{startPoint.Y.ToString("F1", CultureInfo.InvariantCulture)} C {c1.X.ToString("F1", CultureInfo.InvariantCulture)},{c1.Y.ToString("F1", CultureInfo.InvariantCulture)} {c2.X.ToString("F1", CultureInfo.InvariantCulture)},{c2.Y.ToString("F1", CultureInfo.InvariantCulture)} {endPoint.X.ToString("F1", CultureInfo.InvariantCulture)},{endPoint.Y.ToString("F1", CultureInfo.InvariantCulture)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                        }
                        else // Horizontal and Mindmap
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
                double badgeX, badgeY;
                if (LayoutDirection == OrgChartLayoutDirection.Vertical)
                {
                    badgeX = info.CenterX - badgeWidth / 2.0;
                    badgeY = info.Y + info.Height - (badgeHeight / 2.0);
                }
                else if (LayoutDirection == OrgChartLayoutDirection.Mindmap)
                {
                    badgeX = info.IsLeftBranch ? info.X - badgeWidth / 2.0 : info.X + info.Width - badgeWidth / 2.0;
                    badgeY = info.CenterY - (badgeHeight / 2.0);
                }
                else if (LayoutDirection == OrgChartLayoutDirection.Radial)
                {
                    double cos = Math.Cos(info.Angle);
                    badgeX = cos >= 0 ? info.X + info.Width - badgeWidth / 2.0 : info.X - badgeWidth / 2.0;
                    badgeY = info.CenterY - (badgeHeight / 2.0);
                }
                else
                {
                    badgeX = info.X + info.Width - (badgeWidth / 2.0);
                    badgeY = info.CenterY - (badgeHeight / 2.0);
                }

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
        using var writer = new StreamWriter(fileStream, Encoding.Latin1);

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

