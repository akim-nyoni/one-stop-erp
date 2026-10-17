# Builds the solution in Debug and Release, then runs every test suite without rebuilding (see docs/HANDOVER.md).
# Usage (from any folder):  powershell -File tools/dev/verify.ps1 > verify.out 2>&1
# Never edit source files while the Debug or Release build is running; wait for "=== RELEASE OK ===".
$ErrorActionPreference = 'Continue'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
dotnet build "$root\One4All.slnx" -c Debug --nologo -v q -nodeReuse:false -p:UseSharedCompilation=false 2>&1 | Select-String 'error|Build succeeded' | Select-Object -First 15
if ($LASTEXITCODE -ne 0) { Write-Output "=== DEBUG BUILD FAILED ==="; exit 1 }
Write-Output "=== DEBUG OK ==="
dotnet build "$root\One4All.slnx" -c Release --nologo -v q -nodeReuse:false -p:UseSharedCompilation=false 2>&1 | Select-String 'error|Build succeeded' | Select-Object -First 15
if ($LASTEXITCODE -ne 0) { Write-Output "=== RELEASE BUILD FAILED ==="; exit 1 }
Write-Output "=== RELEASE OK ==="
dotnet test "$root\One4All.slnx" -c Debug --no-build --nologo 2>&1 | Select-String -Pattern 'Passed!|Failed!|\[FAIL\]|Error Message|Expected|Exception' -Context 0,6 | ForEach-Object { $_.Line; $_.Context.PostContext }
exit $LASTEXITCODE
