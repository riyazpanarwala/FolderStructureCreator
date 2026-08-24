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
                RefreshExists();
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

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
