param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = '',
    [string]$ModDll = '',
    [string]$SourceSnapshot = ''
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliPilotCheck-' + [guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath "$fixture/Assets/Assembly-CSharp.dll") { throw 'Use an isolated pilot fixture without game runtime initializers.' }
New-Item -ItemType Directory -Force -Path "$fixture/Assets/Resources", "$fixture/Assets/Code", "$fixture/Packages", "$fixture/ProjectSettings" | Out-Null
$editorVersion = if ($Unity -like '*62f3*') { '2022.3.62f3' } else { '2022.3.62f2' }
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: $editorVersion"
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.modules.animation":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0"}}'
# Compile unmodified production source with test-only native type stubs. Do not import
# the game assembly or its Burst/Input bootstrap. Source hashes identify the closure.
foreach ($source in @('modules/Immersion/Visuals/CockpitPilotRig.cs', 'modules/Immersion/Domain/PilotPoseMath.cs',
    'modules/Immersion/Runtime/CockpitPilot.cs', 'Core/Lifecycle/ISceneService.cs',
    'modules/Immersion/Visuals/PilotReflection.cs', 'modules/Immersion/Visuals/PilotShaderBundle.cs',
    'modules/Weather/Visuals/CanopyGlassResolver.cs', 'modules/Weather/Domain/CanopyScoring.cs',
    'modules/Weather/Visuals/CanopyGlassView.cs', 'Core/Contracts/ICanopyGlassView.cs',
    'Core/Contracts/FxBudget.cs', 'Core/Contracts/IClientEffect.cs')) {
    $path = Join-Path $repo $source
    if ($SourceSnapshot) {
        $snapshotPath = Join-Path $SourceSnapshot ([IO.Path]::GetFileName($source))
        if (Test-Path -LiteralPath $snapshotPath) { $path = $snapshotPath }
    }
    if (-not (Test-Path -LiteralPath $path)) { throw "Required pilot implementation missing: $path" }
    Copy-Item -LiteralPath $path -Destination "$fixture/Assets/Code/" -Force
}
Copy-Item -Path "$repo/Core/Fx/*.cs" -Destination "$fixture/Assets/Code/" -Force
$bundle = Join-Path $repo 'modules/Immersion/Assets/pilot.bundle'
if (-not (Test-Path -LiteralPath $bundle)) { throw "Compile the pilot shader bundle first: $bundle" }
# Unity's normal C# response file embeds the production bundle under its real resource
# name, allowing the unchanged bounded loader to run instead of a fixture substitute.
Copy-Item -LiteralPath $bundle -Destination "$fixture/pilot.bundle" -Force
# Unity does not track external response-file resource bytes as a C# dependency.
# A hash-named snapshot changes csc.rsp when the bundle changes, invalidating warm
# script assemblies while keeping the production manifest-resource name intact.
$pilotBundleHash = (Get-FileHash -LiteralPath $bundle).Hash
$pilotEmbeddedFile = "pilot-$pilotBundleHash.bundle"
Copy-Item -LiteralPath $bundle -Destination (Join-Path $fixture $pilotEmbeddedFile) -Force
Set-Content -LiteralPath "$fixture/Assets/csc.rsp" -Value "-resource:$pilotEmbeddedFile,BoscaliSummer.Immersion.pilot.bundle"
Copy-Item -LiteralPath "$PSScriptRoot/PilotUnityCheck.cs" -Destination "$fixture/Assets/Code/" -Force
Copy-Item -LiteralPath "$PSScriptRoot/PilotRuntimeFixtureStubs.cs" -Destination "$fixture/Assets/Code/" -Force
foreach ($shader in 'PilotBody.shader', 'PilotCanopyReflection.shader') {
    Copy-Item -LiteralPath "$repo/modules/Immersion/Assets/Source/$shader" -Destination "$fixture/Assets/Resources/" -Force
}
if (-not $ModDll) { $ModDll = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
if (Test-Path -LiteralPath $ModDll) {
    Get-FileHash -LiteralPath $ModDll | Select-Object Hash, Path | Export-Csv -LiteralPath "$fixture/dll-sha256.csv" -NoTypeInformation
}
$pilotInputHashes = @(Get-ChildItem -LiteralPath "$fixture/Assets" -Recurse -File | Get-FileHash) +
    @(Get-FileHash -LiteralPath "$fixture/pilot.bundle", (Join-Path $fixture $pilotEmbeddedFile), "$fixture/Packages/manifest.json", "$fixture/ProjectSettings/ProjectVersion.txt")
$pilotInputHashes | Select-Object Hash, Path | Export-Csv -LiteralPath "$fixture/input-manifest.csv" -NoTypeInformation
foreach ($oldResult in 'build-result.txt', 'result.txt') {
    $oldPath = Join-Path $fixture $oldResult
    if (Test-Path -LiteralPath $oldPath) { Remove-Item -LiteralPath $oldPath }
}
Write-Output "Pilot URP fixture: $fixture"
$arguments = @('-batchmode', '-projectPath', ('"' + $fixture + '"'), '-executeMethod', 'PilotUnityCheck.Build', '-logFile', ('"' + "$fixture/build.log" + '"'))
$editor = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(12)
while (-not $editor.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -ge $deadline) { $editor.Kill(); $editor.WaitForExit(); throw "Pilot fixture build timed out: $fixture/build.log" }
}
if ($editor.ExitCode -ne 0 -or -not (Test-Path -LiteralPath "$fixture/Player/PilotCheck.exe") -or
    -not (Test-Path -LiteralPath "$fixture/build-result.txt") -or
    -not (Select-String -LiteralPath "$fixture/build-result.txt" -Pattern '^PASS ' -Quiet)) {
    if (Test-Path -LiteralPath "$fixture/build-result.txt") { Get-Content -LiteralPath "$fixture/build-result.txt" }
    throw "Pilot URP build failed ($($editor.ExitCode)): $fixture/build.log"
}
$arguments = @('-batchmode', '-screen-width', '1920', '-screen-height', '1080', '-logFile', ('"' + "$fixture/player.log" + '"'))
$player = Start-Process -FilePath "$fixture/Player/PilotCheck.exe" -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(4)
while (-not $player.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -ge $deadline) { $player.Kill(); $player.WaitForExit(); throw "Pilot fixture timed out: $fixture/player.log" }
}
$resultPath = Join-Path $fixture 'result.txt'
if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
Write-Output "Evidence: $fixture"
if ($player.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or
    -not (Select-String -LiteralPath $resultPath -Pattern '^PASS: ' -Quiet)) {
    throw "Pilot URP fixture failed ($($player.ExitCode)): $fixture/player.log"
}
