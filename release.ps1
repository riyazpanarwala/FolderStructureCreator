<#
.SYNOPSIS
    One-click release script: Creates and pushes a version tag to trigger GitHub Actions.
.EXAMPLE
    .\release.ps1 v5.0.2
#>
param(
    [Parameter(Position=0)]
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Read-Host "Enter version tag (e.g. v5.0.2)"
}

if (-not $Version.StartsWith("v")) {
    $Version = "v$Version"
}

Write-Host "==> Tagging version $Version..." -ForegroundColor Cyan
git tag $Version

Write-Host "==> Pushing $Version to GitHub..." -ForegroundColor Cyan
git push origin $Version

Write-Host ""
Write-Host "==> Done! GitHub Actions workflow triggered for $Version." -ForegroundColor Green
