param(
    [Parameter(Mandatory=$true)][string]$Group,
    [Parameter(Mandatory=$true)][string]$AtlasOut,
    [string]$AtlasStyles,
    [Parameter(Mandatory=$true)][string]$ProjectDir,
    [switch]$Watch
)
$ErrorActionPreference = 'Stop'
if ($Watch) { throw 'These validated fixtures are one-shot. Use nomod panels render.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
# nomod runs each script from its script directory. Resolve CLI output paths
# against the repository so a relative --out still produces the shared atlas.
if (-not [IO.Path]::IsPathRooted($AtlasOut)) { $AtlasOut = Join-Path $repo $AtlasOut }
$AtlasOut = [IO.Path]::GetFullPath($AtlasOut)
$cases = @{
    stock = @('Command/Run-StockMfdUnityCheck.ps1', @{})
    settings = @('Command/Run-SettingsUnityCheck.ps1', @{})
    sqd = @('Progression/Run-PresentationUnityCheck.ps1', @{ SqdOnly = $true })
    events = @('Events/Run-EventsUnityCheck.ps1', @{})
    com = @('Comms/Run-CommsUnityCheck.ps1', @{})
    env = @('Weather/Run-WeatherEnvUnityCheck.ps1', @{})
    mission = @('Command/Run-MissionDeskUnityCheck.ps1', @{})
    radial = @('Autopilot/Run-InteractionMenuUnityCheck.ps1', @{})
    rail = @('Command/Run-RailUnityCheck.ps1', @{})
    hudlog = @('Hud/Run-HudLogUnityCheck.ps1', @{})
    wingradio = @('Wing/Run-WingRadioUnityCheck.ps1', @{})
    radio = @('Wing/Run-WingRadioUnityCheck.ps1', @{ RadioOnly = $true })
    winghud = @('Wing/Hud/Run-WingHudUnityCheck.ps1', @{})
    'comenv-overlays' = @('Comms/Run-ComEnvOverlayUnityCheck.ps1', @{})
    relief = @('Command/Run-ReliefUnityCheck.ps1', @{})
    'relief-naval' = @('Command/Run-ReliefUnityCheck.ps1', @{ AssetName = 'terrain_naval_map' })
    'event-alert' = @('Progression/Run-PresentationUnityCheck.ps1', @{ EventAlertOnly = $true })
    'map-overlays' = @('Command/Run-MapOverlayUnityCheck.ps1', @{ NonWingOnly = $true })
    'map-chrome' = @('Command/Run-MapChromeUnityCheck.ps1', @{})
}
if (-not $cases.ContainsKey($Group)) { throw "Unknown group: $Group" }
$runner = Join-Path $repo ('tests/BoscaliSummer.Tests/Features/' + $cases[$Group][0])
$params = @{ PreviewDirectory = $ProjectDir }
foreach ($key in $cases[$Group][1].Keys) { $params[$key] = $cases[$Group][1][$key] }
$styleApplied = $false
if ((Get-Command -Name $runner).Parameters.ContainsKey('AtlasStyles') -and $AtlasStyles) {
    $params['AtlasStyles'] = $AtlasStyles
    $styleApplied = $true
}
$dest = Join-Path $AtlasOut $Group
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$started = [DateTime]::UtcNow
$manifest = @{ status='running'; time=$started.ToString('o'); group=$Group; panels=@(); styleOverridesApplied=$styleApplied }
try {
    & $runner @params
    $result = Get-Content -LiteralPath (Join-Path $ProjectDir 'result.txt') -Raw
    if ($result -notmatch '\APASS:') { throw "Fixture did not pass: $result" }
    Add-Type -AssemblyName System.Drawing
    $panels = @()
    $images = @(Get-ChildItem -LiteralPath $ProjectDir -File -Filter '*.png')
    foreach ($captureFolder in @('renders', 'com', 'env')) {
        $renders = Join-Path $ProjectDir $captureFolder
        if (Test-Path -LiteralPath $renders) { $images += Get-ChildItem -LiteralPath $renders -File -Filter '*.png' }
    }
    foreach ($file in $images | Sort-Object Name) {
        if ($file.LastWriteTimeUtc -lt $started.AddSeconds(-2)) { continue }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $dest $file.Name) -Force
        $bitmap = [System.Drawing.Image]::FromFile($file.FullName)
        try { $panels += @{ name=$file.BaseName; file=$file.Name; width=$bitmap.Width; height=$bitmap.Height } }
        finally { $bitmap.Dispose() }
    }
    if ($panels.Count -eq 0) { throw 'Passing fixture produced no fresh PNGs.' }
    $manifest['status'] = 'ok'
    $manifest['panels'] = $panels
    $manifest['validation'] = $result.Trim()
    $dll = Join-Path $ProjectDir 'Assets/BoscaliSummer.dll'
    if (Test-Path -LiteralPath $dll) {
        $manifest['productionDllSha256'] = (Get-FileHash -LiteralPath $dll).Hash
    }
    $inputs = @()
    foreach ($inputFile in Get-ChildItem -LiteralPath (Join-Path $ProjectDir 'Assets') -File -Filter '*.cs' -Recurse | Sort-Object FullName) {
        $inputs += @{ file=$inputFile.FullName.Substring($ProjectDir.Length).TrimStart([char[]]'\/'); sha256=(Get-FileHash -LiteralPath $inputFile.FullName).Hash }
    }
    $manifest['sourceInputs'] = $inputs
    Copy-Item -LiteralPath (Join-Path $ProjectDir 'result.txt') -Destination (Join-Path $dest 'result.txt') -Force
}
catch {
    $manifest['status'] = 'failed'
    $manifest['error'] = $_.Exception.Message
    throw
}
finally {
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dest 'manifest.json') -Encoding UTF8
}

