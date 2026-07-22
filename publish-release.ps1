<#
.SYNOPSIS
    Publishes OpenOverlay (the IRacingOverlay.App project) as an optimized, non-debug Release build.

.DESCRIPTION
    Runs `dotnet publish` in Release configuration (JIT optimizations on, debug checks off) with
    ReadyToRun precompilation for faster startup. By default this is framework-dependent (requires
    the .NET 8 desktop runtime already installed, which this dev machine has), so pass -SelfContained
    to bundle the runtime instead, for running on a machine without .NET 8 installed.

.PARAMETER SelfContained
    Bundle the .NET runtime into the output instead of depending on a system-wide install.

.PARAMETER OutputDir
    Where to place the published output. Defaults to ".\publish" under the repo root.

.EXAMPLE
    .\publish-release.ps1
    .\publish-release.ps1 -SelfContained -OutputDir dist
#>
param(
    [switch]$SelfContained,
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot "IRacingOverlay.App\IRacingOverlay.App.csproj"
$outPath = Join-Path $repoRoot $OutputDir

if (-not (Test-Path $project)) {
    Write-Host "Project not found at $project" -ForegroundColor Red
    exit 1
}

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }

Write-Host "Publishing OpenOverlay - Release, ReadyToRun, self-contained=$selfContainedValue" -ForegroundColor Cyan

& dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishReadyToRun=true `
    -p:TieredPGO=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $outPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

$exePath = Join-Path $outPath "OpenOverlay.exe"
Write-Host ""
Write-Host "Done. Optimized executable: $exePath" -ForegroundColor Green
