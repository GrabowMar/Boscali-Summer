param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$ModDll, [string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
if (-not $ModDll) { $ModDll = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
$fixture = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else {
    Join-Path $env:TEMP ('BoscaliMaterialOwnership-' + [guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath (Join-Path $fixture 'Assets/Assembly-CSharp.dll')) {
    throw 'Material ownership uses a minimal isolated project; do not reuse a game-import ENV fixture.'
}
New-Item -ItemType Directory -Force -Path "$fixture/Assets/Harness", "$fixture/ProjectSettings", "$fixture/Packages", "$fixture/Candidate" | Out-Null
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{}}'
Set-Content -LiteralPath "$fixture/Assets/Harness/MaterialOwnership.asmdef" -Value '{"name":"BoscaliMaterialOwnership","includePlatforms":["Editor"]}'
# Outside Assets: load only the actual Unity-only helper after play mode starts. No game
# Burst/Input runtime initializers, source substitutes, or product assembly rewriting.
Copy-Item -LiteralPath $ModDll -Destination "$fixture/Candidate/BoscaliSummer.dll" -Force
Copy-Item "$PSScriptRoot/ImmersionDisplayOwnershipCheck.cs", "$PSScriptRoot/DisplayMaterialFixture.shader",
    "$PSScriptRoot/NativeDamageFixture.shader", "$PSScriptRoot/NativeLiveryFixture.shader" -Destination "$fixture/Assets/Harness/" -Force
Get-FileHash -LiteralPath "$fixture/Candidate/BoscaliSummer.dll" | Select-Object Hash, Path | Export-Csv -LiteralPath "$fixture/dll-sha256.csv" -NoTypeInformation
Get-ChildItem -LiteralPath "$fixture/Assets/Harness" -File | Get-FileHash | Select-Object Hash, Path | Export-Csv -LiteralPath "$fixture/input-manifest.csv" -NoTypeInformation
if (Test-Path -LiteralPath "$fixture/display-result.txt") { Remove-Item -LiteralPath "$fixture/display-result.txt" }
$fixtureArgs = @('-batchmode', '-nographics', '-projectPath', ('"' + $fixture + '"'),
    '-executeMethod', 'ImmersionDisplayOwnershipCheck.Run', '-logFile', ('"' + "$fixture/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $fixtureArgs -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) {
    $process.Kill(); $process.WaitForExit()
    throw "Material ownership fixture exceeded 60 seconds: $fixture/check.log"
}
if (Test-Path -LiteralPath "$fixture/display-result.txt") { Get-Content -LiteralPath "$fixture/display-result.txt" }
else { Get-Content -LiteralPath "$fixture/check.log" -Tail 60 }
Write-Output "Evidence: $fixture"
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath "$fixture/display-result.txt") -or
    !(Select-String -LiteralPath "$fixture/display-result.txt" -Pattern '^PASS: ' -Quiet)) {
    throw "Material ownership fixture failed ($($process.ExitCode)): $fixture/check.log"
}
