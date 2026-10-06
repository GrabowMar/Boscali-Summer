# Shared skeleton for the Run-*UnityCheck.ps1 runners: scratch Unity project, game DLL copy, editor launch, result gate.
# Dot-source it from a runner (the relative path depends on the runner's folder depth):
#   . "$PSScriptRoot/../../../UnityCheck.Common.ps1"
# It defines $repo (the checkout this file lives in) and $UnityCheckGame, plus the helpers below.
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$UnityCheckGame = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option'
# The C# helpers every *UnityCheck.cs shares; a runner copies it next to the check that uses it.
$UnityCheckHarness = "$repo/tests/BoscaliSummer.Tests/UnityCheckHarness.cs"

# Named package manifests and DLL filters that several runners share verbatim.
$UnityCheckManifest = @{
    KitFull = '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.animation":"1.0.0"}}'
    MfdFull = '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
}
$UnityCheckFilter = @{
    Managed = '^(System|Mono\.|UnityEngine|UnityEditor|mscorlib|netstandard|Unity\.TextMeshPro|Unity\.Timeline|Unity\.VisualScripting)'
    ManagedWide = '^(System|Mono|Microsoft|UnityEngine|UnityEditor|mscorlib|netstandard|Unity.TextMeshPro|Accessibility|Novell)'
    BepInEx = '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)'
}

# Creates the project folders ($Folders are relative to $Dir) and writes ProjectVersion.txt and manifest.json.
function New-UnityCheckProject([string]$Dir, [string]$Manifest, [string[]]$Folders = @(), [string]$EditorVersion = '2022.3.62f3') {
    New-Item -ItemType Directory -Force -Path (@('Assets', 'ProjectSettings', 'Packages') + $Folders | ForEach-Object { "$Dir/$_" }) | Out-Null
    Set-Content -LiteralPath "$Dir/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: $EditorVersion"
    Set-Content -LiteralPath "$Dir/Packages/manifest.json" -Value $Manifest
}

# Copies the game's managed DLLs (minus $ManagedExclude) and the BepInEx/Harmony core DLLs matching $BepInExMatch into $Dest.
function Copy-GameDlls([string]$Dest, [string]$ManagedExclude, [string]$BepInExMatch) {
    if ($ManagedExclude) {
        Get-ChildItem -LiteralPath "$UnityCheckGame/NuclearOption_Data/Managed" -Filter '*.dll' | Where-Object { $_.Name -notmatch $ManagedExclude } | Copy-Item -Destination $Dest
    }
    if ($BepInExMatch) {
        Get-ChildItem -LiteralPath "$UnityCheckGame/BepInEx/core" -Filter '*.dll' | Where-Object { $_.Name -match $BepInExMatch } | Copy-Item -Destination $Dest
    }
}

# The shipped stylesheets; -AtlasStyles (a nomod panels folder) overrides them. -IfExists skips a missing NOAvionics folder.
function Copy-AvionicsStyles([string]$Project, [string]$AtlasStyles, [switch]$IfExists, [switch]$NoDefaults) {
    if (-not $NoDefaults) { Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter 'avionics.*.avss' | Copy-Item -Destination "$Project/NOAvionics/" }
    if ($AtlasStyles -and (-not $IfExists -or (Test-Path -LiteralPath "$AtlasStyles/NOAvionics"))) {
        Get-ChildItem -LiteralPath (Join-Path $AtlasStyles 'NOAvionics') -Filter '*.avss' | Copy-Item -Destination "$Project/NOAvionics/" -Force
    }
}

# Hash manifest of everything the fixture compiles/runs ($Roots), written next to the project.
function Export-UnityCheckHashes([string]$Path, $Files) {
    $Files | Get-FileHash | Select-Object Hash, Path | Export-Csv -LiteralPath $Path -NoTypeInformation
}

# Starts the editor on $Project with -executeMethod $Method and waits for it. Returns the process.
# -Flags are the leading editor switches; -TimeoutSeconds > 0 kills the editor and throws $TimeoutMessage.
function Invoke-UnityCheck([string]$Unity, [string]$Project, [string]$Method, [string[]]$Flags = @('-batchmode', '-disable-assembly-updater'),
    [string[]]$ExtraArguments = @(), [string]$LogName = 'check.log', [string]$Result = 'result.txt',
    [int]$TimeoutSeconds = 0, [string]$TimeoutMessage = "Unity check timed out: $Project/$LogName", [switch]$InheritWorkingDirectory) {
    if (Test-Path -LiteralPath "$Project/$Result") { Remove-Item -LiteralPath "$Project/$Result" }
    $arguments = $Flags + @('-projectPath', ('"' + $Project + '"'), '-executeMethod', $Method, '-logFile', ('"' + "$Project/$LogName" + '"')) + $ExtraArguments
    $start = @{ FilePath = $Unity; ArgumentList = $arguments; WindowStyle = 'Hidden'; PassThru = $true }
    if (-not $InheritWorkingDirectory) { $start.WorkingDirectory = $Project }
    $process = Start-Process @start
    Wait-UnityProcess $process $TimeoutSeconds $TimeoutMessage
    $process
}

# Runs a built player exe the same way and returns the exited process.
function Invoke-UnityPlayer([string]$Exe, [string[]]$Arguments, [string]$WorkingDirectory, [int]$TimeoutSeconds = 0, [string]$TimeoutMessage = "Player timed out: $Exe") {
    $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru
    Wait-UnityProcess $process $TimeoutSeconds $TimeoutMessage
    $process
}

# The requested project/evidence directory (full path), or a fresh temp folder named $Prefix + guid.
function Resolve-UnityCheckDir([string]$Requested, [string]$Prefix) {
    if ($Requested) { [IO.Path]::GetFullPath($Requested) } else { Join-Path $env:TEMP ($Prefix + [guid]::NewGuid().ToString('N')) }
}

# WaitForExit with an optional timeout in seconds (0 = forever); a timeout kills the process and throws $Message.
function Wait-UnityProcess($Process, [int]$TimeoutSeconds, [string]$Message) {
    if ($TimeoutSeconds -le 0) { $Process.WaitForExit(); return }
    if (-not $Process.WaitForExit($TimeoutSeconds * 1000)) { $Process.Kill(); throw $Message }
}

# Prints the result file, or the log tail when the editor never wrote one.
function Show-UnityCheckResult([string]$Project, [string]$Result = 'result.txt', [int]$LogTail = 60, [string]$LogName = 'check.log') {
    if (Test-Path -LiteralPath "$Project/$Result") { Get-Content -LiteralPath "$Project/$Result" }
    else { Get-Content -LiteralPath "$Project/$LogName" -Tail $LogTail }
}

# Throws unless the editor exited 0 and $Result exists and matches $Pattern (empty pattern: existence only).
function Assert-UnityResult([string]$Project, $Process, [string]$Pattern = '\APASS:', [string]$Result = 'result.txt',
    [string]$ExitMessage, [string]$FailMessage = "Unity exited without a successful result: $Project") {
    if ($Process.ExitCode -ne 0) { throw $ExitMessage }
    if (-not (Test-Path -LiteralPath "$Project/$Result")) { throw $FailMessage }
    if ($Pattern -and (Get-Content -LiteralPath "$Project/$Result" -Raw) -notmatch $Pattern) { throw $FailMessage }
}
