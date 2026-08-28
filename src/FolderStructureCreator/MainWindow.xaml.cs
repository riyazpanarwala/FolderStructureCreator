using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FolderStructureCreator.Models;
using FolderStructureCreator.ViewModels;
using Microsoft.Win32;

namespace FolderStructureCreator;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.StructureChanged += RefreshOrgChartIfVisible;
        OrgChartHost.NodeClicked += node => ViewModel.SelectedStructureNode = node;
        OrgChartHost.NodeRenamed += (node, newName) => ViewModel.RenameNode(node, newName);
        OrgChartHost.NodeMoved += (source, target) => { if (target == null) ViewModel.MoveNodeToRoot(source); else ViewModel.MoveNode(source, target); };
        OrgChartHost.OpenInExplorerRequested += node => ViewModel.OpenInExplorerCommand.Execute(node);
        OrgChartHost.AddChildRequested += node => ViewModel.AddChildFolderCommand.Execute(null);
        OrgChartHost.AddSiblingRequested += node => ViewModel.AddSiblingFolderCommand.Execute(null);
        OrgChartHost.DeleteRequested += node => ViewModel.DeleteNodeCommand.Execute(null);
        OrgChartHost.ZoomLevelChanged += zoom => UpdateZoomPercentageDisplay(zoom);
        OrgChartHost.StructureEdited += () => { }; // rename already applied directly to the model; nothing else to sync
        Loaded += (_, _) => ViewModel.UpdateWindowWidth(ActualWidth);
        SizeChanged += (_, _) => ViewModel.UpdateWindowWidth(ActualWidth);
        KeyDown += Window_KeyDown;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.IsOrgChartView && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key is Key.OemPlus or Key.Add)
            {
                OrgChartHost.ZoomIn();
                e.Handled = true;
                return;
            }
            if (e.Key is Key.OemMinus or Key.Subtract)
            {
                OrgChartHost.ZoomOut();
                e.Handled = true;
                return;
            }
            if (e.Key is Key.D0 or Key.NumPad0)
            {
                OrgChartHost.ResetZoom();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                OrgChartHost.FitToView();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsOrgChartView) or nameof(MainViewModel.SelectedStructureNode))
            RefreshOrgChartIfVisible();
    }

    private void RefreshOrgChartIfVisible()
    {
        if (ViewModel.IsOrgChartView)
        {
            OrgChartHost.Render(ViewModel.RootFolders, ViewModel.SelectedStructureNode);
        }
    }

    private void PinnedFoldersTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is PinnedFolder pinned)
        {
            ViewModel.SelectPinnedFolder(pinned);
        }
        else if (e.NewValue is FileSystemNode node && !node.IsPlaceholder)
        {
            ViewModel.SelectedTargetNode = node;
        }
    }

    // TreeView.SelectedItem is read-only, so we bridge it into the view model here.
    private void DirectoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileSystemNode node)
            ViewModel.SelectedTargetNode = node;
    }

    private void StructureTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNode node)
            ViewModel.SelectedStructureNode = node;
    }

    private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem item)
        {
            item.BringIntoView();
        }
    }

    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasSearchQuery && ViewModel.SearchMatchCount > 0)
        {
            ViewModel.IsSearchDropdownOpen = true;
        }
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (ViewModel.NavigatePrevMatchCommand.CanExecute(null))
                    ViewModel.NavigatePrevMatchCommand.Execute(null);
            }
            else
            {
                if (ViewModel.NavigateNextMatchCommand.CanExecute(null))
                    ViewModel.NavigateNextMatchCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (ViewModel.IsSearchDropdownOpen)
            {
                ViewModel.IsSearchDropdownOpen = false;
            }
            else
            {
                ViewModel.ClearSearchCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    // Double-click a folder name in the structure builder to open it in Explorer.
    private void StructureNodeText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement { DataContext: FolderNode node }) return;

        ViewModel.OpenInExplorer(node);
        e.Handled = true;
    }

    private void RenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FolderNode node } box)
        {
            node.IsEditing = false;
            ViewModel.RenameNode(node, box.Text);
        }
    }

    private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FolderNode node } box) return;

        if (e.Key == Key.Enter)
        {
            node.IsEditing = false;
            ViewModel.RenameNode(node, box.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            node.IsEditing = false;
            e.Handled = true;
        }
    }

    private void UpdateZoomPercentageDisplay(double zoom)
    {
        ZoomPercentageButton.Content = $"{Math.Round(zoom * 100)}%";
    }

    private void ZoomInOrgChart_Click(object sender, RoutedEventArgs e) => OrgChartHost.ZoomIn();
    private void ZoomOutOrgChart_Click(object sender, RoutedEventArgs e) => OrgChartHost.ZoomOut();
    private void ResetZoomOrgChart_Click(object sender, RoutedEventArgs e) => OrgChartHost.ResetZoom();
    private void FitOrgChart_Click(object sender, RoutedEventArgs e) => OrgChartHost.FitToView();

    private void ExportOrgChart_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasStructureNodes)
        {
            MessageBox.Show("There are no folders in the plan to export.", "Export Diagram", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Dendrogram Diagram",
            Filter = "PNG Image (*.png)|*.png|SVG Vector (*.svg)|*.svg|PDF Document (*.pdf)|*.pdf",
            DefaultExt = ".png",
            FileName = "folder_structure_diagram"
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                switch (ext)
                {
                    case ".svg":
                        OrgChartHost.ExportToSvg(dialog.FileName);
                        break;
                    case ".pdf":
                        OrgChartHost.ExportToPdf(dialog.FileName);
                        break;
                    default:
                        OrgChartHost.ExportToPng(dialog.FileName);
                        break;
                }

                ViewModel.StatusMessage = $"Successfully exported diagram to {Path.GetFileName(dialog.FileName)}";
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to export diagram:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private Point _treeViewDragStartPoint;
    private FolderNode? _treeViewDraggedNode;

    private void StructureTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _treeViewDragStartPoint = e.GetPosition(null);
        _treeViewDraggedNode = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource)?.DataContext as FolderNode;
    }

    private void StructureTreeView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _treeViewDraggedNode == null) return;

        Point currentPos = e.GetPosition(null);
        Vector diff = _treeViewDragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            DataObject data = new DataObject("FolderNode", _treeViewDraggedNode);
            DragDrop.DoDragDrop(StructureTreeView, data, DragDropEffects.Move);
            _treeViewDraggedNode = null;
        }
    }

    private void StructureTreeView_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FolderNode"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void StructureTreeView_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FolderNode"))
        {
            var draggedNode = e.Data.GetData("FolderNode") as FolderNode;
            if (draggedNode != null)
            {
                var targetItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                var targetNode = targetItem?.DataContext as FolderNode;

                if (targetNode != null)
                {
                    ViewModel.MoveNode(draggedNode, targetNode);
                }
                else
                {
                    ViewModel.MoveNodeToRoot(draggedNode);
                }
            }
            e.Handled = true;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            await HandleExternalFileDrop(e);
            e.Handled = true;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            await HandleExternalFileDrop(e);
            e.Handled = true;
        }
    }

    private async Task HandleExternalFileDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
            {
                if (Directory.Exists(file))
                {
                    await ViewModel.ImportFolderFromPathAsync(file);
                }
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T ancestor) return ancestor;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
