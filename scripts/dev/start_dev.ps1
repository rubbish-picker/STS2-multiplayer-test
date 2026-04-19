param(
    [string]$GameDir = "D:\steam\steamapps\common\Slay the Spire 2",
    [switch]$FullLog,
    [string]$ProfileSettingsPath = (Join-Path $env:APPDATA "SlayTheSpire2\default\1\settings.save"),
    [int]$TestApiPort = 0,
    [string]$TestApiHost = "127.0.0.1"
)

$ErrorActionPreference = "Stop"

$gameExe = Join-Path $GameDir "SlayTheSpire2.exe"
$logDir = Join-Path $env:APPDATA "SlayTheSpire2\logs"
$activeLog = Join-Path $logDir "godot.log"

function Ensure-AgentTestApiEnabled {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        throw "Settings file not found: $SettingsPath"
    }

    $settings = Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json
    if ($null -eq $settings.mod_settings) {
        throw "Settings file is missing mod_settings: $SettingsPath"
    }

    $settings.mod_settings.mods_enabled = $true
    $modList = @($settings.mod_settings.mod_list)
    $agentTestApi = @($modList | Where-Object { $_.id -eq "AgentTestApi" }) | Select-Object -First 1

    if ($null -eq $agentTestApi) {
        $modList += [pscustomobject]@{
            id = "AgentTestApi"
            is_enabled = $true
            source = "mods_directory"
        }
        $settings.mod_settings.mod_list = $modList
        Write-Host "Added AgentTestApi to mod list for default/1." -ForegroundColor Yellow
    }
    elseif (-not [bool]$agentTestApi.is_enabled) {
        $agentTestApi.is_enabled = $true
        Write-Host "Enabled AgentTestApi for default/1." -ForegroundColor Yellow
    }

    $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $SettingsPath -Encoding UTF8
}

if (-not (Test-Path -LiteralPath $gameExe)) {
    throw "SlayTheSpire2.exe not found: $gameExe"
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

Ensure-AgentTestApiEnabled -SettingsPath $ProfileSettingsPath

$existingProcesses = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
if ($existingProcesses) {
    Write-Host "Stopping existing SlayTheSpire2 instances..." -ForegroundColor Yellow
    $existingProcesses | Stop-Process -Force
    Start-Sleep -Milliseconds 1200
}

Write-Host "== STS2 Singleplayer Dev Launch ==" -ForegroundColor Cyan
Write-Host "GameDir: $GameDir"
Write-Host "LogDir:  $logDir"
Write-Host "Settings: $ProfileSettingsPath"
Write-Host ""
Write-Host "Starting Slay the Spire 2 without Steam..." -ForegroundColor Yellow

$argumentList = @("--force-steam", "off")
if ($TestApiPort -gt 0) {
    $argumentList += @("--testapiport", $TestApiPort.ToString(), "--testapihost", $TestApiHost)
}

$proc = Start-Process -FilePath $gameExe -ArgumentList $argumentList -WorkingDirectory $GameDir -PassThru

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
