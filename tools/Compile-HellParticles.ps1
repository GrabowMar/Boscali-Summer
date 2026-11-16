param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = '',
    [string]$OutputDir = (Join-Path $PSScriptRoot '../modules/Spooktober/Assets')
)
$ErrorActionPreference = 'Stop'
$commit = 'b51172a9cac5f7e01cb790c4ab91347d8e821319'
$vendor = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.nomodkit/vendor/Unity-AI-ParticleSystem'))
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
if (-not (Test-Path -LiteralPath "$vendor/.git")) {
    & git clone --filter=blob:none --no-checkout https://github.com/IvanMurzak/Unity-AI-ParticleSystem.git $vendor
    if ($LASTEXITCODE) { throw 'Particle authoring source clone failed' }
    & git -C $vendor sparse-checkout set Unity-Package/Packages/com.ivanmurzak.unity.mcp.particlesystem Unity-Package/Assets/Plugins/NuGet
    & git -C $vendor checkout --detach $commit
    if ($LASTEXITCODE) { throw 'Particle authoring source checkout failed' }
}
if ((& git -C $vendor rev-parse HEAD).Trim() -ne $commit) { throw "Particle authoring source must be pinned to $commit" }
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliHellParticles-' + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force -Path "$fixture/Assets/Editor/RqlParticles", "$fixture/Assets/Plugins/Editor", "$fixture/Assets/HellParticles", "$fixture/ProjectSettings", "$fixture/Packages", "$fixture/BundleOutput" | Out-Null
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}'
$package = "$vendor/Unity-Package/Packages/com.ivanmurzak.unity.mcp.particlesystem"
foreach ($source in Get-ChildItem -LiteralPath "$package/Editor/Scripts/Rql" -Filter '*.cs') {
    Copy-Item -LiteralPath $source.FullName -Destination "$fixture/Assets/Editor/RqlParticles/$($source.Name)" -Force
}
# The linked extension's pure RQL adapters, not its MCP transport or agent plugins.
foreach ($name in @('Rql.Contracts', 'Rql.Execution', 'Rql.Parser', 'System.Text.Json', 'System.Text.Encodings.Web', 'System.Memory', 'System.Buffers', 'System.Runtime.CompilerServices.Unsafe', 'System.Threading.Tasks.Extensions', 'Microsoft.Bcl.AsyncInterfaces', 'Microsoft.Bcl.Memory', 'System.IO.Pipelines', 'System.Numerics.Vectors')) {
    Copy-Item -LiteralPath "$vendor/Unity-Package/Assets/Plugins/NuGet/$name.dll" -Destination "$fixture/Assets/Plugins/Editor/$name.dll" -Force
}
Copy-Item -LiteralPath "$PSScriptRoot/Author-HellParticles.cs" -Destination "$fixture/Assets/Editor/AuthorHellParticles.cs" -Force
$log = "$fixture/unity.log"
$process = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-nographics', '-projectPath', ('"' + $fixture + '"'), '-executeMethod', 'AuthorHellParticles.Run', '-logFile', ('"' + $log + '"')) -WindowStyle Hidden -PassThru
Write-Output "Authoring fixture: $fixture; Unity PID $($process.Id)"
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath "$fixture/build-result.txt")) {
    Get-Content -LiteralPath $log -Tail 65
    throw "Hell particle authoring failed (exit $($process.ExitCode))"
}
$result = Get-Content -LiteralPath "$fixture/build-result.txt" -Raw
if (-not $result.StartsWith('PASS')) { throw $result }
New-Item -ItemType Directory -Force -Path $OutputDir, "$OutputDir/Source/HellParticles" | Out-Null
Copy-Item -LiteralPath "$fixture/BundleOutput/hell-particles.bundle" -Destination "$OutputDir/hell-particles.bundle" -Force
Copy-Item -LiteralPath "$fixture/Assets/HellParticles/HellPortalParticles.prefab" -Destination "$OutputDir/Source/HellParticles/HellPortalParticles.prefab" -Force
Copy-Item -LiteralPath "$fixture/rql-authoring.json" -Destination "$OutputDir/Source/HellParticles/rql-authoring.json" -Force
Copy-Item -LiteralPath "$package/LICENSE" -Destination "$OutputDir/Source/HellParticles/LICENSE.Unity-AI-ParticleSystem.txt" -Force
Write-Output $result
