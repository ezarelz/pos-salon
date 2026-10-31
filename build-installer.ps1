<#
.SYNOPSIS
  Builds the POS Salon installer: tests -> dotnet publish (win-x64, framework-dependent) -> Inno Setup.
.DESCRIPTION
  Requirements on this PC: .NET 10 SDK, Inno Setup 6 (https://jrsoftware.org/isdl.php or `winget install JRSoftware.InnoSetup`).
  Output: installer\Output\POSSalon-Setup-<version>.exe  (version from Directory.Build.props).
.EXAMPLE
  .\build-installer.ps1
  .\build-installer.ps1 -SkipTests
#>
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

[xml]$props = Get-Content -Raw 'Directory.Build.props'
$version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'Version tidak ditemukan di Directory.Build.props.' }
Write-Host "== POS Salon $version" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host '== Test' -ForegroundColor Cyan
    dotnet run --project tests/MSPASalon.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Ada test yang gagal; installer tidak dibuat.' }
}

Write-Host '== Publish (win-x64, framework-dependent, single file)' -ForegroundColor Cyan
if (Test-Path 'publish\win-x64') { Remove-Item 'publish\win-x64' -Recurse -Force }
dotnet publish src/MSPASalon.Desktop -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish gagal.' }
if (-not (Test-Path 'publish\win-x64\POSSalon.exe')) { throw 'publish\win-x64\POSSalon.exe tidak ditemukan.' }

$candidates = @(
    $env:ISCC,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }
$iscc = @($candidates)[0]
if (-not $iscc) { $iscc = (Get-Command 'iscc.exe' -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) tidak ditemukan. Pasang dari https://jrsoftware.org/isdl.php atau: winget install JRSoftware.InnoSetup' }

Write-Host "== Installer ($iscc)" -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$version" 'installer\POSSalon.iss'
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup gagal.' }

$setup = "installer\Output\POSSalon-Setup-$version.exe"
Write-Host "== Selesai: $setup" -ForegroundColor Green
