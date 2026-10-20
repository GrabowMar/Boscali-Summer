param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = '',
    [string]$PackageCache = '',
    [string]$BaselineShader = '',
    [string]$BaselineSeaState = '',
    [switch]$EditorRender
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if (-not $EvidenceDir) { $EvidenceDir = "$repo/.nomodkit/evidence/2026-10-19-ocean-refinement/urp-fixture" }
$fixture = Resolve-UnityCheckDir $EvidenceDir 'BoscaliOceanCheck-'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
# Production shader and pure spectrum sources only; no game DLLs, deployment, or game launch.
$dependencies = [ordered]@{ 'com.unity.render-pipelines.universal' = '14.0.12' }
if ($PackageCache) {
    $PackageCache = (Resolve-Path -LiteralPath $PackageCache).Path
    foreach ($package in @('com.unity.render-pipelines.universal@14.0.12', 'com.unity.render-pipelines.core@14.0.12',
        'com.unity.render-pipelines.universal-config@14.0.10', 'com.unity.shadergraph@14.0.12',
        'com.unity.mathematics@1.2.6', 'com.unity.burst@1.8.21', 'com.unity.searcher@4.9.2', 'com.unity.ugui@1.0.0')) {
        $path = Join-Path $PackageCache $package
        if (-not (Test-Path -LiteralPath "$path/package.json")) { throw "Required installed Unity package missing: $path" }
        $dependencies[$package.Split('@')[0]] = 'file:' + $path.Replace('\', '/')
    }
}
foreach ($module in 'imageconversion', 'jsonserialize', 'imgui') { $dependencies['com.unity.modules.' + $module] = '1.0.0' }
New-UnityCheckProject $fixture (@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4) -Folders 'Assets/Code', 'Assets/Resources'
$sources = @("$repo/modules/Weather/Domain/SeaState.cs", "$repo/modules/Weather/Domain/WeatherMath.cs",
    "$repo/Core/Math/Scalar.cs", "$repo/Core/Math/Deterministic.cs", "$PSScriptRoot/OceanUnityCheck.cs")
$shader = "$repo/modules/Weather/Assets/Source/Ocean.shader"
Copy-Item -LiteralPath $sources -Destination "$fixture/Assets/Code/" -Force
Copy-Item -LiteralPath $shader -Destination "$fixture/Assets/Resources/" -Force
if (-not $BaselineShader) { $BaselineShader = $shader }
if (-not $BaselineSeaState) { $BaselineSeaState = "$repo/modules/Weather/Domain/SeaState.cs" }
# Rename identities only in the scratch project; retain the supplied shader and spectrum behavior.
$baselineText = Get-Content -LiteralPath $BaselineShader -Raw
if ($baselineText -notmatch 'Shader\s+"Boscali/Ocean"') { throw "Baseline is not the production Boscali/Ocean shader: $BaselineShader" }
Set-Content -LiteralPath "$fixture/Assets/Resources/OceanBaseline.shader" -Value ($baselineText -replace 'Shader\s+"Boscali/Ocean"', 'Shader "Boscali/OceanBaseline"')
Set-Content -LiteralPath "$fixture/Assets/Code/SeaStateBaseline.cs" -Value ((Get-Content -LiteralPath $BaselineSeaState -Raw) -replace '\bSeaState\b', 'SeaStateBaseline')
Export-UnityCheckHashes "$fixture/source-manifest.csv" ($sources + $shader + $BaselineShader + $BaselineSeaState | Select-Object -Unique)
Export-UnityCheckHashes "$fixture/input-manifest.csv" (Get-ChildItem -LiteralPath "$fixture/Assets" -Recurse -File | Where-Object Extension -in '.cs', '.shader')
Write-Output "Offline URP ocean fixture (procedural stand-in textures): $fixture"
if ($EditorRender) { Set-Content -LiteralPath "$fixture/editor-render.txt" -Value 'Render in isolated Editor PlayMode; no player packaging.' }
elseif (Test-Path -LiteralPath "$fixture/editor-render.txt") { Remove-Item -LiteralPath "$fixture/editor-render.txt" }
# This fixture exercises GPU shaders and pure setup math; URP's managed jobs suffice, so no Burst native-code build is needed.
$editorResult = if ($EditorRender) { 'result.txt' } else { 'build-result.txt' }
$editor = Invoke-UnityCheck $Unity $fixture 'OceanUnityCheck.Build' -Flags '-batchmode', '--burst-disable-compilation' -LogName 'build.log' -Result $editorResult -TimeoutSeconds 600
Show-UnityCheckResult $fixture -Result $editorResult -LogName 'build.log'
Assert-UnityResult $fixture $editor -Result $editorResult -ExitMessage "Ocean fixture build/render failed: $fixture/build.log"
if (-not $EditorRender) {
    if (Test-Path -LiteralPath "$fixture/result.txt") { Remove-Item -LiteralPath "$fixture/result.txt" }
    $player = Invoke-UnityPlayer "$fixture/Player/OceanCheck.exe" @('-batchmode', '-screen-width', '1280', '-screen-height', '720', '-logFile', ('"' + "$fixture/player.log" + '"')) $fixture -TimeoutSeconds 180
    Show-UnityCheckResult $fixture -LogName 'player.log'
    Assert-UnityResult $fixture $player -ExitMessage "Ocean fixture failed: $fixture/player.log"
}

# Two labelled contact sheets: columns = sea state; rows = baseline/improved at noon/sunset.
Add-Type -AssemblyName System.Drawing
foreach ($view in 'low', 'aerial') {
    $sheet = [Drawing.Bitmap]::new(1920, 1640)
    $graphics = [Drawing.Graphics]::FromImage($sheet)
    $font = [Drawing.Font]::new('Segoe UI', 13)
    try {
        $graphics.Clear([Drawing.Color]::FromArgb(18, 24, 29))
        $graphics.DrawString("OFFLINE URP / $view / production shader + spectrum / PROCEDURAL FIXTURE TEXTURES, SKY AND MESH", $font, [Drawing.Brushes]::White, 12, 8)
        $row = 0
        foreach ($sun in 'noon', 'sunset') {
            foreach ($variant in 'baseline', 'improved') {
                $column = 0
                foreach ($sea in 'calm', 'fresh', 'storm') {
                    $name = "$view-$sea-$sun-$variant"
                    $frame = [Drawing.Image]::FromFile("$fixture/$name.png")
                    try { $graphics.DrawImage($frame, $column * 640, 64 + $row * 394, 640, 360) }
                    finally { $frame.Dispose() }
                    $graphics.DrawString("$variant  |  $sea  |  $sun", $font, [Drawing.Brushes]::White, $column * 640 + 12, 40 + $row * 394)
                    $column++
                }
                $row++
            }
        }
        $sheet.Save("$fixture/contact-$view.png", [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $font.Dispose(); $graphics.Dispose(); $sheet.Dispose() }
}
Write-Output "Evidence: $fixture (fixture checks only; native water textures, mesh, camera stack, and live performance remain unverified)."
