# Installs the One4All server as a Windows service on this machine (run as administrator from the published server folder).
#
#   .\server-install.ps1 -Connection "Host=localhost;Port=5432;Database=one4all;Username=one4all;Password=..." [-Port 5000] [-ServiceName "One4All Server"]
#   .\server-install.ps1 -Uninstall
#
# Checks the PostgreSQL prerequisite (the server must answer on the host and port in the connection string; PostgreSQL 16 or later),
# writes the connection string into appsettings.json beside the executable, registers the service to start automatically and starts it.
# The first start creates the database when it does not exist, applies the migrations and seeds the administrator (admin / Admin@123:
# change it at first login).
param(
    [string]$Connection = "",
    [int]$Port = 5000,
    [string]$ServiceName = "One4All Server",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$exe = Join-Path $here "One4All.Server.exe"

if ($Uninstall) {
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        if ($existing.Status -ne "Stopped") { Stop-Service -Name $ServiceName -Force }
        & sc.exe delete "$ServiceName" | Out-Null
        Write-Host "Service '$ServiceName' removed. The database and the files were left in place."
    } else {
        Write-Host "Service '$ServiceName' is not installed."
    }
    exit 0
}

if (-not (Test-Path $exe)) { throw "One4All.Server.exe is not in $here; run this script from the published server folder." }
if ($Connection.Length -eq 0) { throw "Give the PostgreSQL connection string with -Connection." }

# PostgreSQL prerequisite: the host and port in the connection string must answer.
$parts = @{}
foreach ($pair in $Connection.Split(";")) {
    if ($pair -match "^\s*([^=]+)=(.*)$") { $parts[$Matches[1].Trim().ToLowerInvariant()] = $Matches[2].Trim() }
}
$pgHost = if ($parts.ContainsKey("host")) { $parts["host"] } else { "localhost" }
$pgPort = if ($parts.ContainsKey("port")) { [int]$parts["port"] } else { 5432 }
$probe = New-Object System.Net.Sockets.TcpClient
try {
    $probe.Connect($pgHost, $pgPort)
    $probe.Close()
} catch {
    throw "PostgreSQL does not answer on ${pgHost}:${pgPort}. Install PostgreSQL 16 or later (https://www.postgresql.org/download/windows/), or point -Connection at the server that runs it, then run this script again."
}

# The connection string and port go into appsettings.json beside the executable (kept on upgrade).
$settingsPath = Join-Path $here "appsettings.json"
$settings = if (Test-Path $settingsPath) { Get-Content $settingsPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
if ($null -eq $settings.ConnectionStrings) { $settings | Add-Member -NotePropertyName ConnectionStrings -NotePropertyValue ([pscustomobject]@{}) }
$settings.ConnectionStrings | Add-Member -NotePropertyName One4All -NotePropertyValue $Connection -Force
$settings | Add-Member -NotePropertyName Urls -NotePropertyValue "http://0.0.0.0:$Port" -Force
$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding utf8
& icacls $settingsPath /inheritance:r /grant:r "SYSTEM:F" /grant:r "Administrators:F" | Out-Null

# Register (or re-register) the service: automatic start, restart on failure.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    if ($existing.Status -ne "Stopped") { Stop-Service -Name $ServiceName -Force }
    & sc.exe config "$ServiceName" binPath= "`"$exe`"" start= auto | Out-Null
} else {
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName $ServiceName -Description "One4All ERP server (API, scheduler, webhooks)" -StartupType Automatic | Out-Null
}
& sc.exe failure "$ServiceName" reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' is running on port $Port. First start creates the database if needed, migrates it and seeds the administrator; see the log folder beside the executable."
