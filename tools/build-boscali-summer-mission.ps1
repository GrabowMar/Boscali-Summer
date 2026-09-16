#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the "Boscali Summer" custom mission for Nuclear Option.

.DESCRIPTION
    Reads a built-in mission (default: Escalation) as the battlefield baseline, keeps its map
    data and unit placement arrays, and rewrites missionSettings / environment / factions /
    objectives / outcomes from the authored data in this file.

    The authored timeline lives in the "AUTHORED TIMELINE" section below; the wave data lives in
    "AUTHORED WAVES". References (units, airbases, objective/outcome names) are verified before
    anything is written.

.NOTES
    Serialization uses the game's own copy of Newtonsoft.Json with the same settings the game
    uses (NullValueHandling.Ignore, StringEnumConverter, TypeNameHandling.Auto, fields-only
    ordering) so the output diffs cleanly against the game's own pretty-printed mission JSON.
    Field order inside every authored object mirrors the order the game's writer produces:
    base-type fields first, then the fields declared on the concrete SavedObjective/SavedOutcome
    class.
#>
[CmdletBinding()]
param(
    [string]$Baseline = "$env:LOCALAPPDATA\Temp\opencode\nomissions\text\Escalation.txt",
    [string]$RepoRoot,
    [string]$OutDir,
    [string]$NewtonsoftPath
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'missions\Boscali Summer' }

# ---------------------------------------------------------------- baseline I/O

if (-not (Test-Path -LiteralPath $Baseline)) { throw "Baseline mission not found: $Baseline" }

$missionsRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'missions'))
$outFull = [System.IO.Path]::GetFullPath($OutDir)
if (-not $outFull.StartsWith($missionsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write outside '$missionsRoot' (got '$outFull')"
}

if (-not $NewtonsoftPath) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Nuclear Option\NuclearOption_Data\Managed\Newtonsoft.Json.dll'),
        (Join-Path $env:ProgramFiles 'Steam\steamapps\common\Nuclear Option\NuclearOption_Data\Managed\Newtonsoft.Json.dll')
    )
    $NewtonsoftPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $NewtonsoftPath -or -not (Test-Path -LiteralPath $NewtonsoftPath)) {
    throw 'Newtonsoft.Json.dll not found. Pass -NewtonsoftPath <path to the game Managed folder copy>.'
}

if (-not ([System.Management.Automation.PSTypeName]'Newtonsoft.Json.JsonConvert').Type) {
    Add-Type -Path $NewtonsoftPath
}

$root = [Newtonsoft.Json.Linq.JObject]::Parse([System.IO.File]::ReadAllText($Baseline))

# ------------------------------------------------------------ authoring helpers

# Convert PowerShell ordered dictionaries / arrays / scalars into Newtonsoft JTokens, preserving
# insertion order so the emitted JSON keeps the game's field order.
# Authored data is emitted as JSON text and then parsed by Newtonsoft. PowerShell cannot hand a
# JToken to a JToken-typed parameter (it stays wrapped in PSObject), and ConvertTo-Json drops the
# trailing ".0" the game writes for floats - so this emitter keeps the file byte-shaped like the
# game's own writer.
function ConvertTo-JsonString([string]$s) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        $code = [int]$ch
        if ($code -eq 34) { [void]$sb.Append('\"') }
        elseif ($code -eq 92) { [void]$sb.Append('\\') }
        elseif ($code -eq 10) { [void]$sb.Append('\n') }
        elseif ($code -eq 13) { [void]$sb.Append('\r') }
        elseif ($code -eq 9) { [void]$sb.Append('\t') }
        elseif ($code -lt 32) { [void]$sb.Append('\u' + $code.ToString('x4')) }
        else { [void]$sb.Append($ch) }
    }
    return '"' + $sb.ToString() + '"'
}

function ConvertTo-JsonText {
    param([Parameter(Mandatory)] $Value)
    if ($Value -is [string]) { return (ConvertTo-JsonString $Value) }
    if ($Value -is [bool]) { if ($Value) { return 'true' } return 'false' }
    if ($Value -is [int] -or $Value -is [long]) { return ([string]$Value) }
    if ($Value -is [double] -or $Value -is [single] -or $Value -is [decimal]) {
        $d = [double]$Value
        if ([math]::Truncate($d) -eq $d -and [math]::Abs($d) -lt 1e15) {
            return $d.ToString('0.0', [System.Globalization.CultureInfo]::InvariantCulture)
        }
        return $d.ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $parts = @()
        foreach ($k in $Value.Keys) { $parts += ('"' + [string]$k + '":' + (ConvertTo-JsonText $Value[$k])) }
        return '{' + ($parts -join ',') + '}'
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $parts = @()
        foreach ($i in $Value) { $parts += (ConvertTo-JsonText $i) }
        return '[' + ($parts -join ',') + ']'
    }
    # values lifted out of the baseline with ConvertFrom-Json are PSCustomObjects; an empty one
    # (the baseline's road "bounds": {}) is valid and must stay "{}"
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $parts = @()
        foreach ($p in $Value.PSObject.Properties) { $parts += ('"' + $p.Name + '":' + (ConvertTo-JsonText $p.Value)) }
        return '{' + ($parts -join ',') + '}'
    }
    if ($Value.PSObject -and $Value.PSObject.Properties.Count -gt 0) {
        $parts = @()
        foreach ($p in $Value.PSObject.Properties) { $parts += ('"' + $p.Name + '":' + (ConvertTo-JsonText $p.Value)) }
        return '{' + ($parts -join ',') + '}'
    }
    throw "Cannot serialize value of type $($Value.GetType().FullName)"
}

function Ov([bool]$IsOverride, $Value) { return [ordered]@{ IsOverride = $IsOverride; Value = $Value } }

# --- outcomes -----------------------------------------------------------------------------------
function StartObj([string]$Name, [string[]]$Targets) {
    return [ordered]@{ Type = 'StartObjective'; UniqueName = $Name; objectivesToStart = @($Targets) }
}
function StopObj([string]$Name, [string[]]$Targets) {
    return [ordered]@{ Type = 'StopOrCompleteObjective'; UniqueName = $Name; options = 'Stop'; objectivesToStart = @($Targets) }
}
function ShowMsg([string]$Name, [string]$Message, [bool]$Sound = $true, [bool]$FactionOnly = $false) {
    return [ordered]@{ Type = 'ShowMessage'; UniqueName = $Name; Message = $Message; PlaySound = $Sound; ObjectiveFactionOnly = $FactionOnly }
}
function GiveScore([string]$Name, [hashtable]$Over) {
    $h = [ordered]@{
        Type = 'GiveScore'; UniqueName = $Name; bothFactions = $false
        playerFundsType = 'Add'; playerFunds = 0.0
        factionFundsType = 'Add'; factionFunds = 0.0
        playerScoreType = 'Add'; playerScore = 0.0
        rankType = 'Add'; rank = 0
        factionScoreType = 'Add'; factionScore = 0.0
    }
    foreach ($k in $Over.Keys) { $h[$k] = $Over[$k] }
    return $h
}
function SpawnObj([string]$Name, [string[]]$Units) {
    return [ordered]@{ Type = 'SpawnUnit'; UniqueName = $Name; UnitsToSpawn = @($Units) }
}
function EndGameOutcome([string]$Name, [string]$EndType, [double]$Delay) {
    return [ordered]@{ Type = 'EndGame'; UniqueName = $Name; endType = $EndType; endDelay = $Delay }
}
function ModAirbase([string]$Name, [string]$Airbase, [hashtable]$Over) {
    $h = [ordered]@{
        Type = 'ModifyAirbase'; UniqueName = $Name; airbase = $Airbase
        faction = (Ov $false '')
        disabled = (Ov $false $false)
        capturable = (Ov $false $false)
        captureDefense = (Ov $false 0.0)
    }
    foreach ($k in $Over.Keys) { $h[$k] = $Over[$k] }
    return $h
}
function ModEnvironment([string]$Name, [hashtable]$Over) {
    $h = [ordered]@{
        Type = 'ModifyEnvironment'; UniqueName = $Name
        timeOfDay = (Ov $false 0.0); weather = (Ov $false 0.0); cloudAltitude = (Ov $false 0.0)
        windSpeed = (Ov $false 0.0); windTurbulence = (Ov $false 0.0); windHeading = (Ov $false 0.0)
    }
    foreach ($k in $Over.Keys) { $h[$k] = $Over[$k] }
    return $h
}
function ModFaction([string]$Name, [hashtable]$Over) {
    $h = [ordered]@{
        Type = 'ModifyFaction'; UniqueName = $Name; bothFactions = $false
        excessFundsThreshold = (Ov $false 0.0); playerJoinAllowance = (Ov $false 0.0)
        playerTaxRate = (Ov $false 0.0); regularIncome = (Ov $false 0.0)
        killReward = (Ov $false 0.0); preventDonation = (Ov $false $false)
        aiAircraftLimit = (Ov $false 0.0); reduceAIPerFriendlyPlayer = (Ov $false 0.0)
        addAIPerEnemyPlayer = (Ov $false 0.0); warheadsReserve = (Ov $false 0.0)
        reserveAirframes = (Ov $false 0.0); extraReservesPerPlayer = (Ov $false 0.0)
        excessFundsDistributePercent = (Ov $false 0.0); preventJoin = (Ov $false $false)
    }
    foreach ($k in $Over.Keys) { $h[$k] = $Over[$k] }
    return $h
}

