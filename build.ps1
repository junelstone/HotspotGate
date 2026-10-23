<#
.SYNOPSIS
    Builds and tests Stone's Hotspot. Used locally and by CI.

.EXAMPLE
    .\build.ps1                      # restore, build (Release), run all tests
    .\build.ps1 -Configuration Debug
    .\build.ps1 -SkipTests
    .\build.ps1 -Publish             # also publish the service and tray app to .\artifacts\publish (win-x64)
    .\build.ps1 -Installer           # publish, release check, then StonesHotspot-Setup and LicenseManager-Setup
                                     # (Windows + Inno Setup 6) into .\installer\Output
    .\build.ps1 -Installer -AllowDevelopmentKeys   # TEST installer with the development keys: never for a customer
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipTests,
    [switch] $Publish,
    [switch] $Installer,
    [switch] $AllowDevelopmentKeys
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$solution = Join-Path $root 'StonesHotspot.sln'
$artifacts = Join-Path $root 'artifacts'

function Invoke-Step([string] $Title, [scriptblock] $Command) {
    Write-Host ''
    Write-Host "==> $Title" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Title (exit code $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'The .NET 8 SDK is not installed. Get it from https://dotnet.microsoft.com/download/dotnet/8.0' -ForegroundColor Red
    exit 1
}

Invoke-Step 'dotnet --version' { dotnet --version }
Invoke-Step 'WinDivert (packet filter driver)' { & (Join-Path $root 'tools\Get-WinDivert.ps1'); $global:LASTEXITCODE = 0 }
Invoke-Step 'Restore' { dotnet restore $solution }
Invoke-Step "Build ($Configuration)" { dotnet build $solution --configuration $Configuration --no-restore }
# Owner-only licence tool: built to catch errors, never published or packaged.
Invoke-Step 'Build LicenseManager (owner tool)' { dotnet build (Join-Path $root 'tools/StonesHotspot.LicenseManager') --configuration $Configuration }

if (-not $SkipTests) {
    $results = Join-Path $artifacts 'test-results'
    Invoke-Step 'Test' {
        dotnet test $solution --configuration $Configuration --no-build `
            --logger 'trx;LogFileName=tests.trx' --results-directory $results
    }
}

if ($Installer) { $Publish = $true }

if ($Publish) {
    # The LicenseManager (owner-only tool) is deliberately never published here.
    $publishRoot = Join-Path $artifacts 'publish'
    foreach ($project in 'src/StonesHotspot.Service', 'src/StonesHotspot.Tray') {
        $name = Split-Path $project -Leaf
        Invoke-Step "Publish $name" {
            dotnet publish (Join-Path $root $project) --configuration $Configuration --runtime win-x64 `
                --self-contained true --output (Join-Path $publishRoot $name)
        }
    }
}

if ($Installer) {
    # Two separate outputs. The owner's LicenseManager is published to its own folder, outside the customer files.
    $ownerRoot = Join-Path $artifacts 'owner'
    Invoke-Step 'Publish LicenseManager (owner only, separate installer)' {
        dotnet publish (Join-Path $root 'tools/StonesHotspot.LicenseManager') --configuration $Configuration --runtime win-x64 `
            --self-contained true --output (Join-Path $ownerRoot 'LicenseManager')
    }

    $checkArgs = @{ Path = (Join-Path $artifacts 'publish') }
    if ($AllowDevelopmentKeys) { $checkArgs.AllowDevelopmentKeys = $true }
    Invoke-Step 'Release check (nothing of the owner in the customer installer)' {
        & (Join-Path $root 'tools/Test-ReleaseContent.ps1') @checkArgs
    }

    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $iscc = if ($isccCommand) { $isccCommand.Source } else { $null }
    if (-not $iscc) {
        $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
            Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }
    if (-not $iscc) {
        Write-Host 'Inno Setup 6 is not installed (https://jrsoftware.org/isdl.php): installers not built.' -ForegroundColor Red
        exit 1
    }
    $version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
    Invoke-Step "Installer StonesHotspot-Setup-$version.exe" {
        & $iscc "/DAppVersion=$version" "/DPublishDir=$(Join-Path $artifacts 'publish')" (Join-Path $root 'installer/StonesHotspot.iss')
    }
    Invoke-Step "Installer LicenseManager-Setup-$version.exe (OWNER ONLY)" {
        & $iscc "/DAppVersion=$version" "/DPublishDir=$ownerRoot" (Join-Path $root 'installer/LicenseManager.iss')
    }
    Get-ChildItem (Join-Path $root 'installer/Output') -Filter *.exe | ForEach-Object {
        Write-Host ("{0}  SHA-256 {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
    }
}

Write-Host ''
Write-Host 'Build completed successfully.' -ForegroundColor Green
