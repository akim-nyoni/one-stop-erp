# Builds the One4All desktop installer: publishes the desktop app (self-contained, win-x64) and the server, then compiles installer/One4All.iss
# with Inno Setup when it is installed. After building, sign the release for the update channel with the licence generator:
#   One4All.Licensing.Generator update --version <x.y.z> --file artifacts/installer/One4All-<x.y.z>-setup.exe --url <download URL or share path>
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts/publish"

dotnet publish (Join-Path $root "src/One4All.Desktop/One4All.Desktop.csproj") -c $Configuration -r win-x64 --self-contained true -p:Version=$Version -o (Join-Path $publish "desktop")
dotnet publish (Join-Path $root "src/One4All.Server/One4All.Server.csproj") -c $Configuration -p:Version=$Version -o (Join-Path $publish "server")

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($null -eq $iscc) {
    Write-Host "Inno Setup 6 is not installed: the published app is in $publish; install Inno Setup to build the setup program."
    exit 0
}

$env:ONE4ALL_VERSION = $Version
& $iscc (Join-Path $PSScriptRoot "One4All.iss")
Write-Host "Installer: $(Join-Path $root "artifacts/installer/One4All-$Version-setup.exe")"
