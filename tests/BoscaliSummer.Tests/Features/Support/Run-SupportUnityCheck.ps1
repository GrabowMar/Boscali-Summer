param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliSupportCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0"}}'
Get-ChildItem -LiteralPath "$repo/Avionics", "$repo/AvionicsUi" -Filter '*.cs' | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/AvionicsUi/avionics.avss" -Destination "$PreviewDirectory/NOAvionics/"
Copy-Item -LiteralPath "$repo/modules/Support/Presentation/SupportPanel.cs", "$PSScriptRoot/SupportUiStubs.cs", "$PSScriptRoot/SupportUnityCheck.cs", "$repo/Framework/Contracts/IObservationSource.cs", "$repo/Framework/Contracts/IProgressionView.cs", "$repo/Framework/Contracts/IBaseDefenseAlarmService.cs" -Destination "$PreviewDirectory/Assets/"
$common = Get-Content -Raw -LiteralPath "$PSScriptRoot/../Command/SettingsUnityStubs.cs"
$common = $common.Replace('public const string Set = "SET";', 'public const string Set = "SET", Ops = "OPS";')
$common = $common.Replace('public class VirtualMFD', 'public static class GameAccess { public static bool MfdAvailable = true; } public static class WingLink { public static bool Available = true; } public class VirtualMFD')
$common = $common.Replace('internal static class MfdNewsTicker { public static void Ensure(Canvas c, MfdLayout.Columns columns, object settings) { } }', '')
Set-Content -LiteralPath "$PreviewDirectory/Assets/CommonStubs.cs" -Value $common

Copy-Item -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core/BepInEx.dll' -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object { $_.Name -match '^(Mono|0Harmony\.dll$)' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$arguments = @('-batchmode', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'SupportUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") { Get-Content "$PreviewDirectory/result.txt" }
else { Get-Content "$PreviewDirectory/check.log" -Tail 60 }
if ($process.ExitCode -ne 0) { throw "Unity support check failed: $($process.ExitCode)" }


