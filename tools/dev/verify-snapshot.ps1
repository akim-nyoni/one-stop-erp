# Runs verify.ps1 on a separate worktree (..\ERP-verify) checked out at the latest commit, so source can be edited while it runs.
# Usage (from any folder):  powershell -File tools/dev/verify-snapshot.ps1 > verify.out 2>&1
# Only committed work is verified; commit first. The accounting suite shares the dev database, so avoid running it here at the same time.
$ErrorActionPreference = 'Continue'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$snapshot = Join-Path (Split-Path $root -Parent) 'ERP-verify'
$commit = (git -C $root rev-parse HEAD).Trim()
if (-not (Test-Path (Join-Path $snapshot '.git'))) { git -C $root worktree add --detach $snapshot $commit | Out-Null }
git -C $snapshot checkout --detach --force $commit 2>&1 | Out-Null
Write-Output "=== VERIFYING $commit IN $snapshot ==="
& powershell -File (Join-Path $snapshot 'tools\dev\verify.ps1')
exit $LASTEXITCODE
