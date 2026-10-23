<#
.SYNOPSIS
    Fails when a folder about to go into the CUSTOMER installer contains anything of the owner.

.DESCRIPTION
    Run by build.ps1 -Installer on artifacts\publish before Inno Setup. Refuses:
      - LicenseManager / LicenseIssuer / LicenseServer files (the owner's tools and server);
      - key files (*.shkey, *.pfx, *.key, *.pem), appsettings.Local.json, anything from tools\dev-keys;
      - any file containing a private key (PEM "PRIVATE KEY" or the text of a key in tools\dev-keys);
      - the DEVELOPMENT public keys compiled into StonesHotspot.Licensing.dll (unless -AllowDevelopmentKeys, for a
        test build that must never be given to a customer).
    Exit code 0 = clean, 1 = something forbidden (listed).

.EXAMPLE
    .\tools\Test-ReleaseContent.ps1 -Path .\artifacts\publish
    .\tools\Test-ReleaseContent.ps1 -Path .\artifacts\publish -AllowDevelopmentKeys
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Path,
    [switch] $AllowDevelopmentKeys
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path $Path -PathType Container)) {
    Write-Host "Release check: folder not found: $Path" -ForegroundColor Red
    exit 1
}

$problems = [System.Collections.Generic.List[string]]::new()
$latin1 = [System.Text.Encoding]::GetEncoding(28591)   # one char per byte; also on Windows PowerShell 5.1

# Text of the development private keys (only on a developer PC; never committed).
$devKeyDir = Join-Path $PSScriptRoot 'dev-keys'
$secrets = @()
if (Test-Path $devKeyDir) {
    $secrets = Get-ChildItem $devKeyDir -File | ForEach-Object {
        Get-Content $_.FullName | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { $_.Trim() }
    } | Where-Object { $_.Length -ge 16 }
}

$forbiddenNames = @('*LicenseManager*', '*LicenseIssuer*', '*LicenseServer*', '*.shkey', '*.pfx', '*.key', '*.pem',
                    'appsettings.Local.json', 'licence-private*', 'server-private*', 'licences.db', 'licences-server.db')

foreach ($file in Get-ChildItem $Path -File -Recurse) {
    $relative = $file.FullName.Substring((Resolve-Path $Path).Path.Length).TrimStart('\', '/')
    foreach ($pattern in $forbiddenNames) {
        if ($file.Name -like $pattern) { $problems.Add("$relative — forbidden file name ($pattern)") }
    }
    if ($relative -match '(^|[\\/])dev-keys([\\/]|$)') { $problems.Add("$relative — from tools\dev-keys") }

    $text = $latin1.GetString([System.IO.File]::ReadAllBytes($file.FullName))
    if ($text.Contains('PRIVATE KEY-----')) { $problems.Add("$relative — contains a PEM private key") }
    foreach ($secret in $secrets) {
        if ($text.Contains($secret)) { $problems.Add("$relative — contains a development private key") }
    }
    if (-not $AllowDevelopmentKeys -and $text.Contains('# DEVELOPMENT')) {
        $problems.Add("$relative — built with the DEVELOPMENT public keys (put the owner's keys in src\StonesHotspot.Licensing\Keys, see docs\licensing.md)")
    }
}

if ($problems.Count -gt 0) {
    Write-Host "Release check FAILED — the customer installer must not contain:" -ForegroundColor Red
    $problems | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
$note = if ($AllowDevelopmentKeys) { ' (development keys allowed: TEST BUILD, never for a customer)' } else { '' }
Write-Host "Release check passed: nothing of the owner in $Path$note" -ForegroundColor Green
exit 0
