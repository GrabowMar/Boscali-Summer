param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = '',
    [string]$SourceDir = (Join-Path $PSScriptRoot '../modules/Immersion/Assets/Source'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '../modules/Immersion/Assets')
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../tests/UnityCheck.Common.ps1"
$fixture = Resolve-UnityCheckDir $EvidenceDir 'BoscaliPilotModels-'
New-UnityCheckProject $fixture '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}' -Folders 'Assets/Editor', 'Assets/PilotShader/body', 'BundleOutput'
Copy-Item -LiteralPath "$repo/modules/Immersion/Assets/Editor/BuildPilotModels.cs" -Destination "$fixture/Assets/Editor/" -Force
$source = [IO.Path]::GetFullPath($SourceDir)
Copy-Item -LiteralPath "$source/KneeboardInk.shader" -Destination "$fixture/Assets/PilotShader/" -Force
foreach ($suffix in '', '-recon', '-rescue', '-nightops') {
    Copy-Item -LiteralPath "$source/pilot-first-person$suffix.json" -Destination "$fixture/Assets/PilotShader/" -Force
    $textureSuffix = $suffix.Replace('-', '_')
    Copy-Item -LiteralPath "$source/Body/pilot_albedo$textureSuffix.png" -Destination "$fixture/Assets/PilotShader/body/" -Force
}
foreach ($name in 'pilot_normal.png', 'pilot_mask.png', 'kneeboard_albedo.png', 'kneeboard_normal.png', 'kneeboard_mask.png') {
    Copy-Item -LiteralPath "$source/Body/$name" -Destination "$fixture/Assets/PilotShader/body/" -Force
}
Get-ChildItem -LiteralPath "$fixture/Assets" -Recurse -File | Get-FileHash |
    Select-Object Hash, Path | Export-Csv -LiteralPath "$fixture/input-manifest.csv" -NoTypeInformation
Write-Output "Pilot models compiler: $fixture"
$editor = Invoke-UnityCheck $Unity $fixture 'BuildPilotModels.Run' -Flags '-batchmode' -LogName 'build.log' -Result 'build-result.txt' -TimeoutSeconds 900
Show-UnityCheckResult $fixture -Result 'build-result.txt' -LogName 'build.log'
Assert-UnityResult $fixture $editor -Pattern '^PASS ' -Result 'build-result.txt'
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$target = Join-Path ([IO.Path]::GetFullPath($OutputDir)) 'pilot-models.bundle'
Copy-Item -LiteralPath "$fixture/BundleOutput/pilot-models.bundle" -Destination $target -Force
Get-FileHash -LiteralPath $target | Select-Object Hash, Path | Export-Csv -LiteralPath "$fixture/bundle-sha256.csv" -NoTypeInformation
Write-Output "Bundle: $target"
