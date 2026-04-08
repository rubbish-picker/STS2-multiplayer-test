[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Invoke-OneModPublish.ps1"
& $scriptPath -ModFolderName "WatcherExtension" -ProjectFileName "WatcherExtension.csproj" -SkipGodotImport:$SkipGodotImport
