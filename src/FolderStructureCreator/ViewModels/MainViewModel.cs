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

    // ---- Pinned Folders ----
    public ObservableCollection<PinnedFolder> PinnedFolders { get; } = new();
    public bool HasPinnedFolders => PinnedFolders.Count > 0;

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
            PinSelectedFolderCommand?.RaiseCanExecuteChanged();
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

    private bool _isLiveSyncMode = false;
    /// <summary>When true, folder additions, renames, deletions (Recycle Bin), and moves immediately modify physical folders on disk.</summary>
    public bool IsLiveSyncMode
    {
        get => _isLiveSyncMode;
        set => SetField(ref _isLiveSyncMode, value);
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

    private bool _isVerticalOrgChart;
    /// <summary>False = Horizontal (Left-to-Right), True = Vertical (Top-to-Bottom) dendrogram layout.</summary>
    public bool IsVerticalOrgChart
    {
        get => _isVerticalOrgChart;
        set
        {
            if (SetField(ref _isVerticalOrgChart, value))
            {
                OnPropertyChanged(nameof(OrgChartLayoutButtonText));
                RaiseStructureChanged();
            }
        }
    }

    public string OrgChartLayoutButtonText => IsVerticalOrgChart ? "Layout: Vertical ⬇" : "Layout: Horizontal ➡️";

    private bool _isMiniMapVisible = true;
    /// <summary>Controls whether the floating mini-map thumbnail overlay is visible in the Org Chart.</summary>
    public bool IsMiniMapVisible
    {
        get => _isMiniMapVisible;
        set => SetField(ref _isMiniMapVisible, value);
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

    private bool _isSortAscending = true;
    /// <summary>Toggles sorting order between Ascending (A-Z, 1..10) and Descending (Z-A, 10..1).</summary>
    public bool IsSortAscending
    {
        get => _isSortAscending;
        set
        {
            if (SetField(ref _isSortAscending, value))
            {
                FileSystemService.IsSortAscending = value;
                OnPropertyChanged(nameof(SortToggleText));
                ApplySortOrder();
            }
        }
    }

    public string SortToggleText => IsSortAscending ? "Sort: A-Z ⬇" : "Sort: Z-A ⬆";

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
    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value))
            {
                OnPropertyChanged(nameof(HasSearchQuery));
                ApplySearch();
            }
        }
    }

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    private bool _isSearchDropdownOpen;
    public bool IsSearchDropdownOpen
    {
        get => _isSearchDropdownOpen;
        set => SetField(ref _isSearchDropdownOpen, value);
    }

    public ObservableCollection<FolderNode> MatchingSearchResults { get; } = new();
    private int _currentSearchIndex = -1;
    public int CurrentSearchIndex
    {
        get => _currentSearchIndex;
        private set
        {
            if (SetField(ref _currentSearchIndex, value))
            {
                OnPropertyChanged(nameof(SearchMatchStatusText));
                NavigateNextMatchCommand?.RaiseCanExecuteChanged();
                NavigatePrevMatchCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public int SearchMatchCount => MatchingSearchResults.Count;
    public bool HasSearchMatches => SearchMatchCount > 0;

    public string SearchMatchStatusText
    {
        get
        {
            if (!HasSearchQuery) return string.Empty;
            if (SearchMatchCount == 0) return "No matches";
            return $"{CurrentSearchIndex + 1} of {SearchMatchCount} match{(SearchMatchCount == 1 ? "" : "es")}";
        }
    }

    public event Action? StructureChanged;
    private bool _isApplyingSearch;

    private void RaiseStructureChanged()
    {
        if (HasSearchQuery && !_isApplyingSearch)
        {
            ApplySearch();
            return;
        }
        StructureChanged?.Invoke();
    }

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
    public RelayCommand OpenInExplorerCommand { get; }
    public RelayCommand StartRenameCommand { get; }
    public RelayCommand ImportFromReferenceCommand { get; }
    public RelayCommand ClearPlanCommand { get; }
    public RelayCommand ShowTreeViewCommand { get; }
    public RelayCommand ShowOrgChartViewCommand { get; }
    public RelayCommand ToggleDestinationSidebarCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand NavigateNextMatchCommand { get; }
    public RelayCommand NavigatePrevMatchCommand { get; }
    public RelayCommand SelectSearchResultCommand { get; }
    public RelayCommand OpenSearchResultInExplorerCommand { get; }
    public RelayCommand PinSelectedFolderCommand { get; }
    public RelayCommand PinFolderCommand { get; }
    public RelayCommand UnpinFolderCommand { get; }
    public RelayCommand ShowPinnedFolderChartCommand { get; }
    public RelayCommand SelectPinnedFolderCommand { get; }
    public RelayCommand ToggleSortOrderCommand { get; }
    public RelayCommand ToggleOrgChartLayoutCommand { get; }
    public RelayCommand ToggleMiniMapCommand { get; }

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
        OpenInExplorerCommand = new RelayCommand(param => OpenInExplorer(param as FolderNode), _ => SelectedStructureNode != null || RootFolders.Count > 0);
        StartRenameCommand = new RelayCommand(param => StartRename(param as FolderNode));
        ImportFromReferenceCommand = new RelayCommand(async _ => await ImportFromReferenceAsync());
        ClearPlanCommand = new RelayCommand(_ => ClearPlan(), _ => RootFolders.Count > 0);
        ShowTreeViewCommand = new RelayCommand(_ => IsOrgChartView = false);
        ShowOrgChartViewCommand = new RelayCommand(_ => IsOrgChartView = true);
        ToggleDestinationSidebarCommand = new RelayCommand(_ => IsDestinationSidebarCollapsed = !IsDestinationSidebarCollapsed);
        ToggleOrgChartLayoutCommand = new RelayCommand(_ => IsVerticalOrgChart = !IsVerticalOrgChart);
        ToggleMiniMapCommand = new RelayCommand(_ => IsMiniMapVisible = !IsMiniMapVisible);
        ClearSearchCommand = new RelayCommand(_ => { SearchQuery = string.Empty; IsSearchDropdownOpen = false; });
        NavigateNextMatchCommand = new RelayCommand(_ => NavigateSearchMatch(1), _ => SearchMatchCount > 0);
        NavigatePrevMatchCommand = new RelayCommand(_ => NavigateSearchMatch(-1), _ => SearchMatchCount > 0);
        SelectSearchResultCommand = new RelayCommand(param =>
        {
            if (param is FolderNode node)
            {
                SelectedStructureNode = node;
                IsSearchDropdownOpen = false;
            }
        });
        OpenSearchResultInExplorerCommand = new RelayCommand(param =>
        {
            if (param is FolderNode node)
            {
                SelectedStructureNode = node;
                IsSearchDropdownOpen = false;
                OpenInExplorer(node);
            }
        });
        PinSelectedFolderCommand = new RelayCommand(_ => PinFolder(SelectedTargetNode?.FullPath), _ => SelectedTargetNode is { IsPlaceholder: false });
        PinFolderCommand = new RelayCommand(param => PinFolder(param as string ?? (param as FileSystemNode)?.FullPath ?? (param as PinnedFolder)?.Path));
        UnpinFolderCommand = new RelayCommand(param => UnpinFolder(param as PinnedFolder ?? PinnedFolders.FirstOrDefault(p => p.Path == (param as string))));
        ShowPinnedFolderChartCommand = new RelayCommand(async param => await ShowFolderOrgChartFromPathAsync(param as string ?? (param as PinnedFolder)?.Path));
        SelectPinnedFolderCommand = new RelayCommand(param => SelectPinnedFolder(param as PinnedFolder ?? (param is string s ? new PinnedFolder(s) : null)));
        ToggleSortOrderCommand = new RelayCommand(_ => IsSortAscending = !IsSortAscending);

        LoadDrives();
        LoadPinnedFolders();
        AddRootFolder(); // start with one editable root node so the tree isn't empty
        RootFolders.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalFolderCount));
            OnPropertyChanged(nameof(HasStructureNodes));
            ClearPlanCommand.RaiseCanExecuteChanged();
            CreateStructureCommand.RaiseCanExecuteChanged();
            if (HasSearchQuery) ApplySearch();
        };
    }

    private void ApplySortOrder()
    {
        SortPinnedFolders();
        foreach (var drive in Drives)
            drive.RefreshRecursive();
    }

    private void LoadDrives()
    {
        Drives.Clear();
        var drives = FileSystemService.GetDrives().ToList();
        drives.Sort((a, b) =>
        {
            int comp = NaturalStringComparer.Instance.Compare(a, b);
            return IsSortAscending ? comp : -comp;
        });
        foreach (var drivePath in drives)
            Drives.Add(new FileSystemNode(drivePath, drivePath));
    }

    private void LoadPinnedFolders()
    {
        PinnedFolders.Clear();
        var loaded = PinnedFoldersService.LoadPinnedFolders();
        loaded.Sort((a, b) =>
        {
            int comp = NaturalStringComparer.Instance.Compare(a.Name, b.Name);
            return IsSortAscending ? comp : -comp;
        });
        foreach (var pinned in loaded)
            PinnedFolders.Add(pinned);
        OnPropertyChanged(nameof(HasPinnedFolders));
    }

    private void SortPinnedFolders()
    {
        var list = PinnedFolders.ToList();
        list.Sort((a, b) =>
        {
            int comp = NaturalStringComparer.Instance.Compare(a.Name, b.Name);
            return IsSortAscending ? comp : -comp;
        });
        PinnedFolders.Clear();
        foreach (var item in list)
            PinnedFolders.Add(item);
    }

    public void PinFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(path))
        {
            StatusMessage = $"Cannot pin folder: \"{path}\" does not exist.";
            return;
        }

        if (PinnedFolders.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"\"{path}\" is already pinned.";
            return;
        }

        var pinned = new PinnedFolder(path);
        PinnedFolders.Add(pinned);
        SortPinnedFolders();
        PinnedFoldersService.SavePinnedFolders(PinnedFolders);
        OnPropertyChanged(nameof(HasPinnedFolders));
        StatusMessage = $"Pinned folder \"{pinned.Name}\" to the left panel.";
    }

    public void UnpinFolder(PinnedFolder? pinned)
    {
        if (pinned == null) return;
        PinnedFolders.Remove(pinned);
        PinnedFoldersService.SavePinnedFolders(PinnedFolders);
        OnPropertyChanged(nameof(HasPinnedFolders));
        StatusMessage = $"Unpinned folder \"{pinned.Name}\".";
    }

    public void SelectPinnedFolder(PinnedFolder? pinned)
    {
        if (pinned == null || string.IsNullOrWhiteSpace(pinned.Path)) return;
        TargetPath = pinned.Path;
        StatusMessage = $"Selected destination target: \"{pinned.Path}\"";
    }

    public async Task ShowFolderOrgChartFromPathAsync(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            StatusMessage = $"Folder path does not exist: \"{folderPath}\"";
            return;
        }

        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = folderPath;

        StatusMessage = $"Reading \"{name}\" for the org chart…";

        try
        {
            var importResult = await Task.Run(() => FileSystemService.BuildFolderNodeTree(folderPath, MaxOrgChartNodes));
            RootFolders.Clear();
            RootFolders.Add(importResult.Root);
            SelectedStructureNode = importResult.Root;
            TargetPath = folderPath;
            IsOrgChartView = true;
            IsLiveSyncMode = true;

            StatusMessage = importResult.Truncated
                ? $"Showing \"{importResult.Root.Name}\" — {importResult.FolderCount} folder(s). Live computer sync enabled."
                : $"Showing \"{importResult.Root.Name}\" — {importResult.FolderCount} folder(s) in the org chart. Live computer sync enabled.";

            OnPropertyChanged(nameof(TotalFolderCount));
            RaiseStructureChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            StatusMessage = $"Could not read the folder: {ex.Message}";
        }
    }

    /// <summary>
    /// Copies the currently selected real folder into the editable plan and shows its
    /// hierarchy as the org chart. This is the left-pane equivalent of choosing a
    /// reference folder through the file picker.
    /// </summary>
    private async void ShowSelectedFolderOrgChart()
    {
        if (SelectedTargetNode is not { IsPlaceholder: false } node) return;
        await ShowFolderOrgChartFromPathAsync(node.FullPath);
    }

    // ---- Structure editing ----

    private static string ComputeNodePathRelativeToTarget(FolderNode node, string targetPath)
    {
        if (!string.IsNullOrEmpty(node.RealPath)) return node.RealPath;

        var stack = new Stack<string>();
        var curr = node;
        while (curr != null)
        {
            if (!string.IsNullOrEmpty(curr.RealPath))
            {
                var basePath = curr.RealPath;
                while (stack.Count > 0)
                    basePath = Path.Combine(basePath, FileSystemService.SanitizeFolderName(stack.Pop()));
                return basePath;
            }

            stack.Push(curr.Name);
            curr = curr.Parent;
        }

        var resultPath = targetPath;
        while (stack.Count > 0)
            resultPath = Path.Combine(resultPath, FileSystemService.SanitizeFolderName(stack.Pop()));

        return resultPath;
    }

    private void AddRootFolder()
    {
        var folderName = "New Folder";
        if (IsLiveSyncMode && TargetPathExists)
        {
            var result = FileSystemService.CreateFolderOnDisk(TargetPath, folderName);
            if (result.Success)
            {
                var liveNode = new FolderNode(Path.GetFileName(result.NewPath), realPath: result.NewPath);
                liveNode.IsEditing = true;
                RootFolders.Add(liveNode);
                SelectedStructureNode = liveNode;
                StatusMessage = $"Created root folder on computer disk: \"{result.NewPath}\"";
                SelectedTargetNode?.Refresh();
                OnPropertyChanged(nameof(TotalFolderCount));
                RaiseStructureChanged();
                return;
            }
        }

        var node = new FolderNode(folderName);
        node.IsEditing = true;
        RootFolders.Add(node);
        SelectedStructureNode = node;
        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    private void AddChild()
    {
        if (SelectedStructureNode is not { IsFile: false }) return;
        var parentNode = SelectedStructureNode;
        var childName = "New Subfolder";

        string? parentPath = parentNode.RealPath;
        if (string.IsNullOrEmpty(parentPath) && IsLiveSyncMode && TargetPathExists)
        {
            parentPath = ComputeNodePathRelativeToTarget(parentNode, TargetPath);
            parentNode.RealPath = parentPath;
        }

        if (IsLiveSyncMode && !string.IsNullOrEmpty(parentPath))
        {
            var result = FileSystemService.CreateFolderOnDisk(parentPath, childName);
            if (!result.Success)
            {
                StatusMessage = $"Could not create folder on computer disk: {result.Error}";
                MessageBox.Show($"Could not create folder on computer disk:\n{result.Error}", "Create Folder Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var child = new FolderNode(Path.GetFileName(result.NewPath), parentNode, realPath: result.NewPath);
            child.IsEditing = true;
            parentNode.Children.Add(child);
            parentNode.IsExpanded = true;
            SelectedStructureNode = child;
            StatusMessage = $"Created subfolder on computer disk: \"{result.NewPath}\"";
            SelectedTargetNode?.Refresh();
        }
        else
        {
            var child = new FolderNode(childName, parentNode);
            child.IsEditing = true;
            parentNode.Children.Add(child);
            parentNode.IsExpanded = true;
            SelectedStructureNode = child;
        }

        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    private void AddSibling()
    {
        if (SelectedStructureNode is null) return;
        var parent = SelectedStructureNode.Parent;
        var folderName = "New Folder";
        string? targetParentPath = parent?.RealPath ?? (IsLiveSyncMode && TargetPathExists ? TargetPath : null);

        if (IsLiveSyncMode && !string.IsNullOrEmpty(targetParentPath))
        {
            var result = FileSystemService.CreateFolderOnDisk(targetParentPath, folderName);
            if (!result.Success)
            {
                StatusMessage = $"Could not create folder on computer disk: {result.Error}";
                MessageBox.Show($"Could not create folder on computer disk:\n{result.Error}", "Create Folder Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var sibling = new FolderNode(Path.GetFileName(result.NewPath), parent, realPath: result.NewPath);
            sibling.IsEditing = true;
            if (parent is null)
                RootFolders.Add(sibling);
            else
                parent.Children.Add(sibling);

            SelectedStructureNode = sibling;
            StatusMessage = $"Created folder on computer disk: \"{result.NewPath}\"";
            SelectedTargetNode?.Refresh();
        }
        else
        {
            var sibling = new FolderNode(folderName, parent);
            sibling.IsEditing = true;
            if (parent is null)
                RootFolders.Add(sibling);
            else
                parent.Children.Add(sibling);

            SelectedStructureNode = sibling;
        }

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
        string? targetParentPath = targetParent?.RealPath ?? (IsLiveSyncMode && TargetPathExists ? TargetPath : null);

        FolderNode? lastAdded = null;
        foreach (var name in names)
        {
            if (IsLiveSyncMode && !string.IsNullOrEmpty(targetParentPath))
            {
                var result = FileSystemService.CreateFolderOnDisk(targetParentPath, name);
                if (result.Success)
                {
                    var node = new FolderNode(Path.GetFileName(result.NewPath), targetParent, realPath: result.NewPath);
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
            }
            else
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
        }

        QuickAddNames = string.Empty;
        if (lastAdded != null) SelectedStructureNode = lastAdded;
        SelectedTargetNode?.Refresh();
        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    private void DeleteSelected()
    {
        if (SelectedStructureNode is null) return;
        var node = SelectedStructureNode;

        bool existsOnDisk = !string.IsNullOrEmpty(node.RealPath) && Directory.Exists(node.RealPath);

        if (IsLiveSyncMode && existsOnDisk && !string.IsNullOrEmpty(node.RealPath))
        {
            var confirm = MessageBox.Show(
                $"Send \"{node.Name}\" and all of its contents to the Windows Recycle Bin?\n\nPath: {node.RealPath}",
                "Confirm Delete (Recycle Bin)",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            var result = FileSystemService.DeleteFolderToRecycleBin(node.RealPath);
            if (!result.Success)
            {
                StatusMessage = $"Could not delete folder from computer disk: {result.Error}";
                MessageBox.Show($"Could not delete folder from computer disk:\n{result.Error}", "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            StatusMessage = $"Sent \"{node.Name}\" to the Windows Recycle Bin.";
        }
        else if (!existsOnDisk)
        {
            var confirm = MessageBox.Show(
                $"Delete \"{node.Name}\" and everything nested under it from the blueprint?",
                "Confirm delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;
        }

        if (node.Parent is null)
            RootFolders.Remove(node);
        else
            node.Parent.Children.Remove(node);

        SelectedStructureNode = null;
        SelectedTargetNode?.Refresh();
        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    /// <summary>Renames a node, performing physical disk rename when Live Computer Sync is enabled.</summary>
    public void RenameNode(FolderNode node, string newName)
    {
        if (node == null) return;
        var trimmed = string.IsNullOrWhiteSpace(newName) ? "New Folder" : newName.Trim();

        if (IsLiveSyncMode && !string.IsNullOrEmpty(node.RealPath))
        {
            if (string.Equals(node.Name, trimmed, StringComparison.OrdinalIgnoreCase)) return;

            var result = FileSystemService.RenameFolderOnDisk(node.RealPath, trimmed);
            if (result.Success)
            {
                node.Name = Path.GetFileName(result.NewPath);
                node.UpdateRealPaths(result.NewPath);
                StatusMessage = $"Renamed folder on computer disk to: \"{result.NewPath}\"";
                SelectedTargetNode?.Refresh();
            }
            else
            {
                StatusMessage = $"Could not rename folder on computer disk: {result.Error}";
                MessageBox.Show($"Could not rename folder on computer disk:\n{result.Error}", "Rename Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else
        {
            node.Name = trimmed;
        }

        RaiseStructureChanged();
    }

    /// <summary>Moves sourceNode to become a child of targetParent, performing physical disk move when Live Computer Sync is enabled.</summary>
    public void MoveNode(FolderNode sourceNode, FolderNode targetParent)
    {
        if (sourceNode == null || targetParent == null) return;
        if (ReferenceEquals(sourceNode, targetParent)) return;
        if (ReferenceEquals(sourceNode.Parent, targetParent)) return;

        // Prevent circular moving (moving a parent into its own descendant)
        var current = targetParent;
        while (current != null)
        {
            if (ReferenceEquals(current, sourceNode))
            {
                MessageBox.Show("Cannot move a folder into one of its own subfolders.", "Invalid Move", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            current = current.Parent;
        }

        if (IsLiveSyncMode && !string.IsNullOrEmpty(sourceNode.RealPath) && !string.IsNullOrEmpty(targetParent.RealPath))
        {
            var result = FileSystemService.MoveFolderOnDisk(sourceNode.RealPath, targetParent.RealPath);
            if (!result.Success)
            {
                StatusMessage = $"Could not move folder on computer disk: {result.Error}";
                MessageBox.Show($"Could not move folder on computer disk:\n{result.Error}", "Move Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (sourceNode.Parent is null)
                RootFolders.Remove(sourceNode);
            else
                sourceNode.Parent.Children.Remove(sourceNode);

            sourceNode.Parent = targetParent;
            targetParent.Children.Add(sourceNode);
            targetParent.IsExpanded = true;
            sourceNode.UpdateRealPaths(result.NewPath);

            StatusMessage = $"Moved folder on computer disk to \"{result.NewPath}\"";
            SelectedTargetNode?.Refresh();
        }
        else
        {
            if (sourceNode.Parent is null)
                RootFolders.Remove(sourceNode);
            else
                sourceNode.Parent.Children.Remove(sourceNode);

            sourceNode.Parent = targetParent;
            targetParent.Children.Add(sourceNode);
            targetParent.IsExpanded = true;
        }

        SelectedStructureNode = sourceNode;
        RaiseStructureChanged();
    }

    public void OpenInExplorer(FolderNode? node)
    {
        node ??= SelectedStructureNode;
        if (node is null) return;

        string targetPath = ComputeNodePathRelativeToTarget(node, TargetPath);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            StatusMessage = $"Cannot open in Explorer: No valid path for \"{node.Name}\".";
            return;
        }

        var result = FileSystemService.OpenInExplorer(targetPath);
        if (result.Success)
        {
            StatusMessage = $"Opened in Explorer: \"{result.OpenedPath}\"";
        }
        else if (result.Error == "PathDoesNotExistButParentOpened")
        {
            StatusMessage = $"\"{node.Name}\" does not exist on disk yet. Opened parent folder: \"{result.OpenedPath}\"";
        }
        else
        {
            StatusMessage = $"Could not open in Explorer: \"{node.Name}\" does not exist on disk yet.";
            MessageBox.Show(
                $"The folder \"{node.Name}\" does not exist on disk yet.\n\nPath: {targetPath}\n\nCreate the structure or enable Live Computer Sync first.",
                "Folder Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void ApplySearch()
    {
        if (_isApplyingSearch) return;
        _isApplyingSearch = true;

        try
        {
            MatchingSearchResults.Clear();
            var query = SearchQuery?.Trim();

            void ScanNode(FolderNode node)
            {
                bool matches = !string.IsNullOrWhiteSpace(query) && node.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                node.IsMatchingSearch = matches;

                if (matches)
                {
                    MatchingSearchResults.Add(node);

                    // Expand all ancestor nodes up to root so matching items are visible in TreeView
                    var p = node.Parent;
                    while (p != null)
                    {
                        p.IsExpanded = true;
                        p = p.Parent;
                    }
                }

                foreach (var child in node.Children)
                    ScanNode(child);
            }

            foreach (var root in RootFolders)
                ScanNode(root);

            OnPropertyChanged(nameof(SearchMatchCount));
            OnPropertyChanged(nameof(HasSearchMatches));
            OnPropertyChanged(nameof(SearchMatchStatusText));
            NavigateNextMatchCommand.RaiseCanExecuteChanged();
            NavigatePrevMatchCommand.RaiseCanExecuteChanged();

            IsSearchDropdownOpen = HasSearchQuery && MatchingSearchResults.Count > 0;

            if (MatchingSearchResults.Count > 0)
            {
                int index = SelectedStructureNode != null ? MatchingSearchResults.IndexOf(SelectedStructureNode) : -1;
                if (index >= 0)
                {
                    CurrentSearchIndex = index;
                }
                else
                {
                    CurrentSearchIndex = 0;
                    SelectedStructureNode = MatchingSearchResults[0];
                }
            }
            else
            {
                CurrentSearchIndex = -1;
            }
        }
        finally
        {
            _isApplyingSearch = false;
        }

        StructureChanged?.Invoke();
    }

    private void NavigateSearchMatch(int direction)
    {
        if (MatchingSearchResults.Count == 0) return;

        int newIndex = CurrentSearchIndex + direction;
        if (newIndex >= MatchingSearchResults.Count)
            newIndex = 0;
        else if (newIndex < 0)
            newIndex = MatchingSearchResults.Count - 1;

        CurrentSearchIndex = newIndex;
        SelectedStructureNode = MatchingSearchResults[newIndex];

        var p = SelectedStructureNode.Parent;
        while (p != null)
        {
            p.IsExpanded = true;
            p = p.Parent;
        }
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
