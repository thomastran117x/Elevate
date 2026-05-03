[CmdletBinding()]
param(
  [string]$Namespace    = "eventxperience",
  [int]   $FrontendPort = 3090,
  [int]   $BackendPort  = 8090
)

$ErrorActionPreference = "Stop"

function Info($msg) {
  Write-Host "[K8S] $msg" -ForegroundColor Cyan
}

function Success($msg) {
  Write-Host "[OK ] $msg" -ForegroundColor Green
}

function Warn($msg) {
  Write-Host "[WARN] $msg" -ForegroundColor Yellow
}

function Fail($msg) {
  Write-Host "[ERR ] $msg" -ForegroundColor Red
  exit 1
}

Info "Checking dependencies..."

foreach ($cmd in @("docker", "kubectl")) {
  if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
    Fail "$cmd not found on PATH"
  }
}

Success "All dependencies found"

$root = Resolve-Path (Join-Path (Split-Path $MyInvocation.MyCommand.Path) "..")

$frontendPath = Join-Path $root "frontend"
$backendPath  = Join-Path $root "backend"
$eventIndexerPath = Join-Path $root "backend\src\worker\event-indexer"
$clubPostIndexerPath = Join-Path $root "backend\src\worker\clubpost-indexer"
$emailWorkerPath = Join-Path $root "backend\src\worker\email-worker"
$manifest     = Join-Path $root "eventxperience.yml"

Info "Building Docker images (linux/arm64)..."

docker buildx build --platform linux/arm64 -t myapp-frontend:latest $frontendPath | Out-Null
docker buildx build --platform linux/arm64 -t myapp-backend:latest  $backendPath  | Out-Null
docker buildx build --platform linux/arm64 -t myapp-event-indexer:latest -f (Join-Path $eventIndexerPath "Dockerfile") $backendPath | Out-Null
docker buildx build --platform linux/arm64 -t myapp-clubpost-indexer:latest -f (Join-Path $clubPostIndexerPath "Dockerfile") $backendPath | Out-Null
docker buildx build --platform linux/arm64 -t myapp-email-worker:latest -f (Join-Path $emailWorkerPath "Dockerfile") $backendPath | Out-Null

Success "Docker images built"

Info "Applying Kubernetes manifests..."
kubectl apply -f $manifest | Out-Null

Success "Manifests applied"

Info "Waiting for deployments to stabilize..."

kubectl rollout status deployment/frontend -n $Namespace --timeout=120s | Out-Null
kubectl rollout status deployment/backend  -n $Namespace --timeout=120s | Out-Null

Success "Core services are Ready"

Info "Current pod status:"
kubectl get pods -n $Namespace

Info "Starting port-forwards (silent)..."
Info "  Frontend → http://localhost:$FrontendPort"
Info "  Backend  → http://localhost:$BackendPort"
Warn "Press Ctrl+C to stop port-forwarding"

Start-Process powershell -NoNewWindow -ArgumentList @(
  "-NoProfile",
  "-Command",
  "kubectl port-forward -n $Namespace svc/frontend $FrontendPort`:3090 > `$null 2>&1"
)

Start-Process powershell -NoNewWindow -ArgumentList @(
  "-NoProfile",
  "-Command",
  "kubectl port-forward -n $Namespace svc/backend $BackendPort`:8090 > `$null 2>&1"
)

Write-Host ""
Success "Dev environment is ready"
Write-Host "  Frontend: http://localhost:$FrontendPort" -ForegroundColor White
Write-Host "  Backend : http://localhost:$BackendPort"  -ForegroundColor White

Write-Host ""
Write-Host "To clean up everything later:" -ForegroundColor Yellow
Write-Host "  kubectl delete namespace $Namespace" -ForegroundColor Yellow
