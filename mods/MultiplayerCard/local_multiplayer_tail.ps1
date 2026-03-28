param(
    [ValidateSet("host", "client", "merged")]
    [string]$Role = "merged",
    [string]$LogPath = "",
    [switch]$FullLog
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = Join-Path $env:APPDATA "SlayTheSpire2\logs\godot.log"
}

$title = switch ($Role) {
    "host" { "STS2 Host Log" }
    "client" { "STS2 Client Log" }
    default { "STS2 Merged Log" }
}

$Host.UI.RawUI.WindowTitle = $title

while (-not (Test-Path -LiteralPath $LogPath)) {
    Start-Sleep -Milliseconds 300
}

$commonPattern = '\[MultiplayerCard\]|\[ai-event\]|\[BaseLib\]|Disconnect|Divergence|Exception|ERROR|WARN'
$hostPattern = '\[ENetHost\]|Host|StartHost|host_standard|ClientLobbyJoinResponse|player 1000|net ID 1000'
$clientPattern = '\[ENetClient\]|\[JoinFlow\]|join|clientId 1001|net ID 1001'

Write-Host "== $title ==" -ForegroundColor Cyan
Write-Host "LogPath: $LogPath"
Write-Host "Press Ctrl+C to close this log window."
Write-Host ""

Get-Content -LiteralPath $LogPath -Wait -Tail 0 | ForEach-Object {
    $line = $_

    if ($FullLog) {
        Write-Host $line
        return
    }

    $show = $false

    if ($line -match $commonPattern) {
        $show = $true
    }
    elseif ($Role -eq "host" -and $line -match $hostPattern) {
        $show = $true
    }
    elseif ($Role -eq "client" -and $line -match $clientPattern) {
        $show = $true
    }
    elseif ($Role -eq "merged" -and $line -match ($hostPattern + "|" + $clientPattern)) {
        $show = $true
    }

    if (-not $show) {
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
    elseif ($Role -eq "host" -and $line -match $hostPattern) {
        Write-Host $line -ForegroundColor Green
    }
    elseif ($Role -eq "client" -and $line -match $clientPattern) {
        Write-Host $line -ForegroundColor Blue
    }
    else {
        Write-Host $line -ForegroundColor Gray
    }
}
