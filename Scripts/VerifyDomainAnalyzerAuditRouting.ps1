# Static gate: Domain Analyzer audit snapshots must not flow through PersistentSyncReconciler (grep-only).
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$handlerPath = Join-Path $repoRoot 'LmpClient\Systems\PersistentSync\PersistentSyncMessageHandler.cs'
if (-not (Test-Path -LiteralPath $handlerPath)) {
    throw "Missing handler file: $handlerPath"
}

$txt = Get-Content -LiteralPath $handlerPath -Raw

if ($txt -notmatch 'PersistentSyncAuditCoordinator\.Instance\.HandleAuditSnapshot') {
    throw 'PersistentSyncMessageHandler must route AuditSnapshot to PersistentSyncAuditCoordinator.Instance.HandleAuditSnapshot'
}

if ($txt -match '(?s)case\s+PersistentSyncMessageType\.AuditSnapshot\s*:.*?System\.Reconciler\.HandleSnapshot') {
    throw 'AuditSnapshot switch arm must not invoke System.Reconciler.HandleSnapshot'
}

Write-Host 'VerifyDomainAnalyzerAuditRouting: OK'