# --- objectives ---------------------------------------------------------------------------------
function ObjBase([string]$Type, [string]$Name, [string]$Faction, [string]$Display, [bool]$Hidden, [string[]]$Outcomes) {
    return [ordered]@{
        Type = $Type; UniqueName = $Name; Faction = $Faction
        DisplayName = $Display; Hidden = $Hidden; Outcomes = @($Outcomes)
    }
}
function DestroyObj([string]$Name, [string]$Faction, [string]$Display, [bool]$Hidden, [string]$Order, [double]$SomePercent, [string[]]$Targets, [string[]]$Outcomes) {
    $h = ObjBase 'DestroyUnits' $Name $Faction $Display $Hidden $Outcomes
    $h['completeOrder'] = $Order
    $h['completeSomePercent'] = $SomePercent
    $h['targetUnits'] = @($Targets)
    return $h
}
function WaitObj([string]$Name, [string]$Faction, [double]$Seconds, [string[]]$Outcomes, [bool]$Hidden = $true) {
    $h = ObjBase 'WaitSeconds' $Name $Faction $Name $Hidden $Outcomes
    $h['seconds'] = $Seconds
    return $h
}
function CaptureObj([string]$Name, [string]$Faction, [string]$Display, [string]$Order, [double]$SomePercent, [string[]]$Airbases, [string[]]$Outcomes, [bool]$Hidden = $false) {
    $h = ObjBase 'CaptureAirbase' $Name $Faction $Display $Hidden $Outcomes
    $h['completeOrder'] = $Order
    $h['completeSomePercent'] = $SomePercent
    $h['targetAirbases'] = @($Airbases)
    return $h
}
function CompleteOtherObj([string]$Name, [string]$Faction, [string]$Order, [double]$SomePercent, [string[]]$Targets, [string[]]$Outcomes, [bool]$Hidden = $true) {
    $h = ObjBase 'CompleteOtherObjective' $Name $Faction $Name $Hidden $Outcomes
    $h['completeOrder'] = $Order
    $h['completeSomePercent'] = $SomePercent
    $h['targetObjectives'] = @($Targets)
    return $h
}
function SortieObj([string]$Name, [string]$Faction, [double]$MinimumScore, [bool]$Additive, [string[]]$Outcomes) {
    $h = ObjBase 'SuccessfulSortie' $Name $Faction $Name $true $Outcomes
    $h['minimumScore'] = $MinimumScore
    $h['additive'] = $Additive
    return $h
}

# --------------------------------------------------- AUTHORED MISSION SETTINGS

$Description = @'
Three weeks ago the PALA putsch seized the capital ring and half the airfields. The BDF regrouped outside the cordon and this morning the counter-offensive opens. The old order is gone - what began as a coup is now ordinary war, and both sides will spend everything they have.

Victory is achieved when every enemy aircraft factory is destroyed and the enemy fleet carrier, arriving midway through the battle, is sunk. Rising score unlocks heavier airframes and, at 625 and 1225, tactical and strategic warheads.
'@ -replace "`r`n", "`n"

$MissionSettings = [ordered]@{
    description = $Description
    allowEventContent = $false
    Tags = @()
    playerMode = 'SingleAndMultiplayer'
    allowRespawn = $true
    playerStartingRank = 0
    rankMultiplier = 0.75
    successfulSortieBonus = 0.75
    nuclearEscalationThreshold = 625.0
    strategicEscalationThreshold = 1225.0
    minRankTacticalWarhead = 0
    minRankStrategicWarhead = 0
    # cameraStartPosition / missionRoads / missionSeaLanes / wrecks* are battlefield geometry and
    # are carried over from the baseline rather than authored here.
}

$Environment = [ordered]@{
    timeOfDay = 5.4
    timeFactor = 0.0
    weatherIntensity = 0.18
    cloudAltitude = 2600.0
    windSpeed = 3.5
    windTurbulence = 0.08
    windHeading = 95.0
    windRandomHeading = 0.0
    moonPhase = 14.0
}

# Both factions stay joinable; the BDF gets the counter-offensive's supply priority, the
# putschists pay a slightly higher bounty to hold what they took.
$FactionTweaks = [ordered]@{
    Boscali = @{ regularIncome = 2.2; killReward = 3.0; reserveWarheads = 16; reserveAirframes = 1; AIAircraftLimit = 4 }
    Primeva = @{ regularIncome = 2.0; killReward = 3.4; reserveWarheads = 16; reserveAirframes = 1; AIAircraftLimit = 4 }
}

# ---------------------------------------------------------- AUTHORED WAVE DATA

