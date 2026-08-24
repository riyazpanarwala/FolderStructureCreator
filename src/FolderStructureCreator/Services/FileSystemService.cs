using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using FolderStructureCreator.Models;

namespace FolderStructureCreator.Services;

public static class FileSystemService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);
    /// <summary>Enumerates real, mounted drives for the root of the left-hand browser.</summary>
    public static IEnumerable<string> GetDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
                yield return drive.RootDirectory.FullName;
        }
    }

    /// <summary>Safety caps for reading real folders into memory, so a huge or deeply-nested
    /// reference folder can never freeze the UI - the scan always stops at a hard ceiling.</summary>
    public const int MaxItemsPerLevel = 500;   // folders + files combined, per directory
    public const int MaxImportTotalNodes = 8000;
    public const int MaxImportDepth = 60;

    public record DirectoryEntry(string Path, bool IsDirectory);

    public class DirectoryScanResult
    {
        public List<DirectoryEntry> Entries { get; } = new();
        /// <summary>True if this level had more items than MaxItemsPerLevel (some were left out).</summary>
        public bool Truncated { get; set; }
    }

    public static bool IsSortAscending { get; set; } = true;

    /// <summary>
    /// Reads one directory level (folders then files), stopping the instant MaxItemsPerLevel is
    /// reached. Because the underlying enumeration is lazy and we break out immediately, this is
    /// bounded work even if the real directory contains millions of entries - it never scans the
    /// whole thing just to find out it's huge.
    /// </summary>
    public static DirectoryScanResult GetDirectoryEntriesSafe(string path, int maxItems = MaxItemsPerLevel)
    {
        var result = new DirectoryScanResult();
        try
        {
            ScanInto(Directory.EnumerateDirectories(path), isDirectory: true, maxItems, result);
            if (!result.Truncated)
                ScanInto(Directory.EnumerateFiles(path), isDirectory: false, maxItems, result);
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        catch (SecurityException) { }

        result.Entries.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1; // folders first
            var nameA = Path.GetFileName(a.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var nameB = Path.GetFileName(b.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(nameA)) nameA = a.Path;
            if (string.IsNullOrEmpty(nameB)) nameB = b.Path;
            int comp = NaturalStringComparer.Instance.Compare(nameA, nameB);
            return IsSortAscending ? comp : -comp;
        });

        return result;
    }

    private static void ScanInto(IEnumerable<string> source, bool isDirectory, int maxItems, DirectoryScanResult result)
    {
        foreach (var entry in source)
        {
            if (result.Entries.Count >= maxItems)
            {
                result.Truncated = true;
                return; // stop reading this directory immediately - no further disk work at this level
            }

            try
            {
                var attrs = File.GetAttributes(entry);
                if (attrs.HasFlag(FileAttributes.System)) continue;
                if (!isDirectory && attrs.HasFlag(FileAttributes.Hidden)) continue;
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            result.Entries.Add(new DirectoryEntry(entry, isDirectory));
        }
    }

    /// <summary>
    /// Gets subdirectories of a path, silently skipping ones that raise access/IO errors
    /// (system folders, junctions, permission-denied, etc.) instead of blowing up the UI.
    /// </summary>
    public static IEnumerable<string> GetSubDirectoriesSafe(string path)
        => GetDirectoryEntriesSafe(path, MaxItemsPerLevel).Entries
            .Where(e => e.IsDirectory)
            .Select(e => e.Path);

    /// <summary>Strips characters that are illegal in Windows folder names.</summary>
    public static string SanitizeFolderName(string rawName)
    {
        var name = rawName.Trim();
        if (name.Length == 0) return "New Folder";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();

        // Windows also disallows trailing dots/spaces and a handful of reserved names.
        cleaned = cleaned.TrimEnd('.', ' ');
        if (cleaned.Length == 0) cleaned = "New Folder";

        string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
                               "LPT1", "LPT2", "LPT3", "LPT4" };
        if (reserved.Contains(cleaned.ToUpperInvariant()))
            cleaned = "_" + cleaned;

        return cleaned;
    }

    public class CreateResult
    {
        public int CreatedCount { get; set; }
        public int AlreadyExistedCount { get; set; }
        public List<string> Errors { get; } = new();
        public bool Success => Errors.Count == 0;
    }

    /// <summary>Reads only the subdirectories of a level (files are skipped entirely, so they never
    /// consume the per-level item cap), reporting whether this level had more folders than the cap.</summary>
    private static (List<string> Directories, bool Truncated) GetSubDirectoriesWithTruncation(string path, int maxItems = MaxItemsPerLevel)
    {
        var scan = new DirectoryScanResult();
        try
        {
            ScanInto(Directory.EnumerateDirectories(path), isDirectory: true, maxItems, scan);
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        catch (SecurityException) { }

        scan.Entries.Sort((a, b) =>
        {
            var nameA = Path.GetFileName(a.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var nameB = Path.GetFileName(b.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(nameA)) nameA = a.Path;
            if (string.IsNullOrEmpty(nameB)) nameB = b.Path;
            int comp = NaturalStringComparer.Instance.Compare(nameA, nameB);
            return IsSortAscending ? comp : -comp;
        });
        return (scan.Entries.Select(e => e.Path).ToList(), scan.Truncated);
    }

    public class ImportResult
    {
        public FolderNode Root { get; set; } = null!;
        public int FolderCount { get; set; }
        /// <summary>True if the scan hit a safety limit (too many folders or too deep) and stopped early.</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>
    /// Recursively reads a real folder on disk and builds an in-memory tree that mirrors its
    /// subfolder structure. Folders only - files in the reference folder are never read into
    /// this tree, and never create anything on disk later either.
    ///
    /// Bounded so it can never hang: each directory level stops at MaxItemsPerLevel subfolders,
    /// the whole import stops at MaxImportTotalNodes folders total, and recursion stops at
    /// MaxImportDepth levels deep. If any limit is hit, ImportResult.Truncated is set to true.
    /// </summary>
    public static ImportResult BuildFolderNodeTree(string sourcePath, int maxTotalNodes = MaxImportTotalNodes)
    {
        var result = new ImportResult();
        maxTotalNodes = Math.Clamp(maxTotalNodes, 1, MaxImportTotalNodes);
        result.Root = BuildRecursive(sourcePath, null, depth: 0, maxTotalNodes, result);
        return result;
    }

    private static FolderNode BuildRecursive(string sourcePath, FolderNode? parent, int depth, int maxTotalNodes, ImportResult result)
    {
        var name = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = sourcePath; // e.g. a drive root like "D:\"

        var node = new FolderNode(name, parent, realPath: sourcePath);
        result.FolderCount++;

        if (depth >= MaxImportDepth || result.FolderCount >= maxTotalNodes)
        {
            result.Truncated = true;
            return node; // stop descending from here, but keep whatever was already found
        }

        var (subDirs, levelTruncated) = GetSubDirectoriesWithTruncation(sourcePath);
        if (levelTruncated) result.Truncated = true;

        foreach (var subDir in subDirs)
        {
            if (result.FolderCount >= maxTotalNodes)
            {
                result.Truncated = true;
                break;
            }

            var child = BuildRecursive(subDir, node, depth + 1, maxTotalNodes, result);
            node.Children.Add(child);
        }

        return node;
    }

    /// <summary>Renames a real folder on disk using Directory.Move, with graceful fallback to creation if missing.</summary>
    public static (bool Success, string NewPath, string Error) RenameFolderOnDisk(string oldPath, string newName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(oldPath))
                return (false, oldPath, "Original folder path is empty.");

            var parent = Path.GetDirectoryName(oldPath.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
                return (false, oldPath, "Cannot rename a drive root directory.");

            var safeName = SanitizeFolderName(newName);
            var newPath = Path.Combine(parent, safeName);

            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                return (true, oldPath, string.Empty);

            if (Directory.Exists(newPath))
                return (false, oldPath, $"A folder named \"{safeName}\" already exists in the destination.");

            if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath);
                return (true, newPath, string.Empty);
            }
            else if (Directory.Exists(parent))
            {
                Directory.CreateDirectory(newPath);
                return (true, newPath, string.Empty);
            }
            else
            {
                return (true, newPath, string.Empty);
            }
        }
        catch (Exception ex)
        {
            return (false, oldPath, ex.Message);
        }
    }

    /// <summary>Creates a new folder on disk under parentPath.</summary>
    public static (bool Success, string NewPath, string Error) CreateFolderOnDisk(string parentPath, string folderName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
                return (false, string.Empty, "Parent target path does not exist on disk.");

            var safeName = SanitizeFolderName(folderName);
            var newPath = Path.Combine(parentPath, safeName);

            if (!Directory.Exists(newPath))
                Directory.CreateDirectory(newPath);

            return (true, newPath, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }

    /// <summary>Sends a folder to the Windows Recycle Bin using shell SHFileOperation.</summary>
    public static (bool Success, string Error) DeleteFolderToRecycleBin(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return (true, string.Empty);

            var fileop = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
            };

            int res = SHFileOperation(ref fileop);
            if (res == 0)
                return (true, string.Empty);

            // Fallback to Directory.Delete if shell operation fails
            if (Directory.Exists(path))
                Directory.Delete(path, true);

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Moves a folder on disk to a new parent folder directory.</summary>
    public static (bool Success, string NewPath, string Error) MoveFolderOnDisk(string sourcePath, string destParentPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
                return (false, sourcePath, "Source folder does not exist on disk.");

            if (string.IsNullOrWhiteSpace(destParentPath) || !Directory.Exists(destParentPath))
                return (false, sourcePath, "Destination parent folder does not exist on disk.");

            var folderName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
            var targetPath = Path.Combine(destParentPath, folderName);

            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                return (true, sourcePath, string.Empty);

            if (Directory.Exists(targetPath))
                return (false, sourcePath, $"A folder named \"{folderName}\" already exists at the destination.");

            var normSource = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var normTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (normTarget.StartsWith(normSource, StringComparison.OrdinalIgnoreCase))
                return (false, sourcePath, "Cannot move a folder into one of its own subfolders.");

            Directory.Move(sourcePath, targetPath);
            return (true, targetPath, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, sourcePath, ex.Message);
        }
    }

    /// <summary>
    /// Recursively creates every folder in the given blueprint tree(s) under basePath.
    /// If a folder fails to create, its subtree is skipped (nothing meaningful to nest
    /// under a path that doesn't exist) but sibling branches still proceed.
    /// </summary>
    public static CreateResult CreateStructure(IEnumerable<FolderNode> roots, string basePath)
    {
        var result = new CreateResult();

        if (!Directory.Exists(basePath))
        {
            result.Errors.Add($"Target path does not exist: {basePath}");
            return result;
        }

        foreach (var root in roots)
            CreateRecursive(root, basePath, result);

        return result;
    }

    private static void CreateRecursive(FolderNode node, string parentPath, CreateResult result)
    {
        // File-reference nodes (from an import) are for on-screen context only - never created.
        if (node.IsFile) return;

        var safeName = SanitizeFolderName(node.Name);
        var fullPath = Path.Combine(parentPath, safeName);

        try
        {
            if (Directory.Exists(fullPath))
            {
                result.AlreadyExistedCount++;
            }
            else
            {
                Directory.CreateDirectory(fullPath);
                result.CreatedCount++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or PathTooLongException)
        {
            result.Errors.Add($"{fullPath}: {ex.Message}");
            return; // don't attempt children under a path we couldn't create
        }

        foreach (var child in node.Children)
            CreateRecursive(child, fullPath, result);
    }

    /// <summary>
    /// Opens Windows Explorer for the specified file or folder path.
    /// If the path does not exist on disk, attempts to open the nearest existing parent directory.
    /// </summary>
    public static (bool Success, string OpenedPath, string Error) OpenInExplorer(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return (false, string.Empty, "Path is empty.");

            var fullPath = Path.GetFullPath(path);

            if (File.Exists(fullPath))
            {
                Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                return (true, fullPath, string.Empty);
            }

            if (Directory.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                });
                return (true, fullPath, string.Empty);
            }

            // Walk up to find nearest existing parent directory
            var parent = Path.GetDirectoryName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            while (!string.IsNullOrEmpty(parent))
            {
                if (Directory.Exists(parent))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = parent,
                        UseShellExecute = true
                    });
                    return (false, parent, "PathDoesNotExistButParentOpened");
                }
                parent = Path.GetDirectoryName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }

            return (false, string.Empty, "Path does not exist on disk.");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }
}
