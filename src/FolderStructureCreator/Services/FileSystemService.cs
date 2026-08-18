using System.IO;
using System.Security;
using FolderStructureCreator.Models;

namespace FolderStructureCreator.Services;

public static class FileSystemService
{
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
            return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
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

        scan.Entries.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
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
    public static ImportResult BuildFolderNodeTree(string sourcePath)
    {
        var result = new ImportResult();
        result.Root = BuildRecursive(sourcePath, null, depth: 0, result);
        return result;
    }

    private static FolderNode BuildRecursive(string sourcePath, FolderNode? parent, int depth, ImportResult result)
    {
        var name = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = sourcePath; // e.g. a drive root like "D:\"

        var node = new FolderNode(name, parent);
        result.FolderCount++;

        if (depth >= MaxImportDepth || result.FolderCount >= MaxImportTotalNodes)
        {
            result.Truncated = true;
            return node; // stop descending from here, but keep whatever was already found
        }

        var (subDirs, levelTruncated) = GetSubDirectoriesWithTruncation(sourcePath);
        if (levelTruncated) result.Truncated = true;

        foreach (var subDir in subDirs)
        {
            if (result.FolderCount >= MaxImportTotalNodes)
            {
                result.Truncated = true;
                break;
            }

            var child = BuildRecursive(subDir, node, depth + 1, result);
            node.Children.Add(child);
        }

        return node;
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
}
