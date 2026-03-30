[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ModFolderName,

    [Parameter(Mandatory = $true)]
    [string]$ProjectFileName,

    [switch]$SkipGodotImport
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $repoRoot "config.json"
$projectDir = Join-Path $repoRoot (Join-Path "mods" $ModFolderName)
$projectFile = Join-Path $projectDir $ProjectFileName

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "Project file not found: $projectFile"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found in PATH."
}

$config = $null
if (Test-Path -LiteralPath $configPath) {
    $config = Get-Content -LiteralPath $configPath -Encoding UTF8 | ConvertFrom-Json
}

$godotPath = if ($config -and $config.godot_exe_path) { [string]$config.godot_exe_path } else { "" }
$sts2Path = if ($config -and $config.sts2_path) { [string]$config.sts2_path } else { "" }
$steamLibraryPath = ""

if ($sts2Path) {
    $steamCommonDir = Split-Path -Parent $sts2Path
    $steamLibraryPath = Split-Path -Parent $steamCommonDir
}

Write-Host ""
Write-Host "============================================"
Write-Host "  Publishing $ModFolderName"
Write-Host "============================================"
Write-Host ""
Write-Host "[INFO] Project: $projectFile"

if (-not $SkipGodotImport) {
    if ($godotPath -and (Test-Path -LiteralPath $godotPath)) {
        Write-Host "[INFO] Reimporting Godot assets..."
        & $godotPath --headless --path $projectDir --import
        if ($LASTEXITCODE -ne 0) {
            throw "Godot asset import failed."
        }
    }
    elseif ($godotPath) {
        Write-Warning "Configured Godot executable not found: $godotPath"
    }
    else {
        Write-Warning "godot_exe_path is not configured in config.json. Skipping asset import."
    }
}

$publishArgs = @("publish", $projectFile)
if ($steamLibraryPath) {
    $publishArgs += "-p:SteamLibraryPath=$steamLibraryPath"
}
if ($godotPath -and (Test-Path -LiteralPath $godotPath)) {
    $publishArgs += "-p:GodotPath=$godotPath"
}

Write-Host "[INFO] Running: dotnet $($publishArgs -join ' ')"
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed. If the DLL is locked, close Slay the Spire 2 and try again."
}

$outputPath = if ($sts2Path) {
    Join-Path (Join-Path $sts2Path "mods") $ModFolderName
}
else {
    "<mods path unknown>"
}

Write-Host ""
Write-Host "[OK] Publish complete."
Write-Host "[PATH] $outputPath"
Write-Host ""