# Each wave appends new SavedUnits (cloned from a baseline prototype of the same type) to a root
# array and is referenced by exactly one SpawnUnit outcome. Entries: Type + either Count
# (placed in a pad around the anchor airbase) or At (explicit x,y,z, used for ships at sea).
$WaveData = @(
    @{ Name = 'Spawn_BDF_RingStrike';    Faction = 'Boscali'; Kind = 'aircraft'; Anchor = 'Maris Airport';                    Entries = @(
        @{ Type = 'COIN'; Count = 4 } ) }
    @{ Name = 'Spawn_PALA_RingAlert';    Faction = 'Primeva'; Kind = 'aircraft'; Anchor = 'The Farm';                         Entries = @(
        @{ Type = 'AttackHelo1'; Count = 4 } ) }
    @{ Name = 'Spawn_PALA_RingCounter';  Faction = 'Primeva'; Kind = 'vehicles';  Anchor = 'Agrapol Airbase';                  Entries = @(
        @{ Type = 'MBT1'; Count = 3 }, @{ Type = 'SPAAG1'; Count = 2 }, @{ Type = 'Linebreaker_IFV'; Count = 1 } ) }
    @{ Name = 'Spawn_BDF_Bomber';        Faction = 'Boscali'; Kind = 'aircraft'; Anchor = 'North Boscali Airbase';            Entries = @(
        @{ Type = 'Multirole1'; Count = 3 }, @{ Type = 'EW1'; Count = 1 } ) }
    @{ Name = 'Spawn_PALA_Armor';        Faction = 'Primeva'; Kind = 'vehicles';  Anchor = 'Dustbowl Highway Strip';           Entries = @(
        @{ Type = 'MBT1'; Count = 4 }, @{ Type = 'SPAAG1'; Count = 2 }, @{ Type = 'Truck2-FT'; Count = 2 } ) }
    @{ Name = 'Spawn_BDF_CoastSweep';    Faction = 'Boscali'; Kind = 'ships';    Anchor = '';                                 Entries = @(
        @{ Type = 'Corvette1'; At = @(24000.0, 8.0, -37000.0) }, @{ Type = 'Corvette1'; At = @(24750.0, 8.0, -37280.0) }, @{ Type = 'Frigate1'; At = @(25000.0, 6.0, -37600.0) } ) }
    @{ Name = 'Spawn_PALA_CoastalPatrol';Faction = 'Primeva'; Kind = 'ships';    Anchor = '';                                 Entries = @(
        @{ Type = 'Corvette1'; At = @(-35500.0, 8.0, -11800.0) }, @{ Type = 'Corvette1'; At = @(-35000.0, 8.0, -12350.0) }, @{ Type = 'PatrolBoat1'; At = @(-35400.0, 0.0, -12900.0) } ) }
    @{ Name = 'Spawn_BDF_NightStrike';   Faction = 'Boscali'; Kind = 'aircraft'; Anchor = 'K92 Highway Strip';                Entries = @(
        @{ Type = 'Multirole1'; Count = 4 }, @{ Type = 'AttackHelo1'; Count = 2 } ) }
    @{ Name = 'Spawn_PALA_LastStand';    Faction = 'Primeva'; Kind = 'aircraft'; Anchor = 'Vigil Cay Naval Airbase';          Entries = @(
        @{ Type = 'Multirole1'; Count = 3 }, @{ Type = 'EW1'; Count = 2 } ) }
    @{ Name = 'Spawn_BDF_CoastPush';     Faction = 'Boscali'; Kind = 'vehicles';  Anchor = 'South Boscali General Aviation';   Entries = @(
        @{ Type = 'MBT'; Count = 6 }, @{ Type = 'SPAAG2'; Count = 2 }, @{ Type = 'Truck2-M'; Count = 2 } ) }
    @{ Name = 'Spawn_BDF_Tier2';         Faction = 'Boscali'; Kind = 'aircraft'; Anchor = 'K92 Highway Strip';                Entries = @(
        @{ Type = 'COIN'; Count = 4 } ) }
    @{ Name = 'Spawn_PALA_Tier2';        Faction = 'Primeva'; Kind = 'aircraft'; Anchor = 'Sandrift Airbase';                 Entries = @(
        @{ Type = 'Multirole1'; Count = 4 } ) }
    @{ Name = 'Spawn_BDF_Tier3';         Faction = 'Boscali'; Kind = 'aircraft'; Anchor = 'South Boscali General Aviation';   Entries = @(
        @{ Type = 'Multirole1'; Count = 4 } ) }
    @{ Name = 'Spawn_PALA_Tier3';        Faction = 'Primeva'; Kind = 'aircraft'; Anchor = 'Agrapol Airbase';                  Entries = @(
        @{ Type = 'COIN'; Count = 4 } ) }
    @{ Name = 'Spawn_BDF_Tier4';         Faction = 'Boscali'; Kind = 'aircraft'; Anchor = 'Maris Airport';                    Entries = @(
        @{ Type = 'Multirole1'; Count = 3 }, @{ Type = 'EW1'; Count = 1 } ) }
    @{ Name = 'Spawn_PALA_Tier4';        Faction = 'Primeva'; Kind = 'aircraft'; Anchor = 'Vigil Cay Naval Airbase';          Entries = @(
        @{ Type = 'Multirole1'; Count = 3 }, @{ Type = 'EW1'; Count = 1 } ) }
)

# ------------------------------------------------------------ AUTHORED TIMELINE

# Target sets. Every name is checked against the baseline arrays at build time.
$BdfFacilities = @('CityFac1','CityFac2','CityFac3','CityFac4','CityFac5','CityFac6','CityFac9',
                   'NorthFac1','NorthFac2','NorthFac3','NorthFac4','NorthFac7','NorthFac8','NorthFac9')
$PalaFacilities = @('IslandFac1','IslandFac2','IslandFac3','MtnFac8','MtnFac7','MtnFac6','MtnFac5','MtnFac4',
                    'MtnFac1','MtnFac2','RearFac5','RearFac3','RearFac2','RearFac1')
$RingGarrison = @('Linebreaker_SAM_33','HLT-FT_28','HLT-M_32','Emplacement1_MANPADS_6','ammoDump_3','farm_Helipad',
                  'VehicleDepot1_6','VehicleDepot1_7','Emplacement1_23mm_4','Emplacement1_23mm_5',
                  'hwy1_SPAAG1_1','LCV45_9','HLT-M_15','Linebreaker_IFV_15')
$IslandGarrison = @('IslandFac1','IslandFac2','IslandFac3','SPAAG1_9','Linebreaker_SAM','Linebreaker_SAM_1',
                    'Linebreaker_IFV','HLT-M_7','HLT-FT_7','RadarSAM1_2','SPAAG1_2','HLT-M_8')
$K92Garrison = @('hwy2_depot1','hwy2_depot2','Emplacement1_23mm_2','Emplacement1_23mm_3','ammoDump',
                 'AFV8_IFV_4','AFV8_SAM_10','HLT-M','MBT_45','MBT_46','HLT-FT_25','AFV8_APC_16')
$CityFactories = @('CityFac1','CityFac2','CityFac3','CityFac4','CityFac7','CityFac8',
                   'NorthFac5','NorthFac6','BDF_MBTFac1','BDF_MBTFac2')

