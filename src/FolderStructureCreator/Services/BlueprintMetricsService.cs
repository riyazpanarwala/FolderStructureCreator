using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FolderStructureCreator.Models;

namespace FolderStructureCreator.Services;

public static class BlueprintMetricsService
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();

    public static BlueprintMetrics CalculateMetrics(IEnumerable<FolderNode> rootFolders, string? targetDestination = null)
    {
        var metrics = new BlueprintMetrics();
        var rootsList = rootFolders.Where(f => !f.IsFile).ToList();

        if (rootsList.Count == 0)
        {
            return metrics;
        }

        var allFolders = new List<FolderNode>();
        var levelCounts = new Dictionary<int, int>();
        var subtreeCounts = new Dictionary<FolderNode, int>();
        var fullPaths = new Dictionary<FolderNode, string>();

        string basePath = string.IsNullOrWhiteSpace(targetDestination) ? "Destination:" : targetDestination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Recursive tree traversal
        void Traverse(FolderNode node, int level, string currentPath)
        {
            allFolders.Add(node);

            // Depth distribution
            levelCounts[level] = levelCounts.GetValueOrDefault(level, 0) + 1;
            if (level > metrics.MaxDepth)
            {
                metrics.MaxDepth = level;
                metrics.DeepestFolderPath = currentPath;
            }

            fullPaths[node] = currentPath;
            if (currentPath.Length > metrics.MaxPathLength)
            {
                metrics.MaxPathLength = currentPath.Length;
                metrics.LongestPath = currentPath;
            }

            int descendantCount = 0;
            var nonFileChildren = node.Children.Where(c => !c.IsFile).ToList();

            // Check sibling case collisions
            var groupedByCase = nonFileChildren.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groupedByCase)
            {
                if (group.Count() > 1)
                {
                    metrics.SafetyIssues.Add(new PathSafetyIssue
                    {
                        Severity = SafetySeverity.Error,
                        IssueType = "Case Collision",
                        Path = currentPath,
                        Message = $"Duplicate sibling folders differ only by casing: '{string.Join("', '", group.Select(x => x.Name))}'. Windows filesystem cannot distinguish them."
                    });
                }
            }

            foreach (var child in nonFileChildren)
            {
                string childPath = Path.Combine(currentPath, child.Name);
                Traverse(child, level + 1, childPath);
                descendantCount += 1 + subtreeCounts.GetValueOrDefault(child, 0);
            }

            subtreeCounts[node] = descendantCount;
        }

        // Check root-level collisions
        var rootGrouped = rootsList.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var group in rootGrouped)
        {
            if (group.Count() > 1)
            {
                metrics.SafetyIssues.Add(new PathSafetyIssue
                {
                    Severity = SafetySeverity.Error,
                    IssueType = "Case Collision",
                    Path = basePath,
                    Message = $"Duplicate root folders differ only by casing: '{string.Join("', '", group.Select(x => x.Name))}'."
                });
            }
        }

        foreach (var root in rootsList)
        {
            string rootPath = Path.Combine(basePath, root.Name);
            Traverse(root, 1, rootPath);
        }

        metrics.TotalFolders = allFolders.Count;

        // Calculate Average Branching Factor
        var nodesWithChildren = allFolders.Where(n => n.Children.Any(c => !c.IsFile)).ToList();
        if (nodesWithChildren.Count > 0)
        {
            double totalBranches = nodesWithChildren.Sum(n => n.Children.Count(c => !c.IsFile));
            metrics.AvgBranching = Math.Round(totalBranches / nodesWithChildren.Count, 1);
        }
        else
        {
            metrics.AvgBranching = 0;
        }

        // Calculate Depth Distribution
        int maxCountInLevel = levelCounts.Values.DefaultIfEmpty(1).Max();
        for (int l = 1; l <= metrics.MaxDepth; l++)
        {
            int count = levelCounts.GetValueOrDefault(l, 0);
            double percentage = metrics.TotalFolders > 0 ? (double)count / metrics.TotalFolders * 100.0 : 0;
            double barWidth = maxCountInLevel > 0 ? ((double)count / maxCountInLevel) * 160.0 : 0;

            metrics.DepthDistribution.Add(new DepthDistributionItem
            {
                Level = l,
                Count = count,
                Percentage = Math.Round(percentage, 1),
                BarWidth = Math.Max(barWidth, 6.0)
            });
        }

        // Heaviest subtrees (top 3)
        var topSubtrees = subtreeCounts
            .Where(kvp => kvp.Value > 0)
            .OrderByDescending(kvp => kvp.Value)
            .Take(3)
            .Select(kvp =>
            {
                string rel = fullPaths.GetValueOrDefault(kvp.Key, kvp.Key.Name);
                if (rel.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                {
                    rel = rel.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                return new HeaviestSubtreeItem
                {
                    Name = kvp.Key.Name,
                    RelativePath = string.IsNullOrEmpty(rel) ? kvp.Key.Name : rel,
                    TotalDescendants = kvp.Value
                };
            })
            .ToList();
        metrics.HeaviestSubtrees = topSubtrees;

        // Path Safety & Hygiene Checks
        foreach (var folder in allFolders)
        {
            string name = folder.Name;
            string fullPath = fullPaths.GetValueOrDefault(folder, name);

            // 1. Reserved Win32 Device Names
            string baseNameWithoutExt = Path.GetFileNameWithoutExtension(name);
            if (ReservedNames.Contains(name) || ReservedNames.Contains(baseNameWithoutExt))
            {
                metrics.SafetyIssues.Add(new PathSafetyIssue
                {
                    Severity = SafetySeverity.Error,
                    IssueType = "Reserved Name",
                    Path = fullPath,
                    Message = $"'{name}' is a reserved Windows system device name (CON, PRN, AUX, NUL, COM1-9, LPT1-9) and cannot be created on disk."
                });
            }

            // 2. Invalid Characters
            if (name.IndexOfAny(InvalidPathChars) >= 0)
            {
                metrics.SafetyIssues.Add(new PathSafetyIssue
                {
                    Severity = SafetySeverity.Error,
                    IssueType = "Illegal Characters",
                    Path = fullPath,
                    Message = $"'{name}' contains illegal Windows filename characters (< > : \" / \\ | ? *)."
                });
            }

            // 3. Trailing space or period
            if (name.EndsWith(' ') || name.EndsWith('.'))
            {
                metrics.SafetyIssues.Add(new PathSafetyIssue
                {
                    Severity = SafetySeverity.Warning,
                    IssueType = "Trailing Character",
                    Path = fullPath,
                    Message = $"'{name}' ends with a space or dot. Windows Explorer will trim or lock this directory."
                });
            }

            // 4. MAX_PATH check
            if (fullPath.Length >= 260)
            {
                metrics.SafetyIssues.Add(new PathSafetyIssue
                {
                    Severity = SafetySeverity.Error,
                    IssueType = "Path Length Limit (MAX_PATH)",
                    Path = fullPath,
                    Message = $"Full path reaches {fullPath.Length} characters, which exceeds the Windows 260-character MAX_PATH limit."
                });
            }
            else if (fullPath.Length >= 220)
            {
                metrics.SafetyIssues.Add(new PathSafetyIssue
                {
                    Severity = SafetySeverity.Warning,
                    IssueType = "Path Length Warning",
                    Path = fullPath,
                    Message = $"Full path is {fullPath.Length} characters (near 260 MAX_PATH threshold)."
                });
            }
        }

        // Detect Naming Convention
        metrics.NamingConvention = DetectNamingConvention(allFolders.Select(f => f.Name));

        return metrics;
    }

    private static string DetectNamingConvention(IEnumerable<string> names)
    {
        int kebabCount = 0;
        int snakeCount = 0;
        int camelCount = 0;
        int pascalCount = 0;
        int totalAnalyzed = 0;

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2) continue;
            totalAnalyzed++;

            if (name.Contains('-') && !name.Contains('_') && name.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-'))
            {
                kebabCount++;
            }
            else if (name.Contains('_') && !name.Contains('-') && name.All(c => char.IsLower(c) || char.IsDigit(c) || c == '_'))
            {
                snakeCount++;
            }
            else if (char.IsLower(name[0]) && name.Any(char.IsUpper) && !name.Contains('-') && !name.Contains('_') && !name.Contains(' '))
            {
                camelCount++;
            }
            else if (char.IsUpper(name[0]) && name.Any(char.IsLower) && !name.Contains('-') && !name.Contains('_') && !name.Contains(' '))
            {
                pascalCount++;
            }
        }

        if (totalAnalyzed < 3) return "Mixed / Custom";

        var counts = new (string Style, int Count)[]
        {
            ("kebab-case", kebabCount),
            ("snake_case", snakeCount),
            ("camelCase", camelCount),
            ("PascalCase", pascalCount)
        };

        var best = counts.OrderByDescending(x => x.Count).First();
        double pct = (double)best.Count / totalAnalyzed * 100.0;

        if (pct >= 60.0)
        {
            return $"{best.Style} ({Math.Round(pct)}% consistent)";
        }

        return "Mixed styles";
    }
}
