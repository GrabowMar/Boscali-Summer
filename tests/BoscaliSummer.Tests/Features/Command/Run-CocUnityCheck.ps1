param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliCocCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0"}}'
Get-ChildItem -LiteralPath "$repo/Avionics", "$repo/AvionicsUi" -Filter '*.cs' | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/AvionicsUi/avionics.avss" -Destination "$PreviewDirectory/NOAvionics/"
Copy-Item -LiteralPath `
    "$repo/modules/Command/Presentation/MapUi/MfdGlyph.cs", `
    "$repo/modules/Command/Presentation/MapUi/MfdLayout.cs", `
    "$repo/modules/Command/Presentation/StrMfdPanel.cs", `
    "$repo/modules/Command/Presentation/StrMfdPanel.Coc.cs", `
    "$repo/modules/Command/Presentation/StrMfdPanel.Cmd.cs", `
    "$repo/modules/Command/Domain/CommandRosterOrder.cs", `
    "$repo/modules/Command/Domain/TacticalTheaterState.cs", `
    "$repo/modules/Command/Domain/TheaterReadout.cs", `
    "$repo/modules/Command/Domain/SortieClassifier.cs", `
    "$repo/Framework/Contracts/IHighCommandView.cs", `
    "$repo/Framework/Contracts/CommanderLogLine.cs", `
    "$repo/Framework/Contracts/IBaseDefenseAlarmService.cs", `
    "$repo/Framework/Contracts/ITheaterPriorityView.cs", `
    "$repo/Framework/Contracts/ITheaterLogisticsView.cs", `
    "$PSScriptRoot/SettingsUnityStubs.cs", `
    "$PSScriptRoot/CocUnityCheck.cs" `
    -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object { $_.Name -match '^(BepInEx|Mono|0Harmony)' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$arguments = @('-batchmode', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'CocUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") { Get-Content "$PreviewDirectory/result.txt" }
else { Get-Content "$PreviewDirectory/check.log" -Tail 80 }
if ($process.ExitCode -ne 0) { throw "Unity COC check failed: $($process.ExitCode)" }
