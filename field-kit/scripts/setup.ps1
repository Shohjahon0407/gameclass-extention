# Manajoy GameClass Bridge - one-step install for a club PC (run "As Administrator").
#
#   powershell -ExecutionPolicy Bypass -File .\setup.ps1
#
# 1. preflight.ps1 (read-only checks; stops on RED)
# 2. create-readonly-login.ps1 (one read-only SQL login; asks for the sa password, usually empty)
# 3. bridge\<x64|x86>\install-service.ps1 (Windows service, local API http://127.0.0.1:8787)
# The SQL password is never shown: it goes straight into the service's DPAPI-protected file.
# Nothing is sent to Manajoy (push is off unless install-service.ps1 -PushUrl is used later).
[CmdletBinding()]
param(
    [string]$Server = '(local)',
    [string]$Database = 'GameClass',
    [string]$AdminUser = 'sa',
    [securestring]$AdminPassword,
    [switch]$WindowsAuth,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$kit = $PSScriptRoot

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Right-click PowerShell -> "Run as administrator", then run setup.ps1 again.'
}

# A kit downloaded through Telegram/browser carries the "from the Internet" mark (Zone.Identifier);
# remove it so Windows does not block the scripts or the exe copied into Program Files.
Get-ChildItem -LiteralPath $kit -Recurse -File | Unblock-File

Write-Host '=== 1/3 Preflight ===' -ForegroundColor Cyan
& (Join-Path $kit 'preflight.ps1') -OutDir $kit
$preflight = $LASTEXITCODE
if ($preflight -eq 2 -and -not $Force) {
    throw 'Preflight is RED - fix the RED lines first (or re-run with -Force if you are sure).'
}

$arch = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
$bridgeDir = Join-Path $kit "bridge\$arch"
if (-not (Test-Path (Join-Path $bridgeDir 'Manajoy.GameClass.Bridge.exe'))) {
    throw "Bridge files for $arch not found in $bridgeDir"
}

Write-Host '=== 2/3 Read-only SQL login ===' -ForegroundColor Cyan
$loginArgs = @{ Server = $Server; Database = $Database; ResetPassword = $true; PassThru = $true }
if ($WindowsAuth) { $loginArgs.WindowsAuth = $true } else { $loginArgs.AdminUser = $AdminUser }
if ($PSBoundParameters.ContainsKey('AdminPassword')) { $loginArgs.AdminPassword = $AdminPassword }
$sqlPassword = & (Join-Path $kit 'create-readonly-login.ps1') @loginArgs | Select-Object -Last 1
if ($sqlPassword -isnot [securestring]) { throw 'Read-only login was not created.' }

Write-Host "=== 3/3 Windows service ($arch) ===" -ForegroundColor Cyan
& (Join-Path $bridgeDir 'install-service.ps1') -SqlUser 'gcbridge_ro' -SqlPassword $sqlPassword

Write-Host ''
try {
    $snapshot = Invoke-RestMethod 'http://127.0.0.1:8787/api/v1/snapshot' -TimeoutSec 10
    $c = $snapshot.counts
    Write-Host ("Bridge works: {0} PCs - available {1}, busy {2}, offline {3}, unknown {4}. GameClass server alive: {5}" -f `
        $c.total, $c.available, $c.busy, $c.offline, $c.unknown, $snapshot.gameclass.server_alive) -ForegroundColor Green
} catch {
    Write-Warning "The service is installed but no snapshot yet: $($_.Exception.Message)"
}
Write-Host 'Compare with the GameClass Server screen, then run collect-report.ps1 and send the zip to Manajoy.'
