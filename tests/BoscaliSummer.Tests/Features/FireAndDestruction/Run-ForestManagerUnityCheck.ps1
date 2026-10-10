param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$project="$repo/.nomodkit/evidence/forest-front/manager"
New-UnityCheckProject $project $UnityCheckManifest.MfdFull -Folders 'Assets/Harness'
Copy-GameDlls "$project/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" -Destination "$project/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/ForestManagerUnityCheck.cs" -Destination "$project/Assets/Harness/"
Set-Content -LiteralPath "$project/Assets/Harness/ForestManagerCheck.asmdef" -Value '{"name":"ForestManagerCheck","references":["Unity.TextMeshPro","Unity.ugui"],"includePlatforms":["Editor"]}'
Set-Content -LiteralPath "$project/production-dll.sha256" -Value (Get-FileHash "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll").Hash
$process=Invoke-UnityCheck $Unity $project 'ForestManagerUnityCheck.Run' -TimeoutSeconds 180
Show-UnityCheckResult $project
Assert-UnityResult $project $process
