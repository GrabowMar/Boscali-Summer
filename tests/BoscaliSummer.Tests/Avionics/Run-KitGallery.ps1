param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option'
$proj = Join-Path $env:TEMP ('AvKitGallery-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path "$proj/Assets/Harness", "$proj/ProjectSettings", "$proj/Packages" | Out-Null
Set-Content -LiteralPath "$proj/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$proj/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.animation":"1.0.0"}}'
Copy-Item "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" "$proj/Assets/"
Copy-Item "$PSScriptRoot/AvKitGalleryUnityCheck.cs", "$PSScriptRoot/NOAvionicsKitChecks.asmdef" "$proj/Assets/Harness/"
Copy-Item "$repo/AvionicsUi/Assets/avionics-ui.bundle" "$proj/"
Get-ChildItem "$game/NuclearOption_Data/Managed" -Filter '*.dll' | Where-Object { $_.Name -notmatch '^(System|Mono\.|UnityEngine|UnityEditor|mscorlib|netstandard|Unity\.TextMeshPro|Unity\.Timeline|Unity\.VisualScripting)' } | Copy-Item -Destination "$proj/Assets/"
Get-ChildItem "$game/BepInEx/core" -Filter '*.dll' | Where-Object { $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)' } | Copy-Item -Destination "$proj/Assets/"
$p = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-disable-assembly-updater', '-projectPath', "`"$proj`"", '-executeMethod', 'AvKitGalleryUnityCheck.Run', '-logFile', "`"$proj/check.log`"") -WorkingDirectory $proj -WindowStyle Hidden -PassThru
$p.WaitForExit()
if (Test-Path "$proj/result.txt") { Get-Content "$proj/result.txt" } else { Get-Content "$proj/check.log" -Tail 80 }
Write-Output "Gallery: $proj/gallery"
if ($p.ExitCode -ne 0) { throw "Kit gallery failed: $($p.ExitCode) ($proj)" }
