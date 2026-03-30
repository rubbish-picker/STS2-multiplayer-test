[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Publish-Mod.ps1"
& $scriptPath -ModFolderName "CocoRelics" -ProjectFileName "CocoRelics.csproj" -SkipGodotImport:$SkipGodotImport
