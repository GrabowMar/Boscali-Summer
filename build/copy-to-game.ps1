[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "src\BoscaliSummer\bin\$Configuration\netstandard2.1\BoscaliSummer.dll"
$target = Join-Path $GameDir 'BepInEx\plugins\BoscaliSummer'
if (-not (Test-Path -LiteralPath $source)) { throw "Build output not found: $source" }
if (-not (Test-Path -LiteralPath $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
Copy-Item -LiteralPath $source -Destination $target -Force
Write-Host "Deployed BoscaliSummer.dll to $target"
