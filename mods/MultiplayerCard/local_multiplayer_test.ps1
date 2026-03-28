param(
    [string]$GameDir = "D:\steam\steamapps\common\Slay the Spire 2",
    [ValidateSet("auto", "manual")]
    [string]$Mode = "manual",
    [switch]$SeparateWindows,
    [switch]$FullLog
)

$ErrorActionPreference = "Stop"

$logDir = Join-Path $env:APPDATA "SlayTheSpire2\logs"
$saveRoot = Join-Path $env:APPDATA "SlayTheSpire2"
$launchBat = Join-Path $GameDir "launch_no_steam.bat"
$gameExe = Join-Path $GameDir "SlayTheSpire2.exe"
$activeLog = Join-Path $logDir "godot.log"
$tailScript = Join-Path $PSScriptRoot "local_multiplayer_tail.ps1"

if (-not (Test-Path -LiteralPath $launchBat)) {
    throw "launch_no_steam.bat not found: $launchBat"
}

if (-not (Test-Path -LiteralPath $gameExe)) {
    throw "SlayTheSpire2.exe not found: $gameExe"
}

if (-not (Test-Path -LiteralPath $tailScript)) {
    throw "tail script not found: $tailScript"
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Remove-TestRunArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    if (-not (Test-Path -LiteralPath $RootPath)) {
        return @()
    }

    $patterns = @(
        "current_run_mp.save",
        "MultiplayerCard.current_run_mp.config",
        "ai-event.current_run_mp.session.json"
    )

    $removed = New-Object System.Collections.Generic.List[string]

    foreach ($pattern in $patterns) {
        $files = Get-ChildItem -Path $RootPath -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue
        foreach ($file in $files) {
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction SilentlyContinue
            if (-not (Test-Path -LiteralPath $file.FullName)) {
                $removed.Add($file.FullName)
            }
        }
    }

    return $removed
}

$existingProcesses = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
if ($existingProcesses) {
    Write-Host "Stopping existing SlayTheSpire2 instances..." -ForegroundColor Yellow
    $existingProcesses | Stop-Process -Force
    Start-Sleep -Milliseconds 1200
}

$removedArtifacts = Remove-TestRunArtifacts -RootPath $saveRoot

Write-Host "== Multiplayer local test ==" -ForegroundColor Cyan
Write-Host "GameDir: $GameDir"
Write-Host "LogDir:  $logDir"
Write-Host "Mode:    $Mode"
Write-Host "Split:   $SeparateWindows"
if ($removedArtifacts.Count -gt 0) {
    Write-Host "Cleaned stale multiplayer saves/configs:" -ForegroundColor DarkYellow
    foreach ($path in $removedArtifacts) {
        Write-Host "  $path"
    }
}
Write-Host ""
Write-Host "Starting two no-steam instances..." -ForegroundColor Yellow

if ($Mode -eq "auto") {
    $hostArgs = @("--force-steam", "off", "--fastmp", "host_standard", "--clientId", "1000")
    $joinArgs = @("--force-steam", "off", "--fastmp", "join", "--clientId", "1001")

    $proc1 = Start-Process -FilePath $gameExe -ArgumentList $hostArgs -WorkingDirectory $GameDir -PassThru
    Start-Sleep -Milliseconds 1200
    $proc2 = Start-Process -FilePath $gameExe -ArgumentList $joinArgs -WorkingDirectory $GameDir -PassThru

    Write-Host "Auto fastmp enabled:" -ForegroundColor Cyan
    Write-Host "  Host  args: $($hostArgs -join ' ')"
    Write-Host "  Client args: $($joinArgs -join ' ')"
    Write-Host "The host should open multiplayer host flow automatically."
    Write-Host "The client should open join flow automatically and connect to 127.0.0.1."
}
else {
    $proc1 = Start-Process -FilePath $launchBat -WorkingDirectory $GameDir -PassThru
    Start-Sleep -Milliseconds 900
    $proc2 = Start-Process -FilePath $launchBat -WorkingDirectory $GameDir -PassThru

    Write-Host "Manual mode enabled."
    Write-Host "Host in one client and join 127.0.0.1 in the other."
}

Write-Host "Started PID $($proc1.Id) and PID $($proc2.Id)." -ForegroundColor Green

while (-not (Test-Path -LiteralPath $activeLog)) {
    Start-Sleep -Milliseconds 300
}

if ($SeparateWindows) {
    $hostTailArgs = @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-File", $tailScript,
        "-Role", "host",
        "-LogPath", $activeLog
    )

    $clientTailArgs = @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-File", $tailScript,
        "-Role", "client",
        "-LogPath", $activeLog
    )

    if ($FullLog) {
        $hostTailArgs += "-FullLog"
        $clientTailArgs += "-FullLog"
    }

    Start-Process -FilePath "powershell.exe" -ArgumentList $hostTailArgs | Out-Null
    Start-Process -FilePath "powershell.exe" -ArgumentList $clientTailArgs | Out-Null

    Write-Host "Opened split log windows:" -ForegroundColor Cyan
    Write-Host "  STS2 Host Log"
    Write-Host "  STS2 Client Log"
    Write-Host "This launcher terminal is free now."
}
else {
    Write-Host "Merged log mode enabled in current terminal."
    Write-Host "Press Ctrl+C to stop log tailing. The game processes will keep running."
    Write-Host ""

    & $tailScript -Role merged -LogPath $activeLog -FullLog:$FullLog
}
