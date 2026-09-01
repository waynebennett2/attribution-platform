<#
.SYNOPSIS
    Post-deploy functional smoke test for the Call Attribution Platform: health,
    sign-in, the read-only admin surface, and a real DNI allocate/heartbeat/consent
    round trip against a live deployment.

.DESCRIPTION
    Safe to run after every deploy.ps1 run without side effects beyond one DNI session:
    the admin checks are all GETs, and the DNI check releases the number it allocates via
    consent withdrawal at the end. It does not create/mutate any admin config (pools,
    qualification rules, users) — that's deliberate, so it stays idempotent.

    Sign-in requires mandatory TOTP (FR-046) — there is no bypass. To automate this
    unattended, create a dedicated low-privilege "smoke-test" account from the Admin UI
    (Users page) or `POST /v1/admin/users`, capture the `secret=` value from the returned
    `totp_provisioning_uri` at creation time (before enrolling it in an authenticator app
    too, if you also want a human to be able to check it), and store that secret alongside
    the account's password wherever this script's other secrets live — e.g. as the
    SMOKE_TEST_USERNAME / SMOKE_TEST_PASSWORD / SMOKE_TEST_TOTP_SECRET environment
    variables, which this script reads as defaults so nothing needs to be typed on the
    command line (or land in shell history) on every run.

.PARAMETER ApiBaseUrl
    The deployed API's origin, e.g. https://attribution-api.yourcompany.com

.PARAMETER ServiceName
    Windows Service name for the Workers. Defaults to 'Attribution Workers'. Only
    meaningful when this script runs on the server itself; pass -SkipServiceCheck when
    running it remotely.

.PARAMETER Username / Password / TotpSecret
    Credentials for a local account to sign in with, to test Auth + the admin surface.
    Default to the SMOKE_TEST_USERNAME / SMOKE_TEST_PASSWORD / SMOKE_TEST_TOTP_SECRET
    environment variables. TotpSecret is the base32 secret (the `secret=` value from the
    account's `otpauth://` provisioning URI), not a 6-digit code — this script computes
    the current code itself.

.PARAMETER WebsiteId
    A website already configured with an active number pool + tracking numbers, to run
    the DNI allocate/heartbeat/consent check against. Omit to skip that check.

.PARAMETER SkipServiceCheck
    Skip the local Workers service status check (e.g. when running this remotely).

.PARAMETER SkipAdminChecks
    Skip the authenticated admin GET checks (health/pools/users/etc.), even if sign-in
    succeeds.

.PARAMETER SkipDniCheck
    Skip the DNI allocate/heartbeat/consent check even if -WebsiteId is supplied.

.EXAMPLE
    .\smoke-test.ps1 -ApiBaseUrl https://attribution-api.yourcompany.com

.EXAMPLE
    .\smoke-test.ps1 -ApiBaseUrl https://attribution-api.yourcompany.com `
        -Username smoke-test -Password $env:SMOKE_TEST_PASSWORD -TotpSecret $env:SMOKE_TEST_TOTP_SECRET `
        -WebsiteId 00000000-0000-0000-0000-000000000001
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
    [string]$ServiceName = "Attribution Workers",
    [string]$Username = $env:SMOKE_TEST_USERNAME,
    [string]$Password = $env:SMOKE_TEST_PASSWORD,
    [string]$TotpSecret = $env:SMOKE_TEST_TOTP_SECRET,
    [string]$WebsiteId,
    [switch]$SkipServiceCheck,
    [switch]$SkipAdminChecks,
    [switch]$SkipDniCheck
)

$ErrorActionPreference = "Stop"
$ApiBaseUrl = $ApiBaseUrl.TrimEnd('/')
$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param([string]$Check, [ValidateSet("Pass", "Fail", "Skip")][string]$Status, [string]$Detail = "")
    $results.Add([pscustomobject]@{ Check = $Check; Status = $Status; Detail = $Detail })
    $color = switch ($Status) { "Pass" { "Green" }; "Fail" { "Red" }; "Skip" { "Yellow" } }
    $suffix = if ($Detail) { " - $Detail" } else { "" }
    Write-Host "[$($Status.ToUpper())] $Check$suffix" -ForegroundColor $color
}

function ConvertFrom-Base32 {
    param([Parameter(Mandatory = $true)][string]$Base32)
    $alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
    $clean = $Base32.ToUpperInvariant().TrimEnd('=')
    $bits = ""
    foreach ($c in $clean.ToCharArray()) {
        $val = $alphabet.IndexOf($c)
        if ($val -lt 0) { throw "Invalid base32 character '$c' in TOTP secret." }
        $bits += [Convert]::ToString($val, 2).PadLeft(5, '0')
    }
    $byteCount = [Math]::Floor($bits.Length / 8)
    $bytes = New-Object byte[] $byteCount
    for ($i = 0; $i -lt $byteCount; $i++) {
        $bytes[$i] = [Convert]::ToByte($bits.Substring($i * 8, 8), 2)
    }
    return , $bytes
}

# RFC 6238 TOTP, matching Otp.NET's defaults used by the API (SHA1, 30s step, 6 digits).
function Get-TotpCode {
    param([Parameter(Mandatory = $true)][string]$Base32Secret)
    $keyBytes = ConvertFrom-Base32 -Base32 $Base32Secret
    $counter = [uint64][Math]::Floor(([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) / 30)
    $counterBytes = [BitConverter]::GetBytes($counter)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($counterBytes) }
    $hmac = New-Object System.Security.Cryptography.HMACSHA1(, $keyBytes)
    $hash = $hmac.ComputeHash($counterBytes)
    $offset = $hash[$hash.Length - 1] -band 0x0F
    $binary = (($hash[$offset] -band 0x7F) -shl 24) -bor `
              (($hash[$offset + 1] -band 0xFF) -shl 16) -bor `
              (($hash[$offset + 2] -band 0xFF) -shl 8) -bor `
              ($hash[$offset + 3] -band 0xFF)
    $code = $binary % 1000000
    return ([string]$code).PadLeft(6, '0')
}

