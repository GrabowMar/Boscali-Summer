param([string]$AtlasOut, [string]$AtlasStyles, [string]$ProjectDir, [switch]$Watch)
& (Join-Path $PSScriptRoot 'Run-AtlasGroup.ps1') -Group 'comenv-overlays' @PSBoundParameters
