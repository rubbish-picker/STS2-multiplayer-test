[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Publish-Mod.ps1"
& $scriptPath -ModFolderName "MultiplayerCard" -ProjectFileName "MultiplayerCard.csproj" -SkipGodotImport:$SkipGodotImport
