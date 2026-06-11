[CmdletBinding()]
param(
    [switch]$FailOnMissing
)

$ErrorActionPreference = "Stop"

$ScriptDir =
    if ($PSScriptRoot) {
        $PSScriptRoot
    } else {
        Split-Path -Parent $MyInvocation.MyCommand.Definition
    }

$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$BackendRoot = Join-Path $RepoRoot "backend\src\main"
$IntegrationRoot = Join-Path $RepoRoot "backend.tests.Integration"
$RoutesFile = Join-Path $BackendRoot "application\bootstrap\Routes.cs"

function Get-RouteConstants {
    param([string]$Path)

    $constants = @{}
    $content = Get-Content -LiteralPath $Path

    foreach ($line in $content) {
        if ($line -match 'public\s+const\s+string\s+(?<name>\w+)\s*=\s*"(?<value>[^"]*)"') {
            $constants[$Matches.name] = $Matches.value
        }
    }

    return $constants
}

function Resolve-AttributeRoute {
    param(
        [AllowNull()]
        [string]$RawValue,
        [hashtable]$RouteConstants
    )

    if ([string]::IsNullOrWhiteSpace($RawValue)) {
        return ""
    }

    $value = $RawValue.Trim()

    if ($value -match '^"(?<literal>.*)"$') {
        return $Matches.literal
    }

    if ($value -match '^RoutePaths\.(?<name>\w+)$' -and $RouteConstants.ContainsKey($Matches.name)) {
        return $RouteConstants[$Matches.name]
    }

    return $value
}

function Normalize-RouteSegment {
    param([string]$Route)

    if ([string]::IsNullOrWhiteSpace($Route)) {
        return ""
    }

    $normalized = $Route.Trim()
    $normalized = $normalized -replace '\\', '/'
    $normalized = $normalized -replace '\{[^}/:]+(?::[^}]+)?\}', '{}'
    $normalized = $normalized.Trim('/')
    return $normalized.ToLowerInvariant()
}

function Normalize-TestPath {
    param([string]$Path)

    $normalized = $Path.Trim()
    $normalized = $normalized.Split('?', 2)[0]
    $normalized = $normalized -replace '\{[^}/]+\}', '{}'
    $normalized = $normalized -replace '/\d+(?=/|$)', '/{}'
    $normalized = $normalized -replace '/[0-9a-f]{8}-[0-9a-f-]{27,35}(?=/|$)', '/{}'
    $normalized = $normalized.TrimEnd('/')

    if ($normalized -eq "") {
        return "/"
    }

    return $normalized.ToLowerInvariant()
}

function Join-ApiRoute {
    param(
        [string]$ControllerRoute,
        [string]$ActionRoute
    )

    $parts = @()
    foreach ($part in @($ControllerRoute, $ActionRoute)) {
        $normalized = Normalize-RouteSegment $part
        if (-not [string]::IsNullOrWhiteSpace($normalized)) {
            $parts += $normalized
        }
    }

    $joined = "/api"
    if ($parts.Count -gt 0) {
        $joined += "/" + ($parts -join "/")
    }

    return $joined
}

function Get-ControllerEndpoints {
    param(
        [string]$RootPath,
        [hashtable]$RouteConstants
    )

    $endpoints = New-Object System.Collections.Generic.List[object]
    $files = Get-ChildItem -Path $RootPath -Recurse -Filter "*Controller.cs" | Sort-Object FullName

    foreach ($file in $files) {
        $lines = Get-Content -LiteralPath $file.FullName
        $currentController = $null
        $currentControllerRoute = ""
        $pendingClassRoute = $null
        $pendingHttpRoutes = New-Object System.Collections.Generic.List[object]

        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = $lines[$index]

            if ($line -match '\[Route\((?<route>.+?)\)\]') {
                $pendingClassRoute = Resolve-AttributeRoute $Matches.route $RouteConstants
                continue
            }

            if ($line -match '\[(?<attr>HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch)(?:\((?<route>.+?)\))?\]') {
                $verb = $Matches.attr.Substring(4).ToUpperInvariant()
                $actionRoute = Resolve-AttributeRoute $Matches.route $RouteConstants
                $pendingHttpRoutes.Add([pscustomobject]@{
                    Verb = $verb
                    ActionRoute = $actionRoute
                    AttributeLine = $index + 1
                })
                continue
            }

            if ($line -match 'class\s+(?<name>\w+Controller)\b') {
                $currentController = $Matches.name
                $currentControllerRoute = if ($null -ne $pendingClassRoute) { $pendingClassRoute } else { "" }
                $pendingClassRoute = $null
                $pendingHttpRoutes.Clear()
                continue
            }

            if ($pendingHttpRoutes.Count -gt 0 -and $line -match 'public\s+(?:async\s+)?(?:Task<[^>]+>|Task|IActionResult|ActionResult<[^>]+>|ActionResult)\s+(?<method>\w+)\s*\(') {
                foreach ($httpRoute in $pendingHttpRoutes) {
                    $fullRoute = Join-ApiRoute $currentControllerRoute $httpRoute.ActionRoute
                    $endpoints.Add([pscustomobject]@{
                        Controller = $currentController
                        Action = $Matches.method
                        Verb = $httpRoute.Verb
                        Route = $fullRoute
                        Source = $file.FullName
                        Line = $httpRoute.AttributeLine
                    })
                }

                $pendingHttpRoutes.Clear()
            }
        }
    }

    return $endpoints
}

