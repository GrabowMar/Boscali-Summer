param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$ProjectDirectory = '',
    [string]$OutputDirectory = '',
    [switch]$DesktopShell
)
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ProjectDirectory) { $ProjectDirectory = Join-Path $repoDirectory '.nomodkit/sa74-mushroom-shader' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoDirectory 'modules/Vanguard/Assets' }
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
foreach ($folder in 'Assets/Mushroom', 'Assets/Editor', 'Packages', 'ProjectSettings', 'BundleOutput') {
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectDirectory $folder) | Out-Null
}
$editorVersion = Split-Path -Leaf (Split-Path -Parent (Split-Path -Parent $Unity))
Set-Content -LiteralPath "$ProjectDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: $editorVersion"
Set-Content -LiteralPath "$ProjectDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Copy-Item -LiteralPath "$repoDirectory/modules/Vanguard/Assets/Source/Sa74Mushroom.shader" -Destination "$ProjectDirectory/Assets/Mushroom/Sa74Mushroom.shader" -Force
Copy-Item -LiteralPath "$repoDirectory/modules/Weather/Visuals/CloudNoise3D.cs" -Destination "$ProjectDirectory/Assets/Editor/CloudNoise3D.cs" -Force
Copy-Item -LiteralPath "$repoDirectory/Core/Math/Scalar.cs" -Destination "$ProjectDirectory/Assets/Editor/Scalar.cs" -Force
Copy-Item -LiteralPath "$repoDirectory/modules/Vanguard/Domain/Sa74BlastShape.cs" -Destination "$ProjectDirectory/Assets/Editor/Sa74BlastShape.cs" -Force
Copy-Item -LiteralPath "$repoDirectory/tools/Sa74Mushroom/Editor/BuildSa74Mushroom.cs" -Destination "$ProjectDirectory/Assets/Editor/BuildSa74Mushroom.cs" -Force
Copy-Item -LiteralPath "$repoDirectory/tools/Sa74Mushroom/Editor/ScreenFixture.shader" -Destination "$ProjectDirectory/Assets/Mushroom/ScreenFixture.shader" -Force
$env:SA74_REVIEW = Join-Path $repoDirectory 'reports/sa74n/mushroom'
Set-Content -LiteralPath "$ProjectDirectory/review-path.txt" -Value $env:SA74_REVIEW
$resultPath = Join-Path $ProjectDirectory 'result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = @('-batchmode', '-force-d3d11', '-projectPath', $ProjectDirectory, '-executeMethod', 'BuildSa74Mushroom.Run', '-logFile', "$ProjectDirectory/build.log")
$compilerExitCode = 0
if ($DesktopShell) {
    # Some unattended process trees stall Unity's ILPP named pipe; use the interactive desktop's shell instead.
    $shell = New-Object -ComObject Shell.Application
    $quotedArguments = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $shell.ShellExecute($Unity, $quotedArguments, $ProjectDirectory, 'open', 0)
    $deadline = (Get-Date).AddMinutes(8)
    while (-not (Test-Path -LiteralPath $resultPath) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 5 }
} else {
    & $Unity @arguments | Out-Host
    $compilerExitCode = $LASTEXITCODE
}
if ($compilerExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or (Get-Content -LiteralPath $resultPath -Raw) -notmatch '\APASS:') {
    throw "SA-74N mushroom shader build failed: $ProjectDirectory/build.log"
}
$bundlePath = Join-Path $ProjectDirectory 'BundleOutput/sa74-mushroom.bundle'
if (-not (Test-Path -LiteralPath $bundlePath) -or (Get-Item -LiteralPath $bundlePath).Length -gt 4MB) { throw 'SA-74N mushroom bundle missing or exceeds4MiB.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $OutputDirectory 'sa74-mushroom.bundle') -Force
Get-Content -LiteralPath $resultPath
Write-Output "Mushroom bundle: $(Join-Path $OutputDirectory 'sa74-mushroom.bundle')"
