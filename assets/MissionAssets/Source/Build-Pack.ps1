param([switch]$SkipModels,
    [string]$Blender='C:/Program Files/Blender Foundation/Blender 5.2/blender.exe',
    [string]$Unity='C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$pack=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace=[IO.Path]::GetFullPath((Join-Path $pack '../..'))
$project=Join-Path $workspace '.nomodkit/futureops-unity'
if(-not $SkipModels) {
    & $Blender -b --python-exit-code 1 --python "$PSScriptRoot/BuildMissionAssets.py" -- $pack
    if($LASTEXITCODE -ne 0 -or -not (Test-Path "$pack/catalog.json")) {throw 'Mission model generation failed'}
}
if(-not (Test-Path "$project/Packages/manifest.json")) {
    & "$workspace/assets/FutureOps/Source/Build-Pack.ps1" -SkipModels -Blender $Blender -Unity $Unity
}
New-Item -ItemType Directory -Force -Path "$project/Assets/Blueprinter/Mods/MissionAssets", "$project/Assets/SourceModels", "$project/Assets/Editor" | Out-Null
Copy-Item -Path "$pack/Models/*.fbx" -Destination "$project/Assets/SourceModels" -Force
New-Item -ItemType Directory -Force -Path "$project/Assets/Blueprinter/Mods/MissionAssets/Textures" | Out-Null
Copy-Item -Path "$pack/Textures/*.png" -Destination "$project/Assets/Blueprinter/Mods/MissionAssets/Textures" -Force
$catalog=Get-Content "$pack/catalog.json" -Raw | ConvertFrom-Json
foreach($entry in $catalog.assets) {
    $lo=$entry.bounds_blender[0]; $hi=$entry.bounds_blender[1]
    $entry | Add-Member -MemberType NoteProperty -Name sizeUnity -Value @{x=$hi[0]-$lo[0];y=$hi[2]-$lo[2];z=$hi[1]-$lo[1]} -Force
}
$catalog | ConvertTo-Json -Depth 10 | Set-Content "$pack/catalog.json"
Copy-Item -LiteralPath "$pack/catalog.json" -Destination "$project/Assets/Blueprinter/Mods/MissionAssets/catalog.json" -Force
New-Item -ItemType Directory -Force -Path "$project/Assets/Blueprinter/Mods/MissionAssets/DesignStubs" | Out-Null
Copy-Item -Path "$pack/MissionStubs/*.json" -Destination "$project/Assets/Blueprinter/Mods/MissionAssets/DesignStubs" -Force
Copy-Item -LiteralPath "$workspace/assets/FutureOps/Editor/FutureOpsBuilder.cs" -Destination "$project/Assets/Editor/FutureOpsBuilder.cs" -Force
$previousName=$env:FUTUREOPS_PACK_NAME; $previousOut=$env:FUTUREOPS_OUT
try {
    $env:FUTUREOPS_PACK_NAME='MissionAssets'; $env:FUTUREOPS_OUT=$pack
    $arguments=@('-batchmode','-projectPath',"`"$project`"",'-executeMethod','FutureOpsBuilder.Run','-logFile',"`"$project/mission-build.log`"")
    $process=Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if($process.ExitCode -ne 0) {throw "Mission bundle build failed: $project/mission-build.log"}
} finally { $env:FUTUREOPS_PACK_NAME=$previousName; $env:FUTUREOPS_OUT=$previousOut }
Get-Content "$pack/validation.txt"
