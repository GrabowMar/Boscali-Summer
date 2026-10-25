param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\reports\code-atlas')
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$atlasOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $atlasOutput | Out-Null

& dotnet run --project (Join-Path $PSScriptRoot 'CodeAtlas.csproj') -c Release -- --root $repoRoot --out $atlasOutput
if ($LASTEXITCODE -ne 0) { throw "Code Atlas extraction failed (exit $LASTEXITCODE)." }

foreach ($assetName in @('index.html', 'atlas.css', 'atlas.js', 'THIRD-PARTY.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $assetName) -Destination (Join-Path $atlasOutput $assetName) -Force
}
$vendorOutput = Join-Path $atlasOutput 'vendor'
New-Item -ItemType Directory -Force -Path $vendorOutput | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'vendor') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $vendorOutput $_.Name) -Force
}
Write-Output "Open $atlasOutput\index.html in a browser."
