[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option',
    [switch]$SkipPatchProbe
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:APPDATA = Join-Path $root '.appdata'

dotnet restore (Join-Path $root 'BoscaliSummer.sln')
dotnet build (Join-Path $root 'BoscaliSummer.sln') -c $Configuration --no-restore -p:GameDir="$GameDir"

$built = Join-Path $root "src\BoscaliSummer\bin\$Configuration\netstandard2.1\BoscaliSummer.dll"
if (-not (Test-Path -LiteralPath $built)) { throw "Build output not found: $built" }
dotnet run --project (Join-Path $root 'tests\BoscaliSummer.Tests\BoscaliSummer.Tests.csproj') -c $Configuration --no-build
if (-not $SkipPatchProbe) {
    dotnet run --project (Join-Path $root 'tests\BoscaliSummer.PatchProbe\BoscaliSummer.PatchProbe.csproj') -c $Configuration --no-build -- $GameDir $built
}

$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($built).FileVersion
$version = ($version -split '\.')[0..2] -join '.'
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'
$pluginDir = Join-Path $stage 'BepInEx\plugins\BoscaliSummer'

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item -LiteralPath $built -Destination $pluginDir -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $pluginDir -Force
Copy-Item -LiteralPath (Join-Path $root 'CHANGELOG.md') -Destination $pluginDir -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $pluginDir -Force

$meta = [ordered]@{
    id = 'BoscaliSummer'
    artifact = [ordered]@{
        type = 'plugin'
        fileName = 'BoscaliSummer.dll'
        version = $version
        category = 'Release'
        gameVersion = '0.34.2'
    }
}
$meta | ConvertTo-Json -Depth 5 | Out-File (Join-Path $pluginDir 'meta.json') -Encoding utf8

if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
$archive = Join-Path $dist "BoscaliSummer-$version.zip"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($archive, 'Create')
try {
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $entry = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry) | Out-Null
    }
}
finally { $zip.Dispose() }

Remove-Item -LiteralPath $stage -Recurse -Force
$loose = Join-Path $dist 'BoscaliSummer.dll'
Copy-Item -LiteralPath $built -Destination $loose -Force

Write-Host "Boscali Summer $version"
Write-Host "DLL: $loose"
Write-Host "SHA256: $((Get-FileHash -LiteralPath $loose -Algorithm SHA256).Hash.ToLower())"
Write-Host "ZIP: $archive"
Write-Host "SHA256: $((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLower())"
