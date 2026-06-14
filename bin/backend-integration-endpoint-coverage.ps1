[CmdletBinding()]
param(
    [switch]$FailOnMissing
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $repoRoot "tools\Event.DevTasks\Event.DevTasks.csproj"

$arguments = @(
    "run",
    "--project", $taskProject,
    "--",
    "backend-integration-endpoint-coverage"
)

if ($FailOnMissing) {
    $arguments += "--fail-on-missing"
}

dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
