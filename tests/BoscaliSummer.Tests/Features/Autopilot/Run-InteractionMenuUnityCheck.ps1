param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliInteractionMenuCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$Backdrop
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.9","com.unity.modules.audio":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}' -Folders 'NOAvionics'
Get-ChildItem -LiteralPath "$repo/AvionicsUi", "$repo/AvionicsUi/Pure" -Filter '*.cs' | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
# AvDisplayGlass (kit v2's AvLay.Fill) and the rest of the shared kit live one level down.
Get-ChildItem -LiteralPath "$repo/AvionicsUi/Fui" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/modules/Autopilot/Presentation" -Filter 'AceRadial*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/modules/Autopilot/Domain" -Filter 'AceRadial*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/InteractionMenuUnityStubs.cs", "$PSScriptRoot/InteractionMenuUnityCheck.cs", $UnityCheckHarness -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/avionics-ui.bundle"
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles
$extra = @()
if ($Backdrop) { $extra = @('-menuBackdrop', ('"' + (Resolve-Path -LiteralPath $Backdrop).Path + '"')) }
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'InteractionMenuUnityCheck.Run' -Flags '-batchmode' -ExtraArguments $extra
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -Pattern '' -ExitMessage "Unity interaction-menu check failed: $($process.ExitCode)" -FailMessage 'Unity interaction-menu check did not produce a result.'
