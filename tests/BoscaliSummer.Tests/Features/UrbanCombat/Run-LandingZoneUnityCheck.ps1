param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = '',
    [string]$Assembly = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$fixture = Resolve-UnityCheckDir $PreviewDirectory 'BoscaliLandingZoneCheck-'
if (-not $Assembly) { $Assembly = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
New-UnityCheckProject $fixture $UnityCheckManifest.KitFull -Folders 'Assets/Harness', 'NOAvionics'
Copy-GameDlls "$fixture/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath $Assembly -Destination "$fixture/Assets/BoscaliSummer.dll" -Force
Copy-Item -LiteralPath "$PSScriptRoot/LandingZoneUnityCheck.cs", $UnityCheckHarness,
    "$PSScriptRoot/../Command/BoscaliStockPreview.asmdef" -Destination "$fixture/Assets/Harness/" -Force
Copy-AvionicsStyles $fixture ''
Export-UnityCheckHashes "$fixture/input-manifest.csv" @($Assembly, "$PSScriptRoot/LandingZoneUnityCheck.cs", "$repo/modules/UrbanCombat/Runtime/LandingZoneMarker.cs", "$repo/Core/Fx/LandingSmoke.cs")
Write-Output "Offline landing-zone marker/smoke fixture: $fixture"
$process = Invoke-UnityCheck $Unity $fixture 'LandingZoneUnityCheck.Run' -TimeoutSeconds 600
Show-UnityCheckResult $fixture
Assert-UnityResult $fixture $process -ExitMessage "Landing-zone check failed: $fixture/check.log"
Write-Output "Landing-zone renders and evidence: $fixture"
