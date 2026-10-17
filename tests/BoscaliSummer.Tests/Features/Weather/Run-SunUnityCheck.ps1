param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = '',
    [string]$PackageCache = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if (-not $EvidenceDir) { $EvidenceDir = "$repo/.nomodkit/evidence/2026-10-17-sun-remake/urp-fixture" }
$fixture = Resolve-UnityCheckDir $EvidenceDir 'BoscaliSunCheck-'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
# No game DLLs, mod deployment, game launch, or runtime initializers.
# Use Unity's registry by default; -PackageCache can reuse an existing offline package cache.
$dependencies = [ordered]@{ 'com.unity.render-pipelines.universal' = '14.0.12' }
if ($PackageCache) {
    foreach ($package in @('com.unity.render-pipelines.universal@14.0.12', 'com.unity.render-pipelines.core@14.0.12',
        'com.unity.render-pipelines.universal-config@14.0.10', 'com.unity.shadergraph@14.0.12',
        'com.unity.mathematics@1.2.6', 'com.unity.burst@1.8.21', 'com.unity.searcher@4.9.2', 'com.unity.ugui@1.0.0')) {
        $path = Join-Path $PackageCache $package
        if (-not (Test-Path -LiteralPath "$path/package.json")) { throw "Required installed Unity package missing: $path" }
        $dependencies[$package.Split('@')[0]] = 'file:' + $path.Replace('\', '/')
    }
}
foreach ($module in 'imageconversion', 'physics', 'jsonserialize', 'imgui') {
    $dependencies['com.unity.modules.' + $module] = '1.0.0'
}
New-UnityCheckProject $fixture (@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4) -Folders 'Assets/Code', 'Assets/Resources'
$sources = @(
    "$repo/modules/Weather/Visuals/SunShaftsPass.cs", "$repo/modules/Weather/Visuals/SunDisc.cs",
    "$repo/modules/Immersion/Visuals/SunGlare.cs", "$repo/Core/Math/SunDiscMath.cs", "$repo/Core/Math/Scalar.cs",
    "$PSScriptRoot/SunUnityCheck.cs", "$PSScriptRoot/SunUnityCheckStubs.cs"
)
Copy-Item -LiteralPath $sources -Destination "$fixture/Assets/Code/" -Force
$shaders = @("$repo/modules/Weather/Assets/Source/SunShafts.shader", "$repo/modules/Weather/Assets/Source/SunDisc.shader")
Copy-Item -LiteralPath $shaders -Destination "$fixture/Assets/Resources/" -Force
Export-UnityCheckHashes "$fixture/source-manifest.csv" ($sources + $shaders)
Export-UnityCheckHashes "$fixture/input-manifest.csv" (Get-ChildItem -LiteralPath "$fixture/Assets" -Recurse -File | Where-Object Extension -in '.cs', '.shader')
Write-Output "Offline URP sun fixture: $fixture"
$editor = Invoke-UnityCheck $Unity $fixture 'SunUnityCheck.Build' -Flags '-batchmode' -LogName 'build.log' -Result 'build-result.txt' -TimeoutSeconds 600
Show-UnityCheckResult $fixture -Result 'build-result.txt' -LogName 'build.log'
Assert-UnityResult $fixture $editor -Result 'build-result.txt' -ExitMessage "Sun fixture build failed: $fixture/build.log"
if (Test-Path -LiteralPath "$fixture/result.txt") { Remove-Item -LiteralPath "$fixture/result.txt" }
# StandardRequest executes the real URP camera stack inside the batch player.
$player = Invoke-UnityPlayer "$fixture/Player/SunCheck.exe" @('-batchmode', '-screen-width', '1600', '-screen-height', '900', '-logFile', ('"' + "$fixture/player.log" + '"')) $fixture -TimeoutSeconds 180
Show-UnityCheckResult $fixture -LogName 'player.log'
Assert-UnityResult $fixture $player -ExitMessage "Sun fixture failed: $fixture/player.log"
Write-Output "Evidence: $fixture"