$Timeline = [ordered]@{

    # ---------------------------------------------------------------- core / victory
    Core = @{
        Objectives = @(
            (ObjBase 'None' 'Mission Start' '' '' $true @('SetStartingConditions'))
            (DestroyObj 'DestroyAllCriticalBDFFacilities' 'Primeva' 'DestroyAllCriticalBDFFacilities' $true 'InOrder' 0.5 $BdfFacilities @('StartSinkBDFCarrier'))
            (DestroyObj 'DestroyAllCriticalPALAFacilities' 'Boscali' 'DestroyAllCriticalPALAFacilities' $true 'InOrder' 0.5 $PalaFacilities @('StartSinkPALACarrier'))
            (DestroyObj 'SinkBDFCarrier' 'Primeva' 'Sink Fleet Carrier' $false 'CompleteAll' 0.5 @('BDF_Carrier') @('Msg_CarrierSunkBDF'))
            (DestroyObj 'SinkPALACarrier' 'Boscali' 'Sink Fleet Carrier' $false 'CompleteAll' 0.5 @('PALA_Carrier') @('Msg_CarrierSunkPALA'))
            (CompleteOtherObj 'BDF_Victory_Conditions' 'Boscali' 'CompleteAll' 0.5 @('DestroyAllCriticalPALAFacilities','SinkPALACarrier') @('Victory','Msg_VictoryBDF'))
            (CompleteOtherObj 'PALA_Victory_Conditions' 'Primeva' 'CompleteAll' 0.5 @('DestroyAllCriticalBDFFacilities','SinkBDFCarrier') @('Victory','Msg_VictoryPALA'))
            (DestroyObj 'DestroyBDFEnrichment' 'Primeva' 'Destroy Enrichment Plant' $false 'CompleteAny' 0.5 @('BDF_enrichmentPlant1') @())
            (DestroyObj 'DestroyPALAEnrichment' 'Boscali' 'Destroy Enrichment Plant' $false 'CompleteAny' 0.5 @('PALA_enrichmentPlant1') @())
        )
        Outcomes = @(
            # Act4_Coast / Act5_Island are deliberately NOT started here: they open from the
            # Act3_Capital outcomes, so the coast and island phases depend on real world state.
            (StartObj 'SetStartingConditions' @(
                'Act1_Ring','Act3_Capital','Act6_Strategic',
                'PALA_Push1','BDF_Victory_Conditions','PALA_Victory_Conditions',
                'DestroyAllCriticalBDFFacilities','DestroyAllCriticalPALAFacilities',
                'DestroyBDFEnrichment','DestroyPALAEnrichment',
                'ScoreGate_BDF_200','ScoreGate_BDF_400','ScoreGate_BDF_625','ScoreGate_BDF_850','ScoreGate_BDF_1225',
                'ScoreGate_PALA_200','ScoreGate_PALA_400','ScoreGate_PALA_625','ScoreGate_PALA_850','ScoreGate_PALA_1225',
                'Beat_02_OpenFire','Beat_05_Resupply','Beat_08_Haze','Beat_11_Bomber','Beat_14_Storm','Beat_17_Armor',
                'Beat_20_NavalWarn','Beat_24_Naval','Beat_28_CoastSweep','Beat_32_PalaPatrol','Beat_36_Pressure',
                'Beat_40_DeepNight','Beat_46_LastLight','Beat_52_Endgame'))
            (StartObj 'StartSinkBDFCarrier' @('SinkBDFCarrier'))
            (StartObj 'StartSinkPALACarrier' @('SinkPALACarrier'))
            (ShowMsg 'Msg_CarrierSunkBDF' 'INTEL: BDF fleet carrier is down. Their air wings are landlocked.' $true $false)
            (ShowMsg 'Msg_CarrierSunkPALA' 'INTEL: PALA fleet carrier is down. The putsch has no flight deck left.' $true $false)
            (EndGameOutcome 'Victory' 'Victory' 4.0)
            (ShowMsg 'Msg_VictoryBDF' 'BDF COMMAND: The putsch has capitulated. Boscali is whole again.' $true $true)
            (ShowMsg 'Msg_VictoryPALA' 'PALA LOYALIST NET: The counter-offensive has broken. The ring is ours for good.' $true $true)
        )
    }

    # ---------------------------------------------------------------- Act I: H-Hour
    Act1 = @{
        Objectives = @(
            (WaitObj 'Beat_02_OpenFire' '' 120.0 @('Msg_OpenFireBDF','Msg_OpenFirePALA','Spawn_BDF_RingStrike','Spawn_PALA_RingAlert'))
            (CaptureObj 'Act1_Ring' 'Boscali' 'Take the Capital Ring' 'CompleteAll' 0.5 @('The Farm','Dustbowl Highway Strip') @(
                'Msg_RingBrokenBDF','Msg_RingBrokenPALA','StopAct3Armor','StartAct2Garrison','StartAct2Clock',
                'GiveScore_BDF_Ring','ModFaction_BDF_Ring','ModAirbase_FarmHarden'))
        )
        Outcomes = @(
            (ShowMsg 'Msg_OpenFireBDF' 'BDF COMMAND: Counter-offensive is live. All wings, weapons free over the ring.' $true $true)
            (ShowMsg 'Msg_OpenFirePALA' 'PALA LOYALIST NET: The ring holds. Every post manned. Make them pay for the wire.' $true $true)
            (ShowMsg 'Msg_RingBrokenBDF' 'BDF COMMAND: The ring is broken. Push to the coast.' $true $true)
            (ShowMsg 'Msg_RingBrokenPALA' 'PALA LOYALIST NET: Farm and Dustbowl are gone. Fall back to the capital line.' $true $true)
            (StopObj 'StopAct3Armor' @('Beat_17_Armor'))
            (StartObj 'StartAct2Garrison' @('Act2_Garrison'))
            (StartObj 'StartAct2Clock' @('Act2_Clock'))
            (GiveScore 'GiveScore_BDF_Ring' @{ factionFunds = 300.0; factionScore = 15.0; playerFunds = 150.0; playerScore = 10.0 })
            (ModFaction 'ModFaction_BDF_Ring' @{ regularIncome = (Ov $true 2.6); reserveAirframes = (Ov $true 2.0); extraReservesPerPlayer = (Ov $true 2.0) })
            (ModAirbase 'ModAirbase_FarmHarden' 'The Farm' @{ capturable = (Ov $true $false); captureDefense = (Ov $true 30.0) })
        )
    }

    # ----------------------------------------- Act II: haze, ring garrisons, counter-attack
    Act2 = @{
        Objectives = @(
            (WaitObj 'Act2_Clock' 'Primeva' 300.0 @('Msg_PalaCounter','Spawn_PALA_RingCounter'))
            (DestroyObj 'Act2_Garrison' 'Boscali' 'Clear the Ring Garrisons' $false 'CompleteSome' 0.75 $RingGarrison @('Msg_RingCleared','GiveScore_BDF_Garrison'))
        )
        Outcomes = @(
            (ShowMsg 'Msg_PalaCounter' 'PALA LOYALIST NET: Hold the bridgeheads. Reinforcements are en route.' $true $false)
            (ShowMsg 'Msg_RingCleared' 'BDF COMMAND: Ring garrisons are cleared. Engineers are moving up.' $true $true)
            (GiveScore 'GiveScore_BDF_Garrison' @{ factionFunds = 200.0; playerFunds = 100.0; playerScore = 8.0 })
        )
    }

    # -------------------------------------- Act III: mid-morning haze / bomber package
    Act3 = @{
        Objectives = @(
            (WaitObj 'Beat_05_Resupply' '' 300.0 @('Msg_PalaResupply','SpawnConvoyDesertEast','SpawnConvoyDesertWest'))
            (WaitObj 'Beat_08_Haze' '' 480.0 @('ModEnv_Haze','Msg_Haze'))
            (WaitObj 'Beat_11_Bomber' '' 660.0 @('Msg_Bomber','Spawn_BDF_Bomber','SpawnConvoyCityEast','SpawnConvoyCityWest'))
            (WaitObj 'Beat_14_Storm' '' 840.0 @('ModEnv_Storm','Msg_Storm'))
            (WaitObj 'Beat_17_Armor' '' 1020.0 @('Msg_PalaArmor','SpawnConvoyMountainEast','SpawnConvoyMountainWest','Spawn_PALA_Armor'))
        )
        Outcomes = @(
            (ShowMsg 'Msg_PalaResupply' 'PALA LOYALIST NET: Armour column rolling out of the desert. Ring in ten minutes.' $true $false)
            (ModEnvironment 'ModEnv_Haze' @{ timeOfDay = (Ov $true 8.5); weather = (Ov $true 0.55); cloudAltitude = (Ov $true 1700.0); windSpeed = (Ov $true 8.0); windTurbulence = (Ov $true 0.35); windHeading = (Ov $true 120.0) })
            (ShowMsg 'Msg_Haze' 'METEO: Haze layer thickening across the basin. Cloud base one-seven-hundred.' $true $false)
            (ShowMsg 'Msg_Bomber' 'BDF COMMAND: Heavy package launching from the north. Clear the corridor.' $true $true)
            (ModEnvironment 'ModEnv_Storm' @{ timeOfDay = (Ov $true 12.5); weather = (Ov $true 0.88); cloudAltitude = (Ov $true 950.0); windSpeed = (Ov $true 16.0); windTurbulence = (Ov $true 0.75); windHeading = (Ov $true 150.0) })
            (ShowMsg 'Msg_Storm' 'METEO: Storm front crossing the coast. Winds one-six, cloud base nine-five-zero.' $true $false)
            (ShowMsg 'Msg_PalaArmor' 'PALA LOYALIST NET: Mountain column is moving. Keep the passes open.' $true $false)
            # Baseline convoy groups, folded into the staged timeline (patterns expand against the
            # saved unit arrays, so they stay correct if the baseline is refreshed).
            (SpawnObj 'SpawnConvoyDesertEast'   @('PALAConvDsrtEast*','Truck2-MLRS_2','Truck2-MLRS_3'))
            (SpawnObj 'SpawnConvoyDesertWest'   @('PALAConvDsrtWest*','Truck2-MLRS','Truck2-MLRS_1'))
            (SpawnObj 'SpawnConvoyMountainEast' @('PALAConvMntEast*','Truck2-MLRS_6','Truck2-MLRS_7'))
            (SpawnObj 'SpawnConvoyMountainWest' @('PALAConvMntWest*','Truck2-MLRS_4','Truck2-MLRS_5'))
            (SpawnObj 'SpawnConvoyCityEast'     @('BDFConvCityEast*','HLT-MART_6','HLT-MART_7'))
            (SpawnObj 'SpawnConvoyCityWest'     @('BDFConvCityWest*','HLT-MART_4','HLT-MART_5'))
            (SpawnObj 'SpawnConvoyNorthEast'    @('BDFConvNthEast*','HLT-MART_1','HLT-MART_3'))
            (SpawnObj 'SpawnConvoyNorthWest'    @('BDFConvNthWest*','HLT-MART','HLT-MART_2'))
        )
    }

    # ------------------------------------------------- Act IV: the capital / the coast
    Act4 = @{
        Objectives = @(
            (CaptureObj 'Act3_Capital' 'Boscali' 'Take Agrapol' 'CompleteAll' 0.5 @('Agrapol Airbase') @(
                'Msg_CapitalBDF','Msg_CapitalPALA','ModAirbase_AgrapolRearm','GiveScore_BDF_Capital','ModFaction_BDF_Capital',
                'StopAct2Garrison','StartAct4Clock','StartAct4Coast'))
            (WaitObj 'Act4_Clock' 'Boscali' 900.0 @('Msg_CapitalHold','Spawn_BDF_CoastPush'))
            (CaptureObj 'Act4_Coast' 'Boscali' 'Take the Coast Airfield' 'CompleteAll' 0.5 @('Sandrift Airbase') @(
                'Msg_CoastBDF','ModAirbase_SandriftOpen','GiveScore_BDF_Coast','StartAct5Island'))
        )
        Outcomes = @(
            (ShowMsg 'Msg_CapitalBDF' 'BDF COMMAND: The capital is ours. Rearm forward - we are not stopping.' $true $true)
            (ShowMsg 'Msg_CapitalPALA' 'PALA LOYALIST NET: They are at the capital gates. Blow the approaches.' $true $true)
            (ModAirbase 'ModAirbase_AgrapolRearm' 'Agrapol Airbase' @{ capturable = (Ov $true $false); captureDefense = (Ov $true 35.0) })
            (GiveScore 'GiveScore_BDF_Capital' @{ factionFunds = 350.0; factionScore = 20.0; playerFunds = 175.0; playerScore = 12.0 })
            (ModFaction 'ModFaction_BDF_Capital' @{ reserveWarheads = (Ov $true 24.0); aiAircraftLimit = (Ov $true 5.0); addAIPerEnemyPlayer = (Ov $true 1.5) })
            (StopObj 'StopAct2Garrison' @('Act2_Garrison'))
            (StartObj 'StartAct4Clock' @('Act4_Clock'))
            (StartObj 'StartAct4Coast' @('Act4_Coast'))
            (ShowMsg 'Msg_CapitalHold' 'BDF COMMAND: Bridgehead is secure. Armour is rolling for the coast.' $true $true)
            (ShowMsg 'Msg_CoastBDF' 'BDF COMMAND: Coast airfield taken. The island is next.' $true $true)
            (ModAirbase 'ModAirbase_SandriftOpen' 'Sandrift Airbase' @{ capturable = (Ov $true $true); captureDefense = (Ov $true 12.0) })
            (GiveScore 'GiveScore_BDF_Coast' @{ factionFunds = 300.0; factionScore = 18.0; playerFunds = 150.0; playerScore = 10.0 })
            (StartObj 'StartAct5Island' @('Act5_Island'))
        )
    }

    # ------------------------------- Act V: island garrisons / naval / strategic release
    Act5 = @{
        Objectives = @(
            (DestroyObj 'Act5_Island' 'Boscali' 'Neutralise the Island Base' $false 'CompleteSome' 0.6 $IslandGarrison @('Msg_IslandBDF','GiveScore_BDF_Island'))
            (WaitObj 'Beat_20_NavalWarn' '' 1200.0 @('Msg_NavalWarn'))
            (WaitObj 'Beat_24_Naval' '' 1440.0 @('Spawn_Fleets','StartSinkBDFCarrier','StartSinkPALACarrier','Msg_NavalArrived','SpawnConvoyNorthEast','SpawnConvoyNorthWest'))
            (CompleteOtherObj 'Act6_Strategic' 'Boscali' 'CompleteAll' 0.5 @('Act1_Ring','Act3_Capital','Act4_Coast','Act5_Island','Beat_24_Naval') @(
                'ModEnv_Dusk','Msg_StrategicBDF','Msg_StrategicPALA','ModAirbase_VigilHarden','ModAirbase_SandriftHarden',
                'ModFaction_BDF_Strategic','ModFaction_PALA_Strategic'))
        )
        Outcomes = @(
            (ShowMsg 'Msg_IslandBDF' 'BDF COMMAND: Island base is out of action. Their last fields are on the map.' $true $true)
            (GiveScore 'GiveScore_BDF_Island' @{ factionFunds = 350.0; factionScore = 22.0; playerFunds = 175.0; playerScore = 12.0 })
            (ShowMsg 'Msg_NavalWarn' 'NAVAL NET: Two carriers converging on the strait. ETA twenty minutes.' $true $false)
            (ShowMsg 'Msg_NavalArrived' 'NAVAL NET: Fleets have arrived. Carrier groups are in play.' $true $false)
            (SpawnObj 'Spawn_Fleets' @('BDF_Carrier','PALA_Carrier','BDF_Esc1','BDF_Esc2','PALA_Esc1','PALA_Esc3','Destroyer1','Frigate1','BDF_navyplane1','BDF_navyplane2','BDF_navyplane3','PALA_navyplane1','PALA_navyplane2','PALA_navyplane3'))
            (ModEnvironment 'ModEnv_Dusk' @{ timeOfDay = (Ov $true 18.6); weather = (Ov $true 0.72); cloudAltitude = (Ov $true 1500.0); windSpeed = (Ov $true 11.0); windTurbulence = (Ov $true 0.45); windHeading = (Ov $true 175.0) })
            (ShowMsg 'Msg_StrategicBDF' 'BDF COMMAND: Strategic phase. Every factory and carrier is a target now.' $true $true)
            (ShowMsg 'Msg_StrategicPALA' 'PALA LOYALIST NET: Arm the reserve warheads. If we burn, they burn with us.' $true $true)
            (ModAirbase 'ModAirbase_VigilHarden' 'Vigil Cay Naval Airbase' @{ capturable = (Ov $true $true); captureDefense = (Ov $true 45.0) })
            (ModAirbase 'ModAirbase_SandriftHarden' 'Sandrift Airbase' @{ disabled = (Ov $true $false); capturable = (Ov $true $false); captureDefense = (Ov $true 40.0) })
            (ModFaction 'ModFaction_BDF_Strategic' @{ warheadsReserve = (Ov $true 32.0); aiAircraftLimit = (Ov $true 6.0); reserveAirframes = (Ov $true 3.0) })
            (ModFaction 'ModFaction_PALA_Strategic' @{ warheadsReserve = (Ov $true 32.0); aiAircraftLimit = (Ov $true 6.0); reserveAirframes = (Ov $true 3.0); killReward = (Ov $true 4.0) })
        )
    }

    # ---------------------------------------- Acts VI-VIII: night, pressure, endgame
    Act6 = @{
        Objectives = @(
            (WaitObj 'Beat_28_CoastSweep' '' 1680.0 @('Msg_CoastSweep','Spawn_BDF_CoastSweep'))
            (WaitObj 'Beat_32_PalaPatrol' '' 1920.0 @('Msg_PalaPatrol','Spawn_PALA_CoastalPatrol'))
            (WaitObj 'Beat_36_Pressure' '' 2160.0 @('Msg_Pressure','ModFaction_Both_Pressure'))
            (WaitObj 'Beat_40_DeepNight' '' 2400.0 @('ModEnv_Night','Msg_Night'))
            (WaitObj 'Beat_46_LastLight' '' 2760.0 @('Msg_LastLight','Spawn_BDF_NightStrike','Spawn_PALA_LastStand'))
            (WaitObj 'Beat_52_Endgame' '' 3120.0 @('ModEnv_PreDawn','Msg_Endgame','ModFaction_Both_Endgame'))
        )
        Outcomes = @(
            (ShowMsg 'Msg_CoastSweep' 'BDF COMMAND: Naval group on station off the southern coast. Hunters, check in.' $true $true)
            (ShowMsg 'Msg_PalaPatrol' 'PALA LOYALIST NET: Patrol group is holding the island approaches.' $true $true)
            (ShowMsg 'Msg_Pressure' 'WAR DESK: Both sides are drawing on reserves. Replacement airframes released.' $true $false)
            (ModFaction 'ModFaction_Both_Pressure' @{ bothFactions = $true; extraReservesPerPlayer = (Ov $true 2.0); excessFundsDistributePercent = (Ov $true 0.35) })
            (ModEnvironment 'ModEnv_Night' @{ timeOfDay = (Ov $true 21.5); weather = (Ov $true 0.6); cloudAltitude = (Ov $true 1600.0); windSpeed = (Ov $true 9.0); windTurbulence = (Ov $true 0.3); windHeading = (Ov $true 190.0) })
            (ShowMsg 'Msg_Night' 'METEO: Sun down. Thermal contrast will favour whoever owns the ground.' $true $false)
            (ShowMsg 'Msg_LastLight' 'BDF COMMAND: Night strike packages are launching. Finish it.' $true $true)
            (ModEnvironment 'ModEnv_PreDawn' @{ timeOfDay = (Ov $true 3.8); weather = (Ov $true 0.35); cloudAltitude = (Ov $true 2100.0); windSpeed = (Ov $true 6.0); windTurbulence = (Ov $true 0.2); windHeading = (Ov $true 200.0) })
            (ShowMsg 'Msg_Endgame' 'WAR DESK: Long night. Both commands have released their last reserves.' $true $false)
            (ModFaction 'ModFaction_Both_Endgame' @{ bothFactions = $true; warheadsReserve = (Ov $true 40.0); aiAircraftLimit = (Ov $true 7.0); killReward = (Ov $true 4.2) })
        )
    }

    # ------------------------------------------------------ PALA counter-offensive acts
    Pala = @{
        Objectives = @(
            (DestroyObj 'PALA_Push1' 'Primeva' 'Break the Highway Strip' $false 'CompleteSome' 0.6 $K92Garrison @(
                'Msg_PalaK92','GiveScore_PALA_Push1','ModFaction_PALA_Push1','StartPALA_Push2'))
            (CaptureObj 'PALA_Push2' 'Primeva' 'Take Maris Airport' 'CompleteAll' 0.5 @('Maris Airport') @(
                'Msg_PalaMaris','Msg_MarisPALA','ModAirbase_MarisHarden','GiveScore_PALA_Maris','StartPALA_Push3'))
            (DestroyObj 'PALA_Push3' 'Primeva' 'Break the City Factories' $false 'CompleteSome' 0.6 $CityFactories @(
                'Msg_PalaPush3','GiveScore_PALA_Push3','ModFaction_PALA_Push3'))
        )
        Outcomes = @(
            (ShowMsg 'Msg_PalaK92' 'PALA LOYALIST NET: K92 is burning. The coup has teeth yet.' $true $true)
            (GiveScore 'GiveScore_PALA_Push1' @{ factionFunds = 250.0; factionScore = 12.0; playerFunds = 125.0; playerScore = 8.0 })
            (ModFaction 'ModFaction_PALA_Push1' @{ regularIncome = (Ov $true 2.4); reserveAirframes = (Ov $true 2.0) })
            (StartObj 'StartPALA_Push2' @('PALA_Push2'))
            (ShowMsg 'Msg_PalaMaris' 'PALA LOYALIST NET: Maris is in range. Push to the apron.' $true $true)
            (ShowMsg 'Msg_MarisPALA' 'PALA LOYALIST NET: We hold their main field. Now make them come to us.' $true $true)
            (ModAirbase 'ModAirbase_MarisHarden' 'Maris Airport' @{ capturable = (Ov $true $false); captureDefense = (Ov $true 35.0) })
            (GiveScore 'GiveScore_PALA_Maris' @{ factionFunds = 300.0; factionScore = 18.0; playerFunds = 150.0; playerScore = 10.0 })
            (StartObj 'StartPALA_Push3' @('PALA_Push3'))
            (ShowMsg 'Msg_PalaPush3' 'PALA LOYALIST NET: Their factory belt is broken. Hold what we took.' $true $true)
            (GiveScore 'GiveScore_PALA_Push3' @{ factionFunds = 350.0; factionScore = 20.0; playerFunds = 175.0; playerScore = 12.0 })
            (ModFaction 'ModFaction_PALA_Push3' @{ reserveWarheads = (Ov $true 24.0); killReward = (Ov $true 3.8) })
        )
    }

    # ------------------------------------------------------ score-gated escalation
    # Escalation gates track each faction's cumulative successful-sortie score (additive), so the
    # side that pushes hardest crosses them first; the 625/1225 steps match the warhead gates.
    ScoreGates = @{
        Objectives = @(
            (SortieObj 'ScoreGate_BDF_200'  'Boscali' 200.0  $true @('Msg_Tier200','Spawn_BDF_Tier2','ModFaction_BDF_Tier2'))
            (SortieObj 'ScoreGate_BDF_400'  'Boscali' 400.0  $true @('Msg_Tier400','Spawn_BDF_Tier3'))
            (SortieObj 'ScoreGate_BDF_625'  'Boscali' 625.0  $true @('Msg_Tier625BDF','ModFaction_BDF_Tier3'))
            (SortieObj 'ScoreGate_BDF_850'  'Boscali' 850.0  $true @('Msg_Tier850','Spawn_BDF_Tier4'))
            (SortieObj 'ScoreGate_BDF_1225' 'Boscali' 1225.0 $true @('Msg_Tier1225BDF','ModFaction_BDF_Strategic2'))
            (SortieObj 'ScoreGate_PALA_200'  'Primeva' 200.0  $true @('Msg_Tier200','Spawn_PALA_Tier2','ModFaction_PALA_Tier2'))
            (SortieObj 'ScoreGate_PALA_400'  'Primeva' 400.0  $true @('Msg_Tier400','Spawn_PALA_Tier3'))
            (SortieObj 'ScoreGate_PALA_625'  'Primeva' 625.0  $true @('Msg_Tier625PALA','ModFaction_PALA_Tier3'))
            (SortieObj 'ScoreGate_PALA_850'  'Primeva' 850.0  $true @('Msg_Tier850','Spawn_PALA_Tier4'))
            (SortieObj 'ScoreGate_PALA_1225' 'Primeva' 1225.0 $true @('Msg_Tier1225PALA','ModFaction_PALA_Strategic2'))
        )
        Outcomes = @(
            (ShowMsg 'Msg_Tier200' 'WAR DESK: First two hundred on the board. The front is moving.' $true $false)
            (ShowMsg 'Msg_Tier400' 'WAR DESK: Commit reserves. Escalation tier two is open.' $true $false)
            (ShowMsg 'Msg_Tier625BDF' 'WAR DESK: BDF passes six-two-five. Tactical warhead release authorised.' $true $false)
            (ShowMsg 'Msg_Tier625PALA' 'WAR DESK: PALA passes six-two-five. Tactical warhead release authorised.' $true $false)
            (ShowMsg 'Msg_Tier850' 'WAR DESK: Deep strikes reported. Escalation tier four is open.' $true $false)
            (ShowMsg 'Msg_Tier1225BDF' 'WAR DESK: BDF at twelve-two-five. Strategic release. Everything is on the table.' $true $false)
            (ShowMsg 'Msg_Tier1225PALA' 'WAR DESK: PALA at twelve-two-five. Strategic release. Everything is on the table.' $true $false)
            (ModFaction 'ModFaction_BDF_Tier2' @{ reserveAirframes = (Ov $true 2.0); regularIncome = (Ov $true 2.4) })
            (ModFaction 'ModFaction_BDF_Tier3' @{ reserveWarheads = (Ov $true 24.0); warheadsReserve = (Ov $true 24.0) })
            (ModFaction 'ModFaction_BDF_Strategic2' @{ aiAircraftLimit = (Ov $true 7.0); addAIPerEnemyPlayer = (Ov $true 2.0) })
            (ModFaction 'ModFaction_PALA_Tier2' @{ reserveAirframes = (Ov $true 2.0); regularIncome = (Ov $true 2.2) })
            (ModFaction 'ModFaction_PALA_Tier3' @{ reserveWarheads = (Ov $true 24.0); warheadsReserve = (Ov $true 24.0) })
            (ModFaction 'ModFaction_PALA_Strategic2' @{ aiAircraftLimit = (Ov $true 7.0); addAIPerEnemyPlayer = (Ov $true 2.0) })
        )
    }
}

