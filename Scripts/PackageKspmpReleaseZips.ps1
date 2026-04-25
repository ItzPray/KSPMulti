#Requires -Version 5.1
<#
.SYNOPSIS
  Stages the same KSPMP client/server zip layout as AppVeyor (KSPMP Readme + KSPMPClient\GameData, KSPMPServer).

.DESCRIPTION
  1) Runs Scripts\BuildOnly.bat for the chosen configuration
  2) Stages under obj\KSPMPRelease\<Configuration>\
  3) Writes .zip archives (7-Zip preferred, or Windows "tar" fallback) with names expected by GitHub / LmpUpdater.
  Optional: set 7Z_EXE in environment (see Scripts\SetDirectories.bat) if 7z is not on PATH.

.PARAMETER OutputDir
  Default: Build\<Configuration>\artifacts
#>
[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string] $Configuration = "Release",
    [string] $OutputDir = "",
    [switch] $IncludeMasterServer
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "Build\$Configuration\artifacts"
}

function Get-ReleaseZipTool {
    if ($env:7Z_EXE -and (Test-Path -LiteralPath $env:7Z_EXE)) {
        return [PSCustomObject]@{ Kind = "7z"; Exe = (Resolve-Path -LiteralPath $env:7Z_EXE).Path }
    }
    $c = Get-Command "7z" -ErrorAction SilentlyContinue
    if ($c) { return [PSCustomObject]@{ Kind = "7z"; Exe = $c.Source } }
    foreach ($p in @(
        "${env:ProgramW6432}\7-Zip\7z.exe"
        "${env:ProgramFiles}\7-Zip\7z.exe"
        "${env:ProgramFiles(x86)}\7-Zip\7z.exe"
        "${env:LocalAppData}\Programs\7-Zip\7z.exe"
    )) {
        if ($p -and (Test-Path -LiteralPath $p)) { return [PSCustomObject]@{ Kind = "7z"; Exe = $p } }
    }
    # Do not use where.exe 7z: it prints to stderr and can break PowerShell with $ErrorActionPreference = Stop
    $t = Get-Command "tar" -ErrorAction SilentlyContinue
    if ($t) { return [PSCustomObject]@{ Kind = "tar"; Exe = $t.Source } }
    throw @"
No zip tool found. Do one of:
  - Install 7-Zip from https://www.7-zip.org/ (or add 7z to PATH)
  - Or set 7Z_EXE in Scripts\SetDirectories.bat to the full path of 7z.exe
  - Or rely on built-in: Windows 10+ includes 'tar' (this script will use: tar -a -cf ... .zip)
"@
}

function Invoke-ReleaseZip {
    param(
        [Parameter(Mandatory)] [string] $OutZip,
        [Parameter(Mandatory)] [string] $StageDir,
        [Parameter(Mandatory)] [string[]] $RelativeNames,
        [Parameter(Mandatory)] $Tool
    )
    if (Test-Path -LiteralPath $OutZip) { Remove-Item -LiteralPath $OutZip -Force }
    $outFile = [System.IO.Path]::GetFullPath($OutZip)
    $stageAbs = [System.IO.Path]::GetFullPath($StageDir)

    if ($Tool.Kind -eq "7z") {
        Push-Location $stageAbs
        try {
            $argList = @("a", "-tzip", $outFile) + $RelativeNames
            & $Tool.Exe @argList
            if ($LASTEXITCODE -ne 0) { throw "7z failed with exit $LASTEXITCODE" }
        }
        finally { Pop-Location }
        return
    }
    if ($Tool.Kind -eq "tar") {
        $argList = @("-a", "-cf", $outFile, "-C", $stageAbs) + $RelativeNames
        & $Tool.Exe @argList
        if ($LASTEXITCODE -ne 0) { throw "tar failed with exit $LASTEXITCODE" }
        return
    }
    throw "Unknown zip tool: $($Tool.Kind)"
}

$zipTool = Get-ReleaseZipTool
Write-Host "Using zip tool: $($zipTool.Kind) -> $($zipTool.Exe)" -ForegroundColor DarkGray
$buildClient = Join-Path $repoRoot "Build\$Configuration\Client"
$buildServer = Join-Path $repoRoot "Build\$Configuration\Server"
$harmonySource = Join-Path $repoRoot "External\Dependencies\Harmony"
$readMeSource = Join-Path $repoRoot "KSPMP Readme.txt"
$stage = Join-Path $repoRoot "obj\KSPMPRelease\$Configuration"

if (-not (Test-Path -LiteralPath $readMeSource)) { throw "Missing: $readMeSource" }
if (-not (Test-Path -LiteralPath $harmonySource)) { throw "Missing Harmony folder: $harmonySource" }

