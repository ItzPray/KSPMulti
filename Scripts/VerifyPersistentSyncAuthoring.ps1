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
    return @($lines | Where-Object { $_ -and $_.Trim().Length -gt 0 })
}

$failures = New-Object System.Collections.Generic.List[string]

# Domains must use named envelope types, not raw array type parameters on sanctioned templates.
$hits = @(Test-GitGrepEmpty 'SyncDomainStore<[^>]*\[\]')
if ($hits.Length -gt 0) { $failures.Add("SyncDomainStore<T[]> is forbidden (use envelope payload):`n  $($hits -join "`n  ")") }

$hits = @(Test-GitGrepEmpty 'SyncClientDomain<[^>]*\[\]')
if ($hits.Length -gt 0) { $failures.Add("SyncClientDomain<T[]> is forbidden (use envelope payload):`n  $($hits -join "`n  ")") }

$hits = @(Test-GitGrepEmpty 'ScalarPersistentSyncDomainStore|ScenarioSyncDomainStore|TypedPersistentSyncClientDomain|ScalarPersistentSyncClientDomain')
if ($hits.Length -gt 0) { $failures.Add("Old persistent-sync authoring base names are forbidden:`n  $($hits -join "`n  ")") }

$hits = @(Test-GitGrepEmpty 'PersistentSyncDomainKey\([^)]*,')
if ($hits.Length -gt 0) { $failures.Add("PersistentSyncDomainKey is name-only; wire ids belong on PersistentSyncDomainDefinition/catalog rows:`n  $($hits -join "`n  ")") }

# Production domains must not subclass the reducer base directly; use SyncDomainStore<TPayload>.
# Pattern avoids a leading ':' for git grep (':' can be parsed as a revision magic prefix).
$hits = @(Test-GitGrepEmpty '[[:space:]]:[[:space:]]*SyncDomainStoreBase' @('Server/System/PersistentSync/Domains/'))
if ($hits.Length -gt 0) { $failures.Add("Server Domains must not inherit SyncDomainStoreBase<TCanonical> (use SyncDomainStore<TPayload> instead):`n  $($hits -join "`n  ")") }

$hits = @(Test-GitGrepEmpty '\[PersistentSync(Stock|Owned)Scenario' @('LmpClient/Systems/PersistentSync/Domains/'))
if ($hits.Length -gt 0) { $failures.Add("Client persistent-sync domains must not declare scenario ownership metadata; the server catalog owns it:`n  $($hits -join "`n  ")") }

$domainRawPayloadPaths = @(
    'Server/System/PersistentSync/Domains/',
    'LmpClient/Systems/PersistentSync/Domains/'
)
$hits = @(Test-GitGrepEmpty 'byte\[\][[:space:]]+payload,[[:space:]]*int[[:space:]]+numBytes' $domainRawPayloadPaths)
if ($hits.Length -gt 0) { $failures.Add("Normal persistent-sync domains must not expose byte[] payload + numBytes reducer/apply signatures:`n  $($hits -join "`n  ")") }

$persistentShareSenderPaths = @(
    Get-ChildItem -Path @(
        'LmpClient/Systems/ShareAchievements',
        'LmpClient/Systems/ShareContracts',
        'LmpClient/Systems/ShareExperimentalParts',
        'LmpClient/Systems/ShareFunds',
        'LmpClient/Systems/SharePurchaseParts',
        'LmpClient/Systems/ShareReputation',
        'LmpClient/Systems/ShareScience',
        'LmpClient/Systems/ShareScienceSubject',
        'LmpClient/Systems/ShareStrategy',
        'LmpClient/Systems/ShareTechnology',
        'LmpClient/Systems/ShareUpgradeableFacilities'
    ) -Filter '*MessageSender.cs' -Recurse |
        ForEach-Object { (Resolve-Path -Relative $_.FullName).TrimStart('.', '\').Replace('\', '/') }
)

$hits = @(Test-GitGrepEmpty '\.NumBytes[[:space:]]*=' $persistentShareSenderPaths)
if ($hits.Length -gt 0) { $failures.Add("Persistent-sync share senders must not maintain NumBytes manually; send typed payloads through PersistentSyncSystem:`n  $($hits -join "`n  ")") }

$hits = @(Test-GitGrepEmpty 'PersistentSyncSystem\.Singleton\.MessageSender\.Send[A-Za-z]+Intent|SendIntent\(PersistentSyncDomainNames' $persistentShareSenderPaths)
if ($hits.Length -gt 0) { $failures.Add("Persistent-sync share senders must use typed PersistentSyncSystem.SendIntent<TDomain, TPayload> helpers:`n  $($hits -join "`n  ")") }

$hits = @(Test-GitGrepEmpty 'PersistentSyncSystem\.IsLiveForDomain\(PersistentSyncDomainNames\.' $persistentShareSenderPaths)
if ($hits.Length -gt 0) { $failures.Add("Persistent-sync share senders must use IsLiveFor<TDomain>() instead of domain-name live checks:`n  $($hits -join "`n  ")") }

$scalarSharePaths = @(
    'LmpClient/Systems/ShareFunds/',
    'LmpClient/Systems/ShareScience/',
    'LmpClient/Systems/ShareReputation/'
)

$hits = @(Test-GitGrepEmpty 'PersistentSyncSystem\.SendIntent' $scalarSharePaths)
if ($hits.Length -gt 0) { $failures.Add("Scalar persistent-sync producers must live in their SyncClientDomain, not Share* systems/senders:`n  $($hits -join "`n  ")") }

$hits = @(Test-GitGrepEmpty 'GameEvents\.On(Funds|Science|Reputation)Changed\.Add' $scalarSharePaths)
if ($hits.Length -gt 0) { $failures.Add("Scalar KSP event subscriptions must live in the matching SyncClientDomain OnDomainEnabled hook:`n  $($hits -join "`n  ")") }

$hits = @(Test-GitGrepEmpty 'Set(Funds|Science|Reputation)WithoutTriggeringEvent' @('LmpClient/Systems/'))
if ($hits.Length -gt 0) { $failures.Add("Scalar live apply helpers named Set*WithoutTriggeringEvent are no longer authoring APIs; use SyncClientDomain suppression hooks:`n  $($hits -join "`n  ")") }

$stage2ComplexShareSenderPaths = @(
    'LmpClient/Systems/ShareUpgradeableFacilities/ShareUpgradeableFacilitiesMessageSender.cs',
    'LmpClient/Systems/ShareExperimentalParts/ShareExperimentalPartsMessageSender.cs',
    'LmpClient/Systems/SharePurchaseParts/SharePurchasePartsMessageSender.cs',
    'LmpClient/Systems/ShareScienceSubject/ShareScienceSubjectMessageSender.cs',
    'LmpClient/Systems/ShareAchievements/ShareAchievementsMessageSender.cs',
    'LmpClient/Systems/ShareStrategy/ShareStrategyMessageSender.cs'
)

$hits = @(Test-GitGrepEmpty 'PersistentSyncSystem\.SendIntent' $stage2ComplexShareSenderPaths)
if ($hits.Length -gt 0) { $failures.Add("Stage 2 complex-domain Share senders must not call PersistentSyncSystem.SendIntent (publish from SyncClientDomain):`n  $($hits -join "`n  ")") }

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
