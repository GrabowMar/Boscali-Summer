param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$ProjectDir = (Join-Path $env:APPDATA 'nomodkit/cache/panels/proj-sqd'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '../../.nomodkit/portrait-rework/unity'),
    [string]$DllPath = (Join-Path $PSScriptRoot '../../bin/Release/netstandard2.1/BoscaliSummer.dll'),
    [switch]$SqdUi,
    [switch]$PortraitPanelsOnly
)
$ErrorActionPreference = 'Stop'
if ($PortraitPanelsOnly) { $SqdUi = $true }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$ProjectDir = [System.IO.Path]::GetFullPath($ProjectDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
if (-not (Test-Path -LiteralPath (Join-Path $ProjectDir 'Assets/Harness/BoscaliPresentationPreview.asmdef'))) {
    throw 'Prepare the existing SQD project with nomod panels render --mod boscalisummer --group sqd first.'
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -LiteralPath $DllPath -Destination (Join-Path $ProjectDir 'Assets/BoscaliSummer.dll') -Force
$atlasHash = (Get-FileHash -LiteralPath (Join-Path $repo 'modules/Wing/Assets/Pilots/layers.png') -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $ProjectDir 'portrait-atlas-sha256.txt') -Value $atlasHash -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PortraitUnityCheck.cs') -Destination (Join-Path $ProjectDir 'Assets/Harness/PortraitUnityCheck.cs') -Force
$fixturePath = Join-Path $ProjectDir 'Assets/Harness/PresentationUnityCheck.cs'
$fixture = Get-Content -LiteralPath (Join-Path $repo 'tests/BoscaliSummer.Tests/Features/Progression/PresentationUnityCheck.cs') -Raw
if ($SqdUi) {
    $original = 'FixtureSprite(72, 90, new Color(.10f, .18f, .16f), new Color(.02f, .05f, .05f))'
    if (-not $fixture.Contains($original)) { throw 'SQD fixture portrait injection changed; review scratch injection.' }
    $fixture = $fixture.Replace($original, 'PortraitUnityCheck.Pilot()')
    # The current editor has no separately stored editor section. Seed its
    # existing fields and message directly in the scratch copy.
    $oldEditorCaption = 'Call(Get(panel, "studioEditorSection"), "SetCaption", "DAYMAN");'
    if (-not $fixture.Contains($oldEditorCaption)) { throw 'Studio editor seed adapter changed.' }
    $fixture = $fixture.Replace($oldEditorCaption, '// Editor uses the current production card structure.')
    $oldMessageVisibility = 'Call(Get(panel, "studioMessageText"), "SetShown", true);'
    if (-not $fixture.Contains($oldMessageVisibility)) { throw 'Studio message seed adapter changed.' }
    $fixture = $fixture.Replace($oldMessageVisibility, '// The message starts visible; its content is seeded below.')
    # Exercise the longest new clothing label in the compact production stepper.
    $studioSeed = 'SeedStudio(panel, portrait, crest);'
    if (-not $fixture.Contains($studioSeed)) { throw 'Studio outfit label seed changed.' }
    $fixture = $fixture.Replace($studioSeed, $studioSeed + "`r`n        " +
        '((TMP_Text)Get(Get(panel, "studioSuit"), "value")).text = "PALA HIGH-ALT FLIGHT";')
    if ($PortraitPanelsOnly) {
        # Build all production pages, but render only the portrait consumers.
        # Leave the broader fixture's score/skill failures in its own run.
        $start = $fixture.IndexOf('        // Host thresholds are cumulative')
        $end = $fixture.IndexOf('        // ---- ACES', $start)
        if ($start -lt 0 -or $end -le $start) { throw 'Portrait panel isolation marker changed.' }
        $fixture = $fixture.Substring(0, $start) +
            "        // Portrait panels: score/skill behavior belongs to the full SQD fixture.`r`n`r`n" +
            $fixture.Substring($end)
        $start = $fixture.IndexOf('        // ---- PLANE')
        $end = $fixture.IndexOf('        Object.DestroyImmediate(canvasObject);', $start)
        if ($start -lt 0 -or $end -le $start) { throw 'Plane isolation marker changed.' }
        $fixture = $fixture.Substring(0, $start) + $fixture.Substring($end)
        $aceRun = 'if (!eventAlertOnly) RenderAceHunt();'
        if (-not $fixture.Contains($aceRun)) { throw 'ACE HUD isolation marker changed.' }
        $fixture = $fixture.Replace($aceRun, '// ACE HUD text fitting belongs to the broader fixture.')
        Write-Output 'Portrait panel run: PILOT, ACES and STUDIO; broader score, skill, plane and ACE HUD text checks excluded.'
    }
}
Set-Content -LiteralPath $fixturePath -Value $fixture -Encoding UTF8
$method = if ($SqdUi) { 'PortraitUnityCheck.RunSqd' } else { 'PortraitUnityCheck.Run' }
$log = Join-Path $OutputDir 'check.log'
$report = Join-Path $ProjectDir 'portrait-result.txt'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$arguments = @('-batchmode', '-disable-assembly-updater', '-projectPath', ('"' + $ProjectDir + '"'),
    '-executeMethod', $method, '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $ProjectDir -WindowStyle Hidden -PassThru
$process.WaitForExit()
if (Test-Path -LiteralPath $report) {
    $result = Get-Content -LiteralPath $report -Raw
    Copy-Item -LiteralPath $report -Destination (Join-Path $OutputDir 'portrait-result.txt') -Force
    Write-Output $result.Trim()
} else { Get-Content -LiteralPath $log -Tail 60; throw 'Unity exited without a portrait result.' }
if ($process.ExitCode -ne 0 -or $result -notmatch '\APASS:') { throw "Portrait Unity check failed: $($process.ExitCode)" }
Copy-Item -LiteralPath (Join-Path $ProjectDir 'portrait-runtime-roles.png') -Destination $OutputDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir 'portrait-runtime-gear.png') -Destination $OutputDir -Force
if ($SqdUi) {
    $uiResult = Get-Content -LiteralPath (Join-Path $ProjectDir 'result.txt') -Raw
    if ($uiResult -notmatch '\APASS:') { throw 'SQD render did not pass with actual production portraits.' }
    Copy-Item -LiteralPath (Join-Path $ProjectDir 'result.txt') -Destination (Join-Path $OutputDir 'sqd-result.txt') -Force
    Get-ChildItem -LiteralPath $ProjectDir -File -Filter '*.png' | Where-Object {
        ($_.Name -like 'sqd-*' -or $_.Name -like 'ace-hunt-*') -and
        (-not $PortraitPanelsOnly -or $_.Name -match '^sqd-(pilot|wings|studio)(-|empty-)')
    } |
        Copy-Item -Destination $OutputDir -Force
    Write-Output $uiResult.Trim()
}
$hash = (Get-FileHash -LiteralPath (Join-Path $ProjectDir 'Assets/BoscaliSummer.dll') -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $OutputDir 'production-dll-sha256.txt') -Value $hash -Encoding ASCII
Write-Output "Portrait evidence: $OutputDir"
