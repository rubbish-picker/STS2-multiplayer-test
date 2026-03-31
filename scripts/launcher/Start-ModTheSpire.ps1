param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repoRoot "ModTheSpire\ModTheSpire.csproj"

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

dotnet run --project $project
