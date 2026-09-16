#Requires -Version 5.1
<#
.SYNOPSIS
    Validates the "Boscali Summer" custom mission JSON.

.DESCRIPTION
    Independent static gate: checks the schema the game's MissionLoader expects, every
    cross-reference between objectives / outcomes / units / airbases, the dynamism budget the
    mission is authored to, and the mission folder/name contract. Prints every failure and exits
    non-zero when any check fails.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\validate-boscali-summer-mission.ps1
#>
[CmdletBinding()]
param(
    [string]$MissionFile,
    [string]$NewtonsoftPath
)
$ErrorActionPreference = 'Stop'

if (-not $MissionFile) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $MissionFile = Join-Path $repoRoot 'missions\Boscali Summer\Boscali Summer.json'
}
if (-not (Test-Path -LiteralPath $MissionFile)) { throw "Mission file not found: $MissionFile" }

if (-not $NewtonsoftPath) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Nuclear Option\NuclearOption_Data\Managed\Newtonsoft.Json.dll'),
        (Join-Path $env:ProgramFiles 'Steam\steamapps\common\Nuclear Option\NuclearOption_Data\Managed\Newtonsoft.Json.dll')
    )
    $NewtonsoftPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $NewtonsoftPath -or -not (Test-Path -LiteralPath $NewtonsoftPath)) {
    throw 'Newtonsoft.Json.dll not found. Pass -NewtonsoftPath <game Managed folder copy>.'
}
if (-not ([System.Management.Automation.PSTypeName]'Newtonsoft.Json.JsonConvert').Type) { Add-Type -Path $NewtonsoftPath }

$json = [System.IO.File]::ReadAllText($MissionFile)
$m = [Newtonsoft.Json.Linq.JObject]::Parse($json)

$failures = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]
function Fail([string]$Message) { $script:failures.Add($Message) | Out-Null }
function Note([string]$Message) { $script:notes.Add($Message) | Out-Null }
function Has($Obj, [string]$Key) { return ($null -ne $Obj[$Key]) }

# ------------------------------------------------------------------ 1. file / folder contract
$expectedName = 'Boscali Summer'
if ([System.IO.Path]::GetFileNameWithoutExtension($MissionFile) -ne $expectedName) {
    Fail "Mission file name is '$([System.IO.Path]::GetFileName($MissionFile))'; the game derives Mission.Name from it and requires '$expectedName.json'"
}
if ((Split-Path -Leaf (Split-Path -Parent $MissionFile)) -ne $expectedName) {
    Fail "Mission folder is not named '$expectedName' (the game derives Mission.Name from the folder)"
}

# ------------------------------------------------------------------ 2. root shape
$rootFailuresBefore = $failures.Count
$rootOrder = @('JsonVersion','MapKey','missionSettings','environment','aircraft','vehicles','ships','buildings',
               'scenery','containers','missiles','pilots','factions','airbases','objectives','outcomes')
$declared = @($m.Properties() | ForEach-Object { $_.Name })
if (($declared -join ',') -ne ($rootOrder -join ',')) {
    Fail "Root keys are '$($declared -join ',')'; expected '$($rootOrder -join ',')'"
}
if ([int]$m['JsonVersion'] -ne 6) { Fail "JsonVersion is '$($m['JsonVersion'])'; expected 6" }

foreach ($k in 'aircraft','vehicles','ships','buildings','scenery','containers','missiles','pilots') {
    if ($null -eq $m[$k] -or $m[$k].Type -ne [Newtonsoft.Json.Linq.JTokenType]::Array) { Fail "Root '$k' must be an array" }
}
foreach ($k in 'factions','airbases','objectives','outcomes') {
    if ($null -eq $m[$k] -or $m[$k].Type -ne [Newtonsoft.Json.Linq.JTokenType]::Array -or @($m[$k]).Count -eq 0) { Fail "Root '$k' must be a non-empty array" }
}
if ($failures.Count -gt $rootFailuresBefore) { throw 'Mission root shape is invalid; skipping deeper checks.' }

