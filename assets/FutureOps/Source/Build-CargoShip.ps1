param([switch]$SkipModels,
    [string]$Unity='C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$Blender='C:/Program Files/Blender Foundation/Blender 5.2/blender.exe',
    [string]$BlueprinterProject='C:/Users/marci/dev/BlueprinterProject/Blueprinter-Editor')
$ErrorActionPreference='Stop'
$pack=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace=[IO.Path]::GetFullPath((Join-Path $pack '../..'))
$project=Join-Path $workspace '.nomodkit/futureops-unity'
if(-not $SkipModels) {
    & $Blender -b --python "$PSScriptRoot/BuildCargoShip.py" -- $pack
    if($LASTEXITCODE -ne 0 -or -not (Test-Path "$pack/cargo-sections.json")) {throw 'Cargo model generation failed'}
}
if(-not (Test-Path "$project/Packages/nuclearoption")) {throw 'Prepare the isolated FutureOps Unity project with Build-Pack.ps1 first'}
if(-not (Test-Path "$project/Assets/Blueprinter/_donotship/GameObject/Frigate1_PLACEHOLDER.prefab") -and
   -not (Test-Path "$project/Assets/Blueprinter/_donotship/_stale/GameObject/Frigate1_PLACEHOLDER.prefab")) {
    $blenderPython=Join-Path (Split-Path $Blender) '5.2/python/bin/python.exe'
    & $blenderPython "$PSScriptRoot/CopyShipDonor.py" "$BlueprinterProject/Assets/Blueprinter/_donotship" "$project/Assets/Blueprinter/_donotship"
    if($LASTEXITCODE -ne 0) {throw 'Native donor import failed'}
}
# Blueprinter resolves the game's special assembly names from this editor cache.
foreach($assemblyName in @('Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll')) {
    $cachedAssembly=Join-Path "$project/Library/ScriptAssemblies" $assemblyName
    if(-not (Test-Path $cachedAssembly)) {
        New-Item -ItemType Directory -Force -Path "$project/Library/ScriptAssemblies" | Out-Null
        Copy-Item -LiteralPath "$project/Packages/nuclearoption/$assemblyName" -Destination $cachedAssembly
        [IO.File]::SetAttributes($cachedAssembly,[IO.File]::GetAttributes($cachedAssembly) -bor [IO.FileAttributes]::ReadOnly)
    }
}
New-Item -ItemType Directory -Force -Path "$project/Assets/CargoEditor", "$project/Assets/CargoTextures" | Out-Null
Copy-Item -Path "$pack/Editor/CargoNative/*" -Destination "$project/Assets/CargoEditor" -Force
Copy-Item -LiteralPath "$pack/Models/CargoShipNative.fbx" -Destination "$project/Assets/SourceModels/CargoShipNative.fbx" -Force
Copy-Item -Path "$pack/Textures/*.png" -Destination "$project/Assets/CargoTextures" -Force
$env:FUTUREOPS_OUT=$pack
$arguments=@('-batchmode','-projectPath',"`"$project`"",'-executeMethod','CargoShipBuilder.Run','-logFile',"`"$project/cargo-build.log`"")
$process=Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0) {throw "Cargo native build failed: $project/cargo-build.log"}
Get-Content -LiteralPath "$pack/cargo-native-validation.txt"
