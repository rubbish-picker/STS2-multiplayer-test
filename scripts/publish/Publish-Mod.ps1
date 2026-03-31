[CmdletBinding()]
param(
    [switch]$SkipGodotImport
)

$ErrorActionPreference = "Stop"

$currentScriptPath = $MyInvocation.MyCommand.Path
$publishScripts = Get-ChildItem -LiteralPath $PSScriptRoot -Filter "Publish-*.ps1" -File |
    Where-Object { $_.FullName -ne $currentScriptPath } |
    Sort-Object Name

if ($publishScripts.Count -eq 0) {
    throw "No publish scripts found in $PSScriptRoot"
}

Write-Host ""
Write-Host "============================================"
Write-Host "  Publishing All Targets"
Write-Host "============================================"
Write-Host ""

foreach ($script in $publishScripts) {
    Write-Host "[RUN] $($script.Name)" -ForegroundColor Cyan
    $commandInfo = Get-Command $script.FullName
    if ($commandInfo.Parameters.ContainsKey("SkipGodotImport")) {
        & $script.FullName -SkipGodotImport:$SkipGodotImport
    }
    else {
        & $script.FullName
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Publish script failed: $($script.FullName)"
    }
}

Write-Host ""
Write-Host "[OK] All publish scripts completed."
Write-Host ""