# ------------------------------------------------------------------ 3. mission settings
$ms = $m['missionSettings']
$requiredSettings = @('description','allowEventContent','Tags','playerMode','allowRespawn','playerStartingRank',
                      'rankMultiplier','successfulSortieBonus','nuclearEscalationThreshold','strategicEscalationThreshold',
                      'minRankTacticalWarhead','minRankStrategicWarhead','cameraStartPosition','missionRoads',
                      'missionSeaLanes','wrecksMaxNumber','wrecksDecayTime')
foreach ($k in $requiredSettings) { if (-not (Has $ms $k)) { Fail "missionSettings is missing '$k'" } }
if ($ms['playerMode'] -ne 'SingleAndMultiplayer') { Fail "missionSettings.playerMode is '$($ms['playerMode'])'; expected SingleAndMultiplayer" }
if (-not [bool]$ms['allowRespawn']) { Fail 'missionSettings.allowRespawn must be true' }
if ([double]$ms['nuclearEscalationThreshold'] -ne 625.0) { Fail 'nuclearEscalationThreshold must stay 625' }
if ([double]$ms['strategicEscalationThreshold'] -ne 1225.0) { Fail 'strategicEscalationThreshold must stay 1225' }
$desc = [string]$ms['description']
if ($desc.Length -gt 600) { Fail "description is $($desc.Length) chars; keep it under 600 so the briefing page reads without scrolling" }
Note "description length: $($desc.Length) chars"

# ------------------------------------------------------------------ 4. environment
foreach ($k in 'timeOfDay','timeFactor','weatherIntensity','cloudAltitude','windSpeed','windTurbulence','windHeading','windRandomHeading','moonPhase') {
    if (-not (Has $m['environment'] $k)) { Fail "environment is missing '$k'" }
}
if ([double]$m['environment']['timeOfDay'] -lt 0 -or [double]$m['environment']['timeOfDay'] -gt 24) { Fail 'environment.timeOfDay must be within 0..24' }
if ([double]$m['environment']['weatherIntensity'] -lt 0 -or [double]$m['environment']['weatherIntensity'] -gt 1) { Fail 'environment.weatherIntensity must be within 0..1' }

# ------------------------------------------------------------------ 5. factions
$factionNames = @($m['factions'] | ForEach-Object { [string]$_['factionName'] })
foreach ($expected in 'Boscali','Primeva') {
    $f = $m['factions'] | Where-Object { [string]$_['factionName'] -eq $expected } | Select-Object -First 1
    if (-not $f) { Fail "faction '$expected' is missing"; continue }
    if ([bool]$f['preventJoin']) { Fail "faction '$expected' has preventJoin=true; the mission must stay joinable on both sides" }
    if ([bool]$f['preventDonation']) { Fail "faction '$expected' has preventDonation=true" }
    foreach ($k in 'startingBalance','playerTaxRate','regularIncome','killReward','reserveWarheads','reserveAirframes',
                   'AIAircraftLimit','reduceAIPerFriendlyPlayer','addAIPerEnemyPlayer','startingWarheads','supplies') {
        if (-not (Has $f $k)) { Fail "faction '$expected' is missing '$k'" }
    }
}

# ------------------------------------------------------------------ 6. unit index
$unitNames = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($kind in 'aircraft','vehicles','ships','buildings','scenery','containers','missiles','pilots') {
    foreach ($u in $m[$kind]) {
        $name = [string]$u['UniqueName']
        if ([string]::IsNullOrWhiteSpace($name)) { Fail "$kind entry without UniqueName (type '$($u['type'])' at line-of-file $($u.LineNumber))"; continue }
        if (-not $unitNames.Add($name)) { Fail "duplicate unit UniqueName '$name' in '$kind'" }
        foreach ($k in 'type','faction','UniqueName','globalPosition') {
            if (-not (Has $u $k)) { Fail "unit '$name' is missing required SavedUnit field '$k'" }
        }
    }
}
Note "saved units: aircraft=$(@($m['aircraft']).Count) vehicles=$(@($m['vehicles']).Count) ships=$(@($m['ships']).Count) buildings=$(@($m['buildings']).Count) scenery=$(@($m['scenery']).Count) containers=$(@($m['containers']).Count)"