# ---------------------------------------------------------------- wave construction

$unitArrays = @{
    aircraft = $root['aircraft']; vehicles = $root['vehicles']; ships = $root['ships']
    buildings = $root['buildings']; scenery = $root['scenery']; containers = $root['containers']
    missiles = $root['missiles']; pilots = $root['pilots']
}

$allUnitNames = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($k in $unitArrays.Keys) {
    foreach ($u in $unitArrays[$k]) { [void]$allUnitNames.Add([string]$u['UniqueName']) }
}

$airbaseCenters = @{}
foreach ($ab in $root['airbases']) { $airbaseCenters[[string]$ab['UniqueName']] = $ab['Center'] }

$waves = @{}
$newUnitJson = @{}
foreach ($k in $unitArrays.Keys) { $newUnitJson[$k] = New-Object System.Collections.Generic.List[string] }
foreach ($w in $WaveData) {
    if ($waves.ContainsKey($w.Name)) { throw "Duplicate wave name '$($w.Name)'" }
    $arr = $unitArrays[$w.Kind]
    $names = New-Object System.Collections.Generic.List[string]
    $slot = 0
    foreach ($entry in $w.Entries) {
        $count = 1
        if ($entry.ContainsKey('Count')) { $count = [int]$entry['Count'] }
        for ($i = 0; $i -lt $count; $i++) {
            $name = "$($w.Name)_$($slot + 1)"
            if ($allUnitNames.Contains($name)) { throw "Generated unit name collision: $name" }
            [void]$allUnitNames.Add($name)

            $proto = $arr | Where-Object { $_.type -eq $entry.Type -and $_.faction -eq $w.Faction } | Select-Object -First 1
            if (-not $proto) { $proto = $arr | Where-Object { $_.type -eq $entry.Type } | Select-Object -First 1 }
            if (-not $proto) { throw "No baseline prototype for type '$($entry.Type)' in '$($w.Kind)'" }
            # the prototype is read as plain PS objects so its shape (rotation, loadout, capture
            # overrides) can be copied field for field into the new SavedUnit
            $protoPs = $proto.ToString([Newtonsoft.Json.Formatting]::None) | ConvertFrom-Json

            if ($entry.ContainsKey('At')) {
                $pos = $entry['At']
            }
            else {
                if (-not $airbaseCenters.ContainsKey($w.Anchor)) { throw "Unknown wave anchor airbase '$($w.Anchor)'" }
                $c = $airbaseCenters[$w.Anchor]
                $cx = [double]$c['x']
                $cy = [double]$c['y']
                $cz = [double]$c['z']
                # spread the wave in a loose pad around the airbase so units do not stack
                # (each element is parenthesised: PowerShell's comma operator binds tighter than *)
                $pos = @(
                    ($cx + (($slot % 5) - 2) * 55.0),
                    ($cy),
                    ($cz + 110.0 + ([math]::Floor($slot / 5) * 80.0))
                )
            }

            $unit = [ordered]@{
                type = [string]$entry.Type
                faction = $w.Faction
                UniqueName = $name
                globalPosition = [ordered]@{ x = $pos[0]; y = $pos[1]; z = $pos[2] }
                rotation = $protoPs.rotation
                CaptureStrength = $protoPs.CaptureStrength
                CaptureDefense = $protoPs.CaptureDefense
            }
            if ($w.Kind -eq 'aircraft') {
                $unit['playerControlled'] = $false
                $unit['playerControlledPriority'] = 0
                $unit['savedLoadout'] = $protoPs.savedLoadout
                $unit['livery'] = $protoPs.livery
                $unit['liveryType'] = $protoPs.liveryType
                $unit['liveryName'] = $protoPs.liveryName
                $unit['fuel'] = 1.0
                $unit['skill'] = 1.0
                $unit['bravery'] = 0.5
                $unit['startingSpeed'] = 0.0
            }
            else {
                $unit['holdPosition'] = $true
                $unit['skill'] = $protoPs.skill
                $unit['waypoints'] = @()
            }

            $newUnitJson[$w.Kind].Add((ConvertTo-JsonText $unit))
            $names.Add($name)
            $slot++
        }
    }
    $waves[$w.Name] = $names.ToArray()
}

