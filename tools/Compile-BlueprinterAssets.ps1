param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$Project = 'C:/Users/marci/dev/BlueprinterProject/Blueprinter-Editor',
    [string]$Rip = 'C:/Users/marci/dev/BlueprinterProject/Rip/ExportedProject/Assets',
    [string]$PreviewDir = '',
    [int]$TimeoutMinutes = 90
)
# Builds assets/BoscaliSummer.nobp, the one Blueprinter bundle the mod embeds ("Boscali Summer assets"):
# Vanguard weapons, the HALO paratrooper mount and the container feeder, in a single Unity run of
# ModAssetsBuilder.Run (tools/BlueprinterAssets/Editor). Models come from each part's own Blender script;
# this only imports and packs them. $Project is the set-up Blueprinter editor project (see
# modules/Vanguard/AGENTS.md); a project without a Library folder reimports the game assets first, which is slow.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($path in $Unity, $Project, $Rip) { if (-not (Test-Path -LiteralPath $path)) { throw "Not found: $path" } }

# Editor code stays in the repo; the Unity project sees it through junctions.
$links = @{
    VanguardEditor = 'modules/Vanguard/Assets/Editor'
    AirborneEditor = 'modules/UrbanCombat/Assets/Editor'
    BoscaliEditor  = 'tools/BlueprinterAssets/Editor'
}
foreach ($name in $links.Keys) {
    $link = "$Project/Assets/$name"
    if (-not (Test-Path -LiteralPath $link)) {
        New-Item -ItemType Junction -Path $link -Target (Join-Path $repo $links[$name]) | Out-Null
    }
}
# Blueprinter resolves the game's types from this editor cache and only refills it after the first
# editor tick, which a batch run never reaches on a fresh Library.
foreach ($assembly in 'Assembly-CSharp.dll', 'Assembly-CSharp-firstpass.dll') {
    $cached = "$Project/Library/ScriptAssemblies/$assembly"
    if (-not (Test-Path -LiteralPath $cached)) {
        New-Item -ItemType Directory -Force -Path (Split-Path $cached) | Out-Null
        Copy-Item -LiteralPath "$Project/Packages/nuclearoption/$assembly" -Destination $cached
        (Get-Item -LiteralPath $cached).IsReadOnly = $true
    }
}
$cargo = Join-Path $repo 'assets/FutureOps'
New-Item -ItemType Directory -Force -Path "$Project/Assets/CargoTextures", "$Project/Assets/SourceModels" | Out-Null
Copy-Item -Path "$cargo/Textures/*.png" -Destination "$Project/Assets/CargoTextures" -Force
Copy-Item -LiteralPath "$cargo/Models/CargoShipNative.fbx" -Destination "$Project/Assets/SourceModels" -Force

$output = Join-Path $repo 'assets/BoscaliSummer.nobp'
$env:VG_MODELS = Join-Path $repo 'modules/Vanguard/Assets/Models'
$env:VG_OUT = Join-Path $repo 'modules/Vanguard/Assets'
$env:VG_PREVIEWS = $PreviewDir
$env:AB_MODELS = Join-Path $repo 'modules/UrbanCombat/Assets/Models'
$env:AB_RIP = $Rip
$env:FUTUREOPS_OUT = $cargo
$env:BS_ASSETS_OUT = $output
$env:BS_ASSETS_VERSION = [regex]::Match((Get-Content -Raw -LiteralPath "$repo/BoscaliSummer.csproj"), '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $env:BS_ASSETS_VERSION) { throw 'No <Version> in BoscaliSummer.csproj' }

$log = Join-Path (Split-Path $Project) 'compile-assets.log'
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-projectPath', ('"' + $Project + '"'), '-executeMethod', 'ModAssetsBuilder.Run', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline = $started.AddMinutes($TimeoutMinutes)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -ge $deadline) { $process.Kill(); $process.WaitForExit(); throw "Asset build timed out: $log" }
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $output) -or (Get-Item -LiteralPath $output).LastWriteTimeUtc -lt $started) {
    throw "Asset build failed (exit $($process.ExitCode)): $log"
}
Write-Output ("{0} {1:N1} MB, version {2}" -f $output, ((Get-Item -LiteralPath $output).Length / 1MB), $env:BS_ASSETS_VERSION)
