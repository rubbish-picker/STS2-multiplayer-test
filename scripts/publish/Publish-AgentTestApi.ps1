[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Invoke-OneModPublish.ps1"
& $scriptPath -ModFolderName "AgentTestApi" -ProjectFileName "AgentTestApi.csproj" -SkipGodotImport:$SkipGodotImport