function Get-IntegrationRequests {
    param([string]$RootPath)

    $requests = New-Object System.Collections.Generic.List[object]
    $files = Get-ChildItem -Path $RootPath -Recurse -Filter "*.cs" | Sort-Object FullName

    $patterns = @(
        '(?<call>(?<verb>Get|Delete)Async|(?<verb>Post|Put|Patch)(?:AsJson)?Async)\(\s*\$?"(?<path>/api/[^"\r\n]*)"',
        'new\s+HttpRequestMessage\s*\(\s*HttpMethod\.(?<verb>Get|Post|Put|Delete|Patch)\s*,\s*\$?"(?<path>/api/[^"\r\n]*)"',
        'CreateAuthorizedRequest\s*\(\s*HttpMethod\.(?<verb>Get|Post|Put|Delete|Patch)\s*,\s*\$?"(?<path>/api/[^"\r\n]*)"',
        'PostJsonWithCsrfAsync\(\s*\$?"(?<path>/api/[^"\r\n]*)"',
        'CreateCsrfRequestAsync\(\s*\w+\s*,\s*\$?"(?<path>/api/[^"\r\n]*)"'
    )

    foreach ($file in $files) {
        $content = Get-Content -LiteralPath $file.FullName -Raw

        foreach ($pattern in $patterns) {
            $matches = [regex]::Matches($content, $pattern)

            foreach ($match in $matches) {
                $verb = if ($match.Groups["verb"].Success -and $match.Groups["verb"].Value) {
                    $match.Groups["verb"].Value.ToUpperInvariant()
                } else {
                    "POST"
                }

                $path = Normalize-TestPath $match.Groups["path"].Value
                $requests.Add([pscustomobject]@{
                    Verb = $verb
                    Route = $path
                    Source = $file.FullName
                })
            }
        }
    }

    return $requests
}

$routeConstants = Get-RouteConstants -Path $RoutesFile
$controllerEndpoints = Get-ControllerEndpoints -RootPath $BackendRoot -RouteConstants $routeConstants
$integrationRequests = Get-IntegrationRequests -RootPath $IntegrationRoot

$requestKeyCounts = @{}
foreach ($request in $integrationRequests) {
    $key = "$($request.Verb) $($request.Route)"
    if (-not $requestKeyCounts.ContainsKey($key)) {
        $requestKeyCounts[$key] = 0
    }

    $requestKeyCounts[$key] += 1
}

$results = foreach ($endpoint in $controllerEndpoints) {
    $key = "$($endpoint.Verb) $($endpoint.Route)"
    $matchCount = if ($requestKeyCounts.ContainsKey($key)) { [int]$requestKeyCounts[$key] } else { 0 }

    [pscustomobject]@{
        Controller = $endpoint.Controller
        Action = $endpoint.Action
        Verb = $endpoint.Verb
        Route = $endpoint.Route
        Covered = $matchCount -gt 0
        MatchCount = $matchCount
        Source = $endpoint.Source
        Line = $endpoint.Line
    }
}

$total = $results.Count
$covered = @($results | Where-Object Covered).Count
$missing = @($results | Where-Object { -not $_.Covered })
$coveragePercent = if ($total -gt 0) { [math]::Round(($covered * 100.0) / $total, 2) } else { 0.0 }

Write-Host ("Integration endpoint coverage: {0}% ({1}/{2} controller actions)" -f $coveragePercent, $covered, $total) -ForegroundColor Cyan

$byController = $results |
    Group-Object Controller |
    Sort-Object Name |
    ForEach-Object {
        $controllerCovered = @($_.Group | Where-Object Covered).Count
        $controllerTotal = $_.Count
        [pscustomobject]@{
            Controller = $_.Name
            Covered = $controllerCovered
            Total = $controllerTotal
            Percent = [math]::Round(($controllerCovered * 100.0) / $controllerTotal, 2)
        }
    }

Write-Host ""
Write-Host "By controller:" -ForegroundColor Yellow
$byController | Format-Table -AutoSize | Out-Host

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "Endpoints without a matching integration request:" -ForegroundColor Yellow
    $missing |
        Sort-Object Controller, Route, Verb |
        Select-Object Controller, Verb, Route, Action |
        Format-Table -AutoSize |
        Out-Host
}

if ($FailOnMissing -and $missing.Count -gt 0) {
    exit 1
}
