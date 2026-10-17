param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = '',
    [string]$PackageCache = 'C:/Users/marci/dev/BlueprinterProject/Blueprinter-Editor/Library/PackageCache',
    [string]$ChaffSource = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$fixture = Resolve-UnityCheckDir $EvidenceDir 'BoscaliChaffCheck-'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
# Reuse the installed URP packages; the fixture does not install a mod, open the
# game, or fetch packages. Supply -PackageCache for another existing URP cache.
$dependencies = [ordered]@{}
foreach ($package in @('com.unity.render-pipelines.universal@14.0.12', 'com.unity.render-pipelines.core@14.0.12',
    'com.unity.render-pipelines.universal-config@14.0.10', 'com.unity.shadergraph@14.0.12',
    'com.unity.mathematics@1.2.6', 'com.unity.burst@1.8.21', 'com.unity.searcher@4.9.2', 'com.unity.ugui@1.0.0')) {
    $path = Join-Path $PackageCache $package
    if (-not (Test-Path -LiteralPath "$path/package.json")) { throw "Required installed Unity package missing: $path" }
    $dependencies[$package.Split('@')[0]] = 'file:' + $path.Replace('\', '/')
}
foreach ($module in 'particlesystem', 'imageconversion', 'physics', 'terrain', 'jsonserialize', 'imgui') {
    $dependencies['com.unity.modules.' + $module] = '1.0.0'
}
New-UnityCheckProject $fixture (@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4) -Folders 'Assets/Resources'
if (-not $ChaffSource) { $ChaffSource = "$repo/Core/Fx/ChaffVisuals.cs" }
Copy-Item -LiteralPath $ChaffSource, "$PSScriptRoot/ChaffVisualUnityCheck.cs" -Destination "$fixture/Assets/" -Force
Export-UnityCheckHashes "$fixture/input-manifest.csv" (Get-ChildItem -LiteralPath "$fixture/Assets" -Filter '*.cs')
Write-Output "Offline URP chaff fixture: $fixture"
$editor = Invoke-UnityCheck $Unity $fixture 'ChaffVisualUnityCheck.Build' -Flags '-batchmode' -LogName 'build.log' -Result 'build-result.txt' -TimeoutSeconds 600
Show-UnityCheckResult $fixture -Result 'build-result.txt' -LogName 'build.log'
Assert-UnityResult $fixture $editor -Result 'build-result.txt' -ExitMessage "Chaff fixture build failed: $fixture/build.log"
if (Test-Path -LiteralPath "$fixture/result.txt") { Remove-Item -LiteralPath "$fixture/result.txt" }
$player = Invoke-UnityPlayer "$fixture/Player/ChaffCheck.exe" @('-batchmode', '-screen-width', '1600', '-screen-height', '900', '-logFile', ('"' + "$fixture/player.log" + '"')) $fixture -TimeoutSeconds 90
Show-UnityCheckResult $fixture -LogName 'player.log'
Assert-UnityResult $fixture $player -ExitMessage "Chaff fixture failed: $fixture/player.log"
Write-Output "Evidence: $fixture"
