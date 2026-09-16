using System.Collections.Generic;
using System.IO;
using FolderStructureCreator.Models;

namespace FolderStructureCreator.Services;

public record DirectoryDiffResult(int MissingCount, int MatchedCount, int ExtraCount)
{
    public bool HasMissing => MissingCount > 0;
    public string SummaryText => $"🟢 {MissingCount} Missing | ⚪ {MatchedCount} Matched{(ExtraCount > 0 ? $" | 🟠 {ExtraCount} Extra" : "")}";
}

public static class DirectoryDiffService
{
    /// <summary>
    /// Compares the in-memory blueprint tree against the physical target directory on disk.
    /// Sets DiffStatus on each node and returns diff statistics.
    /// </summary>
    public static DirectoryDiffResult EvaluateDiff(IEnumerable<FolderNode> roots, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
        {
            return new DirectoryDiffResult(0, 0, 0);
        }

        int missing = 0;
        int matched = 0;
        int extra = 0;

        var rootsList = roots.ToList();

        // Check extra directories directly under targetPath
        try
        {
            var diskRootDirs = Directory.GetDirectories(targetPath);
            var rootNames = new HashSet<string>(rootsList.Select(r => FileSystemService.SanitizeFolderName(r.Name)), StringComparer.OrdinalIgnoreCase);
            foreach (var d in diskRootDirs)
            {
                var dName = Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!rootNames.Contains(dName))
                    extra++;
            }
        }
        catch { }

        foreach (var root in rootsList)
        {
            EvaluateNodeRecursive(root, targetPath, ref missing, ref matched, ref extra);
        }

        return new DirectoryDiffResult(missing, matched, extra);
    }

    private static void EvaluateNodeRecursive(FolderNode node, string currentBasePath, ref int missing, ref int matched, ref int extra)
    {
        var sanitizedName = FileSystemService.SanitizeFolderName(node.Name);
        var itemPath = Path.Combine(currentBasePath, sanitizedName);

        bool exists = node.IsFile ? File.Exists(itemPath) : Directory.Exists(itemPath);

        if (exists)
        {
            node.DiffStatus = NodeDiffStatus.MatchesDisk;
            matched++;

            if (!node.IsFile)
            {
                try
                {
                    var diskSubDirs = Directory.GetDirectories(itemPath);
                    var childNames = new HashSet<string>(node.Children.Select(c => FileSystemService.SanitizeFolderName(c.Name)), StringComparer.OrdinalIgnoreCase);
                    foreach (var d in diskSubDirs)
                    {
                        var dName = Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (!childNames.Contains(dName))
                            extra++;
                    }
                }
                catch { }
            }
        }
        else
        {
            node.DiffStatus = NodeDiffStatus.MissingOnDisk;
            missing++;
        }

        foreach (var child in node.Children)
        {
            EvaluateNodeRecursive(child, itemPath, ref missing, ref matched, ref extra);
        }
    }
}
