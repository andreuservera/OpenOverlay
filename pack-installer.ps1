<#
.SYNOPSIS
    Builds OpenOverlay and packages it into a real Windows installer (Setup.exe) plus auto-update
    assets, via Velopack.

.DESCRIPTION
    Unlike publish-release.ps1 (which just produces a loose folder of files to zip up), this produces
    an actual installer: running the generated Setup.exe installs the app with Start Menu + Desktop
    shortcuts and an Add/Remove Programs entry, and installed copies check GitHub Releases for newer
    versions on every launch and silently self-update.

    The build is self-contained (bundles the .NET runtime) so installer users never have to separately
    install the .NET Desktop Runtime - worth the larger download for a real "download and run" installer.

    Requires the vpk CLI (installed automatically below if missing) and, the first time you package a
    version, downloads the previous GitHub release so Velopack can compute a small delta patch instead
    of shipping a full download for every update - this step is skipped harmlessly if there is no
    previous Velopack-packaged release yet (e.g. the very first one).

.PARAMETER Version
    Version number for this release, e.g. "0.2.0". Must be a valid version (no leading "v").

.PARAMETER Publish
    Also upload the packaged release directly to GitHub as a published release (tag vVERSION).
    Requires a GitHub token with repo write access in the GITHUB_TOKEN environment variable.
    Without this switch, the script only builds the installer locally under .\Releases for you to
    inspect/test/upload by hand.

.EXAMPLE
    .\pack-installer.ps1 -Version 0.2.0
    .\pack-installer.ps1 -Version 0.2.0 -Publish
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot "IRacingOverlay.App\IRacingOverlay.App.csproj"
$publishDir = Join-Path $repoRoot "publish-selfcontained"
$iconPath = Join-Path $repoRoot "IRacingOverlay.App\Assets\icon.ico"
$repoUrl = "https://github.com/andreuservera/OpenOverlay"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "Installing vpk CLI tool..." -ForegroundColor Cyan
    dotnet tool install -g vpk
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Publishing self-contained build (v$Version)..." -ForegroundColor Cyan
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Fetching previous release for delta patching (skips harmlessly if none exists)..." -ForegroundColor Cyan
vpk download github --repoUrl $repoUrl
# Intentionally not checking the exit code here - failure just means there was nothing to diff
# against yet (e.g. this is the first Velopack-packaged release), which is fine.

Write-Host "Packing installer..." -ForegroundColor Cyan
& vpk pack `
    --packId OpenOverlay `
    --packVersion $Version `
    --packDir $publishDir `
    --packTitle "OpenOverlay" `
    --packAuthors "OpenOverlay contributors" `
    --mainExe OpenOverlay.exe `
    --icon $iconPath `
    --shortcuts Desktop,StartMenuRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Done. Installer and update assets are in .\Releases" -ForegroundColor Green

if ($Publish) {
    if (-not $env:GITHUB_TOKEN) {
        Write-Host "GITHUB_TOKEN environment variable not set - cannot publish." -ForegroundColor Red
        exit 1
    }

    Write-Host "Publishing release v$Version to GitHub..." -ForegroundColor Cyan
    & vpk upload github `
        --repoUrl $repoUrl `
        --token $env:GITHUB_TOKEN `
        --publish `
        --releaseName "OpenOverlay v$Version" `
        --tag "v$Version"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "Published: $repoUrl/releases/tag/v$Version" -ForegroundColor Green
}
