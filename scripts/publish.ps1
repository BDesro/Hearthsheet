<#
.SYNOPSIS
  Builds the release package into artifacts/:
    Hearthsheet-<version>-win-x64.zip  the in-app update package
    Hearthsheet-Setup.exe              the installer people download (same files, built with Inno Setup 6)
    SHA256SUMS.txt                     checksums of both
  These are exactly what a GitHub release must carry. Needs Inno Setup 6: winget install JRSoftware.InnoSetup
.EXAMPLE
  ./scripts/publish.ps1                 # version from Directory.Build.props
  ./scripts/publish.ps1 -Version 1.2.0  # override (CI passes the tag version)
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Version) { $Version = ([xml](Get-Content "$root/Directory.Build.props")).Project.PropertyGroup.Version | Select-Object -First 1 }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid version '$Version'" }

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts 'publish/Hearthsheet'
if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }

# Self-contained: the .NET runtime ships inside, so people install nothing else and never need admin rights.
# The updater is a single trimmed exe because it's copied out of the install folder to replace it.
dotnet publish "$root/src/Hearthsheet.App" -c Release -r win-x64 --self-contained true -p:Version=$Version -o $stage
if ($LASTEXITCODE) { throw 'App publish failed' }
dotnet publish "$root/src/Hearthsheet.Updater" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:Version=$Version -o $stage
if ($LASTEXITCODE) { throw 'Updater publish failed' }
Get-ChildItem $stage -Filter *.pdb | Remove-Item

$zipName = "Hearthsheet-$Version-win-x64.zip"
Compress-Archive -Path "$stage/*" -DestinationPath (Join-Path $artifacts $zipName)
& $iscc /Q "/DAppVersion=$Version" "/DSourceDir=$stage" "/O$artifacts" (Join-Path $root 'packaging/Hearthsheet.iss')
if ($LASTEXITCODE) { throw 'Installer build failed' }

$sums = foreach ($name in $zipName, 'Hearthsheet-Setup.exe') {
    "$((Get-FileHash (Join-Path $artifacts $name) -Algorithm SHA256).Hash.ToLowerInvariant())  $name"
}
[IO.File]::WriteAllText((Join-Path $artifacts 'SHA256SUMS.txt'), ($sums -join "`n") + "`n")
Write-Host "Packages: artifacts/$zipName, artifacts/Hearthsheet-Setup.exe"
$sums | ForEach-Object { Write-Host $_ }
