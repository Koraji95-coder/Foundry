<#
.SYNOPSIS
    Identifies the last stable deployment tag and reports the SHA to roll back to.

.DESCRIPTION
    Searches the local Git repository for tags matching the stable deployment
    marker pattern (stable-YYYYMMDD-HHmmss) and returns the most recent one.
    Used by operators and the CI/CD pipeline when a deployment failure requires
    rolling back to the last known-good state.

.PARAMETER RepoRoot
    Path to the Git repository root. Defaults to the directory two levels above
    this script (i.e., the Foundry repo root).

.PARAMETER TagPrefix
    Prefix used for stable deployment tags. Defaults to "stable-".

.EXAMPLE
    .\rollback.ps1
    # Returns the last stable tag name and SHA.

.EXAMPLE
    .\rollback.ps1 -RepoRoot "C:\Projects\Foundry"
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".." "..")).Path,
    [string]$TagPrefix = "stable-"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Verify we're inside a Git repo ───────────────────────────────────────────
Push-Location $RepoRoot
try {
    $null = git rev-parse --git-dir 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Not a Git repository: $RepoRoot"
        exit 1
    }

    # ── Fetch tags from origin so we have the full picture ───────────────────
    Write-Host "Fetching tags from origin…"
    git fetch --tags --quiet 2>&1 | Out-Null

    # ── List stable tags sorted by creation date (newest first) ─────────────
    $tags = git tag --list "$TagPrefix*" --sort=-creatordate 2>&1
    if ($LASTEXITCODE -ne 0 -or -not $tags) {
        Write-Warning "No stable deployment tags found (pattern: '$TagPrefix*')."
        Write-Warning "This may be the first deployment. No rollback target available."
        exit 0
    }

    $lastTag = ($tags -split "`n" | Where-Object { $_ -match "\S" } | Select-Object -First 1).Trim()

    # ── Resolve the commit SHA for the tag ──────────────────────────────────
    $stableSha = (git rev-list -n 1 $lastTag).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $stableSha) {
        Write-Error "Could not resolve SHA for tag '$lastTag'."
        exit 1
    }

    # ── Output ───────────────────────────────────────────────────────────────
    $result = [PSCustomObject]@{
        Tag       = $lastTag
        SHA       = $stableSha
        RepoRoot  = $RepoRoot
    }

    Write-Host ""
    Write-Host "Last stable deployment tag : $lastTag"
    Write-Host "Commit SHA                 : $stableSha"
    Write-Host ""
    Write-Host "To roll back manually, run:"
    Write-Host "  git checkout $lastTag"
    Write-Host "  dotnet publish src/Foundry.Broker/Foundry.Broker.csproj --configuration Release"
    Write-Host ""

    return $result
}
finally {
    Pop-Location
}
