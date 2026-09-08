<#
.SYNOPSIS
    Interactive Release Script: Detects the latest GitHub release tag,
    calculates next Patch/Minor/Major versions, and pushes the selected tag.
.EXAMPLE
    .\release.ps1
    .\release.ps1 v5.0.5
    .\release.ps1 -DryRun
#>
param(
    [Parameter(Position=0, ValueFromPipeline=$true)]
    [string]$Version = "",
    [switch]$DryRun,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# 1. Fetch latest tags from origin
Write-Host "==> Fetching latest tags from GitHub..." -ForegroundColor Cyan
try {
    git fetch --tags origin 2>$null
} catch {
    Write-Host "Warning: Could not fetch tags from remote origin. Falling back to local tags." -ForegroundColor Yellow
}

# If version wasn't supplied via CLI argument, calculate it interactively
if ([string]::IsNullOrWhiteSpace($Version)) {
    # 2. Detect existing version tags
    $rawTags = git tag -l
    $versionList = @()

    foreach ($tag in $rawTags) {
        $clean = $tag.Trim()
        if ($clean -match '^[vV]?(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?$') {
            $major = [int]$Matches[1]
            $minor = [int]$Matches[2]
            $patch = if ($Matches[3]) { [int]$Matches[3] } else { 0 }
            $versionList += [PSCustomObject]@{
                Raw        = $clean
                Major      = $major
                Minor      = $minor
                Patch      = $patch
                VersionObj = [System.Version]::new($major, $minor, $patch)
            }
        }
    }

    if ($versionList.Count -gt 0) {
        $sorted = $versionList | Sort-Object -Property VersionObj -Descending
        $latest = $sorted[0]
        $latestTag = "v$($latest.Major).$($latest.Minor).$($latest.Patch)"
        $nextPatch = "v$($latest.Major).$($latest.Minor).$($latest.Patch + 1)"
        $nextMinor = "v$($latest.Major).$($latest.Minor + 1).0"
        $nextMajor = "v$($latest.Major + 1).0.0"
    } else {
        $latestTag = "None"
        $nextPatch = "v1.0.0"
        $nextMinor = "v1.1.0"
        $nextMajor = "v2.0.0"
    }

    # 3. Present Menu
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor DarkGray
    Write-Host " Latest release version detected: $latestTag" -ForegroundColor Yellow
    Write-Host "==================================================" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "Select the release bump type:"
    Write-Host "  [1] Patch : $nextPatch (Bug fixes, minor tweaks) [Default]" -ForegroundColor White
    Write-Host "  [2] Minor : $nextMinor (New features, enhancements)" -ForegroundColor White
    Write-Host "  [3] Major : $nextMajor (Major overhaul or breaking change)" -ForegroundColor White
    Write-Host "  [4] Custom: Enter a specific version manually" -ForegroundColor White
    Write-Host "  [5] Cancel" -ForegroundColor DarkGray
    Write-Host ""

    $choice = Read-Host "Choose an option [1-5] (Default is 1)"
    if ([string]::IsNullOrWhiteSpace($choice)) {
        $choice = "1"
    }

    switch ($choice.Trim().ToLower()) {
        "1" { $Version = $nextPatch }
        "2" { $Version = $nextMinor }
        "3" { $Version = $nextMajor }
        "4" {
            $custom = Read-Host "Enter custom version tag (e.g. v5.0.5)"
            if ([string]::IsNullOrWhiteSpace($custom)) {
                Write-Host "No version entered. Release cancelled." -ForegroundColor Red
                exit 0
            }
            $Version = $custom
        }
        "5" {
            Write-Host "Release cancelled." -ForegroundColor Yellow
            exit 0
        }
        "q" {
            Write-Host "Release cancelled." -ForegroundColor Yellow
            exit 0
        }
        default {
            Write-Host "Invalid selection '$choice'. Release cancelled." -ForegroundColor Red
            exit 1
        }
    }
}

# Normalize version prefix to 'v'
if (-not $Version.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $Version = "v$Version"
}

Write-Host ""
Write-Host "Target Version: $Version" -ForegroundColor Green
if (-not $Force) {
    $confirm = Read-Host "Tag and push $Version to GitHub? [Y/n] (Default is Y)"
    if (-not [string]::IsNullOrWhiteSpace($confirm) -and $confirm.Trim() -notmatch '^[yY]') {
        Write-Host "Release cancelled." -ForegroundColor Yellow
        exit 0
    }
}

if ($DryRun) {
    Write-Host ""
    Write-Host "[DRY RUN] git tag $Version" -ForegroundColor Magenta
    Write-Host "[DRY RUN] git push origin $Version" -ForegroundColor Magenta
    Write-Host "[DRY RUN] Completed without modifying git repository." -ForegroundColor Cyan
    return
}

# 4. Create and push git tag
Write-Host ""
Write-Host "==> Creating tag $Version..." -ForegroundColor Cyan
git tag $Version
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Failed to create tag $Version. Tag may already exist." -ForegroundColor Red
    exit 1
}

Write-Host "==> Pushing tag $Version to origin..." -ForegroundColor Cyan
git push origin $Version
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Failed to push tag $Version to origin." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "==> Success! GitHub Actions workflow has been triggered for $Version." -ForegroundColor Green
Write-Host ""
