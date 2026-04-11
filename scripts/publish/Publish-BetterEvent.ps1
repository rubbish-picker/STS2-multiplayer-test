[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Invoke-OneModPublish.ps1"
& $scriptPath -ModFolderName "BetterEvent" -ProjectFileName "BetterEvent.csproj" -SkipGodotImport:$SkipGodotImport
