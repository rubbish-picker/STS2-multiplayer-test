[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SkipGameCopy
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot "ModTheSpire\ModTheSpire.csproj"
$configPath = Join-Path $repoRoot "config.json"
$publishRoot = Join-Path $repoRoot "temp_mod_output\ModTheSpireLauncher"
$publishDir = Join-Path $publishRoot $Runtime
$gameDir = $null
$gameExeOutput = $null

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

if (Test-Path -LiteralPath $configPath) {
    $config = Get-Content -LiteralPath $configPath -Encoding UTF8 | ConvertFrom-Json
    if ($config -and $config.sts2_path) {
        $gameDir = [string]$config.sts2_path
    }
}

if (-not $SkipGameCopy -and -not [string]::IsNullOrWhiteSpace($gameDir)) {
    $gameExeOutput = Join-Path $gameDir "ModTheSpire.exe"
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$args = @(
    "publish",
    $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $publishDir
)

Write-Host ""
Write-Host "============================================"
Write-Host "  Publishing ModTheSpire"
Write-Host "============================================"
Write-Host ""
Write-Host "[INFO] Project: $project"
Write-Host "[INFO] Output:  $publishDir"
Write-Host "[INFO] Runtime: $Runtime"
if ($gameExeOutput) {
    Write-Host "[INFO] GameExe: $gameExeOutput"
}

& dotnet @args
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}

$publishedExe = Join-Path $publishDir "ModTheSpire.exe"
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Published exe not found: $publishedExe"
}

if ($gameExeOutput) {
    Copy-Item -LiteralPath $publishedExe -Destination $gameExeOutput -Force
}

Write-Host ""
Write-Host "[OK] Publish complete."
Write-Host "[PATH] $publishDir"
if ($gameExeOutput) {
    Write-Host "[GAME] $gameExeOutput"
}
Write-Host ""
