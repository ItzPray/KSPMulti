# -----------------------------------------------------------------------------
# CleanContractsInUniverseBackup.ps1
# -----------------------------------------------------------------------------
# Strips stale offered contracts out of a KSP ContractSystem scenario file.
#
# Context:
#   The LMP test server persists the full `ContractSystem` scenario into
#   `<LMPSERVER>\Universe\Scenarios\ContractSystem.txt` and the canonical
#   reset source `<LMPSERVER>\Universe_Backup\Scenarios\ContractSystem.txt`.
#   Stock KSP stores every offered / active contract inside a single
#   top-level `CONTRACTS { CONTRACT { ... } CONTRACT { ... } ... }` block.
#
#   A reset source captured long after gameplay started easily contains
#   hundreds of stale `state = Offered` contracts from past sessions. On
#   reconnect stock KSP runs `Contract.Update` on each, fails their
#   `MeetRequirements` check against the current career progression and
#   physically removes them (partly outside of the `Contract.Withdraw`
#   path our Harmony guard covers). Client-visible symptom is "Available
#   missions flood on connect then collapse to a handful on reconnect".
#
# What this script does:
#   - Parses the scenario text with a brace-counting walker (PARAM
#     sub-blocks are fine: brace depth is tracked, not naive regex).
#   - Removes every direct child `CONTRACT { ... }` inside the top-level
#     `CONTRACTS { ... }` block whose `state = Offered`.
#   - Leaves every other `CONTRACT` (e.g. `state = Active`) untouched so
#     any in-flight accepted mission the backup happens to contain
#     survives.
#   - Leaves weights, seeds, agent rolls, and any other top-level
#     children completely untouched -- only pruning inside `CONTRACTS`.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass `
#       -File Scripts\CleanContractsInUniverseBackup.ps1 `
#       -Path "C:\LMPServer-test\Universe_Backup\Scenarios\ContractSystem.txt"
#
# Flags:
#   -Path      Required. Full path to the ContractSystem.txt to rewrite.
#   -NoBackup  Skip writing "<Path>.prestrip.bak" beside the source file.
#   -DryRun    Report what would change without writing.
#
# Exit codes:
#   0 success (file rewritten or nothing to do)
#   1 usage / input error
#   2 parse error (unbalanced braces etc.)
# -----------------------------------------------------------------------------

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [switch]$NoBackup,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Write-Error "Input file '$Path' not found."
    exit 1
}

$lines = [System.IO.File]::ReadAllLines($Path)

# Parser states (string, not numeric, to make PowerShell happy in branch
# comparisons): BEFORE_CONTRACTS, CONTRACTS_OPENING, INSIDE_CONTRACTS,
# INSIDE_CHILD, AFTER_CONTRACTS.
$state = 'BEFORE_CONTRACTS'
$output = New-Object System.Collections.Generic.List[string]
$buffer = $null
$childDepth = 0
$removedOffered = 0
$keptContracts = 0
$contractsBlockFound = $false

foreach ($line in $lines) {
    $trim = $line.Trim()

    if ($state -eq 'BEFORE_CONTRACTS') {
        $output.Add($line)
        if ($trim -eq 'CONTRACTS') {
            $state = 'CONTRACTS_OPENING'
            $contractsBlockFound = $true
        }
    }
    elseif ($state -eq 'CONTRACTS_OPENING') {
        $output.Add($line)
        if ($trim -eq '{') {
            $state = 'INSIDE_CONTRACTS'
        }
        elseif ($trim.Length -ne 0) {
            # Unexpected -- fall back to "just copy".
            $state = 'AFTER_CONTRACTS'
        }
    }
    elseif ($state -eq 'INSIDE_CONTRACTS') {
        if ($trim -eq '}') {
            $output.Add($line)
            $state = 'AFTER_CONTRACTS'
        }
        elseif ($trim -eq 'CONTRACT') {
            $buffer = New-Object System.Collections.Generic.List[string]
            $buffer.Add($line)
            $childDepth = 0
            $state = 'INSIDE_CHILD'
        }
        else {
            # Any other line inside CONTRACTS we don't understand: preserve.
            $output.Add($line)
        }
    }
    elseif ($state -eq 'INSIDE_CHILD') {
        $buffer.Add($line)
        if ($trim -eq '{') {
            $childDepth++
        }
        elseif ($trim -eq '}') {
            $childDepth--
            if ($childDepth -eq 0) {
                $isOffered = $false
                foreach ($bl in $buffer) {
                    if ($bl.Trim() -eq 'state = Offered') { $isOffered = $true; break }
                }
                if ($isOffered) {
                    $removedOffered++
                }
                else {
                    $keptContracts++
                    foreach ($bl in $buffer) { $output.Add($bl) }
                }
                $buffer = $null
                $state = 'INSIDE_CONTRACTS'
            }
        }
    }
    elseif ($state -eq 'AFTER_CONTRACTS') {
        $output.Add($line)
    }
}

if ($state -eq 'INSIDE_CONTRACTS' -or $state -eq 'INSIDE_CHILD' -or $state -eq 'CONTRACTS_OPENING') {
    Write-Error "Parse error: unbalanced CONTRACTS / CONTRACT braces in '$Path' (ended in state $state)."
    exit 2
}

if (-not $contractsBlockFound) {
    Write-Host "No top-level 'CONTRACTS' block found in '$Path'. Nothing to do."
    exit 0
}

Write-Host ("Scan complete: removedOfferedContracts={0} keptContracts={1}" -f $removedOffered, $keptContracts)

if ($DryRun) {
    Write-Host "DryRun: not writing changes."
    exit 0
}

if ($removedOffered -eq 0) {
    Write-Host "Offered pool already clean -- not rewriting."
    exit 0
}

if (-not $NoBackup) {
    $bak = $Path + '.prestrip.bak'
    Copy-Item -LiteralPath $Path -Destination $bak -Force
    Write-Host ("Backup written: {0}" -f $bak)
}

# Preserve the source file's line-ending style and trailing newline.
$rawBytes = [System.IO.File]::ReadAllBytes($Path)
$separator = "`r`n"
if ($rawBytes -notcontains 0x0D) { $separator = "`n" }

$joined = [string]::Join($separator, $output)
if ($rawBytes.Length -gt 0 -and ($rawBytes[$rawBytes.Length - 1] -eq 0x0A)) {
    $joined += $separator
}

[System.IO.File]::WriteAllText($Path, $joined, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("Rewrote '{0}'. removedOfferedContracts={1} keptContracts={2}" -f $Path, $removedOffered, $keptContracts)
exit 0
