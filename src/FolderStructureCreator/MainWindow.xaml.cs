using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FolderStructureCreator.Models;
using FolderStructureCreator.ViewModels;

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
        OrgChartHost.NodeMoved += (source, target) => ViewModel.MoveNode(source, target);
        OrgChartHost.StructureEdited += () => { }; // rename already applied directly to the model; nothing else to sync
        Loaded += (_, _) => ViewModel.UpdateWindowWidth(ActualWidth);
        SizeChanged += (_, _) => ViewModel.UpdateWindowWidth(ActualWidth);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsOrgChartView) or nameof(MainViewModel.SelectedStructureNode))
            RefreshOrgChartIfVisible();
    }

    private void RefreshOrgChartIfVisible()
    {
        if (ViewModel.IsOrgChartView)
            OrgChartHost.Render(ViewModel.RootFolders, ViewModel.SelectedStructureNode);
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

    // Double-click a folder name in the structure builder to rename it in place.
    private void StructureNodeText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement { DataContext: FolderNode node }) return;

        node.IsEditing = true;
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

    private void ZoomInOrgChart_Click(object sender, RoutedEventArgs e) => OrgChartHost.ZoomIn();
    private void ZoomOutOrgChart_Click(object sender, RoutedEventArgs e) => OrgChartHost.ZoomOut();
    private void FitOrgChart_Click(object sender, RoutedEventArgs e) => OrgChartHost.FitToView();
}
