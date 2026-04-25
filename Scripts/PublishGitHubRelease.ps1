#Requires -Version 5.1
<#
.SYNOPSIS
  Build release zips (PackageKspmpReleaseZips.ps1) and create/publish a GitHub Release with the GitHub CLI.

.PREREQUISITES
  - 7-Zip, MSBuild/dotnet (same as build scripts)
  - GitHub CLI: winget install GitHub.cli, then: gh auth login
  - Push access to the repo; gh uses your stored credentials (repo scope).

.EXAMPLE
  # Tag = MAJOR.MINOR.PATCH from KSPMultiplayer.version, Release zips, published
  .\Scripts\PublishGitHubRelease.ps1

.EXAMPLE
  # Explicit tag, keep as draft; publish the draft in GitHub UI to satisfy /releases/latest API
  .\Scripts\PublishGitHubRelease.ps1 -Tag "0.32.0" -Draft
#>
[CmdletBinding()]
param(
    [string] $Tag = "",
    [string] $Title = "",
    [string] $Notes = "",
    [string] $NotesFile = "",
    [ValidateSet("Release", "Debug")]
    [string] $Configuration = "Release",
    [switch] $IncludeMasterServer,
    [switch] $Draft,
    [switch] $Prerelease,
    [switch] $GenerateReleaseNotes,
    [string] $TargetBranch = "",
    [switch] $SkipPackage
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$versionFile = Join-Path $repoRoot "KSPMultiplayer.version"

if (-not (Get-Command "gh" -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) not found. Install: winget install GitHub.cli, then: gh auth login"
}
Push-Location $repoRoot
$prevErr = $ErrorActionPreference
$ErrorActionPreference = "SilentlyContinue"
$null = & gh auth status 2>&1
$authEx = $LASTEXITCODE
$ErrorActionPreference = $prevErr
Pop-Location
if ($authEx -ne 0) {
    throw "gh is not logged in. Run: gh auth login (from a shell with a TTY is best). Exit code: $authEx"
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
    if (-not (Test-Path -LiteralPath $versionFile)) { throw "Missing $versionFile; pass -Tag." }
    $jv = Get-Content -LiteralPath $versionFile -Raw | ConvertFrom-Json
    $Tag = "$($jv.VERSION.MAJOR).$($jv.VERSION.MINOR).$($jv.VERSION.PATCH)"
    Write-Host "Using tag from KSPMultiplayer.version: $Tag" -ForegroundColor Cyan
}

$remoteRepo = $null
Push-Location $repoRoot
try {
    $j = & gh repo view --json nameWithOwner 2>$null
    if ($LASTEXITCODE -eq 0 -and $j) { $remoteRepo = ($j | ConvertFrom-Json).nameWithOwner }
} finally { Pop-Location }
if (-not $remoteRepo) {
    $u = & git -C $repoRoot remote get-url origin 2>$null
    if ($u -match "github\.com[:/]([^/]+)/(.+?)(?:\.git)?$") { $remoteRepo = "$($matches[1])/$($matches[2] -replace '\.git$','')" }
    if (-not $remoteRepo) { throw "Could not resolve owner/repo. Run: cd `"$repoRoot`"; gh repo set-default or fix origin." }
}
Write-Host "Repository: $remoteRepo" -ForegroundColor Cyan

# Package
if (-not $SkipPackage) {
    $pkgPath = Join-Path $PSScriptRoot "PackageKspmpReleaseZips.ps1"
    if ($IncludeMasterServer) { $pkg = & $pkgPath -Configuration $Configuration -IncludeMasterServer }
    else { $pkg = & $pkgPath -Configuration $Configuration }
    if (-not $pkg -or -not $pkg.ClientZip) { throw "PackageKspmpReleaseZips did not return paths." }
} else {
    $outDir = Join-Path $repoRoot "Build\$Configuration\artifacts"
    $clientName = "KSPMultiplayer-Client-$Configuration.zip"
    $serverName = "KSPMultiplayer-Server-$Configuration.zip"
    $mName = "KSPMultiplayerMasterServer-$Configuration.zip"
    $pkg = [PSCustomObject]@{
        ClientZip = Join-Path $outDir $clientName
        ServerZip = Join-Path $outDir $serverName
        MasterZip = if ($IncludeMasterServer) { Join-Path $outDir $mName } else { $null }
    }
    foreach ($p in @($pkg.ClientZip, $pkg.ServerZip)) {
        if (-not (Test-Path -LiteralPath $p)) { throw "Missing: $p. Build first or run without -SkipPackage." }
    }
    if ($IncludeMasterServer -and -not (Test-Path -LiteralPath $pkg.MasterZip)) { throw "Missing: $($pkg.MasterZip)" }
}

$releaseTitle = if ([string]::IsNullOrWhiteSpace($Title)) { "KSPMP $Tag" } else { $Title }
$releaseNotes = $null
if ($NotesFile) {
    if (-not (Test-Path -LiteralPath $NotesFile)) { throw "Notes file not found: $NotesFile" }
    $releaseNotes = [System.IO.File]::ReadAllText($NotesFile)
} elseif (-not [string]::IsNullOrEmpty($Notes)) {
    $releaseNotes = $Notes
} else {
    $releaseNotes = "Build $Tag (configuration: $Configuration). In-game KSP mod + standalone KSPMPServer. The client zip path matches the GitHub autoupdate asset name."
}

# gh needs notes via a file to avoid Windows quoting issues
$notesFileFinal = $null
$tempNotes = $false
if (-not $GenerateReleaseNotes) {
    $notesFileFinal = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), "md")
    $releaseNotes | Set-Content -Path $notesFileFinal -Encoding UTF8
    $tempNotes = $true
}

$ghArgs = @("release", "create", $Tag, $pkg.ClientZip, $pkg.ServerZip)
if ($pkg.MasterZip -and (Test-Path -LiteralPath $pkg.MasterZip)) { $ghArgs += $pkg.MasterZip }
$ghArgs += @("--repo", $remoteRepo, "--title", $releaseTitle)
if ($GenerateReleaseNotes) {
    $ghArgs += "--generate-notes"
} else {
    $ghArgs += @("--notes-file", $notesFileFinal)
}
if ($Draft) { $ghArgs += "--draft" }
if ($Prerelease) { $ghArgs += "--prerelease" }
if (-not [string]::IsNullOrWhiteSpace($TargetBranch)) { $ghArgs += @("--target", $TargetBranch) }

try {
    Write-Host "==> gh $($ghArgs[0] + ' ' + $ghArgs[1] + ' ' + $ghArgs[2]) ..." -ForegroundColor Cyan
    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) {
        throw "gh release create failed ($LASTEXITCODE). If this tag/release already exists, use a new tag or delete the release on GitHub."
    }
} finally {
    if ($tempNotes -and $notesFileFinal -and (Test-Path -LiteralPath $notesFileFinal)) {
        Remove-Item -LiteralPath $notesFileFinal -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Done: https://github.com/$remoteRepo/releases" -ForegroundColor Green
if ($Draft) {
    Write-Host "  Draft: click Publish in GitHub so the API (releases/latest) and the mod can see the build." -ForegroundColor Yellow
}