# ------------------------------------------------------------------ 7. airbase index
$airbases = @{}
foreach ($ab in $m['airbases']) {
    $name = [string]$ab['UniqueName']
    if ([string]::IsNullOrWhiteSpace($name)) { Fail 'airbase entry without UniqueName'; continue }
    if ($airbases.ContainsKey($name)) { Fail "duplicate airbase UniqueName '$name'" }
    $airbases[$name] = $ab
}

# ------------------------------------------------------------------ 8. objective / outcome indexes
$objectiveTypes = @('None','DestroyUnits','ReachUnits','ReachWaypoints','WaitSeconds','CaptureAirbase','DialogueBox',
                    'CompleteOtherObjective','SpotUnit','CrashAircraft','SuccessfulSortie')
$outcomeTypes = @('None','StartObjective','StopOrCompleteObjective','ShowMessage','GiveScore','SpawnUnit','RemoveUnit',
                  'RevealUnit','EndGame','ModifyAirbase','ModifyEnvironment','ModifyFaction')
$completeOrders = @('CompleteAny','CompleteAll','InOrder','CompleteSome')
$changeTypes = @('Add','Subtract','Set')

$objFields = @{
    'None'                 = @('UniqueName','Faction','DisplayName','Hidden','Outcomes')
    'DestroyUnits'         = @('completeOrder','completeSomePercent','targetUnits')
    'ReachUnits'           = @('completeOrder','completeSomePercent','targets')
    'ReachWaypoints'       = @('completeOrder','completeSomePercent','waypoints')
    'WaitSeconds'          = @('seconds')
    'CaptureAirbase'       = @('completeOrder','completeSomePercent','targetAirbases')
    'DialogueBox'          = @('title','body','button','factionOnly')
    'CompleteOtherObjective' = @('completeOrder','completeSomePercent','targetObjectives')
    'SpotUnit'             = @('completeOrder','completeSomePercent','targetUnits')
    'CrashAircraft'        = @('livesPerPlayer','extraLives','includeDestroy','includeEject')
    'SuccessfulSortie'     = @('minimumScore','additive')
}
$outFields = @{
    'None'                   = @()
    'StartObjective'         = @('objectivesToStart')
    'StopOrCompleteObjective'= @('options','objectivesToStart')
    'ShowMessage'            = @('Message','PlaySound','ObjectiveFactionOnly')
    'GiveScore'              = @('bothFactions','playerFundsType','playerFunds','factionFundsType','factionFunds',
                                 'playerScoreType','playerScore','rankType','rank','factionScoreType','factionScore')
    'SpawnUnit'              = @('UnitsToSpawn')
    'RemoveUnit'             = @('UnitsToRemove')
    'RevealUnit'             = @('UnitsToReveal')
    'EndGame'                = @('endType','endDelay')
    'ModifyAirbase'          = @('airbase','faction','disabled','capturable','captureDefense')
    'ModifyEnvironment'      = @('timeOfDay','weather','cloudAltitude','windSpeed','windTurbulence','windHeading')
    'ModifyFaction'          = @('bothFactions','excessFundsThreshold','playerJoinAllowance','playerTaxRate','regularIncome',
                                 'killReward','preventDonation','aiAircraftLimit','reduceAIPerFriendlyPlayer','addAIPerEnemyPlayer',
                                 'warheadsReserve','reserveAirframes','extraReservesPerPlayer','excessFundsDistributePercent','preventJoin')
}

