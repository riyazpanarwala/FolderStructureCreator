using System.Windows.Input;

namespace FolderStructureCreator.Models;

public class CommandItem
{
    public CommandItem(string title, string category, string icon, ICommand command, string? shortcutHint = null, object? commandParameter = null)
    {
        Title = title;
        Category = category;
        Icon = icon;
        Command = command;
        ShortcutHint = shortcutHint;
        CommandParameter = commandParameter;
    }

    public string Title { get; }
    public string Category { get; }
    public string Icon { get; }
    public string? ShortcutHint { get; }
    public ICommand Command { get; }
    public object? CommandParameter { get; }
}
