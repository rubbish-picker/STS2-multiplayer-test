[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Invoke-OneModPublish.ps1"
& $scriptPath -ModFolderName "BetaDirectConnect" -ProjectFileName "BetaDirectConnect.csproj" -SkipGodotImport:$SkipGodotImport
