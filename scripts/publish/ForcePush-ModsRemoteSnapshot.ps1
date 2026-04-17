[CmdletBinding()]
param(
    [string]$GameDirectory,
    [string]$Branch,
    [string]$Remote = "origin",
    [string]$RemoteUrl,
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

function Get-GitLines {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & git -C $WorkingDirectory @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return @()
    }

    return @($output | ForEach-Object { "$_".Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Resolve-PreferredBranch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [string]$RemoteName = "origin"
    )

    $upstream = Get-GitOutput -WorkingDirectory $WorkingDirectory -Arguments @("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}")
    if (-not [string]::IsNullOrWhiteSpace($upstream) -and $upstream.StartsWith("$RemoteName/")) {
        return $upstream.Substring($RemoteName.Length + 1)
    }

    $remoteHead = Get-GitOutput -WorkingDirectory $WorkingDirectory -Arguments @("symbolic-ref", "--short", "refs/remotes/$RemoteName/HEAD")
    if (-not [string]::IsNullOrWhiteSpace($remoteHead) -and $remoteHead.StartsWith("$RemoteName/")) {
        return $remoteHead.Substring($RemoteName.Length + 1)
    }

    $currentBranch = Get-GitOutput -WorkingDirectory $WorkingDirectory -Arguments @("branch", "--show-current")
    if (-not [string]::IsNullOrWhiteSpace($currentBranch)) {
        return $currentBranch
    }

    return "main"
}

function Assert-OnlyAllowedFilesStaged {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory
    )

    $stagedFiles = Get-GitLines -WorkingDirectory $WorkingDirectory -Arguments @("diff", "--cached", "--name-only")
    $unexpectedFiles = @(
        $stagedFiles | Where-Object {
            $_ -ne ".gitignore" -and -not $_.StartsWith("mods/", [System.StringComparison]::OrdinalIgnoreCase)
        }
    )

    if ($unexpectedFiles.Count -gt 0) {
        throw "Unexpected staged files detected: $($unexpectedFiles -join ', ')"
    }
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
$modsDirectory = Join-Path $GameDirectory "mods"
$gitDirectory = Join-Path $GameDirectory ".git"

if (-not (Test-Path -LiteralPath $GameDirectory -PathType Container)) {
    throw "Game directory not found: $GameDirectory"
}

if (-not (Test-Path -LiteralPath $gitDirectory -PathType Container)) {
    throw "Game directory is not a git repository: $GameDirectory"
}

if (-not (Test-Path -LiteralPath $modsDirectory -PathType Container)) {
    throw "Mods directory not found: $modsDirectory"
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git was not found in PATH."
}

if ([string]::IsNullOrWhiteSpace($Branch)) {
    $Branch = Resolve-PreferredBranch -WorkingDirectory $GameDirectory -RemoteName $Remote
}

if ([string]::IsNullOrWhiteSpace($RemoteUrl)) {
    $RemoteUrl = Get-GitOutput -WorkingDirectory $GameDirectory -Arguments @("remote", "get-url", $Remote)
}

if ([string]::IsNullOrWhiteSpace($RemoteUrl)) {
    throw "Unable to determine remote URL from '$Remote'. Pass -RemoteUrl explicitly."
}

if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
    $CommitMessage = "Full mod snapshot $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
}

$originalBranch = Get-GitOutput -WorkingDirectory $GameDirectory -Arguments @("branch", "--show-current")
$originalHead = Get-GitOutput -WorkingDirectory $GameDirectory -Arguments @("rev-parse", "HEAD")
$originalRemoteBinding = Get-GitOutput -WorkingDirectory $GameDirectory -Arguments @("config", "--get", "branch.$Branch.remote")
$originalMergeBinding = Get-GitOutput -WorkingDirectory $GameDirectory -Arguments @("config", "--get", "branch.$Branch.merge")
$tempBranch = "__rewrite_history_" + [guid]::NewGuid().ToString("N").Substring(0, 8)

Write-Host ""
Write-Host "============================================"
Write-Host "  Force Push Mods Snapshot"
Write-Host "============================================"
Write-Host ""
Write-Host "[INFO] GameDir:      $GameDirectory"
Write-Host "[INFO] Remote:       $Remote"
Write-Host "[INFO] RemoteUrl:    $RemoteUrl"
Write-Host "[INFO] TargetBranch: $Branch"
Write-Host "[INFO] OriginalHead: $originalHead"
Write-Host "[WARN] This will rewrite local branch history and force-push the remote branch." -ForegroundColor Yellow

if ([string]::IsNullOrWhiteSpace((Get-GitOutput -WorkingDirectory $GameDirectory -Arguments @("remote", "get-url", $Remote)))) {
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("remote", "add", $Remote, $RemoteUrl)
}
else {
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("remote", "set-url", $Remote, $RemoteUrl)
}
Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("checkout", "--orphan", $tempBranch)

try {
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("read-tree", "--empty")
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("add", "-A", "--", "mods", ".gitignore")
    Assert-OnlyAllowedFilesStaged -WorkingDirectory $GameDirectory
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("commit", "-m", $CommitMessage)

    $localBranches = Get-GitLines -WorkingDirectory $GameDirectory -Arguments @("for-each-ref", "--format=%(refname:short)", "refs/heads")
    foreach ($localBranch in $localBranches) {
        if ($localBranch -and $localBranch -ne $tempBranch -and $localBranch -ne $Branch) {
            Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("branch", "-D", $localBranch)
        }
    }

    if ($tempBranch -ne $Branch) {
        if ($localBranches -contains $Branch) {
            Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("branch", "-D", $Branch)
        }
        Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("branch", "-M", $Branch)
    }

    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("push", "--force", $Remote, "HEAD:$Branch")
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("fetch", $Remote, $Branch)
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("branch", "--set-upstream-to=$Remote/$Branch", $Branch)
    if (-not [string]::IsNullOrWhiteSpace($originalRemoteBinding)) {
        Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("config", "branch.$Branch.remote", $originalRemoteBinding)
    }
    if (-not [string]::IsNullOrWhiteSpace($originalMergeBinding)) {
        Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("config", "branch.$Branch.merge", $originalMergeBinding)
    }
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("reflog", "expire", "--expire=now", "--all")
    Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("gc", "--prune=now", "--aggressive")

    Write-Host ""
    Write-Host "[OK] Local history has been rewritten to a single snapshot commit and force-pushed to $Remote/$Branch." -ForegroundColor Green
    Write-Host ""
}
catch {
    if (-not [string]::IsNullOrWhiteSpace($originalBranch)) {
        try {
            Invoke-Git -WorkingDirectory $GameDirectory -Arguments @("checkout", $originalBranch)
        }
        catch {
        }
    }

    throw
}