Write-Host "==> Smoke testing $ApiBaseUrl" -ForegroundColor Cyan

# --- 1. Health -------------------------------------------------------------
try {
    $health = Invoke-WebRequest -Uri "$ApiBaseUrl/health" -UseBasicParsing -TimeoutSec 15
    Add-Result "API /health" $(if ($health.StatusCode -eq 200) { "Pass" } else { "Fail" }) "HTTP $($health.StatusCode)"
} catch {
    Add-Result "API /health" "Fail" $_.Exception.Message
}

if (-not $SkipServiceCheck) {
    try {
        $svc = Get-Service -Name $ServiceName -ErrorAction Stop
        Add-Result "Workers service running" $(if ($svc.Status -eq 'Running') { "Pass" } else { "Fail" }) "Status: $($svc.Status)"
    } catch {
        Add-Result "Workers service running" "Fail" "Could not query service '$ServiceName' (run on the server itself, or pass -SkipServiceCheck): $($_.Exception.Message)"
    }
}

# --- 2. Auth -----------------------------------------------------------
$accessToken = $null
if ($Username -and $Password -and $TotpSecret) {
    try {
        $totpCode = Get-TotpCode -Base32Secret $TotpSecret
        $body = @{ username = $Username; password = $Password; totp_code = $totpCode } | ConvertTo-Json
        $signIn = Invoke-RestMethod -Uri "$ApiBaseUrl/v1/auth/sign-in" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 15
        $accessToken = $signIn.access_token
        Add-Result "Auth sign-in" $(if ($accessToken) { "Pass" } else { "Fail" }) "token issued, expires $($signIn.expires_at)"
    } catch {
        Add-Result "Auth sign-in" "Fail" $_.Exception.Message
    }
} else {
    Add-Result "Auth sign-in" "Skip" "pass -Username/-Password/-TotpSecret (or set SMOKE_TEST_* env vars) to test sign-in and the admin surface"
}

