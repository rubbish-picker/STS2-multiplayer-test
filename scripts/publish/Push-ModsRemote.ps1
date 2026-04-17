[CmdletBinding()]
param(
    [string]$GameDirectory,
    [string]$Remote = "origin",
    [string]$Branch,
    [string]$CommitMessage,
    [switch]$AllowAnyUser
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host ("[GIT] git " + ($Arguments -join " ")) -ForegroundColor Cyan
    & git -C $WorkingDirectory @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git command failed: git $($Arguments -join ' ')"
    }
}

function Get-GitOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & git -C $WorkingDirectory @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return ""
    }

    return (($output | Out-String).Trim())
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$configPath = Join-Path $repoRoot "config.json"

if (-not $AllowAnyUser -and $env:USERNAME -ne "27940") {
    throw "Current Windows user '$($env:USERNAME)' is not authorized. Use -AllowAnyUser to bypass."
}

if ([string]::IsNullOrWhiteSpace($GameDirectory) -and (Test-Path -LiteralPath $configPath)) {
    $config = Get-Content -LiteralPath $configPath -Encoding UTF8 | ConvertFrom-Json
    if ($config -and $config.sts2_path) {
        $GameDirectory = [string]$config.sts2_path
    }
}

if ([string]::IsNullOrWhiteSpace($GameDirectory)) {
    throw "Game directory is required. Pass -GameDirectory or set sts2_path in config.json."
}

$GameDirectory = [System.IO.Path]::GetFullPath($GameDirectory)

if (-not (Test-Path -LiteralPath $GameDirectory -PathType Container)) {
    throw "Game directory not found: $GameDirectory"
}

if (-not (Test-Path -LiteralPath (Join-Path $GameDirectory ".git") -PathType Container)) {
    throw "Game directory is not a git repository: $GameDirectory"
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git was not found in PATH."
}

if ([string]::IsNullOrWhiteSpace($Branch)) {
    $Branch = Get-GitOutput -WorkingDirectory $GameDirectory -Arguments @("branch", "--show-current")
    if ([string]::IsNullOrWhiteSpace($Branch)) {
        $Branch = "main"
    }
}

$status = Get-GitOutput -WorkingDirectory $GameDirectory -Arguments @("status", "--porcelain", "--", "mods", ".gitignore")
$hasChanges = -not [string]::IsNullOrWhiteSpace($status)

Write-Host ""
Write-Host "============================================"
Write-Host "  Push Mods Remote"
Write-Host "============================================"
Write-Host ""
Write-Host "[INFO] GameDir: $GameDirectory"
Write-Host "[INFO] Remote:  $Remote"
Write-Host "[INFO] Branch:  $Branch"
Write-Host "[INFO] Scope:   mods, .gitignore"

if ($hasChanges) {
    if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
        $CommitMessage = "Mod update $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    }

    Write-Host "[INFO] Local changes detected. Creating commit..." -ForegroundColor Yellow
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("add", "-A", "--", "mods", ".gitignore")
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("commit", "-m", $CommitMessage)
}
else {
    Write-Host "[INFO] No local changes under mods/.gitignore. Pushing current HEAD..." -ForegroundColor Yellow
}

Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("push", $Remote, "HEAD:$Branch")

Write-Host ""
if ($hasChanges) {
    Write-Host "[OK] Commit created and pushed to $Remote/$Branch." -ForegroundColor Green
}
else {
    Write-Host "[OK] Push attempted to $Remote/$Branch with no new local changes." -ForegroundColor Green
}
Write-Host ""
