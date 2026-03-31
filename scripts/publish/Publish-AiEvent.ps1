[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Invoke-OneModPublish.ps1"
& $scriptPath -ModFolderName "ai-event" -ProjectFileName "ai-event.csproj" -SkipGodotImport:$SkipGodotImport
