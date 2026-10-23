<#
.SYNOPSIS
    Downloads WinDivert (packet-filter driver used by the hotspot gate) from its official GitHub release,
    checks its SHA-256 and unpacks it to third_party\WinDivert. Run by build.ps1; safe to run again.
#>
[CmdletBinding()]
param(
    [string] $Destination = (Join-Path $PSScriptRoot '..\third_party\WinDivert')
)

$ErrorActionPreference = 'Stop'

$version = '2.2.2'
$url = "https://github.com/basil00/WinDivert/releases/download/v$version/WinDivert-$version-A.zip"
$expectedSha256 = '63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15'

$destination = [System.IO.Path]::GetFullPath($Destination)
$marker = Join-Path $destination "VERSION-$version"
if (Test-Path $marker) {
    Write-Host "WinDivert $version already present in $destination"
    return
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) "windivert-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $zip = Join-Path $temp 'windivert.zip'
    Write-Host "Downloading WinDivert $version ..."
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

    $actual = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expectedSha256) {
        throw "WinDivert download has the wrong SHA-256 ($actual). Expected $expectedSha256. Not using it."
    }

    Expand-Archive -Path $zip -DestinationPath $temp -Force
    $source = Join-Path $temp "WinDivert-$version-A"
    if (Test-Path $destination) { Remove-Item -Recurse -Force $destination }
    New-Item -ItemType Directory -Path (Join-Path $destination 'x64') | Out-Null
    foreach ($file in 'WinDivert.dll', 'WinDivert64.sys') {
        Copy-Item (Join-Path $source "x64\$file") (Join-Path $destination 'x64')
    }
    Copy-Item (Join-Path $source 'LICENSE') $destination
    Copy-Item (Join-Path $source 'README') $destination
    Set-Content -Path $marker -Value $url
    Write-Host "WinDivert $version ready in $destination"
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
