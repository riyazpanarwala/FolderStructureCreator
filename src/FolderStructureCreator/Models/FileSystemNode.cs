using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using FolderStructureCreator.Services;

namespace FolderStructureCreator.Models;

/// <summary>
/// Represents a real folder on disk, shown in the left-hand "Windows Directory" browser.
/// Children are loaded lazily (only when the node is expanded) so opening the tree
/// doesn't walk the entire drive up front.
/// </summary>
public class FileSystemNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _childrenLoaded;

    public FileSystemNode(string fullPath, string? displayName = null, bool isPlaceholder = false)
    {
        FullPath = fullPath;
        Name = displayName ?? Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(Name)) Name = fullPath;
        IsPlaceholder = isPlaceholder;
        Children = new ObservableCollection<FileSystemNode>();

        // Placeholder so the expand arrow shows before we've actually scanned the folder.
        // Only real (non-placeholder) nodes get a placeholder child - otherwise this recurses forever.
        if (!isPlaceholder)
            Children.Add(new FileSystemNode(string.Empty, "Loading...", isPlaceholder: true));
    }

    public string Name { get; private set; }
    public string FullPath { get; }
    public bool IsPlaceholder { get; }
    public ObservableCollection<FileSystemNode> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value) && value)
                EnsureChildrenLoaded();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>Loads real subdirectories from disk the first time this node is expanded.</summary>
    public void EnsureChildrenLoaded()
    {
        if (_childrenLoaded) return;
        _childrenLoaded = true;

        Children.Clear();
        foreach (var dir in FileSystemService.GetSubDirectoriesSafe(FullPath))
            Children.Add(new FileSystemNode(dir));

        if (Children.Count == 0)
            Children.Add(new FileSystemNode(string.Empty, "(empty)", isPlaceholder: true));
    }

    /// <summary>Forces a fresh re-scan next time this node is expanded (used after creating new folders).</summary>
    public void Refresh()
    {
        _childrenLoaded = false;
        Children.Clear();
        Children.Add(new FileSystemNode(string.Empty, "Loading...", isPlaceholder: true));
        if (IsExpanded)
            EnsureChildrenLoaded();
    }

    /// <summary>Forces a fresh re-scan of this node and all currently expanded sub-nodes.</summary>
    public void RefreshRecursive()
    {
        if (!_childrenLoaded) return;
        var expandedPaths = Children.Where(c => !c.IsPlaceholder && c.IsExpanded)
                                    .Select(c => c.FullPath)
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _childrenLoaded = false;
        EnsureChildrenLoaded();

        foreach (var child in Children.Where(c => !c.IsPlaceholder).ToList())
        {
            if (expandedPaths.Contains(child.FullPath))
            {
                child.IsExpanded = true;
                child.RefreshRecursive();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
