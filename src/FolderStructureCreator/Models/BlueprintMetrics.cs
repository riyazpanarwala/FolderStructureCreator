using System;
using System.Collections.Generic;

namespace FolderStructureCreator.Models;

public class DepthDistributionItem
{
    public int Level { get; set; }
    public string LevelLabel => $"Level {Level}";
    public int Count { get; set; }
    public double Percentage { get; set; }
    public double BarWidth { get; set; }
}

public class HeaviestSubtreeItem
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public int TotalDescendants { get; set; }
}

public class BlueprintMetrics
{
    public int TotalFolders { get; set; }
    public bool IsTruncated { get; set; }
    public int TruncatedLimit { get; set; }
    public string TotalFoldersDisplay => IsTruncated ? $"{TotalFolders:N0}+" : $"{TotalFolders:N0}";
    public int MaxDepth { get; set; }
    public double AvgBranching { get; set; }
    public string DeepestFolderPath { get; set; } = string.Empty;

    public string NamingConvention { get; set; } = "Not enough data";

    public List<DepthDistributionItem> DepthDistribution { get; set; } = new();
    public List<HeaviestSubtreeItem> HeaviestSubtrees { get; set; } = new();
}
