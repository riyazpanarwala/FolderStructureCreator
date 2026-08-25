using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using FolderStructureCreator.Models;
using FolderStructureCreator.Services;
using FolderStructureCreator.ViewModels;

namespace FolderStructureCreator;

public partial class App : Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global crash guard so the app never dies silently with a raw exception in GUI mode.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{args.Exception.Message}",
                "Folder Structure Creator",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        var options = ParseCliArgs(e.Args);

        if (options.IsCliMode)
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                }
            }
            catch { }

            if (options.ShowHelp)
            {
                PrintHelp();
                Shutdown(0);
                return;
            }

            if (string.IsNullOrWhiteSpace(options.SourcePath) || string.IsNullOrWhiteSpace(options.TargetPath))
            {
                Console.WriteLine("\n[ERROR] Both --source and --target paths are required for CLI folder-to-folder copy.\n");
                PrintHelp();
                Shutdown(1);
                return;
            }

            var fullSourcePath = Path.GetFullPath(options.SourcePath);
            var fullTargetPath = Path.GetFullPath(options.TargetPath);

            if (!Directory.Exists(fullSourcePath))
            {
                Console.WriteLine($"\n[ERROR] Source directory does not exist: {fullSourcePath}\n");
                Shutdown(1);
                return;
            }

            try
            {
                if (!Directory.Exists(fullTargetPath))
                {
                    Directory.CreateDirectory(fullTargetPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[ERROR] Unable to create target directory '{fullTargetPath}': {ex.Message}\n");
                Shutdown(1);
                return;
            }

            Console.WriteLine("\n--- Folder Structure Creator (CLI Mode) ---");
            Console.WriteLine($"Source: {fullSourcePath}");
            Console.WriteLine($"Target: {fullTargetPath}");
            if (options.IsDryRun) Console.WriteLine("Mode:   [DRY RUN - No disk writes]");
            Console.WriteLine();

            var importResult = FileSystemService.BuildFolderNodeTree(fullSourcePath);
            if (importResult.Root == null || importResult.Root.Children.Count == 0)
            {
                Console.WriteLine($"[INFO] Source directory '{fullSourcePath}' contains no subfolders to copy.");
                Shutdown(0);
                return;
            }

            if (options.IsDryRun)
            {
                Console.WriteLine("Folders that would be created:");
                int count = PrintDryRun(importResult.Root.Children, fullTargetPath);
                Console.WriteLine($"\nTotal: {count} folder(s) would be created under target.\n");
                Shutdown(0);
                return;
            }

            var createResult = FileSystemService.CreateStructure(importResult.Root.Children, fullTargetPath);
            Console.WriteLine($"[RESULT] Folders created: {createResult.CreatedCount}");
            if (createResult.AlreadyExistedCount > 0)
                Console.WriteLine($"[RESULT] Already existed: {createResult.AlreadyExistedCount}");

            if (createResult.Errors.Count > 0)
            {
                Console.WriteLine($"[ERRORS] {createResult.Errors.Count} error(s) occurred:");
                foreach (var err in createResult.Errors)
                    Console.WriteLine($"  - {err}");
                Shutdown(1);
                return;
            }

            Console.WriteLine("✅ Folder structure successfully copied!\n");
            Shutdown(0);
            return;
        }

        var window = new MainWindow();
        if (!string.IsNullOrEmpty(options.DirectOpenPath) && window.DataContext is MainViewModel vm)
        {
            vm.TargetPath = options.DirectOpenPath;
        }
        window.Show();
    }

    private class CliOptions
    {
        public string? SourcePath { get; set; }
        public string? TargetPath { get; set; }
        public bool IsDryRun { get; set; }
        public bool IsSilent { get; set; }
        public bool ShowHelp { get; set; }
        public bool IsCliMode { get; set; }
        public string? DirectOpenPath { get; set; }
    }

    private static CliOptions ParseCliArgs(string[] args)
    {
        var options = new CliOptions();
        if (args.Length == 0) return options;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim();
            if (arg.Equals("--source", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-src", StringComparison.OrdinalIgnoreCase))
            {
                options.IsCliMode = true;
                if (i + 1 < args.Length) options.SourcePath = args[++i];
            }
            else if (arg.Equals("--target", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-dst", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-t", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-d", StringComparison.OrdinalIgnoreCase))
            {
                options.IsCliMode = true;
                if (i + 1 < args.Length) options.TargetPath = args[++i];
            }
            else if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                options.IsCliMode = true;
                options.IsDryRun = true;
            }
            else if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
            {
                options.IsCliMode = true;
                options.IsSilent = true;
            }
            else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-?", StringComparison.OrdinalIgnoreCase))
            {
                options.IsCliMode = true;
                options.ShowHelp = true;
            }
            else if (i == 0 && Directory.Exists(arg) && !arg.StartsWith("-"))
            {
                options.DirectOpenPath = arg;
            }
        }

        return options;
    }

    private static int PrintDryRun(IEnumerable<FolderNode> nodes, string currentPath)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            var targetPath = Path.Combine(currentPath, node.Name);
            bool exists = Directory.Exists(targetPath);
            Console.WriteLine($"  {(exists ? "[EXISTS] " : "+ CREATE  ")}{targetPath}");
            count++;
            count += PrintDryRun(node.Children, targetPath);
        }
        return count;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
Folder Structure Creator - CLI Usage

Description:
  Copies folder directory structures from a source folder directly to a target destination folder.

Usage:
  FolderStructureCreator.exe --source <source_folder> --target <target_folder> [options]

Options:
  -src, --source <path>   Source reference folder to copy structure from
  -dst, --target <path>   Destination target folder where structure will be created
  --dry-run               Preview folders to be created without writing to disk
  --silent, -s            Run headlessly without GUI popups
  -h, --help              Show this CLI help information

Examples:
  FolderStructureCreator.exe --source ""C:\Templates\CleanArchitecture"" --target ""D:\Projects\NewApp""
  FolderStructureCreator.exe -src ""C:\MyTemplate"" -dst ""C:\NewProject"" --dry-run
");
    }
}
