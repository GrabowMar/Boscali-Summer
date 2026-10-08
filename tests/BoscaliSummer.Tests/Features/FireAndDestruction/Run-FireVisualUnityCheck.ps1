param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliFireCheck-' + [guid]::NewGuid().ToString('N'))),
    [switch]$Render,
    [switch]$Connected
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.modules.particlesystem":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.audio":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/FireVisualUnityCheck.cs", "$repo/modules/FireAndDestruction/Visuals/FireVisualPool.cs", "$repo/modules/FireAndDestruction/Visuals/FuelDepotSmokePool.cs", "$repo/modules/FireAndDestruction/Domain/FireFrontCell.cs", "$repo/Core/Math/Deterministic.cs", "$repo/modules/FireAndDestruction/Visuals/BurnScarPool.cs", "$repo/modules/FireAndDestruction/Runtime/TerrainProbeCache.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/FireAndDestruction/Assets/forestfire.bundle" -Destination "$PreviewDirectory/"
if ($Render -or $Connected) {
    Copy-Item -LiteralPath "$PSScriptRoot/FirePreview.shader" -Destination "$PreviewDirectory/Assets/"
    $previous = & git -C $repo show HEAD:modules/FireAndDestruction/Visuals/FireVisualPool.cs
    Set-Content -LiteralPath "$PreviewDirectory/Assets/FireVisualPoolBefore.cs" -Value (($previous -join "`n").Replace('FireVisualPool', 'FireVisualPoolBefore'))
    $env:BOSCALI_FIRE_PREVIEW = "$PreviewDirectory/Assets"
    @'
import os, UnityPy
from pathlib import Path
e=UnityPy.load('C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/NuclearOption_Data/resources.assets')
for o in e.objects:
    if o.type.name=='Texture2D' and o.peek_name() in ['smoke_hard','fire_small_e']:
        o.read().image.save(Path(os.environ['BOSCALI_FIRE_PREVIEW'])/(o.peek_name()+'.png'))
'@ | & 'C:/Users/marci/dev/nomodkit/.venv/Scripts/python.exe' -
    if ($LASTEXITCODE -ne 0) { throw 'Native fire texture extraction failed' }
    if ($Connected) {
        $sequence = "$repo/.nomodkit/evidence/forest-front/manager/front-cells.txt"
        if (-not (Test-Path -LiteralPath $sequence)) { throw 'Run-ForestManagerUnityCheck.ps1 must export the production spread sequence first.' }
        if (-not (Get-Content -LiteralPath "$repo/.nomodkit/evidence/forest-front/manager/result.txt" -Raw).StartsWith('PASS:')) { throw 'Production manager check has not passed.' }
        Copy-Item -LiteralPath $sequence -Destination "$PreviewDirectory/front-cells.txt"
    }
    $method = if ($Connected) { 'FireVisualUnityCheck.ConnectedPreview' } else { 'FireVisualUnityCheck.Preview' }
    $process = Invoke-UnityCheck $Unity $PreviewDirectory $method -Flags '-batchmode' -TimeoutSeconds 180
} else {
    $process = Invoke-UnityCheck $Unity $PreviewDirectory 'FireVisualUnityCheck.Run' -Flags '-batchmode', '-nographics' -TimeoutSeconds 180
}
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process
Write-Output "Evidence: $PreviewDirectory/result.txt"
