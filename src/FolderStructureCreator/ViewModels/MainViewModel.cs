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
            OpenSelectedFolderInExplorerCommand?.RaiseCanExecuteChanged();
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
                HasCreatedSuccessfully = false;
                OnPropertyChanged(nameof(TargetPathExists));
                OnPropertyChanged(nameof(IsDestinationReady));
                OnPropertyChanged(nameof(DestinationReadinessText));
                OnPropertyChanged(nameof(IsCreateReady));
                OnPropertyChanged(nameof(IsCreateCompleted));
                OnPropertyChanged(nameof(IsCreateActionable));
                OnPropertyChanged(nameof(IsCreatePending));
                OnPropertyChanged(nameof(CreateReadinessText));
                OnPropertyChanged(nameof(CreateButtonTooltip));
                OnPropertyChanged(nameof(ConciseStatusText));
                OnPropertyChanged(nameof(DetailedStatusTooltip));
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

    public bool IsPlanReady => HasStructureNodes;
    public bool IsDestinationReady => TargetPathExists;
    public bool IsCreateReady => CanCreateStructure();

    private bool _hasCreatedSuccessfully;
    public bool HasCreatedSuccessfully
    {
        get => _hasCreatedSuccessfully;
        private set
        {
            if (SetField(ref _hasCreatedSuccessfully, value))
            {
                OnPropertyChanged(nameof(IsCreateCompleted));
                OnPropertyChanged(nameof(IsCreateActionable));
                OnPropertyChanged(nameof(IsCreatePending));
                OnPropertyChanged(nameof(CreateReadinessText));
            }
        }
    }

    public bool IsCreateCompleted => HasCreatedSuccessfully;
    public bool IsCreateActionable => !HasCreatedSuccessfully && CanCreateStructure();
    public bool IsCreatePending => !HasCreatedSuccessfully && !CanCreateStructure();

    public string PlanReadinessText => HasStructureNodes ? $"✓ Plan ({TotalFolderCount})" : "Plan needed";
    public string DestinationReadinessText => TargetPathExists ? "✓ Destination" : "Destination needed";
    public string CreateReadinessText => HasCreatedSuccessfully
        ? "✓ Created"
        : (CanCreateStructure() ? "▶ Ready to create" : "Create");

    public string CreateButtonTooltip
    {
        get
        {
            if (!TargetPathExists)
                return "Select a valid destination folder on disk first.";
            if (RootFolders.Count == 0)
                return "Add at least one folder to the plan before creating.";
            return $"Create all {TotalFolderCount} planned folders under \"{TargetPath}\".";
        }
    }

    private bool _isQuickAddExpanded;
    public bool IsQuickAddExpanded
    {
        get => _isQuickAddExpanded;
        set
        {
            if (SetField(ref _isQuickAddExpanded, value))
                OnPropertyChanged(nameof(IsQuickAddVisible));
        }
    }

    public bool IsQuickAddVisible => IsQuickAddExpanded || HasQuickAddText;

    private int _lastImportIgnoredCount;

    public string ConciseStatusText
    {
        get
        {
            if (IsDiffActive && !string.IsNullOrEmpty(DiffSummaryText))
                return DiffSummaryText;

            if (TotalFolderCount > 0)
            {
                if (_lastImportIgnoredCount > 0)
                    return $"{TotalFolderCount} planned · {_lastImportIgnoredCount} ignored";
                return $"{TotalFolderCount} planned folder{(TotalFolderCount == 1 ? "" : "s")}";
            }

            return "No folders planned";
        }
    }

    public string DetailedStatusTooltip
    {
        get
        {
            if (IsLiveSyncMode && TargetPathExists)
                return $"Live sync is active on \"{TargetPath}\": edits to the plan immediately create, rename, or delete folders on disk.";
            if (IsDiffActive && !string.IsNullOrEmpty(DiffSummaryText))
                return $"Disk comparison against \"{TargetPath}\": {DiffSummaryText}.";
            if (!string.IsNullOrWhiteSpace(StatusMessage))
                return StatusMessage;
            if (CanCreateStructure())
                return $"Plan contains {TotalFolderCount} folder(s) ready to create in \"{TargetPath}\".";
            return "Plan folders in the central canvas and select a target destination on the left.";
        }
    }

    private string _quickAddNames = string.Empty;
    /// <summary>Comma-separated names typed into the quick-add box, e.g. "src, docs, tests".</summary>
    public string QuickAddNames
    {
        get => _quickAddNames;
        set
        {
            if (SetField(ref _quickAddNames, value))
            {
                OnPropertyChanged(nameof(HasQuickAddText));
                OnPropertyChanged(nameof(IsQuickAddVisible));
            }
        }
    }

    /// <summary>True once text is typed in the quick-add box - drives whether the "Add as children" button is shown.</summary>
    public bool HasQuickAddText => !string.IsNullOrWhiteSpace(QuickAddNames);

    private string _statusMessage = "Build a folder plan on the right, choose a destination on the left, then click Create folders.";
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetField(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(ConciseStatusText));
                OnPropertyChanged(nameof(DetailedStatusTooltip));
            }
        }
    }

    private bool _isLiveSyncMode = true;
    /// <summary>When true, folder additions, renames, deletions (Recycle Bin), and moves immediately modify physical folders on disk.</summary>
    public bool IsLiveSyncMode
    {
        get => _isLiveSyncMode;
        set
        {
            if (SetField(ref _isLiveSyncMode, value))
            {
                StatusMessage = value ? "Live disk sync enabled: folder actions will directly modify disk." : "Live disk sync disabled.";
            }
        }
    }

    private bool _enableIgnoreRules = true;
    /// <summary>When true, folder imports automatically ignore system/build folders (node_modules, bin, obj, .git, etc.) and .structureignore / .gitignore rules.</summary>
    public bool EnableIgnoreRules
    {
        get => _enableIgnoreRules;
        set
        {
            if (SetField(ref _enableIgnoreRules, value))
            {
                StatusMessage = value ? "Smart ignore rules enabled (.structureignore & build folders ignored)." : "Smart ignore rules disabled (all folders will be included).";
            }
        }
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

    public string OrgChartLayoutButtonText => IsVerticalOrgChart ? "Vertical" : "Horizontal";


    private bool _isDestinationSidebarCollapsed;
    /// <summary>Lets the chart use the full workspace on smaller screens without clearing the selected target.</summary>
    public bool IsDestinationSidebarCollapsed
    {
        get => _isDestinationSidebarCollapsed;
        set
        {
            if (SetField(ref _isDestinationSidebarCollapsed, value))
            {
                OnPropertyChanged(nameof(ShouldHideDestinationSidebar));
            }
        }
    }

    private bool _isFullscreenMode;
    /// <summary>Distraction-free fullscreen presentation whiteboard mode.</summary>
    public bool IsFullscreenMode
    {
        get => _isFullscreenMode;
        set
        {
            if (SetField(ref _isFullscreenMode, value))
            {
                OnPropertyChanged(nameof(ShouldHideDestinationSidebar));
            }
        }
    }

    public bool ShouldHideDestinationSidebar => IsFullscreenMode || IsDestinationSidebarCollapsed;

    public event Action? RequestToggleFullscreen;

    public void ToggleFullscreen()
    {
        RequestToggleFullscreen?.Invoke();
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

    public string SortToggleText => IsSortAscending ? "A–Z" : "Z–A";

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
        HasCreatedSuccessfully = false;
        OnPropertyChanged(nameof(TotalFolderCount));
        OnPropertyChanged(nameof(HasStructureNodes));
        OnPropertyChanged(nameof(IsPlanReady));
        OnPropertyChanged(nameof(PlanReadinessText));
        OnPropertyChanged(nameof(IsCreateReady));
        OnPropertyChanged(nameof(IsCreateCompleted));
        OnPropertyChanged(nameof(IsCreateActionable));
        OnPropertyChanged(nameof(IsCreatePending));
        OnPropertyChanged(nameof(CreateReadinessText));
        OnPropertyChanged(nameof(CreateButtonTooltip));
        OnPropertyChanged(nameof(ConciseStatusText));
        OnPropertyChanged(nameof(DetailedStatusTooltip));
        StructureChanged?.Invoke();
        ExpandAllOrgChartCommand?.RaiseCanExecuteChanged();
        CollapseAllOrgChartCommand?.RaiseCanExecuteChanged();
        ExpandAllTreeCommand?.RaiseCanExecuteChanged();
        CollapseAllTreeCommand?.RaiseCanExecuteChanged();
    }

    public int TotalFolderCount => RootFolders.Sum(r => r.CountFoldersOnly());

    public AppTheme SelectedTheme
    {
        get => ThemeService.CurrentTheme;
        set
        {
            if (ThemeService.CurrentTheme != value)
            {
                ThemeService.ApplyTheme(value);
                OnPropertyChanged(nameof(SelectedTheme));
            }
        }
    }

    public event Action? RequestExportScript;

    // ---- Blueprint vs. Disk Diff ----
    private bool _isDiffActive;
    public bool IsDiffActive
    {
        get => _isDiffActive;
        set => SetField(ref _isDiffActive, value);
    }

    private string _diffSummaryText = string.Empty;
    public string DiffSummaryText
    {
        get => _diffSummaryText;
        set => SetField(ref _diffSummaryText, value);
    }

    private bool _hasMissingFolders;
    public bool HasMissingFolders
    {
        get => _hasMissingFolders;
        set => SetField(ref _hasMissingFolders, value);
    }

    public void CompareBlueprintWithDisk()
    {
        if (string.IsNullOrWhiteSpace(TargetPath) || !Directory.Exists(TargetPath))
        {
            StatusMessage = "Please select or enter a valid destination folder on disk first.";
            return;
        }

        var result = DirectoryDiffService.EvaluateDiff(RootFolders, TargetPath);
        IsDiffActive = true;
        DiffSummaryText = result.SummaryText;
        HasMissingFolders = result.HasMissing;
        StatusMessage = $"Diff complete: {result.MissingCount} missing, {result.MatchedCount} matched.";
        RaiseStructureChanged();
    }

    public void ClearDiff()
    {
        IsDiffActive = false;
        DiffSummaryText = string.Empty;
        HasMissingFolders = false;
        foreach (var root in RootFolders)
            root.ResetDiffStatusRecursive();
        RaiseStructureChanged();
    }

    public void CreateMissingFoldersOnly()
    {
        if (!IsDiffActive || !HasMissingFolders) return;
        CreateStructure();
        CompareBlueprintWithDisk();
    }

    // ---- Point 8: Command Palette (Ctrl + K) ----
    private bool _isCommandPaletteOpen;
    public bool IsCommandPaletteOpen
    {
        get => _isCommandPaletteOpen;
        set => SetField(ref _isCommandPaletteOpen, value);
    }

    private string _commandPaletteQuery = string.Empty;
    public string CommandPaletteQuery
    {
        get => _commandPaletteQuery;
        set
        {
            if (SetField(ref _commandPaletteQuery, value))
                ApplyCommandPaletteFilter();
        }
    }

    public ObservableCollection<CommandItem> AllCommands { get; } = new();
    public ObservableCollection<CommandItem> FilteredCommands { get; } = new();

    private CommandItem? _selectedCommandPaletteItem;
    public CommandItem? SelectedCommandPaletteItem
    {
        get => _selectedCommandPaletteItem;
        set => SetField(ref _selectedCommandPaletteItem, value);
    }

    private void ApplyCommandPaletteFilter()
    {
        FilteredCommands.Clear();
        var q = CommandPaletteQuery.Trim();
        var matches = string.IsNullOrWhiteSpace(q)
            ? AllCommands
            : AllCommands.Where(c => c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                     c.Category.Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var m in matches)
            FilteredCommands.Add(m);

        SelectedCommandPaletteItem = FilteredCommands.FirstOrDefault();
    }

    public void ExecuteCommandPaletteItem(CommandItem? item)
    {
        if (item == null) return;
        IsCommandPaletteOpen = false;
        if (item.Command.CanExecute(item.CommandParameter))
        {
            item.Command.Execute(item.CommandParameter);
        }
    }

    public void OpenCommandPalette()
    {
        CommandPaletteQuery = string.Empty;
        ApplyCommandPaletteFilter();
        IsCommandPaletteOpen = true;
    }

    // ---- Commands ----
    public RelayCommand AddRootFolderCommand { get; }
    public RelayCommand AddChildFolderCommand { get; }
    public RelayCommand AddSiblingFolderCommand { get; }
    public RelayCommand QuickAddCommand { get; }
    public RelayCommand ToggleQuickAddCommand { get; }
    public RelayCommand DeleteNodeCommand { get; }
    public RelayCommand MoveToRootCommand { get; }
    public RelayCommand RefreshDrivesCommand { get; }
    public RelayCommand ShowSelectedFolderOrgChartCommand { get; }
    public RelayCommand OpenSelectedFolderInExplorerCommand { get; }
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
    public RelayCommand ExpandAllOrgChartCommand { get; }
    public RelayCommand CollapseAllOrgChartCommand { get; }
    public RelayCommand ExpandAllTreeCommand { get; }
    public RelayCommand CollapseAllTreeCommand { get; }
    public RelayCommand SetThemeCommand { get; }
    public RelayCommand ToggleFullscreenCommand { get; }
    public RelayCommand ToggleCommandPaletteCommand { get; }
    public RelayCommand OpenCommandPaletteCommand { get; }
    public RelayCommand CloseCommandPaletteCommand { get; }
    public RelayCommand ExecuteCommandPaletteItemCommand { get; }
    public RelayCommand CompareBlueprintWithDiskCommand { get; }
    public RelayCommand CreateMissingFoldersOnlyCommand { get; }
    public RelayCommand ClearDiffCommand { get; }

    public MainViewModel()
    {
        AddRootFolderCommand = new RelayCommand(_ => AddRootFolder());
        AddChildFolderCommand = new RelayCommand(_ => AddChild(), _ => SelectedStructureNode is { IsFile: false });
        AddSiblingFolderCommand = new RelayCommand(_ => AddSibling(), _ => SelectedStructureNode != null);
        QuickAddCommand = new RelayCommand(_ => QuickAdd(), _ => !string.IsNullOrWhiteSpace(QuickAddNames));
        ToggleQuickAddCommand = new RelayCommand(_ => IsQuickAddExpanded = !IsQuickAddExpanded);
        DeleteNodeCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedStructureNode != null);
        MoveToRootCommand = new RelayCommand(_ => MoveNodeToRoot(SelectedStructureNode!), _ => SelectedStructureNode?.Parent != null);
        RefreshDrivesCommand = new RelayCommand(_ => LoadDrives());
        ShowSelectedFolderOrgChartCommand = new RelayCommand(_ => ShowSelectedFolderOrgChart(), _ => SelectedTargetNode is { IsPlaceholder: false });
        OpenSelectedFolderInExplorerCommand = new RelayCommand(
            param =>
            {
                var path = param as string
                           ?? (param as FileSystemNode)?.FullPath
                           ?? (param as PinnedFolder)?.Path
                           ?? SelectedTargetNode?.FullPath;
                OpenFolderPathInExplorer(path);
            },
            param =>
            {
                var path = param as string
                           ?? (param as FileSystemNode)?.FullPath
                           ?? (param as PinnedFolder)?.Path
                           ?? SelectedTargetNode?.FullPath;
                return !string.IsNullOrWhiteSpace(path);
            });
        CreateStructureCommand = new RelayCommand(_ => CreateStructure(), _ => CanCreateStructure());
        OpenInExplorerCommand = new RelayCommand(param => OpenInExplorer(param as FolderNode), _ => SelectedStructureNode != null || RootFolders.Count > 0);
        StartRenameCommand = new RelayCommand(param => StartRename(param as FolderNode));
        ImportFromReferenceCommand = new RelayCommand(async _ => await ImportFromReferenceAsync());
        ClearPlanCommand = new RelayCommand(_ => ClearPlan(), _ => RootFolders.Count > 0);
        ShowTreeViewCommand = new RelayCommand(_ => IsOrgChartView = false);
        ShowOrgChartViewCommand = new RelayCommand(_ => IsOrgChartView = true);
        ToggleDestinationSidebarCommand = new RelayCommand(_ => IsDestinationSidebarCollapsed = !IsDestinationSidebarCollapsed);
        ToggleOrgChartLayoutCommand = new RelayCommand(_ => IsVerticalOrgChart = !IsVerticalOrgChart);
        ExpandAllOrgChartCommand = new RelayCommand(_ => ExpandAllOrgChart(), _ => RootFolders.Count > 0);
        CollapseAllOrgChartCommand = new RelayCommand(_ => CollapseAllOrgChart(), _ => RootFolders.Count > 0);
        ExpandAllTreeCommand = new RelayCommand(_ => ExpandAllTree(), _ => RootFolders.Count > 0);
        CollapseAllTreeCommand = new RelayCommand(_ => CollapseAllTree(), _ => RootFolders.Count > 0);
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
        UnpinFolderCommand = new RelayCommand(param => UnpinFolder(param as PinnedFolder ?? PinnedFolders.FirstOrDefault(p => string.Equals(p.Path, param as string ?? (param as FileSystemNode)?.FullPath, StringComparison.OrdinalIgnoreCase))));
        ShowPinnedFolderChartCommand = new RelayCommand(async param => await ShowFolderOrgChartFromPathAsync(param as string ?? (param as PinnedFolder)?.Path ?? (param as FileSystemNode)?.FullPath));
        SelectPinnedFolderCommand = new RelayCommand(param =>
        {
            if (param is PinnedFolder pinned)
                SelectPinnedFolder(pinned);
            else if (param is FileSystemNode node && !node.IsPlaceholder)
                SelectedTargetNode = node;
            else if (param is string s)
                SelectPinnedFolder(new PinnedFolder(s));
        });
        ToggleSortOrderCommand = new RelayCommand(_ => IsSortAscending = !IsSortAscending);
        SetThemeCommand = new RelayCommand(param =>
        {
            if (param is AppTheme t)
                SelectedTheme = t;
            else if (param is string s && Enum.TryParse<AppTheme>(s, out var parsedTheme))
                SelectedTheme = parsedTheme;
        });

        ToggleFullscreenCommand = new RelayCommand(_ => ToggleFullscreen());
        ToggleCommandPaletteCommand = new RelayCommand(_ => IsCommandPaletteOpen = !IsCommandPaletteOpen);
        OpenCommandPaletteCommand = new RelayCommand(_ => OpenCommandPalette());
        CloseCommandPaletteCommand = new RelayCommand(_ => IsCommandPaletteOpen = false);
        ExecuteCommandPaletteItemCommand = new RelayCommand(param => ExecuteCommandPaletteItem(param as CommandItem ?? SelectedCommandPaletteItem));
        CompareBlueprintWithDiskCommand = new RelayCommand(_ => CompareBlueprintWithDisk(), _ => RootFolders.Count > 0 && !string.IsNullOrWhiteSpace(TargetPath));
        CreateMissingFoldersOnlyCommand = new RelayCommand(_ => CreateMissingFoldersOnly(), _ => IsDiffActive && HasMissingFolders);
        ClearDiffCommand = new RelayCommand(_ => ClearDiff(), _ => IsDiffActive);

        InitializeCommandPaletteRegistry();

        ThemeService.ThemeChanged += _ =>
        {
            OnPropertyChanged(nameof(Drives));
            OnPropertyChanged(nameof(PinnedFolders));
            OnPropertyChanged(nameof(RootFolders));
        };

        LoadDrives();
        LoadPinnedFolders();
        AddRootFolder(); // start with one editable root node so the tree isn't empty
        RootFolders.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalFolderCount));
            OnPropertyChanged(nameof(HasStructureNodes));
            OnPropertyChanged(nameof(IsPlanReady));
            OnPropertyChanged(nameof(IsCreateReady));
            OnPropertyChanged(nameof(ConciseStatusText));
            OnPropertyChanged(nameof(DetailedStatusTooltip));
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
            var ignoreRules = EnableIgnoreRules ? IgnoreRuleService.CreateForSource(folderPath) : new IgnoreRuleService(includeBuiltInDefaults: false);
            var importResult = await Task.Run(() => FileSystemService.BuildFolderNodeTree(folderPath, MaxOrgChartNodes, ignoreRules));

            void ApplyResult()
            {
                RootFolders.Clear();
                RootFolders.Add(importResult.Root);
                SelectedStructureNode = importResult.Root;
                TargetPath = folderPath;
                IsOrgChartView = true;
                IsLiveSyncMode = true;

                string ignoreText = importResult.IgnoredCount > 0 ? $" ({importResult.IgnoredCount} skipped via ignore rules)" : "";
                StatusMessage = importResult.Truncated
                    ? $"Showing \"{importResult.Root.Name}\" — {importResult.FolderCount} folder(s){ignoreText}. Live computer sync enabled."
                    : $"Showing \"{importResult.Root.Name}\" — {importResult.FolderCount} folder(s){ignoreText} in the org chart. Live computer sync enabled.";

                OnPropertyChanged(nameof(TotalFolderCount));
                RaiseStructureChanged();
            }

            if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(ApplyResult);
            }
            else
            {
                ApplyResult();
            }
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
        string? targetParentPath = parent != null
            ? (parent.RealPath ?? (IsLiveSyncMode && TargetPathExists ? ComputeNodePathRelativeToTarget(parent, TargetPath) : null))
            : (IsLiveSyncMode && TargetPathExists ? TargetPath : null);

        if (parent != null && string.IsNullOrEmpty(parent.RealPath) && !string.IsNullOrEmpty(targetParentPath))
        {
            parent.RealPath = targetParentPath;
        }

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
        string? targetParentPath = targetParent != null
            ? (targetParent.RealPath ?? (IsLiveSyncMode && TargetPathExists ? ComputeNodePathRelativeToTarget(targetParent, TargetPath) : null))
            : (IsLiveSyncMode && TargetPathExists ? TargetPath : null);

        if (targetParent != null && string.IsNullOrEmpty(targetParent.RealPath) && !string.IsNullOrEmpty(targetParentPath))
        {
            targetParent.RealPath = targetParentPath;
        }

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
                else
                {
                    StatusMessage = $"Could not create folder \"{name}\" on disk: {result.Error}";
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
                $"Delete \"{node.Name}\" and everything nested under it from the folder plan?",
                "Confirm Delete",
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
            if (string.Equals(node.Name, trimmed, StringComparison.Ordinal)) return;

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

        string? sourceRealPath = sourceNode.RealPath ?? (IsLiveSyncMode && TargetPathExists ? ComputeNodePathRelativeToTarget(sourceNode, TargetPath) : null);
        string? targetParentRealPath = targetParent.RealPath ?? (IsLiveSyncMode && TargetPathExists ? ComputeNodePathRelativeToTarget(targetParent, TargetPath) : null);

        if (IsLiveSyncMode && !string.IsNullOrEmpty(sourceRealPath) && !string.IsNullOrEmpty(targetParentRealPath))
        {
            var result = FileSystemService.MoveFolderOnDisk(sourceRealPath, targetParentRealPath);
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
            targetParent.RealPath = targetParentRealPath;

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

    /// <summary>Moves sourceNode to the top level (RootFolders), performing physical disk move when Live Computer Sync is enabled.</summary>
    public void MoveNodeToRoot(FolderNode sourceNode)
    {
        if (sourceNode == null) return;
        if (sourceNode.Parent == null) return; // Already a root folder

        if (IsLiveSyncMode && TargetPathExists && !string.IsNullOrEmpty(sourceNode.RealPath))
        {
            var targetRootPath = TargetPath;
            var result = FileSystemService.MoveFolderOnDisk(sourceNode.RealPath, targetRootPath);
            if (!result.Success)
            {
                StatusMessage = $"Could not move folder to root on computer disk: {result.Error}";
                MessageBox.Show($"Could not move folder to root on computer disk:\n{result.Error}", "Move Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            sourceNode.Parent.Children.Remove(sourceNode);
            sourceNode.Parent = null;
            RootFolders.Add(sourceNode);
            sourceNode.UpdateRealPaths(result.NewPath);

            StatusMessage = $"Moved folder to root on computer disk: \"{result.NewPath}\"";
            SelectedTargetNode?.Refresh();
        }
        else
        {
            sourceNode.Parent.Children.Remove(sourceNode);
            sourceNode.Parent = null;
            RootFolders.Add(sourceNode);
        }

        SelectedStructureNode = sourceNode;
        OnPropertyChanged(nameof(TotalFolderCount));
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

    public void OpenFolderPathInExplorer(string? path)
    {
        path ??= SelectedTargetNode?.FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Cannot open in Explorer: No valid folder path selected.";
            return;
        }

        var result = FileSystemService.OpenInExplorer(path);
        if (result.Success)
        {
            StatusMessage = $"Opened in Explorer: \"{result.OpenedPath}\"";
        }
        else
        {
            StatusMessage = $"Could not open in Explorer: \"{path}\".";
            MessageBox.Show(
                $"Could not open folder in Explorer:\n\nPath: {path}\n\nError: {result.Error}",
                "Explorer Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
            var ignoreRules = EnableIgnoreRules ? IgnoreRuleService.CreateForSource(sourcePath) : new IgnoreRuleService(includeBuiltInDefaults: false);
            var importResult = await Task.Run(() => FileSystemService.BuildFolderNodeTree(sourcePath, FileSystemService.MaxImportTotalNodes, ignoreRules));
            RootFolders.Add(importResult.Root);
            SelectedStructureNode = importResult.Root;
            IsOrgChartView = true; // an import reads best as the visual org-chart diagram

            string ignoreInfo = importResult.IgnoredCount > 0 ? $" ({importResult.IgnoredCount} skipped via ignore rules)" : "";
            var message = $"Imported \"{importResult.Root.Name}\" — {importResult.FolderCount} folder(s){ignoreInfo}. ";

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

    /// <summary>Imports a folder hierarchy from disk by path into the current blueprint plan (e.g. via drag and drop from Explorer).</summary>
    public async Task ImportFolderFromPathAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

        StatusMessage = $"Reading \"{Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar))}\"…";

        try
        {
            var ignoreRules = EnableIgnoreRules ? IgnoreRuleService.CreateForSource(folderPath) : new IgnoreRuleService(includeBuiltInDefaults: false);
            var importResult = await Task.Run(() => FileSystemService.BuildFolderNodeTree(folderPath, FileSystemService.MaxImportTotalNodes, ignoreRules));
            RootFolders.Add(importResult.Root);
            SelectedStructureNode = importResult.Root;
            _lastImportIgnoredCount = importResult.IgnoredCount;

            string ignoreInfo = importResult.IgnoredCount > 0 ? $" ({importResult.IgnoredCount} skipped via ignore rules)" : "";
            var message = $"Imported \"{importResult.Root.Name}\" — {importResult.FolderCount} folder(s){ignoreInfo}.";
            if (importResult.Truncated)
            {
                message += $" Note: very large folder, import capped at safety limit (~{FileSystemService.MaxImportTotalNodes} folders).";
            }
            StatusMessage = message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not read imported folder: {ex.Message}";
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
        _lastImportIgnoredCount = 0;
        StatusMessage = "Plan cleared. Add folders manually or import from an existing folder.";
        OnPropertyChanged(nameof(TotalFolderCount));
        RaiseStructureChanged();
    }

    public void ExpandAllOrgChart()
    {
        void SetExpandedRecursive(FolderNode node, bool expanded)
        {
            node.IsExpanded = expanded;
            foreach (var child in node.Children)
                SetExpandedRecursive(child, expanded);
        }

        foreach (var root in RootFolders)
            SetExpandedRecursive(root, true);

        RaiseStructureChanged();
    }

    public void CollapseAllOrgChart()
    {
        void CollapseDescendants(FolderNode node)
        {
            node.IsExpanded = false;
            foreach (var child in node.Children)
                CollapseDescendants(child);
        }

        foreach (var root in RootFolders)
        {
            root.IsExpanded = true;
            foreach (var child in root.Children)
                CollapseDescendants(child);
        }

        RaiseStructureChanged();
    }

    public void ExpandAllTree()
    {
        void SetExpandedRecursive(FolderNode node, bool expanded)
        {
            node.IsExpanded = expanded;
            foreach (var child in node.Children)
                SetExpandedRecursive(child, expanded);
        }

        foreach (var root in RootFolders)
            SetExpandedRecursive(root, true);

        RaiseStructureChanged();
    }

    public void CollapseAllTree()
    {
        void CollapseDescendants(FolderNode node)
        {
            node.IsExpanded = false;
            foreach (var child in node.Children)
                CollapseDescendants(child);
        }

        bool keepRootExpanded = RootFolders.Count == 1 && RootFolders[0].Children.Any(c => c.IsExpanded);

        foreach (var root in RootFolders)
        {
            root.IsExpanded = keepRootExpanded;
            foreach (var child in root.Children)
                CollapseDescendants(child);
        }

        RaiseStructureChanged();
    }

    // ---- Structure creation on disk ----

    private bool CanCreateStructure() => TargetPathExists && RootFolders.Count > 0;

    private async void CreateStructure()
    {
        if (!CanCreateStructure())
        {
            StatusMessage = TargetPathExists
                ? "Add at least one folder to the structure first."
                : "Choose a valid target folder on the left first.";
            return;
        }

        StatusMessage = "Creating folder structure on disk…";
        var rootSnapshot = RootFolders.ToList();
        var targetSnapshot = TargetPath;

        var result = await Task.Run(() => FileSystemService.CreateStructure(rootSnapshot, targetSnapshot));

        if (result.Success)
        {
            HasCreatedSuccessfully = true;
            StatusMessage = $"Done: {result.CreatedCount} folder(s) created" +
                             (result.AlreadyExistedCount > 0 ? $", {result.AlreadyExistedCount} already existed" : "") +
                             $" under \"{targetSnapshot}\".";
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

    private void InitializeCommandPaletteRegistry()
    {
        AllCommands.Clear();
        // Folder Plan Actions
        AllCommands.Add(new CommandItem("Add Root Folder", "Folder Plan", "+", AddRootFolderCommand, "Ctrl+N"));
        AllCommands.Add(new CommandItem("Import Folder", "Folder Plan", "↓", ImportFromReferenceCommand, "Ctrl+I"));
        AllCommands.Add(new CommandItem("Export Standalone Script (.ps1, .bat, .sh)", "Export", "↗", new RelayCommand(_ => RequestExportScript?.Invoke())));
        AllCommands.Add(new CommandItem("Clear Folder Plan", "Folder Plan", "×", ClearPlanCommand));

        // Views & Layout
        AllCommands.Add(new CommandItem("Toggle Fullscreen Meeting Mode", "Presentation", "⛶", ToggleFullscreenCommand, "F11"));
        AllCommands.Add(new CommandItem("Switch to Tree View", "View Mode", "≡", ShowTreeViewCommand));
        AllCommands.Add(new CommandItem("Switch to Org Chart View", "View Mode", "☵", ShowOrgChartViewCommand));
        AllCommands.Add(new CommandItem("Toggle Chart Layout (Horizontal / Vertical)", "Org Chart", "⇄", ToggleOrgChartLayoutCommand));
        AllCommands.Add(new CommandItem("Expand All Chart Folders", "Org Chart", "⊞", ExpandAllOrgChartCommand));
        AllCommands.Add(new CommandItem("Collapse All Chart Folders", "Org Chart", "⊟", CollapseAllOrgChartCommand));
        AllCommands.Add(new CommandItem("Expand All Tree Folders", "Tree View", "⊞", ExpandAllTreeCommand));
        AllCommands.Add(new CommandItem("Collapse All Tree Folders", "Tree View", "⊟", CollapseAllTreeCommand));
        AllCommands.Add(new CommandItem("Toggle Destination Sidebar", "Workspace", "◫", ToggleDestinationSidebarCommand));

        // Appearance
        AllCommands.Add(new CommandItem("Switch Theme: Dark Mode", "Appearance", "●", SetThemeCommand, commandParameter: AppTheme.Dark));
        AllCommands.Add(new CommandItem("Switch Theme: Light Mode", "Appearance", "○", SetThemeCommand, commandParameter: AppTheme.Light));
        AllCommands.Add(new CommandItem("Switch Theme: High Contrast Mode", "Appearance", "◐", SetThemeCommand, commandParameter: AppTheme.HighContrast));
        AllCommands.Add(new CommandItem("Switch Theme: Windows System Match", "Appearance", "💻", SetThemeCommand, commandParameter: AppTheme.System));

        // Options & Diff
        AllCommands.Add(new CommandItem("Compare Plan with Destination (Diff)", "Diff", "◩", CompareBlueprintWithDiskCommand));
        AllCommands.Add(new CommandItem("Create Missing Folders Only", "Folder Plan", "✓", CreateMissingFoldersOnlyCommand));
        AllCommands.Add(new CommandItem("Toggle Live Disk Sync", "Settings", "↻", new RelayCommand(_ => IsLiveSyncMode = !IsLiveSyncMode)));
        AllCommands.Add(new CommandItem("Toggle Smart Ignore Rules", "Settings", "⊘", new RelayCommand(_ => EnableIgnoreRules = !EnableIgnoreRules)));
        AllCommands.Add(new CommandItem("Toggle Drive Sort Order (A-Z / Z-A)", "Destination", "⇅", ToggleSortOrderCommand));
        AllCommands.Add(new CommandItem("Refresh Destination Drives", "Destination", "↻", RefreshDrivesCommand));
        AllCommands.Add(new CommandItem("Open Selected Folder in Explorer", "Destination", "↗", OpenSelectedFolderInExplorerCommand));

        ApplyCommandPaletteFilter();
    }
}
