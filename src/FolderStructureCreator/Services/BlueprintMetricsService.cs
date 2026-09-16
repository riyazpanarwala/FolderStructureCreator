using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FolderStructureCreator.Models;

namespace FolderStructureCreator.Services;

public static class BlueprintMetricsService
{
    public static BlueprintMetrics CalculateMetrics(IEnumerable<FolderNode> rootFolders, string? targetDestination = null)
    {
        var metrics = new BlueprintMetrics();
        var rootsList = rootFolders.Where(f => !f.IsFile).ToList();

        if (rootsList.Count == 0)
        {
            return metrics;
        }

        metrics.IsTruncated = rootsList.Any(r => r.IsTruncated);
        metrics.TruncatedLimit = FileSystemService.MaxImportTotalNodes;

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

            int descendantCount = 0;
            var nonFileChildren = node.Children.Where(c => !c.IsFile).ToList();

            foreach (var child in nonFileChildren)
            {
                string childPath = !string.IsNullOrWhiteSpace(child.RealPath)
                    ? child.RealPath
                    : Path.Combine(currentPath, child.Name);
                Traverse(child, level + 1, childPath);
                descendantCount += 1 + subtreeCounts.GetValueOrDefault(child, 0);
            }

            subtreeCounts[node] = descendantCount;
        }

        foreach (var root in rootsList)
        {
            string rootPath;
            if (!string.IsNullOrWhiteSpace(root.RealPath))
            {
                rootPath = root.RealPath;
            }
            else
            {
                var baseFolder = Path.GetFileName(basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrEmpty(baseFolder) && string.Equals(baseFolder, root.Name, StringComparison.OrdinalIgnoreCase))
                {
                    rootPath = basePath;
                }
                else
                {
                    rootPath = Path.Combine(basePath, root.Name);
                }
            }

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
