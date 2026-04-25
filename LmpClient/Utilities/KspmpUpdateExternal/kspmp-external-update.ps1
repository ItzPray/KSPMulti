# KSPMP client update — run from KSP root KSPMP-Update-External folder. Requires PowerShell 5+.
# Params file format (UTF-8, line-based; values after first =, base64 for the first three):
#   v1
#   KspRootB64=...
#   ZipUrlB64=...
#   UserAgentB64=...
#   ModFolder=KSPMultiplayer
#   PackagePrefix=KSPMPClient/GameData/KSPMultiplayer/

$ErrorActionPreference = 'Stop'

function Get-B64([string] $b) {
  [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b))
}

function Get-ParamMap([string] $path) {
  $h = @{}
  Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ForEach-Object { $_ -split "`r?`n" } | ForEach-Object {
    $l = $_.Trim()
    if ($l.Length -eq 0) { return }
    $i = $l.IndexOf('=')
    if ($i -lt 1) { return }
    $k = $l.Substring(0, $i)
    $v = $l.Substring($i + 1)
    $h[$k] = $v
  }
  return $h
}

$ParamsFile = $args[0]
if (-not $ParamsFile) { throw "No params file." }
$map = Get-ParamMap $ParamsFile
if (-not $map.ContainsKey('v1')) { throw "Params missing v1 header." }
if (-not $map['KspRootB64'] -or -not $map['ZipUrlB64'] -or -not $map['UserAgentB64']) { throw "Params file is incomplete (KspRootB64, ZipUrlB64, UserAgentB64 required)." }

$KspRoot   = (Get-B64 $map['KspRootB64'])
$ZipUrl    = (Get-B64 $map['ZipUrlB64'])
$UserAgent = (Get-B64 $map['UserAgentB64'])
$ModFolder = if ($map['ModFolder']) { $map['ModFolder'] } else { 'KSPMultiplayer' }
$Pfx = if ($map['PackagePrefix']) { $map['PackagePrefix'] } else { "KSPMPClient/GameData/${ModFolder}/" }

$K = (Resolve-Path -LiteralPath $KspRoot -ErrorAction Stop).Path
Write-Host "KSP install: $K" -ForegroundColor Cyan
Write-Host "This script will try to close Kerbal Space Program under this install, then update GameData\\${ModFolder} from the client zip." -ForegroundColor Yellow
Write-Host ""

# --- close anything running from under KSP root (game + KSP* exes) ---
$names = @('KSP_x64', 'KSP32', 'KSP')
$hit = 0
Get-Process -ErrorAction SilentlyContinue | Where-Object {
  $p = $_.Path
  $p -and $p.StartsWith($K, [StringComparison]::OrdinalIgnoreCase)
} | ForEach-Object {
  $hit++
  Write-Host "Stopping: $($_.ProcessName) (PID $($_.Id))" -ForegroundColor DarkYellow
  try { Stop-Process -Id $_.Id -Force -ErrorAction Stop } catch { }
}

foreach ($n in $names) {
  $e = [IO.Path]::Combine($K, $n + '.exe')
  if (Test-Path -LiteralPath $e) {
    Get-Process -Name $n -ErrorAction SilentlyContinue | ForEach-Object {
      $hit++
      Write-Host "Stopping: $n (PID $($_.Id))" -ForegroundColor DarkYellow
      try { Stop-Process -Id $_.Id -Force } catch { }
    }
  }
}
Write-Host "Stopped $hit process(es) or none were running. Waiting for files to be released..."

# --- wait for no process with path under Ksp ---
$dead = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $dead) {
  $le = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $p = $_.Path; $p -and $p.StartsWith($K, [StringComparison]::OrdinalIgnoreCase) })
  if ($le.Count -eq 0) { break }
  Start-Sleep -Seconds 1
}
Start-Sleep -Seconds 1

