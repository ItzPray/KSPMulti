# Persistent Sync authoring regression gate (grep-only). Run from repo root.
# Fails on patterns that contradict AGENTS.md Scenario Sync Domain Contract unless explicitly allowlisted.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot

function Test-GitGrepEmpty {
    param(
        [Parameter(Mandatory)] [string] $Pattern,
        [string[]] $PathSpec = @('*.cs')
    )
    $outFile = Join-Path $env:TEMP 'ps-authoring-grep-out.txt'
    $errFile = Join-Path $env:TEMP 'ps-authoring-grep-err.txt'
    $psi = @{
        FilePath               = 'git'
        ArgumentList           = @('grep', '-n', '-E', '-e', $Pattern, '--') + $PathSpec
        RedirectStandardOutput = $outFile
        RedirectStandardError  = $errFile
        PassThru               = $true
        NoNewWindow            = $true
    }
    $p = Start-Process @psi
    $null = $p.WaitForExit()
    if ($p.ExitCode -eq 1) {
        return @()
    }
    if ($p.ExitCode -ne 0) {
        $err = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "git grep failed ($($p.ExitCode)): $err"
    }
    # Force array: a single-line git grep would otherwise yield a [string] and break downstream Count/pipeline behavior.
    $lines = @(Get-Content $outFile -ErrorAction SilentlyContinue)
    return , @(@($lines | Where-Object { $_ -and $_.Trim().Length -gt 0 }))
}

$failures = New-Object System.Collections.Generic.List[string]

# Domains must use named envelope types, not raw array type parameters on sanctioned templates.
$hits = @(Test-GitGrepEmpty 'SyncDomainStore<[^>]*\[\]')
if ($hits.Length -gt 0) { $failures.Add("SyncDomainStore<T[]> is forbidden (use envelope payload):`n  $($hits -join "`n  ")") }

$hits = @(Test-GitGrepEmpty 'SyncClientDomain<[^>]*\[\]')
if ($hits.Length -gt 0) { $failures.Add("SyncClientDomain<T[]> is forbidden (use envelope payload):`n  $($hits -join "`n  ")") }

# Production domains must not subclass the reducer base directly; use SyncDomainStore<TPayload>.
# Pattern avoids a leading ':' for git grep (':' can be parsed as a revision magic prefix).
$hits = @(Test-GitGrepEmpty '[[:space:]]:[[:space:]]*SyncDomainStoreBase' @('Server/System/PersistentSync/Domains/'))
if ($hits.Length -gt 0) { $failures.Add("Server Domains must not inherit SyncDomainStoreBase<TCanonical> (use SyncDomainStore<TPayload> instead):`n  $($hits -join "`n  ")") }

$replyPath = Join-Path $repoRoot 'LmpCommon\Message\Data\Settings\SetingsReplyMsgData.cs'
if (Test-Path $replyPath) {
    $catalogInReply = Select-String -Path $replyPath -Pattern 'PersistentSyncCatalog' -SimpleMatch -Quiet
    if ($catalogInReply) {
        $failures.Add('SetingsReplyMsgData.cs must not reference PersistentSyncCatalog (use PersistentSyncCatalogMsgData on the Settings channel).')
    }
}

Pop-Location

if ($failures.Count -gt 0) {
    foreach ($f in $failures) {
        Write-Host $f
    }
    exit 1
}

Write-Host 'VerifyPersistentSyncAuthoring: OK'
