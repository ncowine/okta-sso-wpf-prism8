#requires -Version 5.1
<#
.SYNOPSIS
    Runs the WPF app against the bundled dummy IdP + demo APIs, with no real Okta tenant.

.DESCRIPTION
    Starts tools/DummyIdp, tools/DemoApi and tools/DemoApiB, waits for them, points the WPF app
    at them, launches it, and stops everything when the app exits.

    DEV ONLY. The app runs with Okta:AllowInsecureHttp = true for this session.
#>
[CmdletBinding()]
param(
    [string] $IdpUrl   = 'http://localhost:5005',
    [string] $ApiUrl   = 'http://localhost:5006',
    [string] $ApiBUrl  = 'http://localhost:5007',
    [string] $ClientId = 'sso-demo-wpf'
)

$ErrorActionPreference = 'Stop'
$repoRoot  = Split-Path -Parent $PSScriptRoot
$discovery = "$IdpUrl/oauth2/default/.well-known/openid-configuration"
$procs     = @()

function Wait-For([string] $url, [string] $name)
{
    foreach ($i in 1..40)
    {
        try { Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2 | Out-Null; return }
        catch { Start-Sleep -Milliseconds 500 }
    }
    throw "$name did not become ready at $url"
}

try
{
    Write-Host "Starting dummy IdP ($IdpUrl)..." -ForegroundColor Cyan
    $env:DummyIdp__PublicUrl = $IdpUrl
    $procs += Start-Process dotnet -PassThru -WorkingDirectory $repoRoot `
        -ArgumentList 'run','--project','tools/DummyIdp'
    Wait-For $discovery 'Dummy IdP'

    Write-Host "Starting demo API ($ApiUrl)..." -ForegroundColor Cyan
    $env:Api__PublicUrl            = $ApiUrl
    $env:Api__Authority            = "$IdpUrl/oauth2/default"
    $env:Api__RequireHttpsMetadata = 'false'
    $env:Api__DownstreamApiUrl     = $ApiBUrl
    $procs += Start-Process dotnet -PassThru -WorkingDirectory $repoRoot `
        -ArgumentList 'run','--project','tools/DemoApi'
    Wait-For "$ApiUrl/api/health" 'Demo API'

    Write-Host "Starting demo API B ($ApiBUrl)..." -ForegroundColor Cyan
    # DemoApiB reads its own appsettings.json; only its listen URL is overridden here.
    $procs += Start-Process dotnet -PassThru -WorkingDirectory $repoRoot `
        -ArgumentList 'run','--project','tools/DemoApiB','--','--Api:PublicUrl',$ApiBUrl
    Wait-For "$ApiBUrl/b/health" 'Demo API B'

    Write-Host "Services up. Launching the WPF app..." -ForegroundColor Green
    $env:SSO_OKTA_DOMAIN            = $IdpUrl
    $env:SSO_OKTA_CLIENTID          = $ClientId
    $env:SSO_OKTA_ALLOWINSECUREHTTP = 'true'
    $env:SSO_API_BASEURL            = $ApiUrl
    $env:SSO_API_BASEURL_B          = $ApiBUrl

    dotnet run --project (Join-Path $repoRoot 'src/SsoDemo.Wpf')
}
finally
{
    Write-Host 'Stopping services...' -ForegroundColor Cyan
    foreach ($p in $procs) { if ($p -and -not $p.HasExited) { $p.Kill() } }
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match 'DummyIdp|DemoApi' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Get-Process -Name DummyIdp, DemoApi, DemoApiB -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}