# --- download ---
$tmp = [IO.Path]::Combine([IO.Path]::GetTempPath(), "Kspmp-Update-" + [Guid]::NewGuid() + ".zip")
Write-Host "Downloading release zip..." -ForegroundColor Cyan
try {
  $hdr = @{ 'User-Agent' = $UserAgent; 'Accept' = 'application/octet-stream' }
  Invoke-WebRequest -Uri $ZipUrl -OutFile $tmp -Headers $hdr -UseBasicParsing -MaximumRedirection 8
} catch { throw "Download failed: $($_.Exception.Message)" }
if (-not (Test-Path -LiteralPath $tmp) -or ((Get-Item -LiteralPath $tmp).Length -lt 8)) {
  try { Remove-Item -LiteralPath $tmp -Force } catch { }
  throw "Downloaded file is missing or too small."
}
Write-Host "OK ($((Get-Item -LiteralPath $tmp).Length) bytes)." -ForegroundColor Green

# --- extract to temp tree ---
$ext = [IO.Path]::Combine([IO.Path]::GetTempPath(), "Kspmp-Extract-" + [Guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($ext) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
# .NET 4.5+ — two-argument form; $ext is empty, no overwrite needed
[IO.Compression.ZipFile]::ExtractToDirectory($tmp, $ext)

$norm = ($Pfx -replace '/','\' ).Trim().TrimEnd('\')
$sourceRoot = $ext
if ($norm.Length -gt 0) {
  foreach ($part in ($norm -split '\\')) {
    if ($part) { $sourceRoot = [IO.Path]::Combine($sourceRoot, $part) }
  }
}
$sourceRoot = $sourceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [char]0x2F)
if (-not (Test-Path -LiteralPath $sourceRoot)) {
  throw "Release zip does not contain folder for: $Pfx (looked for: $sourceRoot). Use the official KSPMP client zip from GitHub releases."
}
if (-not (Get-Item -LiteralPath $sourceRoot -ErrorAction SilentlyContinue).PSIsContainer) { throw "Validation: expected a folder at $sourceRoot" }
Write-Host "Package folder in zip: $Pfx" -ForegroundColor Cyan
$sourceRoot = (Resolve-Path -LiteralPath $sourceRoot -ErrorAction Stop).Path

# destination mod folder
$destRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($K, 'GameData', $ModFolder))
if (-not (Test-Path -LiteralPath $destRoot)) { [IO.Directory]::CreateDirectory($destRoot) | Out-Null }

# --- copy tree, overwrite existing ---
function Copy-OverrideTree($from, $to) {
  if (-not (Test-Path -LiteralPath $to)) { [IO.Directory]::CreateDirectory($to) | Out-Null }
  Get-ChildItem -LiteralPath $from -ErrorAction SilentlyContinue | ForEach-Object {
    $d = [IO.Path]::Combine($to, $_.Name)
    if ($_.PSIsContainer) { Copy-OverrideTree $_.FullName $d }
    else { [IO.File]::Copy($_.FullName, $d, $true) }
  }
}
Write-Host "Installing into: $destRoot" -ForegroundColor Cyan
Copy-OverrideTree $sourceRoot $destRoot

# --- quick validation: key DLLs + MZ on one ---
$lmp = [IO.Path]::Combine($destRoot, 'Plugins\LmpClient.dll')
$lz  = [IO.Path]::Combine($destRoot, 'Plugins\CachedQuickLz.dll')
if (-not (Test-Path -LiteralPath $lmp)) { throw "Validation: missing LmpClient.dll" }
if (-not (Test-Path -LiteralPath $lz))  { throw "Validation: missing CachedQuickLz.dll" }
foreach ($dll in @($lmp, $lz)) {
  $len = (Get-Item -LiteralPath $dll).Length
  if ($len -lt 2) { throw "Validation: $dll is too small." }
  $fs = [IO.File]::OpenRead($dll)
  try { $a = $fs.ReadByte(); $b = $fs.ReadByte() } finally { $fs.Close() }
  if ($a -ne 0x4D -or $b -ne 0x5A) { throw "Validation: $dll does not look like a valid PE (MZ) module." }
}
Write-Host "PE header check OK on LmpClient.dll and CachedQuickLz.dll." -ForegroundColor Green
Write-Host ""
Write-Host "KSPMP client update completed successfully. You can start Kerbal Space Program again." -ForegroundColor Green

try { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -LiteralPath $ext -Recurse -Force -ErrorAction SilentlyContinue } catch { }

exit 0
