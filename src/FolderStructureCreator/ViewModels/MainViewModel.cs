using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Windows;
using FolderStructureCreator.Models;
using FolderStructureCreator.Services;
using Microsoft.Win32;

namespace FolderStructureCreator.ViewModels;

public class MainViewModel : ViewModelBase
{
    /// <summary>At this width, the destination browser and chart have comfortable space side by side.</summary>
    public const double SideBySideOrgChartWidth = 1500;
    // A chart creates one WPF control per folder. Keep this deliberately lower than the
    // general import limit so folders such as AppData remain useful without slowing the UI.
    private const int MaxOrgChartNodes = 750;
    // ---- Left pane: live Windows directory browser ----
    public ObservableCollection<FileSystemNode> Drives { get; } = new();

    private FileSystemNode? _selectedTargetNode;
    public FileSystemNode? SelectedTargetNode
    {
        get => _selectedTargetNode;
        set
        {
            if (SetField(ref _selectedTargetNode, value) && value is { IsPlaceholder: false })
            {
                TargetPath = value.FullPath;
                value.IsExpanded = true; // clicking a folder also opens it, so you can drill down in one click
            }

            ShowSelectedFolderOrgChartCommand?.RaiseCanExecuteChanged();
        }
    }

    private string _targetPath = string.Empty;
    public string TargetPath
    {
        get => _targetPath;
        set
        {
            if (SetField(ref _targetPath, value))
            {
                OnPropertyChanged(nameof(TargetPathExists));
                CreateStructureCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool TargetPathExists => !string.IsNullOrWhiteSpace(TargetPath) && Directory.Exists(TargetPath);

    // ---- Right pane: structure blueprint being built ----
    public ObservableCollection<FolderNode> RootFolders { get; } = new();

    private FolderNode? _selectedStructureNode;
    public FolderNode? SelectedStructureNode
    {
        get => _selectedStructureNode;
        set
        {
            if (ReferenceEquals(_selectedStructureNode, value)) return;

            if (_selectedStructureNode is not null)
                _selectedStructureNode.IsSelected = false;

            if (SetField(ref _selectedStructureNode, value))
            {
                if (_selectedStructureNode is not null)
                    _selectedStructureNode.IsSelected = true;

                OnPropertyChanged(nameof(HasSelectedStructureNode));
            }
        }
    }

    /// <summary>True once a folder in the plan is selected - drives whether the edit toolbar (Child/Sibling/Rename/Delete) is shown at all.</summary>
    public bool HasSelectedStructureNode => SelectedStructureNode != null;

    /// <summary>Drives the builder's empty-state prompt.</summary>
    public bool HasStructureNodes => RootFolders.Count > 0;

    private string _quickAddNames = string.Empty;
    /// <summary>Comma-separated names typed into the quick-add box, e.g. "src, docs, tests".</summary>
    public string QuickAddNames
    {
        get => _quickAddNames;
        set
        {
            if (SetField(ref _quickAddNames, value))
                OnPropertyChanged(nameof(HasQuickAddText));
        }
    }

    /// <summary>True once text is typed in the quick-add box - drives whether the "Add as children" button is shown.</summary>
    public bool HasQuickAddText => !string.IsNullOrWhiteSpace(QuickAddNames);

    private string _statusMessage = "Build a structure on the right, choose a target folder on the left, then click Create Structure.";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    private bool _isOrgChartView;
    /// <summary>False = editable TreeView, True = the read-visual org-chart diagram (auto-enabled after an import).</summary>
    public bool IsOrgChartView
    {
        get => _isOrgChartView;
        set
        {
            if (SetField(ref _isOrgChartView, value))
                OnPropertyChanged(nameof(ShouldShowDestinationSidebarToggle));
        }
    }

    private bool _isDestinationSidebarCollapsed;
    /// <summary>Lets the chart use the full workspace on smaller screens without clearing the selected target.</summary>
    public bool IsDestinationSidebarCollapsed
    {
        get => _isDestinationSidebarCollapsed;
        set => SetField(ref _isDestinationSidebarCollapsed, value);
    }

    private bool _isWideWindow;
    /// <summary>Only smaller windows need a way to let the chart temporarily use the sidebar's space.</summary>
    public bool ShouldShowDestinationSidebarToggle => IsOrgChartView && !_isWideWindow;

    /// <summary>Called by the window when it is first shown and whenever it is resized.</summary>
    public void UpdateWindowWidth(double width)
    {
        var isWideWindow = width >= SideBySideOrgChartWidth;
        if (!SetField(ref _isWideWindow, isWideWindow)) return;

        OnPropertyChanged(nameof(ShouldShowDestinationSidebarToggle));
        if (isWideWindow)
            IsDestinationSidebarCollapsed = false;
    }

    /// <summary>Raised whenever the plan's shape changes (add/remove/import/clear), so any view
    /// that draws its own visualization (like the org chart) knows to redraw.</summary>
    public event Action? StructureChanged;
    private void RaiseStructureChanged() => StructureChanged?.Invoke();

    public int TotalFolderCount => RootFolders.Sum(r => r.CountFoldersOnly());

    // ---- Commands ----
    public RelayCommand AddRootFolderCommand { get; }
    public RelayCommand AddChildFolderCommand { get; }
    public RelayCommand AddSiblingFolderCommand { get; }
    public RelayCommand QuickAddCommand { get; }
    public RelayCommand DeleteNodeCommand { get; }
    public RelayCommand RefreshDrivesCommand { get; }
    public RelayCommand ShowSelectedFolderOrgChartCommand { get; }
    public RelayCommand CreateStructureCommand { get; }
    public RelayCommand StartRenameCommand { get; }
    public RelayCommand ImportFromReferenceCommand { get; }
    public RelayCommand ClearPlanCommand { get; }
    public RelayCommand ShowTreeViewCommand { get; }
    public RelayCommand ShowOrgChartViewCommand { get; }
    public RelayCommand ToggleDestinationSidebarCommand { get; }

    public MainViewModel()
    {
        AddRootFolderCommand = new RelayCommand(_ => AddRootFolder());
        AddChildFolderCommand = new RelayCommand(_ => AddChild(), _ => SelectedStructureNode is { IsFile: false });
        AddSiblingFolderCommand = new RelayCommand(_ => AddSibling(), _ => SelectedStructureNode != null);
        QuickAddCommand = new RelayCommand(_ => QuickAdd(), _ => !string.IsNullOrWhiteSpace(QuickAddNames));
        DeleteNodeCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedStructureNode != null);
        RefreshDrivesCommand = new RelayCommand(_ => LoadDrives());
        ShowSelectedFolderOrgChartCommand = new RelayCommand(_ => ShowSelectedFolderOrgChart(), _ => SelectedTargetNode is { IsPlaceholder: false });
        CreateStructureCommand = new RelayCommand(_ => CreateStructure(), _ => CanCreateStructure());
        StartRenameCommand = new RelayCommand(param => StartRename(param as FolderNode));
        ImportFromReferenceCommand = new RelayCommand(async _ => await ImportFromReferenceAsync());
        ClearPlanCommand = new RelayCommand(_ => ClearPlan(), _ => RootFolders.Count > 0);
        ShowTreeViewCommand = new RelayCommand(_ => IsOrgChartView = false);
        ShowOrgChartViewCommand = new RelayCommand(_ => IsOrgChartView = true);
        ToggleDestinationSidebarCommand = new RelayCommand(_ => IsDestinationSidebarCollapsed = !IsDestinationSidebarCollapsed);

        LoadDrives();
        AddRootFolder(); // start with one editable root node so the tree isn't empty
        RootFolders.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalFolderCount));
            OnPropertyChanged(nameof(HasStructureNodes));
            ClearPlanCommand.RaiseCanExecuteChanged();
            CreateStructureCommand.RaiseCanExecuteChanged();
        };
    }

    private void LoadDrives()
    {
        Drives.Clear();
        foreach (var drivePath in FileSystemService.GetDrives())
            Drives.Add(new FileSystemNode(drivePath, drivePath));
    }

    /// <summary>
    /// Copies the currently selected real folder into the editable plan and shows its
    /// hierarchy as the org chart. This is the left-pane equivalent of choosing a
    /// reference folder through the file picker.
    /// </summary>
    private async void ShowSelectedFolderOrgChart()
    {
        if (SelectedTargetNode is not { IsPlaceholder: false } node) return;

        StatusMessage = $"Reading \"{node.Name}\" for the org chart…";

        try
        {
            var importResult = await Task.Run(() => FileSystemService.BuildFolderNodeTree(node.FullPath, MaxOrgChartNodes));
            RootFolders.Clear();
            RootFolders.Add(importResult.Root);
            SelectedStructureNode = importResult.Root;
            IsOrgChartView = true;

            StatusMessage = importResult.Truncated
                ? $"Showing \"{importResult.Root.Name}\" — {importResult.FolderCount} folder(s). Only the first {MaxOrgChartNodes} folders are shown so the chart stays responsive."
                : $"Showing \"{importResult.Root.Name}\" — {importResult.FolderCount} folder(s) in the org chart.";

            OnPropertyChanged(nameof(TotalFolderCount));
            RaiseStructureChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            StatusMessage = $"Could not read the selected folder: {ex.Message}";
        }
    }

    // ---- Structure editing ----

    private void AddRootFolder()
    {
        var node = new FolderNode("New Folder");
        RootFolders.Add(node);
        SelectedStructureNode = node;
        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    private void AddChild()
    {
        if (SelectedStructureNode is not { IsFile: false }) return;
        var child = new FolderNode("New Subfolder", SelectedStructureNode);
        SelectedStructureNode.Children.Add(child);
        SelectedStructureNode.IsExpanded = true;
        SelectedStructureNode = child;
        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    private void AddSibling()
    {
        if (SelectedStructureNode is null) return;
        var parent = SelectedStructureNode.Parent;
        var sibling = new FolderNode("New Folder", parent);

        if (parent is null)
            RootFolders.Add(sibling);
        else
            parent.Children.Add(sibling);

        SelectedStructureNode = sibling;
        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    /// <summary>Adds one or more comma-separated folder names as children of the selected node (or as new roots if nothing is selected).</summary>
    private void QuickAdd()
    {
        var names = QuickAddNames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => n.Length > 0)
            .ToList();

        if (names.Count == 0) return;

        // Files aren't valid parents - fall back to adding as new roots instead of under a file.
        var targetParent = SelectedStructureNode is { IsFile: false } ? SelectedStructureNode : null;

        FolderNode? lastAdded = null;
        foreach (var name in names)
        {
            var node = new FolderNode(name, targetParent);
            if (targetParent != null)
            {
                targetParent.Children.Add(node);
                targetParent.IsExpanded = true;
            }
            else
            {
                RootFolders.Add(node);
            }
            lastAdded = node;
        }

        QuickAddNames = string.Empty;
        if (lastAdded != null) SelectedStructureNode = lastAdded;
        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    private void DeleteSelected()
    {
        if (SelectedStructureNode is null) return;

        var confirm = MessageBox.Show(
            $"Delete \"{SelectedStructureNode.Name}\" and everything nested under it from the blueprint?\n\n(This only affects the plan on screen - nothing on disk is touched.)",
            "Confirm delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        var node = SelectedStructureNode;
        if (node.Parent is null)
            RootFolders.Remove(node);
        else
            node.Parent.Children.Remove(node);

        SelectedStructureNode = null;
        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    private void StartRename(FolderNode? node)
    {
        node ??= SelectedStructureNode;
        if (node is null) return;
        node.IsEditing = true;
    }

    // ---- Import an existing folder's structure ----

    private async Task ImportFromReferenceAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a reference folder to copy the structure from",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;

        var sourcePath = dialog.FolderName;
        StatusMessage = $"Reading \"{Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar))}\"…";

        try
        {
            var importResult = await Task.Run(() => FileSystemService.BuildFolderNodeTree(sourcePath));
            RootFolders.Add(importResult.Root);
            SelectedStructureNode = importResult.Root;
            IsOrgChartView = true; // an import reads best as the visual org-chart diagram

            var message = $"Imported \"{importResult.Root.Name}\" — {importResult.FolderCount} folder(s) (folders only, files were not read or copied). ";

            message += importResult.Truncated
                ? $"Note: this folder is very large, so the import stopped early at a safety limit (~{FileSystemService.MaxImportTotalNodes} folders) to avoid freezing the app - not everything nested deep inside is shown."
                : "Choose a target on the left, then click Create Structure.";

            StatusMessage = message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not read reference folder: {ex.Message}";
        }

        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    private void ClearPlan()
    {
        if (RootFolders.Count == 0) return;

        var confirm = MessageBox.Show(
            "Clear the entire structure plan? This only affects the plan on screen - nothing on disk is touched.",
            "Confirm clear",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        RootFolders.Clear();
        SelectedStructureNode = null;
        StatusMessage = "Plan cleared. Add folders manually or import from an existing folder.";
        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    // ---- Structure creation on disk ----

    private bool CanCreateStructure() => TargetPathExists && RootFolders.Count > 0;

    private void CreateStructure()
    {
        if (!CanCreateStructure())
        {
            StatusMessage = TargetPathExists
                ? "Add at least one folder to the structure first."
                : "Choose a valid target folder on the left first.";
            return;
        }

        var result = FileSystemService.CreateStructure(RootFolders, TargetPath);

        if (result.Success)
        {
            StatusMessage = $"Done: {result.CreatedCount} folder(s) created" +
                             (result.AlreadyExistedCount > 0 ? $", {result.AlreadyExistedCount} already existed" : "") +
                             $" under \"{TargetPath}\".";
        }
        else
        {
            StatusMessage = $"Created {result.CreatedCount} folder(s), but hit {result.Errors.Count} error(s). See details below.";
            MessageBox.Show(
                string.Join(Environment.NewLine, result.Errors),
                "Some folders could not be created",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // Reflect the newly created folders in the live browser, expanded all the way down
        // so you can see the whole result without manually opening each level.
        SelectedTargetNode?.Refresh();
        ExpandCreatedTree(SelectedTargetNode, RootFolders);
    }

    /// <summary>Walks the live tree alongside the blueprint, expanding each matching real folder that was just created.</summary>
    private static void ExpandCreatedTree(FileSystemNode? liveParent, IEnumerable<FolderNode> blueprintChildren)
    {
        if (liveParent is null) return;

        liveParent.EnsureChildrenLoaded();

        foreach (var blueprintNode in blueprintChildren)
        {
            var sanitizedName = FileSystemService.SanitizeFolderName(blueprintNode.Name);
            var liveMatch = liveParent.Children.FirstOrDefault(c =>
                !c.IsPlaceholder && string.Equals(c.Name, sanitizedName, StringComparison.OrdinalIgnoreCase));

            if (liveMatch is null) continue;

            liveMatch.IsExpanded = true; // triggers lazy load of this level
            ExpandCreatedTree(liveMatch, blueprintNode.Children);
        }
    }
}
