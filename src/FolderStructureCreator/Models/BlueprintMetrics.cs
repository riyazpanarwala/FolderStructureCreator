using System;
using System.Collections.Generic;

namespace FolderStructureCreator.Models;

public enum SafetySeverity
{
    Info,
    Warning,
    Error
}

public class PathSafetyIssue
{
    public SafetySeverity Severity { get; set; } = SafetySeverity.Warning;
    public string IssueType { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string SeverityBadge => Severity switch
    {
        SafetySeverity.Error => "[ERROR]",
        SafetySeverity.Warning => "[WARN]",
        _ => "[INFO]"
    };
}

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
    public int MaxDepth { get; set; }
    public double AvgBranching { get; set; }
    public int MaxPathLength { get; set; }
    public string DeepestFolderPath { get; set; } = string.Empty;
    public string LongestPath { get; set; } = string.Empty;

    public string NamingConvention { get; set; } = "Not enough data";

    public List<DepthDistributionItem> DepthDistribution { get; set; } = new();
    public List<HeaviestSubtreeItem> HeaviestSubtrees { get; set; } = new();
    public List<PathSafetyIssue> SafetyIssues { get; set; } = new();

    public bool HasIssues => SafetyIssues.Count > 0;
    public int ErrorCount => SafetyIssues.FindAll(i => i.Severity == SafetySeverity.Error).Count;
    public int WarningCount => SafetyIssues.FindAll(i => i.Severity == SafetySeverity.Warning).Count;

    public string PathLengthStatus => MaxPathLength switch
    {
        0 => "None",
        < 200 => "Safe (<200)",
        < 260 => "Near Limit (200-259)",
        _ => "Violation (>=260)"
    };
}
