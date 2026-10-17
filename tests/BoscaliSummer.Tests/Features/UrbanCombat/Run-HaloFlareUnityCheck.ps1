param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$ProjectDir = '',
    [string]$EvidenceDir = '',
    [string]$BlueprinterProject = 'C:/Users/marci/dev/BlueprinterProject/Blueprinter-Editor'
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$fixture = Resolve-UnityCheckDir $ProjectDir 'BoscaliHaloFlareCheck-'
if (-not $EvidenceDir) { $EvidenceDir = $fixture }
$evidence = [IO.Path]::GetFullPath($EvidenceDir)
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$dependencies = [ordered]@{}
foreach ($package in @('com.unity.render-pipelines.universal@14.0.12', 'com.unity.render-pipelines.core@14.0.12',
    'com.unity.render-pipelines.universal-config@14.0.10', 'com.unity.shadergraph@14.0.12',
    'com.unity.mathematics@1.2.6', 'com.unity.burst@1.8.21', 'com.unity.searcher@4.9.2', 'com.unity.ugui@1.0.0')) {
    $path = Join-Path $BlueprinterProject "Library/PackageCache/$package"
    if (-not (Test-Path -LiteralPath "$path/package.json")) { throw "Required installed package missing: $path" }
    $dependencies[$package.Split('@')[0]] = 'file:' + $path.Replace('\', '/')
}
foreach ($module in 'particlesystem', 'imageconversion', 'physics', 'terrain', 'jsonserialize', 'imgui', 'animation', 'audio') {
    $dependencies['com.unity.modules.' + $module] = '1.0.0'
}
New-UnityCheckProject $fixture (@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4) -Folders 'Assets/Resources', 'Assets/HaloModels'
$sources = @("$repo/modules/UrbanCombat/Visuals/HaloFlareVisuals.cs", "$repo/modules/UrbanCombat/Runtime/HaloGlidePlan.cs",
    "$repo/modules/Support/Visuals/CineFx.cs", "$repo/modules/Support/Visuals/SupportParticles.cs",
    "$repo/modules/Support/Visuals/ProceduralVfxTextures.cs", "$PSScriptRoot/HaloFlareUnityCheck.cs")
Copy-Item -LiteralPath $sources -Destination "$fixture/Assets/" -Force
$models = Join-Path $BlueprinterProject 'Assets/Blueprinter/Mods/BoscaliSummer/Airborne'
Copy-Item -LiteralPath "$models/Models", "$models/Materials" -Destination "$fixture/Assets/HaloModels/" -Recurse -Force
Write-Output "Offline HALO flare project: $fixture"
$editor = Invoke-UnityCheck $Unity $fixture 'HaloFlareUnityCheck.Build' -Flags '-batchmode' -LogName 'halo-build.log' -Result 'halo-build-result.txt' -TimeoutSeconds 600
Show-UnityCheckResult $fixture -Result 'halo-build-result.txt' -LogName 'halo-build.log'
Assert-UnityResult $fixture $editor -Result 'halo-build-result.txt' -ExitMessage "HALO fixture build failed: $fixture/halo-build.log"
if ($evidence -ne $fixture) { Copy-Item -LiteralPath "$fixture/halo-measure.txt", "$fixture/halo-build-result.txt", "$fixture/halo-build.log" -Destination $evidence -Force }
$inputs = @($sources | ForEach-Object { Join-Path "$fixture/Assets" ([IO.Path]::GetFileName($_)) }) + @(Get-ChildItem -LiteralPath "$fixture/Assets/HaloModels" -Recurse -File | Select-Object -ExpandProperty FullName)
Export-UnityCheckHashes "$evidence/halo-input-manifest.csv" $inputs
if (Test-Path -LiteralPath "$evidence/halo-result.txt") { Remove-Item -LiteralPath "$evidence/halo-result.txt" }
$player = Invoke-UnityPlayer "$fixture/HaloPlayer/HaloCheck.exe" @('-batchmode', '-screen-width', '1600', '-screen-height', '900', '-haloOutput', ('"' + $evidence + '"'), '-logFile', ('"' + "$evidence/halo-player.log" + '"')) $fixture -TimeoutSeconds 90
Show-UnityCheckResult $evidence -Result 'halo-result.txt' -LogName 'halo-player.log'
Assert-UnityResult $evidence $player -Result 'halo-result.txt' -ExitMessage "HALO fixture failed: $evidence/halo-player.log"
Write-Output "HALO evidence: $evidence"
