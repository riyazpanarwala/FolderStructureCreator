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

    public FolderNode(string name, FolderNode? parent = null, bool isFile = false)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "New Folder" : name;
        Parent = parent;
        IsFile = isFile;
        Children = new ObservableCollection<FolderNode>();
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

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
