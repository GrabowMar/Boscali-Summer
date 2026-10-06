param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$ModDll, [string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if (-not $ModDll) { $ModDll = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
$fixture = Resolve-UnityCheckDir $EvidenceDirectory 'BoscaliMaterialOwnership-'
if (Test-Path -LiteralPath (Join-Path $fixture 'Assets/Assembly-CSharp.dll')) {
    throw 'Material ownership uses a minimal isolated project; do not reuse a game-import ENV fixture.'
}
New-UnityCheckProject $fixture '{"dependencies":{}}' -Folders 'Assets/Harness', 'Candidate'
Set-Content -LiteralPath "$fixture/Assets/Harness/MaterialOwnership.asmdef" -Value '{"name":"BoscaliMaterialOwnership","includePlatforms":["Editor"]}'
# Outside Assets: load only the actual Unity-only helper after play mode starts. No game
# Burst/Input runtime initializers, source substitutes, or product assembly rewriting.
Copy-Item -LiteralPath $ModDll -Destination "$fixture/Candidate/BoscaliSummer.dll" -Force
Copy-Item "$PSScriptRoot/ImmersionDisplayOwnershipCheck.cs", "$PSScriptRoot/DisplayMaterialFixture.shader",
    "$PSScriptRoot/NativeDamageFixture.shader", "$PSScriptRoot/NativeLiveryFixture.shader" -Destination "$fixture/Assets/Harness/" -Force
Get-FileHash -LiteralPath "$fixture/Candidate/BoscaliSummer.dll" | Select-Object Hash, Path | Export-Csv -LiteralPath "$fixture/dll-sha256.csv" -NoTypeInformation
Export-UnityCheckHashes "$fixture/input-manifest.csv" (Get-ChildItem -LiteralPath "$fixture/Assets/Harness" -File)
$process = Invoke-UnityCheck $Unity $fixture 'ImmersionDisplayOwnershipCheck.Run' -Flags '-batchmode', '-nographics' -Result 'display-result.txt' `
    -TimeoutSeconds 60 -TimeoutMessage "Material ownership fixture exceeded 60 seconds: $fixture/check.log"
Show-UnityCheckResult $fixture -Result 'display-result.txt'
Write-Output "Evidence: $fixture"
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath "$fixture/display-result.txt") -or
    !(Select-String -LiteralPath "$fixture/display-result.txt" -Pattern '^PASS: ' -Quiet)) {
    throw "Material ownership fixture failed ($($process.ExitCode)): $fixture/check.log"
}