$objectives = @{}
$outcomes = @{}
foreach ($o in $m['objectives']) {
    $name = [string]$o['UniqueName']
    $type = [string]$o['Type']
    if ([string]::IsNullOrWhiteSpace($name)) { Fail 'objective without UniqueName'; continue }
    if ($objectiveTypes -notcontains $type) { Fail "objective '$name' has unknown Type '$type'"; continue }
    if ($outcomes.ContainsKey($name)) { Fail "'$name' is used by both an objective and an outcome (they share one namespace)" }
    if ($objectives.ContainsKey($name)) { Fail "duplicate objective UniqueName '$name'" }
    foreach ($k in 'UniqueName','Faction','DisplayName','Hidden','Outcomes') { if (-not (Has $o $k)) { Fail "objective '$name' ($type) is missing '$k'" } }
    foreach ($k in $objFields[$type]) { if (-not (Has $o $k)) { Fail "objective '$name' ($type) is missing '$k'" } }
    $objectives[$name] = $o
}
foreach ($o in $m['outcomes']) {
    $name = [string]$o['UniqueName']
    $type = [string]$o['Type']
    if ([string]::IsNullOrWhiteSpace($name)) { Fail 'outcome without UniqueName'; continue }
    if ($outcomeTypes -notcontains $type) { Fail "outcome '$name' has unknown Type '$type'"; continue }
    if ($objectives.ContainsKey($name)) { Fail "'$name' is used by both an objective and an outcome (they share one namespace)" }
    if ($outcomes.ContainsKey($name)) { Fail "duplicate outcome UniqueName '$name'" }
    foreach ($k in 'UniqueName') { if (-not (Has $o $k)) { Fail "outcome '$name' is missing '$k'" } }
    foreach ($k in $outFields[$type]) { if (-not (Has $o $k)) { Fail "outcome '$name' ($type) is missing '$k'" } }
    $outcomes[$name] = $o
}

