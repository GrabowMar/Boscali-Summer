param(
    [string]$Blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe',
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$BlueprinterProject = 'C:/Users/marci/dev/BlueprinterProject/Blueprinter-Editor',
    [switch]$SkipModels
)
$ErrorActionPreference = 'Stop'
$pack = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace = [IO.Path]::GetFullPath((Join-Path $pack '../..'))
$project = Join-Path $workspace '.nomodkit/futureops-unity'
if (-not $SkipModels) {
    & $Blender -b --python-exit-code 1 --python (Join-Path $PSScriptRoot 'BuildFutureOps.py') -- $pack
    if ($LASTEXITCODE -ne 0) { throw 'Blender asset generation failed' }
}
New-Item -ItemType Directory -Force -Path "$project/Assets/Editor", "$project/Assets/SourceModels", "$project/Assets/Blueprinter/Mods/FutureOps", "$project/Packages", "$project/ProjectSettings" | Out-Null
$blueprinter = Get-ChildItem -LiteralPath "$BlueprinterProject/Library/PackageCache" -Directory | Where-Object Name -Like 'com.nikkorap.blueprinter@*' | Select-Object -First 1
if (-not $blueprinter) { throw 'Blueprinter package cache not found' }
$manifest = @{ dependencies = @{
    'com.nikkorap.blueprinter' = 'file:' + $blueprinter.FullName.Replace('\','/')
    'com.unity.render-pipelines.universal' = '14.0.12'
    'com.unity.addressables' = '1.22.3'
    'com.unity.modules.assetbundle' = '1.0.0'
    'com.unity.modules.physics' = '1.0.0'
    'com.unity.modules.jsonserialize' = '1.0.0'
    'com.unity.modules.animation' = '1.0.0'
    'com.unity.modules.imgui' = '1.0.0'
    'com.unity.modules.imageconversion' = '1.0.0'
} }
# Reuse the donor project's cached packages; do not depend on registry downloads.
foreach ($cached in Get-ChildItem -LiteralPath "$BlueprinterProject/Library/PackageCache" -Directory) {
    $packageName = $cached.Name.Split('@')[0]
    if ($packageName -like 'com.unity.modules.*') {
        $manifest.dependencies[$packageName] = '1.0.0'
    }
    elseif ($packageName -in @('com.unity.addressables','com.unity.render-pipelines.universal',
        'com.unity.render-pipelines.core','com.unity.render-pipelines.universal-config',
        'com.unity.shadergraph','com.unity.searcher','com.unity.burst','com.unity.mathematics',
        'com.unity.scriptablebuildpipeline','com.unity.ugui',
        'com.unity.test-framework','com.unity.ext.nunit','com.unity.textmeshpro')) {
        $manifest.dependencies[$packageName] = 'file:' + $cached.FullName.Replace('\','/')
    }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$project/Packages/manifest.json"
Set-Content -LiteralPath "$project/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
if (-not (Test-Path "$project/Packages/nuclearoption")) {
    Copy-Item -LiteralPath "$BlueprinterProject/Packages/nuclearoption" -Destination "$project/Packages/nuclearoption" -Recurse
}
Copy-Item -Path "$pack/Models/*.fbx" -Destination "$project/Assets/SourceModels" -Force
New-Item -ItemType Directory -Force -Path "$project/Assets/Blueprinter/Mods/FutureOps/Textures" | Out-Null
$surfaceKeys=@(Get-ChildItem -LiteralPath "$pack/Textures" -Filter '*_MetalGloss.png' -File | ForEach-Object { $_.BaseName -replace '_MetalGloss$','' })
$surfaceRoot=[IO.Path]::GetFullPath("$project/Assets/Blueprinter/Mods/FutureOps/Textures")
# The marine maps already live in CargoTextures. Ship only one copy of that skin.
foreach($surfaceFile in Get-ChildItem -LiteralPath $surfaceRoot -Filter '*.png' -File) {
    if(($surfaceFile.BaseName -replace '_(Albedo|Normal|MetalGloss)$','') -notin $surfaceKeys) {Remove-Item -LiteralPath $surfaceFile.FullName}
}
foreach($surfaceKey in $surfaceKeys) {
    foreach($channel in @('Albedo','Normal','MetalGloss')) {
        Copy-Item -LiteralPath "$pack/Textures/${surfaceKey}_${channel}.png" -Destination $surfaceRoot -Force
    }
}
if(Test-Path "$pack/Textures") {
    New-Item -ItemType Directory -Force -Path "$project/Assets/CargoTextures" | Out-Null
    Copy-Item -Path "$pack/Textures/*.png" -Destination "$project/Assets/CargoTextures" -Force
}
$catalog = Get-Content -LiteralPath "$pack/catalog.json" -Raw | ConvertFrom-Json
foreach ($entry in $catalog.assets) {
    $lo = $entry.bounds_blender[0]; $hi = $entry.bounds_blender[1]
    $entry | Add-Member -MemberType NoteProperty -Name sizeUnity -Value @{ x=$hi[0]-$lo[0]; y=$hi[2]-$lo[2]; z=$hi[1]-$lo[1] } -Force
}
$catalog | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$pack/catalog.json"
Copy-Item -LiteralPath "$pack/catalog.json" -Destination "$project/Assets/Blueprinter/Mods/FutureOps/catalog.json" -Force
Copy-Item -LiteralPath "$pack/Editor/FutureOpsBuilder.cs" -Destination "$project/Assets/Editor/FutureOpsBuilder.cs" -Force
$env:FUTUREOPS_OUT = $pack
$arguments = @('-batchmode', '-projectPath', "`"$project`"", '-executeMethod', 'FutureOpsBuilder.Run', '-logFile', "`"$project/build.log`"")
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path "$pack/validation.txt")) { throw "Unity bundle build failed: $project/build.log" }
Get-Content -LiteralPath "$pack/validation.txt"
