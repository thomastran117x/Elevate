$ErrorActionPreference = "Stop"

function Check-Command {
    param(
        [string]$cmd,
        [string]$friendlyName
    )
    $exists = Get-Command $cmd -ErrorAction SilentlyContinue
    if (-not $exists) {
        Write-Host "ERROR: $friendlyName not found. Please install it before running this script."
        exit 1
    } else {
        Write-Host "Found $friendlyName."
    }
}

Write-Host "Checking dependencies..."
Check-Command "node" "Node.js"
Check-Command "npm" "npm"
Check-Command "dotnet" ".NET SDK"
Write-Host ""

$binDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $binDir "..") | ForEach-Object { $_.Path }
$frontend = Join-Path $repoRoot "frontend"
$backend  = Join-Path $repoRoot "backend"
$worker   = Join-Path $repoRoot "backend\src\worker"

Write-Host "Project paths:"
Write-Host "  Frontend: $frontend"
Write-Host "  Backend : $backend"
Write-Host "  Worker : $worker"
Write-Host ""

if (Test-Path $frontend) {
    Write-Host "Installing frontend dependencies..."
    Push-Location $frontend
    npm install
    Pop-Location
    Write-Host "Frontend setup completed."
} else {
    Write-Host "WARNING: Frontend folder not found at $frontend"
}

if (Test-Path $backend) {
    Write-Host ""
    Write-Host "Setting up backend..."
    Push-Location $backend

    $envPath = Join-Path $backend ".env"
    if (-not (Test-Path $envPath)) {
        Write-Host "ERROR: .env file not found in backend. Please create one before continuing."
        Pop-Location
        exit 1
    } else {
        Write-Host "Found .env file."
    }

    Write-Host "Restoring .NET dependencies..."
    dotnet restore

    Write-Host "Applying Entity Framework Core migrations..."
    try {
        dotnet ef database update
        Write-Host "EF Core migrations applied successfully."
    } catch {
        Write-Host "ERROR: EF Core migration failed. Check your connection string or EF setup."
    }

    Pop-Location
} else {
    Write-Host "WARNING: Backend folder not found at $backend"
}

if (Test-Path $worker) {
    Write-Host ""
    Write-Host "Setting up worker..."
    Push-Location $worker

    $workerProjFiles = Get-ChildItem -Path $worker -Filter *.csproj -Recurse
    if ($workerProjFiles.Count -eq 0) {
        Write-Host "ERROR: No worker .csproj file found under $worker"
        Pop-Location
        exit 1
    }

    Write-Host "Restoring .NET dependencies..."
    dotnet restore $workerProjFiles[0].FullName

    Pop-Location
} else {
    Write-Host "WARNING: Worker folder not found at $worker"
}

Write-Host ""
Write-Host "Setup completed successfully."
