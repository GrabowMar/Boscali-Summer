param([string]$AtlasOut, [string]$AtlasStyles, [string]$ProjectDir, [switch]$Watch)
& "$PSScriptRoot/Run-AtlasGroup.ps1" -Group winghud @PSBoundParameters
