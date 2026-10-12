param([switch]$SkipModels,
    [string]$Blender='C:/Program Files/Blender Foundation/Blender 5.2/blender.exe')
$ErrorActionPreference='Stop'
$pack=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace=[IO.Path]::GetFullPath((Join-Path $pack '../..'))
if(-not $SkipModels) {
    & $Blender -b --python "$PSScriptRoot/BuildCargoShip.py" -- $pack
    if($LASTEXITCODE -ne 0 -or -not (Test-Path "$pack/cargo-sections.json")) {throw 'Cargo model generation failed'}
}
# The ship is packed with the rest of the mod's Blueprinter content.
& "$workspace/tools/Compile-BlueprinterAssets.ps1"
