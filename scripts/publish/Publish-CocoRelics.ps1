[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Invoke-OneModPublish.ps1"
& $scriptPath -ModFolderName "CocoRelics" -ProjectFileName "CocoRelics.csproj" -SkipGodotImport:$SkipGodotImport
