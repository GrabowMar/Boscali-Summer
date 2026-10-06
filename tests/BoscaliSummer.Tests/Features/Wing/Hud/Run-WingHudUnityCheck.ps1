param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliWingHudCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$ProductionDll,
    [string]$AtlasStyles
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness/Native', 'NOAvionics'
$keepFrozenStyles = $AtlasStyles -and [IO.Path]::GetFullPath($AtlasStyles) -eq [IO.Path]::GetFullPath($PreviewDirectory)
if (-not $keepFrozenStyles) { Copy-AvionicsStyles $PreviewDirectory $AtlasStyles }
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
if (-not $ProductionDll) { $ProductionDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
if ((Resolve-Path -LiteralPath $ProductionDll).Path -ne [IO.Path]::GetFullPath("$PreviewDirectory/Assets/BoscaliSummer.dll")) {
    Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
}
Copy-Item -LiteralPath "$PSScriptRoot/WingHudUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/BoscaliWingHudPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$PSScriptRoot/Native/WingHudNativeAdapter.cs", "$PSScriptRoot/Native/BoscaliWingHudNativeAdapter.asmdef" -Destination "$PreviewDirectory/Assets/Harness/Native/"
foreach ($obsoleteAdapter in @("$PreviewDirectory/Assets/Harness/WingHudNativeAdapter.cs", "$PreviewDirectory/Assets/Harness/WingHudNativeAdapter.cs.meta")) {
    if (Test-Path -LiteralPath $obsoleteAdapter) { Remove-Item -LiteralPath $obsoleteAdapter }
}
Set-Content -LiteralPath "$PreviewDirectory/production-dll.sha256" -Value (Get-FileHash -LiteralPath "$PreviewDirectory/Assets/BoscaliSummer.dll").Hash
Export-UnityCheckHashes "$PreviewDirectory/input-manifest.csv" (@((Get-Item -LiteralPath "$PreviewDirectory/Assets/BoscaliSummer.dll"), (Get-ChildItem -LiteralPath "$PreviewDirectory/NOAvionics" -Filter '*.avss'), (Get-ChildItem -LiteralPath "$PreviewDirectory/Assets/Harness" -File -Recurse)) | ForEach-Object { $_ })
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'WingHudUnityCheck.Run'
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 80
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity Wing HUD check failed: $($process.ExitCode)"