# Each authored wave gets exactly one auto-generated SpawnUnit outcome named after the wave, so
# the timeline can simply name the wave it fires.
$waveBlock = @{ Objectives = @(); Outcomes = @() }
foreach ($w in $WaveData) { $waveBlock.Outcomes += (SpawnObj $w.Name $waves[$w.Name]) }
$Timeline['Waves'] = $waveBlock

# -------------------------------------------------- reference collection + checks

function Get-ObjectiveNames {
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($block in $Timeline.Values) {
        foreach ($o in $block.Objectives) { $names.Add([string]$o['UniqueName']) }
    }
    return $names
}
function Get-OutcomeNames {
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($block in $Timeline.Values) {
        foreach ($o in $block.Outcomes) { $names.Add([string]$o['UniqueName']) }
    }
    return $names
}

$objectiveNames = Get-ObjectiveNames
$outcomeNames = Get-OutcomeNames
foreach ($n in $objectiveNames) { if ($outcomeNames -contains $n) { throw "Name '$n' is used by both an objective and an outcome" } }
$dupes = $objectiveNames + $outcomeNames | Group-Object | Where-Object Count -gt 1
if ($dupes) { throw "Duplicate authored names: $($dupes.Name -join ', ')" }
$objectiveSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($n in $objectiveNames) { [void]$objectiveSet.Add($n) }
$outcomeSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($n in $outcomeNames) { [void]$outcomeSet.Add($n) }
$airbaseNames = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($ab in $root['airbases']) { [void]$airbaseNames.Add([string]$ab['UniqueName']) }

