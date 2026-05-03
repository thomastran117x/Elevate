[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

try {
  $nodeVersion   = & node -v
  $npmVersion    = & npm -v
  $dotnetVersion = & dotnet --version
  Write-Host "Node: $nodeVersion  npm: $npmVersion  .NET: $dotnetVersion" -ForegroundColor Green
} catch {
  throw "Missing dependency: Node.js, npm, or .NET SDK not installed or not on PATH."
}

$RootDir      = Resolve-Path (Join-Path $PSScriptRoot "..")
$FrontendPath = Resolve-Path (Join-Path $RootDir "frontend")
$BackendPath  = Resolve-Path (Join-Path $RootDir "backend")
$WorkerPath   = Resolve-Path (Join-Path $RootDir "backend\src\worker")

function Assert-Package([string]$Path) {
  if (-not (Test-Path (Join-Path $Path "package.json"))) {
    throw "package.json not found in $Path"
  }
}
Assert-Package $FrontendPath

$projFiles = Get-ChildItem -Path $BackendPath -Filter *.csproj -Recurse
if ($projFiles.Count -eq 0) {
  throw "No .csproj file found under $BackendPath"
}
$ProjPath = $projFiles[0].FullName

$workerprojFiles = Get-ChildItem -Path $WorkerPath -Filter *.csproj -Recurse
if ($workerprojFiles.Count -eq 0) {
  throw "No .csproj file found under $WorkerPath"
}

Write-Host ("Starting frontend in {0}" -f $FrontendPath) -ForegroundColor Cyan
$feProc = Start-Process -FilePath "powershell.exe" `
  -ArgumentList "-NoExit", "-Command", "cd '$FrontendPath'; npm run start" `
  -PassThru

Write-Host ("Starting backend in {0}" -f $BackendPath) -ForegroundColor Cyan
$beProc = Start-Process -FilePath "powershell.exe" `
  -ArgumentList "-NoExit", "-Command", "cd '$BackendPath'; dotnet run --no-launch-profile --project '$ProjPath'" `
  -PassThru

$workerProcs = @()
foreach ($workerProj in $workerprojFiles) {
  Write-Host ("Starting worker {0}" -f $workerProj.BaseName) -ForegroundColor Cyan
  $workerProcs += Start-Process -FilePath "powershell.exe" `
    -ArgumentList "-NoExit", "-Command", "cd '$WorkerPath'; dotnet run --no-launch-profile --project '$($workerProj.FullName)'" `
    -PassThru
}
Write-Host "`nAll services running in new terminals."
Write-Host "Press Ctrl+C or close this window to stop everything..." -ForegroundColor Green

try {
  while ($true) { Start-Sleep -Seconds 2 }
} finally {
  Write-Host "`nStopping all services..." -ForegroundColor Yellow
  foreach ($p in @($feProc, $beProc) + $workerProcs) {
    try {
      if ($p -and -not $p.HasExited) {
        & taskkill /T /PID $p.Id /F | Out-Null
        Write-Host "Killed PID $($p.Id)" -ForegroundColor DarkYellow
      }
    } catch {
      Write-Host "Note: could not kill PID $($p.Id): $($_.Exception.Message)" -ForegroundColor DarkGray
    }
  }
  Write-Host "All services stopped. Done." -ForegroundColor Green
}