# ------------------------------------------------------------------ 9. objective checks
foreach ($name in $objectives.Keys) {
    $o = $objectives[$name]
    $type = [string]$o['Type']
    $faction = [string]$o['Faction']
    if ($faction -ne '' -and $factionNames -notcontains $faction) { Fail "objective '$name' names unknown faction '$faction'" }
    if ($type -in 'CaptureAirbase','ReachUnits','ReachWaypoints','SpotUnit' -and $faction -eq '') {
        Fail "objective '$name' ($type) requires a faction"
    }
    if ($type -eq 'DialogueBox' -and [bool]$o['factionOnly'] -and $faction -eq '') { Fail "objective '$name' is a faction-only dialogue box without a faction" }
    if ($type -eq 'DialogueBox' -and ([string]$o['body']).Length -gt 2000) { Fail "objective '$name' dialogue body is suspiciously long" }

    foreach ($k in $o['Outcomes']) {
        $kn = [string]$k
        if (-not $outcomes.ContainsKey($kn)) { Fail "objective '$name' fires unknown outcome '$kn'" }
    }

    switch ($type) {
        'DestroyUnits' {
            if (@($o['targetUnits']).Count -eq 0) { Fail "objective '$name' has an empty targetUnits" }
            if ($completeOrders -notcontains [string]$o['completeOrder']) { Fail "objective '$name' has invalid completeOrder '$($o['completeOrder'])'" }
            if ([double]$o['completeSomePercent'] -le 0 -or [double]$o['completeSomePercent'] -gt 1) { Fail "objective '$name' completeSomePercent out of range" }
            $seen = New-Object 'System.Collections.Generic.HashSet[string]'
            foreach ($t in $o['targetUnits']) {
                $tn = [string]$t
                if (-not $unitNames.Contains($tn)) { Fail "objective '$name' targets absent unit '$tn'" }
                if (-not $seen.Add($tn)) { Fail "objective '$name' lists unit '$tn' twice" }
            }
        }
        'SpotUnit' {
            foreach ($t in $o['targetUnits']) { if (-not $unitNames.Contains([string]$t)) { Fail "objective '$name' spots absent unit '$t'" } }
        }
        'CaptureAirbase' {
            if (@($o['targetAirbases']).Count -eq 0) { Fail "objective '$name' has an empty targetAirbases" }
            if ($completeOrders -notcontains [string]$o['completeOrder']) { Fail "objective '$name' has invalid completeOrder" }
            foreach ($t in $o['targetAirbases']) {
                $tn = [string]$t
                if (-not $airbases.ContainsKey($tn)) { Fail "objective '$name' targets unknown airbase '$tn'"; continue }
                if ([bool]$airbases[$tn]['Disabled']) { Fail "objective '$name' targets disabled airbase '$tn'" }
            }
        }
        'CompleteOtherObjective' {
            if (@($o['targetObjectives']).Count -eq 0) { Fail "objective '$name' has an empty targetObjectives" }
            if ($completeOrders -notcontains [string]$o['completeOrder']) { Fail "objective '$name' has invalid completeOrder" }
            foreach ($t in $o['targetObjectives']) {
                $tn = [string]$t
                if (-not $objectives.ContainsKey($tn)) { Fail "objective '$name' waits on unknown objective '$tn'" }
                if ($tn -eq $name) { Fail "objective '$name' waits on itself" }
            }
        }
        'WaitSeconds' {
            $seconds = [double]$o['seconds']
            if ($seconds -le 0) { Fail "objective '$name' has WaitSeconds <= 0 ($seconds)" }
        }
        'SuccessfulSortie' {
            if ([double]$o['minimumScore'] -lt 0) { Fail "objective '$name' has negative minimumScore" }
        }
        'ReachWaypoints' {
            if (@($o['waypoints']).Count -eq 0) { Fail "objective '$name' has an empty waypoints list" }
        }
        'CrashAircraft' {
            if ([int]$o['livesPerPlayer'] -lt 0) { Fail "objective '$name' has negative livesPerPlayer" }
        }
    }
}