function Resolve-SpawnList([string[]]$Tokens, [string]$Owner) {
    $list = New-Object System.Collections.Generic.List[string]
    foreach ($t in $Tokens) {
        if ($t.StartsWith('wave:')) {
            $wn = $t.Substring(5)
            if (-not $waves.ContainsKey($wn)) { throw "$Owner references unknown wave '$wn'" }
            foreach ($u in $waves[$wn]) { $list.Add($u) }
        }
        elseif ($t.Contains('*')) {
            $matched = $allUnitNames | Where-Object { $_ -like $t } | Sort-Object
            if (-not $matched) { throw "$Owner pattern '$t' matched no saved unit" }
            foreach ($u in $matched) { $list.Add($u) }
        }
        else {
            if (-not $allUnitNames.Contains($t)) { throw "$Owner references missing unit '$t'" }
            $list.Add($t)
        }
    }
    return $list.ToArray()
}

foreach ($block in $Timeline.Values) {
    foreach ($o in $block.Outcomes) {
        $oName = [string]$o['UniqueName']
        switch ([string]$o['Type']) {
            'SpawnUnit' { $o['UnitsToSpawn'] = @(Resolve-SpawnList $o['UnitsToSpawn'] $oName) }
            'StartObjective' {
                foreach ($t in $o['objectivesToStart']) { if (-not $objectiveSet.Contains([string]$t)) { throw "$oName starts unknown objective '$t'" } }
            }
            'StopOrCompleteObjective' {
                foreach ($t in $o['objectivesToStart']) { if (-not $objectiveSet.Contains([string]$t)) { throw "$oName stops unknown objective '$t'" } }
            }
            'ModifyAirbase' {
                if (-not $airbaseNames.Contains([string]$o['airbase'])) { throw "$oName modifies unknown airbase '$($o['airbase'])'" }
            }
        }
    }
    foreach ($obj in $block.Objectives) {
        $oName = [string]$obj['UniqueName']
        foreach ($t in $obj['Outcomes']) { if (-not $outcomeSet.Contains([string]$t)) { throw "$oName fires unknown outcome '$t'" } }
        switch ([string]$obj['Type']) {
            'DestroyUnits' { foreach ($t in $obj['targetUnits']) { if (-not $allUnitNames.Contains([string]$t)) { throw "$oName targets missing unit '$t'" } } }
            'CaptureAirbase' { foreach ($t in $obj['targetAirbases']) { if (-not $airbaseNames.Contains([string]$t)) { throw "$oName targets unknown airbase '$t'" } } }
            'CompleteOtherObjective' { foreach ($t in $obj['targetObjectives']) { if (-not $objectiveSet.Contains([string]$t)) { throw "$oName depends on unknown objective '$t'" } } }
            'WaitSeconds' { if ([double]$obj['seconds'] -le 0.0) { throw "$oName has non-positive seconds" } }
        }
    }
}

