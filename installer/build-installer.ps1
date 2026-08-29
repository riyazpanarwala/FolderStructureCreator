<#
.SYNOPSIS
    Publishes Folder Structure Creator as a self-contained win-x64 exe and compiles
    the Inno Setup installer, in one step.

.EXAMPLE
    cd installer
    .\build-installer.ps1

Produces:
    ..\publish\FolderStructureCreator.exe          (raw self-contained exe)
    ..\installer-output\FolderStructureCreatorSetup.exe   (installer to share with your team)
#>

$ErrorActionPreference = "Stop"

$root       = Split-Path -Parent $PSScriptRoot
$root       = if ($root) { $root } else { (Get-Location).Path }
$scriptDir  = $PSScriptRoot
$repoRoot   = Split-Path -Parent $scriptDir
$csproj     = Join-Path $repoRoot "src\FolderStructureCreator\FolderStructureCreator.csproj"
$publishDir = Join-Path $repoRoot "publish"

Write-Host "==> Publishing self-contained win-x64 build..." -ForegroundColor Cyan
dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed. Fix build errors above before packaging the installer."
    exit 1
}

Write-Host "==> Locating Inno Setup Compiler (ISCC.exe)..." -ForegroundColor Cyan
$isccCandidates = New-Object System.Collections.Generic.List[string]
$isccCandidates.Add("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")
$isccCandidates.Add("$env:ProgramFiles\Inno Setup 6\ISCC.exe")

$fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($fromPath) { $isccCandidates.Add($fromPath.Source) }

$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning "Inno Setup Compiler not found. Install it from https://jrsoftware.org/isdl.php, then either:"
    Write-Warning "  - re-run this script, or"
    Write-Warning "  - open installer\FolderStructureCreator.iss in the Inno Setup app and click Compile."
    exit 1
}

Write-Host "==> Compiling installer with $iscc ..." -ForegroundColor Cyan
& $iscc "$scriptDir\FolderStructureCreator.iss"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed. See output above."
    exit 1
}

Write-Host ""
Write-Host "==> Done. Installer is at:" -ForegroundColor Green
Write-Host "    $repoRoot\installer-output\FolderStructureCreatorSetup.exe" -ForegroundColor Green
