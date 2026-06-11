[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [decimal]$Threshold = 90
)

$ErrorActionPreference = "Stop"

$ScriptDir =
    if ($PSScriptRoot) {
        $PSScriptRoot
    } else {
        Split-Path -Parent $MyInvocation.MyCommand.Definition
    }

$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$ProjectPath = Join-Path $RepoRoot "backend.tests.Unit\backend.tests.Unit.csproj"
$RunSettingsPath = Join-Path $RepoRoot "backend.coverage.runsettings"
$ResultsRoot = Join-Path $RepoRoot ".tmp\backend-unit-coverage"

if (Test-Path $ResultsRoot) {
    Remove-Item -LiteralPath $ResultsRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $ResultsRoot | Out-Null

$testArgs = @(
    "test",
    $ProjectPath,
    "--configuration", "Release",
    "--settings", $RunSettingsPath,
    "--collect:XPlat Code Coverage",
    "--results-directory", $ResultsRoot
)

Write-Host "Running backend unit tests with coverage..." -ForegroundColor Cyan
& dotnet @testArgs

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$report = Get-ChildItem -Path $ResultsRoot -Recurse -Filter "coverage.cobertura.xml" |
    Select-Object -First 1

if ($null -eq $report) {
    throw "Coverage report was not generated under $ResultsRoot"
}

[xml]$coverageXml = Get-Content -LiteralPath $report.FullName

$linesCovered = [decimal]$coverageXml.coverage."lines-covered"
$linesValid = [decimal]$coverageXml.coverage."lines-valid"
$branchesCovered = [decimal]$coverageXml.coverage."branches-covered"
$branchesValid = [decimal]$coverageXml.coverage."branches-valid"

if ($linesValid -le 0) {
    throw "Coverage report did not contain any valid lines."
}

$lineCoverage = ($linesCovered * [decimal]100) / $linesValid
$branchCoverage = if ($branchesValid -gt 0) {
    ($branchesCovered * [decimal]100) / $branchesValid
} else {
    [decimal]0
}

$lineSummary = "{0:F2}" -f $lineCoverage
$branchSummary = "{0:F2}" -f $branchCoverage

Write-Host ("Backend unit coverage: {0}% ({1}/{2})" -f $lineSummary, $linesCovered, $linesValid) -ForegroundColor Green
Write-Host ("Backend unit branch coverage: {0}% ({1}/{2})" -f $branchSummary, $branchesCovered, $branchesValid) -ForegroundColor DarkGray

if ($lineCoverage -lt $Threshold) {
    Write-Host ("Coverage threshold not met. Required: {0:F2}% Actual: {1:F2}%" -f $Threshold, $lineCoverage) -ForegroundColor Red
    exit 1
}
