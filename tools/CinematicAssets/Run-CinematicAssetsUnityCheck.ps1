param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../UnityCheck.Common.ps1"
$fixture = Resolve-UnityCheckDir $EvidenceDir 'BoscaliCinematicAssetsCheck-'
New-UnityCheckProject $fixture '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}' -Folders 'Assets/Code', 'Assets/Plugins'
foreach ($source in 'modules/Effects/Runtime/CinematicAssets.cs', 'Core/Util/EmbeddedResources.cs') {
    Copy-Item -LiteralPath (Join-Path $repo $source) -Destination "$fixture/Assets/Code/" -Force
}
foreach ($dll in 'BepInEx.dll', '0Harmony.dll', 'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll', 'Mono.Cecil.dll') {
    Copy-Item -LiteralPath "$UnityCheckGame/BepInEx/core/$dll" -Destination "$fixture/Assets/Plugins/" -Force
}
$bundle = "$repo/modules/Effects/Assets/cinematic.bundle"
$embedded = 'cinematic-' + (Get-FileHash -LiteralPath $bundle).Hash + '.bundle'
Copy-Item -LiteralPath $bundle -Destination (Join-Path $fixture $embedded) -Force
Set-Content -LiteralPath "$fixture/Assets/csc.rsp" -Value "-resource:$embedded,BoscaliSummer.Effects.cinematic.bundle"
Copy-Item -LiteralPath "$PSScriptRoot/CinematicAssetsUnityCheck.cs" -Destination "$fixture/Assets/Code/" -Force
Export-UnityCheckHashes "$fixture/inputs.csv" @(Get-ChildItem -LiteralPath "$fixture/Assets" -Recurse -File; Get-Item -LiteralPath (Join-Path $fixture $embedded))
$process = Invoke-UnityCheck $Unity $fixture 'CinematicAssetsUnityCheck.Run' -TimeoutSeconds 300
Write-Output "Evidence: $fixture"
Show-UnityCheckResult $fixture
Assert-UnityResult $fixture $process -ExitMessage "Cinematic assets check failed: $($process.ExitCode)"
