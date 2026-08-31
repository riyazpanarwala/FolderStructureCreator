using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FolderStructureCreator.Models;

/// <summary>
/// Represents a single folder in the user-defined structure tree (the "blueprint"
/// that will be created on disk under the chosen target path). This is purely an
/// in-memory model - nothing here touches the file system.
/// </summary>
public class FolderNode : INotifyPropertyChanged
{
    private string _name;
    private bool _isExpanded = true;
    private bool _isSelected;
    private bool _isEditing;
    private bool _isMatchingSearch;
    private NodeDiffStatus _diffStatus = NodeDiffStatus.None;

    public NodeDiffStatus DiffStatus
    {
        get => _diffStatus;
        set
        {
            if (SetField(ref _diffStatus, value))
            {
                OnPropertyChanged(nameof(DiffBadgeText));
                OnPropertyChanged(nameof(HasDiffBadge));
            }
        }
    }

    public bool HasDiffBadge => DiffStatus != NodeDiffStatus.None;

    public string DiffBadgeText => DiffStatus switch
    {
        NodeDiffStatus.MissingOnDisk => "[+ MISSING]",
        NodeDiffStatus.MatchesDisk => "[✓ MATCH]",
        NodeDiffStatus.ExtraOnDisk => "[⚡ EXTRA]",
        _ => string.Empty
    };

    public void ResetDiffStatusRecursive()
    {
        DiffStatus = NodeDiffStatus.None;
        foreach (var child in Children)
            child.ResetDiffStatusRecursive();
    }

    private string? _realPath;

    public FolderNode(string name, FolderNode? parent = null, bool isFile = false, string? realPath = null)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "New Folder" : name;
        Parent = parent;
        IsFile = isFile;
        _realPath = realPath;
        Children = new ObservableCollection<FolderNode>();
    }

    /// <summary>
    /// Holds the physical disk path when this node is bound to a real folder on the computer.
    /// </summary>
    public string? RealPath
    {
        get => _realPath;
        set => SetField(ref _realPath, value);
    }

    /// <summary>Recursively updates RealPath for this node and all of its descendants.</summary>
    public void UpdateRealPaths(string newPath)
    {
        RealPath = newPath;
        foreach (var child in Children)
        {
            var childName = System.IO.Path.GetFileName(child.RealPath?.TrimEnd(System.IO.Path.DirectorySeparatorChar) ?? child.Name);
            var childNewPath = System.IO.Path.Combine(newPath, childName);
            child.UpdateRealPaths(childNewPath);
        }
    }

    /// <summary>
    /// True for a file shown in an imported tree for reference/context only.
    /// File nodes are never created on disk - see FileSystemService.CreateStructure,
    /// which skips any node with IsFile = true before it ever calls Directory.CreateDirectory.
    /// </summary>
    public bool IsFile { get; }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, string.IsNullOrWhiteSpace(value) ? "New Folder" : value.Trim());
    }

    public FolderNode? Parent { get; set; }

    public ObservableCollection<FolderNode> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>True while the name TextBox is being shown for rename-in-place.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => SetField(ref _isEditing, value);
    }

    /// <summary>True when this node matches the active search query in the right panel.</summary>
    public bool IsMatchingSearch
    {
        get => _isMatchingSearch;
        set => SetField(ref _isMatchingSearch, value);
    }

    /// <summary>Returns path breadcrumb from root to this node (e.g. "Root / SubFolder / Target").</summary>
    public string FullPathDisplay
    {
        get
        {
            var stack = new System.Collections.Generic.Stack<string>();
            var curr = this;
            while (curr != null)
            {
                stack.Push(curr.Name);
                curr = curr.Parent;
            }
            return string.Join(" / ", stack);
        }
    }

    /// <summary>Total count of this node plus every descendant, folders and files together.</summary>
    public int CountAll()
    {
        int count = 1;
        foreach (var child in Children)
            count += child.CountAll();
        return count;
    }

    /// <summary>Count of real folders only (excludes file-reference nodes) - this is what will actually be created on disk.</summary>
    public int CountFoldersOnly()
    {
        int count = IsFile ? 0 : 1;
        foreach (var child in Children)
            count += child.CountFoldersOnly();
        return count;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
