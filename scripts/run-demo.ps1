#requires -Version 5.1
<#
.SYNOPSIS
    Runs the WPF app against the bundled dummy IdP + demo APIs, with no real Okta tenant.

.DESCRIPTION
    Starts tools/DummyIdp, tools/DemoApi and tools/DemoApiB on the ports SsoDemo.Wpf's App.config
    already points at (localhost:5005/5006/5007), waits for them, launches the app, and stops
    everything when it exits. The app itself reads only App.config — no env vars, no params here
    to keep in sync with it — so if you need different ports, edit App.config to match instead of
    passing new ones to this script.

    DEV ONLY. The app runs with Okta:AllowInsecureHttp = true for this session.
#>
[CmdletBinding()]
param()

$IdpUrl   = 'http://localhost:5005'
$ApiUrl   = 'http://localhost:5006'
$ApiBUrl  = 'http://localhost:5007'

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
