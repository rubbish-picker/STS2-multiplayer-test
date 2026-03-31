[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Invoke-OneModPublish.ps1"
& $scriptPath -ModFolderName "MultiplayerCard" -ProjectFileName "MultiplayerCard.csproj" -SkipGodotImport:$SkipGodotImport
