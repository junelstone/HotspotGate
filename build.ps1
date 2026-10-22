<#
.SYNOPSIS
    Builds and tests Stone's Hotspot. Used locally and by CI.

.EXAMPLE
    .\build.ps1                      # restore, build (Release), run all tests
    .\build.ps1 -Configuration Debug
    .\build.ps1 -SkipTests
    .\build.ps1 -Publish             # also publish the service and tray app to .\artifacts\publish (win-x64)
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipTests,
    [switch] $Publish
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
Invoke-Step 'Restore' { dotnet restore $solution }
Invoke-Step "Build ($Configuration)" { dotnet build $solution --configuration $Configuration --no-restore }

if (-not $SkipTests) {
    $results = Join-Path $artifacts 'test-results'
    Invoke-Step 'Test' {
        dotnet test $solution --configuration $Configuration --no-build `
            --logger 'trx;LogFileName=tests.trx' --results-directory $results
    }
}

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

Write-Host ''
Write-Host 'Build completed successfully.' -ForegroundColor Green
