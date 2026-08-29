using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FolderStructureCreator.Services;

public class IgnoreRuleService
{
    private static readonly string[] DefaultIgnorePatterns = new[]
    {
        // Software & IDE defaults
        ".git",
        ".vs",
        ".vscode",
        ".idea",
        "node_modules",
        "bin",
        "obj",
        "dist",
        "build",
        ".gradle",
        "__pycache__",
        ".pytest_cache",
        ".DS_Store",
        "Thumbs.db",

        // SolidWorks & CAD defaults
        "swbackup",
        "SWBackup",
        "AutoRecover",
        "SolidWorks AutoRecover",
        "simulation_results",
        "sldtemp",
        "SolidWorksTemp"
    };

    private readonly List<Regex> _compiledPatterns = new();
    private readonly HashSet<string> _exactNames = new(StringComparer.OrdinalIgnoreCase);

    public bool HasRules => _compiledPatterns.Count > 0 || _exactNames.Count > 0;

    public IgnoreRuleService(bool includeBuiltInDefaults = true)
    {
        if (includeBuiltInDefaults)
        {
            foreach (var pattern in DefaultIgnorePatterns)
            {
                AddPattern(pattern);
            }
        }
    }

    /// <summary>
    /// Reads and parses rules from a .structureignore or .gitignore file path if it exists.
    /// </summary>
    public void LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        try
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                AddPattern(line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore errors reading ignore files
        }
    }

    /// <summary>
    /// Parses and adds a single rule line (ignoring empty lines and comments starting with #).
    /// </summary>
    public void AddPattern(string rawPattern)
    {
        if (string.IsNullOrWhiteSpace(rawPattern)) return;

        var trimmed = rawPattern.Trim();
        if (trimmed.StartsWith('#')) return; // Comment line

        // Normalize trailing/leading slashes
        trimmed = trimmed.TrimStart('/', '\\');
        trimmed = trimmed.TrimEnd('/', '\\');

        if (string.IsNullOrWhiteSpace(trimmed)) return;

        // Simple exact name match (no glob wildcards)
        if (!trimmed.Contains('*') && !trimmed.Contains('?') && !trimmed.Contains('/') && !trimmed.Contains('\\'))
        {
            _exactNames.Add(trimmed);
            return;
        }

        // Glob to Regex conversion
        string regexPattern = "^" + Regex.Escape(trimmed)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";

        try
        {
            _compiledPatterns.Add(new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
        }
        catch
        {
            // Fallback for invalid regex pattern
        }
    }

    /// <summary>
    /// Checks if a folder or file name/path matches any of the loaded ignore rules.
    /// </summary>
    public bool IsIgnored(string name, string? relativePath = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var cleanName = name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 1. Check exact name match (e.g. "node_modules", "bin", ".git")
        if (_exactNames.Contains(cleanName))
            return true;

        // 2. Check regex/glob pattern match against name
        foreach (var regex in _compiledPatterns)
        {
            if (regex.IsMatch(cleanName))
                return true;
        }

        // 3. Check relative path match if provided
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            var normalizedPath = relativePath.Replace('\\', '/').Trim('/');
            if (_exactNames.Contains(normalizedPath))
                return true;

            foreach (var regex in _compiledPatterns)
            {
                if (regex.IsMatch(normalizedPath))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Factory helper to build an IgnoreRuleService for a source directory, auto-detecting
    /// .structureignore and .gitignore files in the root folder.
    /// </summary>
    public static IgnoreRuleService CreateForSource(string sourceDirectory, bool includeBuiltInDefaults = true)
    {
        var ignoreService = new IgnoreRuleService(includeBuiltInDefaults);

        if (!string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(sourceDirectory))
        {
            var structureIgnore = Path.Combine(sourceDirectory, ".structureignore");
            if (File.Exists(structureIgnore))
            {
                ignoreService.LoadFromFile(structureIgnore);
            }

            var gitIgnore = Path.Combine(sourceDirectory, ".gitignore");
            if (File.Exists(gitIgnore))
            {
                ignoreService.LoadFromFile(gitIgnore);
            }
        }

        return ignoreService;
    }
}