# Spawn bookkeeping: a unit may only be spawned once (SpawnUnitOutcome ignores HasSpawned repeats).
$spawnCount = 0
$seenSpawn = @{}
foreach ($block in $Timeline.Values) {
    foreach ($o in $block.Outcomes) {
        if ([string]$o['Type'] -ne 'SpawnUnit') { continue }
        $list = @($o['UnitsToSpawn'])
        if ($list.Count -gt 24) { throw "$($o['UniqueName']) spawns $($list.Count) units (ceiling is 24)" }
        foreach ($u in $list) {
            if ($seenSpawn.ContainsKey($u)) { throw "Unit '$u' is spawned by both '$($seenSpawn[$u])' and '$($o['UniqueName'])'" }
            $seenSpawn[$u] = [string]$o['UniqueName']
        }
        $spawnCount += $list.Count
    }
}
if ($spawnCount -ge 400) { throw "Total spawned units $spawnCount exceeds the mission ceiling of 400" }

# Every authored wave must be referenced; every unit added by the builder must be spawned later.
foreach ($wn in $waves.Keys) {
    $referenced = $false
    foreach ($block in $Timeline.Values) {
        foreach ($o in $block.Outcomes) {
            if ([string]$o['Type'] -eq 'SpawnUnit' -and ($o['UnitsToSpawn'] -contains $waves[$wn][0])) { $referenced = $true }
        }
    }
    if (-not $referenced) { throw "Wave '$wn' is never spawned by any outcome" }
}

# ------------------------------------------------------------------ apply + write

# battlefield geometry the game itself authored is carried over verbatim
$baselineSettings = $root['missionSettings'].ToString([Newtonsoft.Json.Formatting]::None) | ConvertFrom-Json
$ms = [ordered]@{}
foreach ($k in $MissionSettings.Keys) { $ms[$k] = $MissionSettings[$k] }
$ms['cameraStartPosition'] = $baselineSettings.cameraStartPosition
$ms['missionRoads'] = $baselineSettings.missionRoads
$ms['missionSeaLanes'] = $baselineSettings.missionSeaLanes
$ms['wrecksMaxNumber'] = $baselineSettings.wrecksMaxNumber
$ms['wrecksDecayTime'] = $baselineSettings.wrecksDecayTime
$root['missionSettings'] = [Newtonsoft.Json.Linq.JObject]::Parse((ConvertTo-JsonText $ms))
$root['environment'] = [Newtonsoft.Json.Linq.JObject]::Parse((ConvertTo-JsonText $Environment))

foreach ($f in $root['factions']) {
    $tweaks = $FactionTweaks[[string]$f['factionName']]
    foreach ($k in $tweaks.Keys) { $f[$k] = [Newtonsoft.Json.Linq.JToken]::Parse((ConvertTo-JsonText $tweaks[$k])) }
}

# append the authored wave units to the baseline unit arrays
foreach ($k in $unitArrays.Keys) {
    if ($newUnitJson[$k].Count -eq 0) { continue }
    $existing = $root[$k].ToString([Newtonsoft.Json.Formatting]::None).Trim()
    $combined = $existing.Substring(0, $existing.Length - 1) + ',' + ($newUnitJson[$k] -join ',') + ']'
    $root[$k] = [Newtonsoft.Json.Linq.JArray]::Parse($combined)
}

$objJson = New-Object System.Collections.Generic.List[string]
foreach ($block in $Timeline.Values) { foreach ($o in $block.Objectives) { $objJson.Add((ConvertTo-JsonText $o)) } }
$outJson = New-Object System.Collections.Generic.List[string]
foreach ($block in $Timeline.Values) { foreach ($o in $block.Outcomes) { $outJson.Add((ConvertTo-JsonText $o)) } }
$root['objectives'] = [Newtonsoft.Json.Linq.JArray]::Parse('[' + ($objJson -join ',') + ']')
$root['outcomes'] = [Newtonsoft.Json.Linq.JArray]::Parse('[' + ($outJson -join ',') + ']')

# LF line endings, like the game's own mission text assets
$json = [Newtonsoft.Json.JsonConvert]::SerializeObject($root, [Newtonsoft.Json.Formatting]::Indented).Replace("`r`n", "`n")
$target = Join-Path $outFull 'Boscali Summer.json'
New-Item -ItemType Directory -Path $outFull -Force | Out-Null
[System.IO.File]::WriteAllText($target, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))

# --------------------------------------------------------------------- reporting

$counts = @{}
foreach ($k in 'aircraft','vehicles','ships','buildings','scenery','containers') { $counts[$k] = @($root[$k]).Count }
$waitBeats = 0; $spawnOutcomes = 0; $envBeats = 0; $airbaseBeats = 0; $messages = 0; $sorties = 0
foreach ($block in $Timeline.Values) {
    foreach ($o in $block.Objectives) {
        switch ([string]$o['Type']) { 'WaitSeconds' { $waitBeats++ } 'SuccessfulSortie' { $sorties++ } }
    }
    foreach ($o in $block.Outcomes) {
        switch ([string]$o['Type']) { 'SpawnUnit' { $spawnOutcomes++ } 'ModifyEnvironment' { $envBeats++ } 'ModifyAirbase' { $airbaseBeats++ } 'ShowMessage' { $messages++ } }
    }
}

Write-Host "Wrote $target"
Write-Host ("  objectives={0} outcomes={1} aircraft={2} vehicles={3} ships={4} buildings={5} airbases={6}" -f `
    @($root['objectives']).Count, @($root['outcomes']).Count, $counts['aircraft'], $counts['vehicles'], $counts['ships'], $counts['buildings'], @($root['airbases']).Count)
Write-Host ("  waitSeconds={0} spawnOutcomes={1} spawnedUnits={2} modifyEnvironment={3} modifyAirbase={4} showMessage={5} scoreGates={6} size={7}" -f `
    $waitBeats, $spawnOutcomes, $spawnCount, $envBeats, $airbaseBeats, $messages, $sorties, (Get-Item -LiteralPath $target).Length)
