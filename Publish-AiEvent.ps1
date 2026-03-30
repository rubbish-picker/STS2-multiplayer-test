[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Publish-Mod.ps1"
& $scriptPath -ModFolderName "ai-event" -ProjectFileName "ai-event.csproj" -SkipGodotImport:$SkipGodotImport
