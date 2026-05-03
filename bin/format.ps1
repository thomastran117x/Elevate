$root = Get-Location
Set-Location -Path (Split-Path -Parent $MyInvocation.MyCommand.Definition)

Write-Host "Running format script for backend and frontend..." -ForegroundColor Cyan

function Prettier-Format {
    param (
        [string]$path
    )

    Write-Host ""
    Write-Host "Formatting in: $path" -ForegroundColor Yellow

    Push-Location $path
    npm run format
    Pop-Location
}

function Dotnet-Format {
    param (
        [string]$path
    )

    Write-Host ""
    Write-Host "Formatting in: $path" -ForegroundColor Yellow

    Push-Location $path
    dotnet format
    Pop-Location
}

Dotnet-Format "..\backend"
Prettier-Format "..\frontend"
Dotnet-Format "..\backend\src\worker\event-indexer"

Write-Host ""
Write-Host "All formatting complete." -ForegroundColor Cyan

Set-Location $root