# --- 3. Admin surface (read-only) -------------------------------------------
if ($SkipAdminChecks) {
    Add-Result "Admin surface checks" "Skip" "-SkipAdminChecks was passed"
} elseif ($accessToken) {
    $headers = @{ Authorization = "Bearer $accessToken" }
    $adminEndpoints = @(
        @{ Name = "List websites";            Path = "/v1/admin/websites" }
        @{ Name = "List pools";               Path = "/v1/admin/pools" }
        @{ Name = "List qualification rules"; Path = "/v1/admin/qualification-rules" }
        @{ Name = "List users";               Path = "/v1/admin/users" }
        @{ Name = "List open alerts";         Path = "/v1/admin/alerts" }
        @{ Name = "List open review cases";   Path = "/v1/admin/review-cases" }
        @{ Name = "Ingestion health";         Path = "/v1/admin/health/ingestion" }
        @{ Name = "Publication health";       Path = "/v1/admin/health/publication" }
    )
    foreach ($ep in $adminEndpoints) {
        try {
            $null = Invoke-RestMethod -Uri "$ApiBaseUrl$($ep.Path)" -Headers $headers -TimeoutSec 15
            Add-Result $ep.Name "Pass"
        } catch {
            Add-Result $ep.Name "Fail" $_.Exception.Message
        }
    }
} else {
    Add-Result "Admin surface checks" "Skip" "no access token (sign-in failed or was not attempted)"
}

# --- 4. DNI allocate / heartbeat / consent ----------------------------------
if ($SkipDniCheck) {
    Add-Result "DNI allocate/heartbeat/consent" "Skip" "-SkipDniCheck was passed"
} elseif (-not $WebsiteId) {
    Add-Result "DNI allocate/heartbeat/consent" "Skip" "pass -WebsiteId (a website with an active pool + tracking numbers) to run this check"
} else {
    try {
        $clientToken = "smoke-test-$(Get-Date -Format 'yyyyMMddHHmmss')"
        $dniHeaders = @{ "X-Attribution-Client-Token" = $clientToken }

        $allocateBody = @{
            website_id       = $WebsiteId
            client_token     = $clientToken
            consent_granted  = $true
            landing_page     = "https://smoke-test.invalid/"
        } | ConvertTo-Json
        $allocate = Invoke-RestMethod -Uri "$ApiBaseUrl/v1/dni/allocate" -Method Post -Body $allocateBody -ContentType "application/json" -Headers $dniHeaders -TimeoutSec 15
        Add-Result "DNI allocate" $(if ($allocate.session_id -and $allocate.number) { "Pass" } else { "Fail" }) "session=$($allocate.session_id) number=$($allocate.number)"

        if ($allocate.session_id) {
            $heartbeatBody = @{ session_id = $allocate.session_id } | ConvertTo-Json
            $heartbeat = Invoke-RestMethod -Uri "$ApiBaseUrl/v1/dni/heartbeat" -Method Post -Body $heartbeatBody -ContentType "application/json" -Headers $dniHeaders -TimeoutSec 15
            Add-Result "DNI heartbeat" $(if ($heartbeat.still_valid -eq $true) { "Pass" } else { "Fail" }) "still_valid=$($heartbeat.still_valid)"

            $consentBody = @{ session_id = $allocate.session_id; website_id = $WebsiteId; consent = "withdrawn" } | ConvertTo-Json
            $null = Invoke-RestMethod -Uri "$ApiBaseUrl/v1/dni/consent" -Method Post -Body $consentBody -ContentType "application/json" -Headers $dniHeaders -TimeoutSec 15
            Add-Result "DNI consent withdraw (releases the number)" "Pass"
        }
    } catch {
        Add-Result "DNI allocate/heartbeat/consent" "Fail" $_.Exception.Message
    }
}

# --- Summary -----------------------------------------------------------
Write-Host ""
Write-Host "==> Summary" -ForegroundColor Cyan
$results | Format-Table Check, Status, Detail -AutoSize

$failed = $results | Where-Object { $_.Status -eq "Fail" }
$skipped = $results | Where-Object { $_.Status -eq "Skip" }
if ($failed.Count -gt 0) {
    Write-Host "$($failed.Count) check(s) failed." -ForegroundColor Red
    exit 1
}
if ($skipped.Count -gt 0) {
    Write-Host "All run checks passed ($($skipped.Count) skipped)." -ForegroundColor Yellow
    exit 0
}

Write-Host "All checks passed." -ForegroundColor Green
exit 0
