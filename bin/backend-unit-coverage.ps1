[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [decimal]$Threshold = 90
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $repoRoot "tools\Event.DevTasks\Event.DevTasks.csproj"

dotnet run --project $taskProject -- backend-unit-coverage --threshold $Threshold

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