# ------------------------------------------------------------------ 10. outcome checks
$spawnedUnits = New-Object 'System.Collections.Generic.HashSet[string]'
$spawnCount = 0
$seenSpawn = @{}
foreach ($name in $outcomes.Keys) {
    $o = $outcomes[$name]
    $type = [string]$o['Type']
    switch ($type) {
        'StartObjective' {
            foreach ($t in $o['objectivesToStart']) {
                if (-not $objectives.ContainsKey([string]$t)) { Fail "outcome '$name' starts unknown objective '$t'" }
            }
        }
        'StopOrCompleteObjective' {
            if ([string]$o['options'] -notin 'Start','Stop','Complete') { Fail "outcome '$name' has invalid options '$($o['options'])'" }
            foreach ($t in $o['objectivesToStart']) {
                if (-not $objectives.ContainsKey([string]$t)) { Fail "outcome '$name' stops/complete unknown objective '$t'" }
            }
        }
        'ShowMessage' {
            $msg = [string]$o['Message']
            if ([string]::IsNullOrWhiteSpace($msg)) { Fail "outcome '$name' has an empty message" }
            elseif ($msg.Length -ge 120) { Fail "outcome '$name' message is $($msg.Length) chars (limit 120): $msg" }
        }
        'GiveScore' {
            foreach ($k in 'playerFundsType','factionFundsType','playerScoreType','rankType','factionScoreType') {
                if ($changeTypes -notcontains [string]$o[$k]) { Fail "outcome '$name' has invalid $k '$($o[$k])'" }
            }
        }
        'SpawnUnit' {
            $list = @($o['UnitsToSpawn'])
            if ($list.Count -eq 0) { Fail "outcome '$name' spawns nothing" }
            if ($list.Count -gt 24) { Fail "outcome '$name' spawns $($list.Count) units; the per-wave ceiling is 24" }
            foreach ($t in $list) {
                $tn = [string]$t
                if (-not $unitNames.Contains($tn)) { Fail "outcome '$name' spawns absent unit '$tn'"; continue }
                if (-not $spawnedUnits.Add($tn)) { Fail "unit '$tn' is spawned more than once ('$($seenSpawn[$tn])' and '$name'); the game only spawns it once" }
                $seenSpawn[$tn] = $name
            }
            $spawnCount += $list.Count
        }
        'RemoveUnit' { foreach ($t in $o['UnitsToRemove']) { if (-not $unitNames.Contains([string]$t)) { Fail "outcome '$name' removes absent unit '$t'" } } }
        'RevealUnit' { foreach ($t in $o['UnitsToReveal']) { if (-not $unitNames.Contains([string]$t)) { Fail "outcome '$name' reveals absent unit '$t'" } } }
        'EndGame' {
            if ([string]$o['endType'] -notin 'Victory','Defeat') { Fail "outcome '$name' has invalid endType '$($o['endType'])'" }
            if ([double]$o['endDelay'] -lt 0) { Fail "outcome '$name' has negative endDelay" }
        }
        'ModifyAirbase' {
            $ab = [string]$o['airbase']
            if (-not $airbases.ContainsKey($ab)) { Fail "outcome '$name' modifies unknown airbase '$ab'" }
            foreach ($k in 'faction','disabled','capturable','captureDefense') {
                $ov = $o[$k]
                if ($null -eq $ov -or -not (Has $ov 'IsOverride') -or -not (Has $ov 'Value')) { Fail "outcome '$name' has a malformed Override in '$k'" }
            }
            if ((Has $o['faction'] 'IsOverride') -and [bool]$o['faction']['IsOverride']) {
                if ($factionNames -notcontains [string]$o['faction']['Value']) { Fail "outcome '$name' force-captures for unknown faction '$($o['faction']['Value'])'" }
            }
        }
        'ModifyEnvironment' {
            foreach ($k in 'timeOfDay','weather','cloudAltitude','windSpeed','windTurbulence','windHeading') {
                $ov = $o[$k]
                if ($null -eq $ov -or -not (Has $ov 'IsOverride') -or -not (Has $ov 'Value')) { Fail "outcome '$name' has a malformed Override in '$k'" }
            }
            $tod = $o['timeOfDay']
            if ([bool]$tod['IsOverride'] -and ([double]$tod['Value'] -lt 0 -or [double]$tod['Value'] -gt 24)) { Fail "outcome '$name' timeOfDay override out of range" }
            $w = $o['weather']
            if ([bool]$w['IsOverride'] -and ([double]$w['Value'] -lt 0 -or [double]$w['Value'] -gt 1)) { Fail "outcome '$name' weather override out of range" }
        }
        'ModifyFaction' {
            foreach ($k in 'excessFundsThreshold','playerJoinAllowance','playerTaxRate','regularIncome','killReward','preventDonation',
                           'aiAircraftLimit','reduceAIPerFriendlyPlayer','addAIPerEnemyPlayer','warheadsReserve','reserveAirframes',
                           'extraReservesPerPlayer','excessFundsDistributePercent','preventJoin') {
                $ov = $o[$k]
                if ($null -eq $ov -or -not (Has $ov 'IsOverride') -or -not (Has $ov 'Value')) { Fail "outcome '$name' has a malformed Override in '$k'" }
            }
        }
    }
}
if ($spawnCount -ge 400) { Fail "mission spawns $spawnCount units in total; the ceiling is 400" }

