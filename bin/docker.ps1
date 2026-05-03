$ErrorActionPreference = "Stop"

$binDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $binDir "..") | ForEach-Object { $_.Path }

Write-Host "Project root: $repoRoot"
Write-Host ""

function Check-Command {
    param([string]$cmd, [string]$friendlyName)

    $exists = Get-Command $cmd -ErrorAction SilentlyContinue
    if (-not $exists) {
        Write-Host "ERROR: $friendlyName not found. Please install it."
        exit 1
    }
    Write-Host "Found $friendlyName."
}

Write-Host "Checking dependencies..."
Check-Command "docker" "Docker CLI"
Check-Command "docker-compose" "Docker Compose"
Write-Host ""

Push-Location $repoRoot

Write-Host "Building Docker images..."
docker-compose build

Write-Host ""
Write-Host "Starting full application stack..."
docker compose up -d

Start-Sleep -Seconds 3

Pop-Location

Write-Host ""
Write-Host "All services are now running."
Write-Host "Frontend: http://localhost:3090"
Write-Host "Backend : http://localhost:8090"
Write-Host ""
Write-Host "To follow logs, run: docker compose logs -f"
