[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Publish-Mod.ps1"
& $scriptPath -ModFolderName "BetaDirectConnect" -ProjectFileName "BetaDirectConnect.csproj" -SkipGodotImport:$SkipGodotImport