# ------------------------------------------------------------------ 11. timeline / dynamism budget
$counts = @{}
foreach ($k in 'WaitSeconds','SuccessfulSortie','DestroyUnits','CaptureAirbase','CompleteOtherObjective') { $counts[$k] = 0 }
foreach ($o in $m['objectives']) { if ($counts.ContainsKey([string]$o['Type'])) { $counts[[string]$o['Type']]++ } }
$outCounts = @{}
foreach ($k in 'SpawnUnit','ShowMessage','ModifyEnvironment','ModifyAirbase','ModifyFaction','GiveScore','EndGame') { $outCounts[$k] = 0 }
foreach ($o in $m['outcomes']) { if ($outCounts.ContainsKey([string]$o['Type'])) { $outCounts[[string]$o['Type']]++ } }
if ($counts['WaitSeconds'] -lt 14) { Fail "only $($counts['WaitSeconds']) WaitSeconds beats; the timeline needs at least 14" }
if ($outCounts['SpawnUnit'] -lt 8) { Fail "only $($outCounts['SpawnUnit']) SpawnUnit waves; the timeline needs at least 8" }
if ($outCounts['ModifyEnvironment'] -lt 4) { Fail "only $($outCounts['ModifyEnvironment']) ModifyEnvironment beats; need at least 4" }
if ($outCounts['ModifyAirbase'] -lt 3) { Fail "only $($outCounts['ModifyAirbase']) ModifyAirbase beats; need at least 3" }
if ($outCounts['ShowMessage'] -lt 12) { Fail "only $($outCounts['ShowMessage']) ShowMessage beats; need at least 12" }
if ($counts['SuccessfulSortie'] -lt 5) { Fail "only $($counts['SuccessfulSortie']) score-gated objectives; need at least 5" }
if ($counts['CompleteOtherObjective'] -lt 2) { Fail 'the mission needs CompleteOtherObjective chains' }
if ($outCounts['EndGame'] -lt 1) { Fail 'the mission has no EndGame outcome' }
if ($outCounts['GiveScore'] -lt 1 -and $outCounts['ModifyFaction'] -lt 1) { Fail 'the mission never shifts faction economy' }

# every objective should be reachable: started from a StartObjective somewhere (or Playable)
$startedSomewhere = New-Object 'System.Collections.Generic.HashSet[string]'
[void]$startedSomewhere.Add('Mission Start')
foreach ($o in $m['outcomes']) { if ([string]$o['Type'] -eq 'StartObjective') { foreach ($t in $o['objectivesToStart']) { [void]$startedSomewhere.Add([string]$t) } } }
foreach ($name in $objectives.Keys) {
    if (-not $startedSomewhere.Contains($name)) { Fail "objective '$name' is never started by any outcome" }
}

# ------------------------------------------------------------------ 12. report
if ($failures.Count -gt 0) {
    Write-Host "VALIDATION FAILED ($($failures.Count) problems):"
    $failures | ForEach-Object { Write-Host "  FAIL: $_" }
    exit 1
}
Write-Host 'VALIDATION PASSED'
Write-Host "  file            : $MissionFile"
foreach ($n in $notes) { Write-Host "  $n" }
Write-Host "  objectives      : $(@($m['objectives']).Count)"
Write-Host "  outcomes        : $(@($m['outcomes']).Count)"
Write-Host "  airbases        : $(@($m['airbases']).Count)"
Write-Host "  spawned units   : $spawnCount across $($outCounts['SpawnUnit']) SpawnUnit outcomes"
Write-Host "  waitSeconds=$($counts['WaitSeconds']) scoreGates=$($counts['SuccessfulSortie']) destroyUnits=$($counts['DestroyUnits']) captureAirbase=$($counts['CaptureAirbase']) completeOther=$($counts['CompleteOtherObjective'])"
Write-Host "  showMessage=$($outCounts['ShowMessage']) modifyEnvironment=$($outCounts['ModifyEnvironment']) modifyAirbase=$($outCounts['ModifyAirbase']) modifyFaction=$($outCounts['ModifyFaction']) giveScore=$($outCounts['GiveScore'])"
exit 0