Write-Host "==> Running BuildOnly $Configuration" -ForegroundColor Cyan
$bat = Join-Path $PSScriptRoot "BuildOnly.bat"
& cmd.exe /c "`"$bat`" $Configuration" | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { throw "BuildOnly.bat failed with exit $LASTEXITCODE" }

if (-not (Test-Path (Join-Path $buildClient "Plugins\LmpClient.dll"))) { throw "Client build output missing. Expected under $buildClient" }
if (-not (Test-Path (Join-Path $buildServer "Server.dll"))) { throw "Server build output missing. Expected under $buildServer" }

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$gameData = Join-Path $stage "KSPMPClient\GameData"
$kspMulti = Join-Path $gameData "KSPMultiplayer"
New-Item -ItemType Directory -Path $kspMulti -Force | Out-Null

Write-Host "==> Staging Harmony to GameData" -ForegroundColor Cyan
$null = & robocopy $harmonySource $gameData /E /NFL /NDL /NJH /NJS /nc /ns /np
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE) for Harmony" }

Write-Host "==> Staging client (Build client -> GameData\KSPMultiplayer)" -ForegroundColor Cyan
$null = & robocopy $buildClient $kspMulti /E /NFL /NDL /NJH /NJS /nc /ns /np
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE) for client" }

Copy-Item -LiteralPath $readMeSource -Destination (Join-Path $stage "KSPMP Readme.txt") -Force

Write-Host "==> Staging KSPMPServer" -ForegroundColor Cyan
$serverSt = Join-Path $stage "KSPMPServer"
New-Item -ItemType Directory -Path $serverSt -Force | Out-Null
$null = & robocopy $buildServer $serverSt /E /NFL /NDL /NJH /NJS /nc /ns /np
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE) for server" }

$clientZip = "KSPMultiplayer-Client-$Configuration.zip"
$serverZip = "KSPMultiplayer-Server-$Configuration.zip"
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$outClient = Join-Path $OutputDir $clientZip
$outServer = Join-Path $OutputDir $serverZip
if (Test-Path -LiteralPath $outClient) { Remove-Item -LiteralPath $outClient -Force }
if (Test-Path -LiteralPath $outServer) { Remove-Item -LiteralPath $outServer -Force }

Write-Host "==> Zip: $clientZip" -ForegroundColor Cyan
Invoke-ReleaseZip -OutZip $outClient -StageDir $stage -RelativeNames @("KSPMP Readme.txt", "KSPMPClient\GameData") -Tool $zipTool

Write-Host "==> Zip: $serverZip" -ForegroundColor Cyan
Invoke-ReleaseZip -OutZip $outServer -StageDir $stage -RelativeNames @("KSPMP Readme.txt", "KSPMPServer") -Tool $zipTool

$masterPath = $null
if ($IncludeMasterServer) {
    Write-Host "==> Building MasterServer" -ForegroundColor Cyan
    if (-not $env:MSBUILD_EXE) {
        $cf = @("${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
            "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe")
        $found = $null
        foreach ($p in $cf) { if (Test-Path -LiteralPath $p) { $found = $p; break } }
        if ($found) { $env:MSBUILD_EXE = $found; Write-Host "Using $found" }
    }
    $msb = if ($env:MSBUILD_EXE) { $env:MSBUILD_EXE } else { "msbuild" }
    $mproj = Join-Path $repoRoot "MasterServer\MasterServer.csproj"
    & $msb $mproj /t:Build /p:Configuration=$Configuration /p:Platform=AnyCPU /m /v:m
    if ($LASTEXITCODE -ne 0) { throw "MSBuild MasterServer failed" }
    $mbin = Join-Path $repoRoot "MasterServer\bin\$Configuration"
    $mst = Join-Path $stage "KSPMPMasterServer"
    if (Test-Path -LiteralPath $mst) { Remove-Item -LiteralPath $mst -Recurse -Force }
    New-Item -ItemType Directory -Path $mst -Force | Out-Null
    $null = & robocopy $mbin $mst /E /NFL /NDL /NJH /NJS /nc /ns /np
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for master server" }
    $masterName = "KSPMultiplayerMasterServer-$Configuration.zip"
    $masterPath = Join-Path $OutputDir $masterName
    if (Test-Path -LiteralPath $masterPath) { Remove-Item -LiteralPath $masterPath -Force }
    Write-Host "==> Zip: $masterName" -ForegroundColor Cyan
    Invoke-ReleaseZip -OutZip $masterPath -StageDir $stage -RelativeNames @("KSPMP Readme.txt", "KSPMPMasterServer") -Tool $zipTool
}

Write-Host "Done. Output:" -ForegroundColor Green
Get-Item $outClient, $outServer | ForEach-Object { "  $($_.FullName)  ($($_.Length) bytes)" }
if ($masterPath) { $i = Get-Item $masterPath; "  $($i.FullName)  ($($i.Length) bytes)" }
return [PSCustomObject]@{
    OutDir      = $OutputDir
    ClientZip   = $outClient
    ServerZip   = $outServer
    MasterZip   = $masterPath
}
