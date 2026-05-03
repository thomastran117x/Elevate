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
$workerRoot = Resolve-Path "..\backend\src\worker"
$workerProjects = Get-ChildItem -Path $workerRoot -Filter *.csproj -Recurse
foreach ($workerProject in $workerProjects) {
    Dotnet-Format $workerProject.Directory.FullName
}

Write-Host ""
Write-Host "All formatting complete." -ForegroundColor Cyan

Set-Location $root
