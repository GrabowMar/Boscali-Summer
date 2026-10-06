param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../UnityCheck.Common.ps1"
$proj = Join-Path $env:TEMP ('AvBundleCheck-' + [guid]::NewGuid().ToString('N'))
New-UnityCheckProject $proj $UnityCheckManifest.KitFull -Folders 'Assets/Harness'
Copy-Item "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" "$proj/Assets/"
Copy-Item "$PSScriptRoot/AvBundleUnityCheck.cs", "$PSScriptRoot/NOAvionicsKitChecks.asmdef" "$proj/Assets/Harness/"
Copy-Item "$repo/AvionicsUi/Assets/avionics-ui.bundle" "$proj/"
Copy-GameDlls "$proj/Assets/" $UnityCheckFilter.Managed $UnityCheckFilter.BepInEx
$p = Invoke-UnityCheck $Unity $proj 'AvBundleUnityCheck.Run'
if (-not (Test-Path "$proj/result.txt")) { Get-Content "$proj/check.log" -Tail 80; throw "Unity did not write a result ($proj)" }
$result = Get-Content -LiteralPath "$proj/result.txt" -Raw
Write-Output $result
if ($result -notmatch '^OK') { throw "Unity check reported failure ($proj)" }
if ($p.ExitCode -ne 0) { throw "AvBundle Unity check failed: $($p.ExitCode) ($proj)" }
