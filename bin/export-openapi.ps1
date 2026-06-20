param(
    [string]$OutputPath = "backend/openapi.yaml",
    [int]$Port = 8090
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $repoRoot "tools\Event.DevTasks\Event.DevTasks.csproj"

dotnet run --project $taskProject -- export-openapi --output $OutputPath --port $Port

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
