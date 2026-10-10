param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
      [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliCinematicPreview-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.KitFull -Folders 'NOAvionics'
Set-Content -LiteralPath "$PreviewDirectory/Assets/CinematicConsolePreview.asmdef" -Value '{"name":"BoscaliCinematicPreview","references":["Unity.TextMeshPro","Unity.ugui"]}'
Get-ChildItem -LiteralPath "$repo/AvionicsUi", "$repo/AvionicsUi/Pure", "$repo/AvionicsUi/Fui" -Filter '*.cs' |
    Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter '*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination $PreviewDirectory
Get-ChildItem -LiteralPath "$repo/modules/Cinematography/Domain" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Cinematography/Presentation/CinematicConsole.cs", "$repo/modules/Cinematography/Presentation/CinematicOverlay.cs", "$repo/modules/Cinematography/Runtime/CameraPathClearance.cs", "$PSScriptRoot/ConsolePreview.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/PreviewDirector.cs" -Destination "$PreviewDirectory/Assets/CinematicDirector.cs"
Copy-GameDlls "$PreviewDirectory/Assets/" -ManagedExclude $UnityCheckFilter.ManagedWide -BepInExMatch $UnityCheckFilter.BepInEx
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'CinematicConsolePreview.Run' -Flags '-batchmode' -TimeoutSeconds 180
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage 'Cinematic console preview failed.'
Write-Output "Offline editor renders: $PreviewDirectory/renders/console-camera.png, console-effects.png, console-edit.png, cinematic-overlay.png, cinematic-fade.png"
