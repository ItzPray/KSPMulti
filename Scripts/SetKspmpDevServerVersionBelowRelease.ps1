#Requires -Version 5.1
<#
.SYNOPSIS
  Lower the dedicated server assembly version by one (patch/minor/major) for self-update testing, then publish and copy to your server folder.

.DESCRIPTION
  The KSPMP server self-updater compares GitHub LATEST tag to the Server assembly version. To test the update path,
  you need a running install with a version number below the latest published tag.

  This script:
  1) Edits Server/Properties/AssemblyInfo.cs one step down (same rules as SetKspmpDevVersionBelowRelease for the client).
  2) Publishes Server\Server.csproj to Build\<Config>\Server (overwrites that staging folder only).
  3) Merges the new bits into -ServerPath with robocopy, preserving data dirs (Universe, Config, logs) like PublishServerToTest.

  It does not change LmpClient or KSPMultiplayer.version (use SetKspmpDevVersionBelowRelease.ps1 for the in-game mod).

  Optional dotnet: set DOTNET_EXE in the environment, or add DOTNET_EXE to Scripts\SetDirectories.bat (sourced if you
  want to set it before running from cmd).

.EXAMPLE
  # Default copy target C:\KSPMultiServer, Release build
  .\Scripts\SetKspmpDevServerVersionBelowRelease.ps1

.EXAMPLE
  Install location override
  .\Scripts\SetKspmpDevServerVersionBelowRelease.ps1 -ServerPath "D:\Games\KSPMPServer"

.EXAMPLE
  From a specific tag base (e.g. step down from 0.32.0)
  .\Scripts\SetKspmpDevServerVersionBelowRelease.ps1 -FromTag 0.32.0

.EXAMPLE
  Only patch version files, publish yourself later
  .\Scripts\SetKspmpDevServerVersionBelowRelease.ps1 -SkipBuild
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = "",

    [string] $FromTag = "",

    [string] $ServerPath = "C:\KSPMultiServer",

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [switch] $SkipBuild,

    [switch] $WhatIf
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
} else {
    $RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
}

$serverAsm = Join-Path $RepoRoot "Server\Properties\AssemblyInfo.cs"
if (-not (Test-Path -LiteralPath $serverAsm)) { throw "Missing: $serverAsm" }

if (-not [string]::IsNullOrWhiteSpace($ServerPath)) {
    $ServerPath = [System.IO.Path]::GetFullPath($ServerPath.Trim())
}

function Parse-ThreePartVersion([string] $s) {
    $t = $s.Trim()
    if ($t.Length -ge 1 -and ($t[0] -eq "v" -or $t[0] -eq "V")) { $t = $t.Substring(1) }
    if (-not ($t -match "^(?<ma>\d+)\.(?<mi>\d+)\.(?<pa>\d+)$")) {
        throw "Version must be like 0.32.0 or v0.32.0 (got: $s)"
    }
    return @{
        Ma = [int]$Matches['ma']
        Mi = [int]$Matches['mi']
        Pa = [int]$Matches['pa']
    }
}

function StepDown-ThreePart([int] $ma, [int] $mi, [int] $pa) {
    if ($pa -gt 0) { return $ma, $mi, ($pa - 1) }
    if ($mi -gt 0) { return $ma, ($mi - 1), 0 }
    if ($ma -gt 0) { return ($ma - 1), 0, 0 }
    throw "Cannot go below 0.0.0. Bump Server/Properties/AssemblyInfo or use SetKspmpReleaseVersion.ps1 first."
}

$base = $null
if (-not [string]::IsNullOrWhiteSpace($FromTag)) {
    $base = Parse-ThreePartVersion -s $FromTag
} else {
    $tAsm = [System.IO.File]::ReadAllText($serverAsm, [System.Text.Encoding]::UTF8)
    if ($tAsm -notmatch 'AssemblyVersion\("(?<v>\d+\.\d+\.\d+)"\)') {
        throw "Could not find [assembly: AssemblyVersion(""x.y.z"")] in Server/Properties/AssemblyInfo.cs"
    }
    $base = Parse-ThreePartVersion -s $Matches['v']
}

$oldStr = "$($base.Ma).$($base.Mi).$($base.Pa)"
$nd = StepDown-ThreePart -ma $base.Ma -mi $base.Mi -pa $base.Pa
$ma, $mi, $pa = $nd[0], $nd[1], $nd[2]
$verStr = "$ma.$mi.$pa"

