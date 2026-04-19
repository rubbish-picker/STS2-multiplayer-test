[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$scriptPath = Join-Path $PSScriptRoot "Invoke-OneModPublish.ps1"
& $scriptPath -ModFolderName "BalanceTheSpire" -ProjectFileName "BalanceTheSpire.csproj" -SkipGodotImport:$SkipGodotImport
