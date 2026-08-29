using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FolderStructureCreator.Models;

namespace FolderStructureCreator.Services;

public static class ScriptGeneratorService
{
    /// <summary>
    /// Recursively collects relative folder paths from top to bottom (parents before children).
    /// </summary>
    public static List<string> CollectRelativePaths(IEnumerable<FolderNode> roots)
    {
        var paths = new List<string>();

        void Traverse(FolderNode node, string parentPath)
        {
            string currentPath = string.IsNullOrEmpty(parentPath) ? node.Name : $"{parentPath}/{node.Name}";
            paths.Add(currentPath);

            foreach (var child in node.Children)
            {
                Traverse(child, currentPath);
            }
        }

        foreach (var root in roots)
        {
            Traverse(root, string.Empty);
        }

        return paths;
    }

    /// <summary>
    /// Generates a standalone PowerShell (.ps1) script to create the folder blueprint structure.
    /// </summary>
    public static string GeneratePowerShellScript(IEnumerable<FolderNode> roots)
    {
        var relativePaths = CollectRelativePaths(roots);
        var sb = new StringBuilder();

        sb.AppendLine("# ==============================================================================");
        sb.AppendLine("# Folder Structure Creator - Auto-generated PowerShell Script");
        sb.AppendLine($"# Total Folders: {relativePaths.Count}");
        sb.AppendLine($"# Generated Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("# ==============================================================================");
        sb.AppendLine();
        sb.AppendLine("param(");
        sb.AppendLine("    [Parameter(Position=0)]");
        sb.AppendLine("    [string]$Target = ''");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("if ([string]::IsNullOrWhiteSpace($Target)) {");
        sb.AppendLine("    $Target = if ($PSScriptRoot) { $PSScriptRoot } else { Get-Location }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Write-Host \"🚀 Creating folder structure in: $Target\" -ForegroundColor Cyan");
        sb.AppendLine();
        sb.AppendLine("$folders = @(");

        for (int i = 0; i < relativePaths.Count; i++)
        {
            string escaped = relativePaths[i].Replace("'", "''");
            string comma = (i < relativePaths.Count - 1) ? "," : "";
            sb.AppendLine($"    '{escaped}'{comma}");
        }

        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("$createdCount = 0");
        sb.AppendLine("foreach ($relPath in $folders) {");
        sb.AppendLine("    $fullPath = Join-Path $Target $relPath");
        sb.AppendLine("    if (-not (Test-Path -LiteralPath $fullPath)) {");
        sb.AppendLine("        New-Item -ItemType Directory -LiteralPath $fullPath -Force | Out-Null");
        sb.AppendLine("        $createdCount++");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Write-Host \"✅ Done! Created $createdCount folder(s) successfully.\" -ForegroundColor Green");
        sb.AppendLine("Write-Host \"\"");
        sb.AppendLine("Read-Host -Prompt \"Press Enter to exit...\"");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a standalone Windows Batch (.bat) script to create the folder blueprint structure.
    /// </summary>
    public static string GenerateBatchScript(IEnumerable<FolderNode> roots)
    {
        var relativePaths = CollectRelativePaths(roots);
        var sb = new StringBuilder();

        sb.AppendLine("@echo off");
        sb.AppendLine(":: ==============================================================================");
        sb.AppendLine(":: Folder Structure Creator - Auto-generated Windows Batch Script");
        sb.AppendLine($":: Total Folders: {relativePaths.Count}");
        sb.AppendLine($":: Generated Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(":: ==============================================================================");
        sb.AppendLine();
        sb.AppendLine("set \"TARGET_DIR=%~1\"");
        sb.AppendLine("if \"%TARGET_DIR%\"==\"\" set \"TARGET_DIR=%CD%\"");
        sb.AppendLine();
        sb.AppendLine("echo Creating folder structure in: %TARGET_DIR%");
        sb.AppendLine("echo.");

        foreach (var relPath in relativePaths)
        {
            string winPath = relPath.Replace("%", "%%").Replace("\"", "").Replace('/', '\\');
            sb.AppendLine($"mkdir \"%TARGET_DIR%\\{winPath}\" 2>nul");
        }

        sb.AppendLine();
        sb.AppendLine("echo.");
        sb.AppendLine("echo Done! Folder structure created successfully.");
        sb.AppendLine("pause");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a standalone Linux/macOS Bash (.sh) script to create the folder blueprint structure.
    /// Uses LF (\n) line endings for Unix compatibility.
    /// </summary>
    public static string GenerateBashScript(IEnumerable<FolderNode> roots)
    {
        var relativePaths = CollectRelativePaths(roots);
        var sb = new StringBuilder();

        sb.Append("#!/usr/bin/env bash\n");
        sb.Append("# ==============================================================================\n");
        sb.Append("# Folder Structure Creator - Auto-generated Bash Script\n");
        sb.Append($"# Total Folders: {relativePaths.Count}\n");
        sb.Append($"# Generated Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        sb.Append("# ==============================================================================\n\n");
        sb.Append("TARGET_DIR=\"${1:-$(pwd)}\"\n\n");
        sb.Append("echo \"Creating folder structure in: ${TARGET_DIR}\"\n\n");

        foreach (var relPath in relativePaths)
        {
            string escaped = relPath.Replace("\\", "\\\\")
                                    .Replace("\"", "\\\"")
                                    .Replace("$", "\\$")
                                    .Replace("`", "\\`");
            sb.Append($"mkdir -p \"${{TARGET_DIR}}/{escaped}\"\n");
        }

        sb.Append("\necho \"Done! Folder structure created successfully.\"\n");

        return sb.ToString();
    }
}
