<#
.SYNOPSIS
  Builds Hearthsheet from source and runs the installer it produces.
  Needs the .NET 10 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/install.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$sdks = if (Get-Command dotnet -ErrorAction SilentlyContinue) { dotnet --list-sdks }
if (-not ($sdks -match '^10\.')) {
    Write-Warning '.NET 10 SDK is required but was not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and run this again.'
    exit 1
}

& "$PSScriptRoot/publish.ps1"
Start-Process (Join-Path $root 'artifacts/Hearthsheet-Setup.exe') -Wait
