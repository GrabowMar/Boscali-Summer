param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliSpookCheck-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.KitFull -Folders 'NOAvionics'
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter '*.cs' -Recurse | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/modules/Spooktober/Domain", "$repo/modules/Spooktober/Presentation" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/Core/Contracts/ISetExtras.cs", "$PSScriptRoot/SpookUnityCheck.cs", "$PSScriptRoot/SpookUnityStubs.cs", $UnityCheckHarness -Destination "$PreviewDirectory/Assets/"
Copy-AvionicsStyles $PreviewDirectory
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/avionics-ui.bundle"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'SpookUnityCheck.Run' -TimeoutSeconds 240
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity Spooktober check failed: $($process.ExitCode)"
