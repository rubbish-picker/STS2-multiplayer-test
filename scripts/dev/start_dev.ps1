param(
    [string]$GameDir = "D:\steam\steamapps\common\Slay the Spire 2",
    [switch]$FullLog
)

$ErrorActionPreference = "Stop"

$gameExe = Join-Path $GameDir "SlayTheSpire2.exe"
$logDir = Join-Path $env:APPDATA "SlayTheSpire2\logs"
$activeLog = Join-Path $logDir "godot.log"

if (-not (Test-Path -LiteralPath $gameExe)) {
    throw "SlayTheSpire2.exe not found: $gameExe"
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$existingProcesses = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
if ($existingProcesses) {
    Write-Host "Stopping existing SlayTheSpire2 instances..." -ForegroundColor Yellow
    $existingProcesses | Stop-Process -Force
    Start-Sleep -Milliseconds 1200
}

Write-Host "== STS2 Singleplayer Dev Launch ==" -ForegroundColor Cyan
Write-Host "GameDir: $GameDir"
Write-Host "LogDir:  $logDir"
Write-Host ""
Write-Host "Starting Slay the Spire 2 without Steam..." -ForegroundColor Yellow

$proc = Start-Process -FilePath $gameExe -ArgumentList @("--force-steam", "off") -WorkingDirectory $GameDir -PassThru

Write-Host "Started PID $($proc.Id)." -ForegroundColor Green
Write-Host "Merged log mode enabled in current terminal."
Write-Host "Press Ctrl+C to stop log tailing. The game process will keep running."
Write-Host ""

while (-not (Test-Path -LiteralPath $activeLog)) {
    Start-Sleep -Milliseconds 300
}

$pattern = '\[MultiplayerCard\]|\[ai-event\]|\[BaseLib\]|Exception|ERROR|WARN|Disconnect|Divergence'

Get-Content -LiteralPath $activeLog -Wait -Tail 0 | ForEach-Object {
    $line = $_

    if (-not $FullLog -and $line -notmatch $pattern) {
        return
    }

    if ($line -match 'ERROR|Exception|Divergence') {
        Write-Host $line -ForegroundColor Red
    }
    elseif ($line -match 'WARN|Disconnect') {
        Write-Host $line -ForegroundColor Yellow
    }
    elseif ($line -match '\[MultiplayerCard\]') {
        Write-Host $line -ForegroundColor Cyan
    }
    elseif ($line -match '\[ai-event\]') {
        Write-Host $line -ForegroundColor Magenta
    }
    elseif ($line -match '\[BaseLib\]') {
        Write-Host $line -ForegroundColor DarkCyan
    }
    else {
        Write-Host $line -ForegroundColor Gray
    }
}
