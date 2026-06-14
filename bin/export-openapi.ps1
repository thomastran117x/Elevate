param(
    [string]$OutputPath = "backend/openapi.yaml",
    [int]$Port = 8091
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$backendDir = Join-Path $repoRoot "backend"
$projectPath = Join-Path $backendDir "backend.csproj"
$backendDll = Join-Path $backendDir "bin/Debug/net9.0/backend.dll"
$resolvedOutputPath = Join-Path $repoRoot $OutputPath
$outputDirectory = Split-Path -Parent $resolvedOutputPath
$outputExtension = [System.IO.Path]::GetExtension($resolvedOutputPath).ToLowerInvariant()

if (-not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

switch ($outputExtension) {
    ".json" {
        $documentUrl = "http://127.0.0.1:$Port/openapi.json"
        $documentLabel = "JSON"
    }
    ".yaml" {
        $documentUrl = "http://127.0.0.1:$Port/openapi.yaml"
        $documentLabel = "YAML"
    }
    default {
        throw "Unsupported OpenAPI output extension '$outputExtension'. Use .json or .yaml."
    }
}

dotnet build $projectPath | Out-Host

$previousPort = $env:PORT
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousExportMode = $env:OPENAPI_EXPORT
$previousIncludePrefix = $env:OPENAPI_INCLUDE_PREFIX

$env:PORT = "$Port"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:OPENAPI_EXPORT = "true"
Remove-Item Env:OPENAPI_INCLUDE_PREFIX -ErrorAction SilentlyContinue

$process = $null

try {
    $process = Start-Process dotnet -ArgumentList $backendDll -WorkingDirectory $backendDir -PassThru -WindowStyle Hidden
    $downloaded = $false

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 500

        try {
            Invoke-WebRequest -Uri $documentUrl -OutFile $resolvedOutputPath -UseBasicParsing
            $downloaded = $true
            break
        }
        catch {
            if ($process.HasExited) {
                throw "OpenAPI export server exited before the document became available."
            }
        }
    }

    if (-not $downloaded) {
        throw "Timed out waiting for $documentUrl."
    }

    Write-Host "OpenAPI $documentLabel exported to $resolvedOutputPath"
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        Wait-Process -Id $process.Id -ErrorAction SilentlyContinue
    }

    $env:PORT = $previousPort
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:OPENAPI_EXPORT = $previousExportMode

    if ([string]::IsNullOrWhiteSpace($previousIncludePrefix)) {
        Remove-Item Env:OPENAPI_INCLUDE_PREFIX -ErrorAction SilentlyContinue
    }
    else {
        $env:OPENAPI_INCLUDE_PREFIX = $previousIncludePrefix
    }
}
