using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace FolderStructureCreator.Models;

public class PinnedFolder : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    private bool _exists;

    public PinnedFolder(string path, string? customName = null)
    {
        Path = path;
        Name = !string.IsNullOrWhiteSpace(customName)
            ? customName
            : System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(Name)) Name = path;
        RefreshExists();
    }

    private FileSystemNode? _node;
    public FileSystemNode Node
    {
        get
        {
            if (_node == null || _node.FullPath != Path || _node.Name != Name)
            {
                _node = new FileSystemNode(Path, Name);
            }
            return _node;
        }
    }

    public bool IsExpanded
    {
        get => Node.IsExpanded;
        set
        {
            if (Node.IsExpanded != value)
            {
                Node.IsExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSelected
    {
        get => Node.IsSelected;
        set
        {
            if (Node.IsSelected != value)
            {
                Node.IsSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Path
    {
        get => _path;
        set
        {
            if (SetField(ref _path, value))
            {
                _node = null;
                RefreshExists();
            }
        }
    }

    public bool Exists
    {
        get => _exists;
        private set => SetField(ref _exists, value);
    }

    public void RefreshExists()
    {
        Exists = !string.IsNullOrWhiteSpace(Path) && Directory.Exists(Path);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
