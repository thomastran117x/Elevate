param(
    [switch]$Force,
    [switch]$RandomSecret
)

function New-RandomSecret {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

$backendDir = "backend"
if (-not (Test-Path -Path $backendDir -PathType Container)) {
    New-Item -ItemType Directory -Path $backendDir | Out-Null
    Write-Host "Created backend directory."
}

$backendEnvPath = Join-Path -Path $backendDir -ChildPath ".env"

if ((Test-Path $backendEnvPath) -and -not $Force) {
    Write-Host "Warning: $backendEnvPath already exists. Use -Force to overwrite."
}
else {
    $secret = if ($RandomSecret) { New-RandomSecret } else { "change_me_dev_secret" }

    $backendEnvContent = @"
##############################################
# Server
##############################################

FRONTEND_CLIENT="http://localhost:3090"
PORT=8090

##############################################
# Databases
##############################################

DB_CONNECTION_STRING="Host=localhost;Port=5432;Database=database;Username=postgres;Password=password"
REDIS_CONNECTION="localhost:6379"

##############################################
# CORS Configuration
##############################################
CORS_ALLOWED_REGION=["http://localhost:3090"]

##############################################
# Security / JWT
##############################################
JWT_SECRET_KEY=$secret

##############################################
# Email (SMTP credentials)
##############################################
EMAIL="example@email.com"
PASSWORD="your_email_password_here"

##############################################
# Google OAuth2
##############################################
GOOGLE_CLIENT_ID="your-google-client-id.apps.googleusercontent.com"

##############################################
# Microsoft OAuth2
##############################################
MS_TENANT_ID="your-microsoft-tenant-id"
MS_CLIENT_ID="your-microsoft-client-id"
"@

    $backendEnvContent | Set-Content -Path $backendEnvPath -Encoding UTF8 -NoNewline
    Write-Host "Created backend .env at $backendEnvPath"
}

$frontendDir = "frontend"
if (-not (Test-Path -Path $frontendDir -PathType Container)) {
    New-Item -ItemType Directory -Path $frontendDir | Out-Null
    Write-Host "Created frontend directory."
}

$frontendEnvPath = Join-Path -Path $frontendDir -ChildPath ".env"

if ((Test-Path $frontendEnvPath) -and -not $Force) {
    Write-Host "Warning: $frontendEnvPath already exists. Use -Force to overwrite."
}
else {
    $frontendEnvContent = @"
##############################################
# Frontend Environment
##############################################

BACKEND_URL="http://localhost:8090/api"
FRONTEND_URL="http://localhost:3090"
GOOGLE_CLIENT_ID="google-client"
MSAL_CLIENT_ID="msal-client"
GOOGLE_SITE_KEY="google-site"
PAYPAL_MODE="sandbox"
PAYPAL_CLIENT_ID="id"
PAYPAL_SECRET_KEY="secret"
"@

    $frontendEnvContent | Set-Content -Path $frontendEnvPath -Encoding UTF8 -NoNewline
    Write-Host "Created frontend .env at $frontendEnvPath"
}

$envFolderPath = "frontend/src/environments"

if (-not (Test-Path -Path $envFolderPath -PathType Container)) {
    New-Item -ItemType Directory -Path $envFolderPath -Force | Out-Null
    Write-Host "Created frontend Angular environments folder at $envFolderPath"
}
else {
    Write-Host "Frontend environments folder already exists."
}
