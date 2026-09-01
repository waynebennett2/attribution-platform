<#
.SYNOPSIS
    Deploys Attribution.Api and Attribution.Workers from this Git repo onto an
    already-provisioned Windows Server, replacing the manual "unzip a new build over the
    old one" process described in the server cutover pack.

.DESCRIPTION
    Assumes the one-time server setup from that pack is already done: IIS installed with
    the ANCM hosting bundle, a dedicated IIS site + app pool for the API, the
    "Attribution Workers" Windows Service registered, and appsettings.Production.local.json
    already sitting in both deployed folders (this script never touches that file — it
    isn't part of the build output, so `dotnet publish` can't overwrite or delete it).

    This script only handles what changes on every deploy:
      1. Reset this clone to match origin/<Branch> exactly (refuses to run if the clone
         has local changes — it's meant to be deploy-only, never hand-edited).
      2. Stop the app pool and the Workers service (both hold their binaries open).
      3. `dotnet publish` the API and Workers straight into their existing deployed
         folders.
      4. Apply pending EF/FluentMigrator migrations via `dotnet Attribution.Api.dll migrate`.
      5. Restart the app pool and service, and optionally hit a health-check URL.

    Run from an elevated PowerShell prompt on the server, either from inside the cloned
    repo or via -RepoPath.

.PARAMETER RepoPath
    Path to the git clone to deploy from. Defaults to this script's parent directory.

.PARAMETER Branch
    Branch to deploy. Defaults to 'master'.

.PARAMETER ApiSitePath
    IIS physical path for the API. Defaults to C:\AttributionApi.

.PARAMETER WorkersServicePath
    Install path for the Workers service. Defaults to C:\AttributionWorkers.

.PARAMETER AppPoolName
    IIS application pool hosting the API. Defaults to AttributionApi.

.PARAMETER ServiceName
    Windows Service name for the Workers. Defaults to 'Attribution Workers'.

.PARAMETER SkipMigrate
    Skip the `migrate` step, e.g. for a deploy that ships no schema change.

.PARAMETER HealthCheckUrl
    If set, GETs this URL after restart and fails the deploy if it doesn't return 200.

.EXAMPLE
    .\deploy.ps1

.EXAMPLE
    .\deploy.ps1 -Branch master -HealthCheckUrl https://attribution-api.yourcompany.com/health
#>
[CmdletBinding()]
param(
    [string]$RepoPath = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [string]$Branch = "master",
    [string]$ApiSitePath = "C:\AttributionApi",
    [string]$WorkersServicePath = "C:\AttributionWorkers",
    [string]$AppPoolName = "AttributionApi",
    [string]$ServiceName = "Attribution Workers",
    [switch]$SkipMigrate,
    [string]$HealthCheckUrl
)

$ErrorActionPreference = "Stop"

function Invoke-Checked {
    param([Parameter(Mandatory = $true)][string]$Command, [string[]]$CommandArgs = @())
    & $Command @CommandArgs
    if ($LASTEXITCODE -ne 0) {
        throw "'$Command $($CommandArgs -join ' ')' exited with code $LASTEXITCODE"
    }
}

$commit = $null

Write-Host "==> Deploying branch '$Branch' from $RepoPath" -ForegroundColor Cyan
Push-Location $RepoPath
try {
    $dirty = git status --porcelain
    if ($dirty) {
        throw "Working tree at $RepoPath has local changes. This clone is deploy-only; resolve or stash them first:`n$dirty"
    }

    Invoke-Checked git @("fetch", "origin", $Branch)
    Invoke-Checked git @("checkout", $Branch)
    Invoke-Checked git @("reset", "--hard", "origin/$Branch")

    $commit = (git rev-parse --short HEAD).Trim()
    Write-Host "==> Now at commit $commit" -ForegroundColor Cyan

    Import-Module WebAdministration -ErrorAction Stop

    Write-Host "==> Stopping '$AppPoolName' app pool and '$ServiceName' service" -ForegroundColor Cyan
    Stop-WebAppPool -Name $AppPoolName
    Stop-Service -Name $ServiceName

    Write-Host "==> Publishing API to $ApiSitePath" -ForegroundColor Cyan
    Invoke-Checked dotnet @("publish", "src\Attribution.Api\Attribution.Api.csproj", "-c", "Release", "-o", $ApiSitePath)

    Write-Host "==> Publishing Workers to $WorkersServicePath" -ForegroundColor Cyan
    Invoke-Checked dotnet @("publish", "src\Attribution.Workers\Attribution.Workers.csproj", "-c", "Release", "-o", $WorkersServicePath)

    if (-not $SkipMigrate) {
        Write-Host "==> Applying database migrations" -ForegroundColor Cyan
        Push-Location $ApiSitePath
        try {
            Invoke-Checked dotnet @("Attribution.Api.dll", "migrate")
        } finally {
            Pop-Location
        }
    } else {
        Write-Host "==> Skipping migrate (-SkipMigrate)" -ForegroundColor Yellow
    }
}
finally {
    Write-Host "==> Restarting '$AppPoolName' app pool and '$ServiceName' service" -ForegroundColor Cyan
    try { Start-WebAppPool -Name $AppPoolName } catch { Write-Warning "Failed to start app pool '$AppPoolName': $_" }
    try { Start-Service -Name $ServiceName } catch { Write-Warning "Failed to start service '$ServiceName': $_" }
    Pop-Location
}

Start-Sleep -Seconds 3
Write-Host "==> Status" -ForegroundColor Cyan
Get-Service -Name $ServiceName | Format-Table -AutoSize
Get-WebAppPoolState -Name $AppPoolName | Format-Table -AutoSize

if ($HealthCheckUrl) {
    Write-Host "==> Checking $HealthCheckUrl" -ForegroundColor Cyan
    try {
        $response = Invoke-WebRequest -Uri $HealthCheckUrl -UseBasicParsing -TimeoutSec 15
        if ($response.StatusCode -ne 200) {
            throw "Health check returned status $($response.StatusCode)"
        }
        Write-Host "==> Health check OK" -ForegroundColor Green
    } catch {
        Write-Warning "Health check failed: $_"
        exit 1
    }
}

Write-Host "==> Deploy of commit $commit complete." -ForegroundColor Green