Write-Host "KSPMP dev server: base $oldStr  ->  $verStr  (for self-update testing)" -ForegroundColor Cyan

if ($WhatIf) {
    Write-Host "WhatIf: no files written, no build." -ForegroundColor Yellow
    exit 0
}

$utf8 = New-Object System.Text.UTF8Encoding $false
$sText = [System.IO.File]::ReadAllText($serverAsm, $utf8)
$s2 = $sText
$s2 = $s2 -replace '\[assembly: AssemblyVersion\("[^"]+"\)\]', "[assembly: AssemblyVersion(`"$verStr`")]"
$s2 = $s2 -replace '\[assembly: AssemblyFileVersion\("[^"]+"\)\]', "[assembly: AssemblyFileVersion(`"$verStr`")]"
$s2 = $s2 -replace '\[assembly: AssemblyInformationalVersion\("[^"]+"\)\]', "[assembly: AssemblyInformationalVersion(`"$verStr-compiled`")]"
[System.IO.File]::WriteAllText($serverAsm, $s2, $utf8)
Write-Host "Updated Server/Properties/AssemblyInfo.cs -> $verStr" -ForegroundColor Cyan

if ($SkipBuild) {
    Write-Host ""
    Write-Host "SkipBuild: publish the server and copy to the install by hand, e.g.:" -ForegroundColor Yellow
    Write-Host "  Scripts\PublishServerToTest.bat $Configuration   (uses LMPSERVERPATH in SetDirectories.bat)" -ForegroundColor Yellow
    Write-Host "  or: dotnet publish Server\Server.csproj -c $Configuration -o <out>  then robocopy <out> $ServerPath /E /XD Universe Config logs" -ForegroundColor Yellow
    exit 0
}

# --- Find dotnet (same idea as .bat) ---
$dotnet = $null
if ($env:DOTNET_EXE -and (Test-Path -LiteralPath $env:DOTNET_EXE)) { $dotnet = (Resolve-Path -LiteralPath $env:DOTNET_EXE).Path }
if (-not $dotnet) { $c = Get-Command "dotnet" -ErrorAction SilentlyContinue; if ($c) { $dotnet = $c.Source } }
if (-not $dotnet) { throw "dotnet not found. Set DOTNET_EXE in the environment to dotnet.exe" }

$publishOut = Join-Path $RepoRoot "Build\$Configuration\Server"
if (Test-Path -LiteralPath $publishOut) {
    Write-Host "Removing: $publishOut" -ForegroundColor DarkGray
    Remove-Item -LiteralPath $publishOut -Recurse -Force
}
$null = New-Item -ItemType Directory -Path $publishOut -Force
$csproj = Join-Path $RepoRoot "Server\Server.csproj"
Write-Host ""
Write-Host "Publishing: $csproj  ->  $publishOut" -ForegroundColor Cyan
& $dotnet publish $csproj -c $Configuration -o $publishOut
if ($null -ne $LASTEXITCODE -and 0 -ne $LASTEXITCODE) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

if (-not (Test-Path (Join-Path $publishOut "Server.dll"))) { throw "Publish output missing Server.dll: $publishOut" }

$dest = $ServerPath
if (-not (Test-Path -LiteralPath $dest)) {
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Write-Host "Created: $dest" -ForegroundColor DarkCyan
}

Write-Host ""
Write-Host "Merging into server install: $dest" -ForegroundColor Cyan
Write-Host "  (preserving: Universe, Config, logs; same as PublishServerToTest.bat)" -ForegroundColor DarkGray
# Match Scripts\PublishServerToTest.bat: do not nuke data folders; merge the rest.
$robo = & robocopy $publishOut $dest /E /R:1 /W:1 /XD Universe Config logs /NFL /NDL /NJH /NJS 2>&1
$code = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
if ($code -ge 8) {
    throw "robocopy failed (exit $code). Check paths and permissions."
}
Write-Host $robo
Write-Host ""
$barW = [Math]::Max(60, 20 + $verStr.Length + $oldStr.Length)
$bar = "=" * $barW
Write-Host $bar -ForegroundColor Green
Write-Host "  Server downgraded: $oldStr  ->  $verStr" -ForegroundColor Green
Write-Host "  Staged: $publishOut" -ForegroundColor Green
Write-Host "  Install: $dest" -ForegroundColor Green
Write-Host $bar -ForegroundColor Green
