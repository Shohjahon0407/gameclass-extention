# Installs (or updates) Manajoy GameClass Bridge as a Windows Service.
# Run from an elevated PowerShell in the folder that contains Manajoy.GameClass.Bridge.exe:
#   .\install-service.ps1 -SqlUser gcbridge_ro
#   .\install-service.ps1 -PushUrl https://api.manajoy.uz/api/integrations/gameclass/26/snapshot   # asks for the key
# Secrets (SQL password, push key) are asked interactively and stored DPAPI-encrypted (LocalMachine
# scope) in <InstallDir>\bridge.secret and push.secret. The install folder is reset to
# Administrators/SYSTEM-only access. Parameters left out keep their current values.
# Uninstall:  .\install-service.ps1 -Uninstall
[CmdletBinding()]
param(
    [string]$SqlUser,
    [securestring]$SqlPassword,
    [string]$PushUrl,
    [securestring]$PushKey,
    [switch]$DisablePush,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Manajoy\GameClassBridge'),
    [string]$ServiceName = 'ManajoyGameClassBridge',
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated (Administrator) PowerShell.'
}

# Folder: Administrators + SYSTEM only, inherited by everything inside. Files get their ACL only through
# inheritance (/reset), because (OI)(CI) grants applied directly to a file would leave it with an empty DACL.
function Set-FolderAcl([string]$Path) {
    icacls $Path /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' /C /Q | Out-Null
    if (Get-ChildItem $Path -Force) {
        icacls (Join-Path $Path '*') /reset /T /C /Q | Out-Null
    }
}

$existing = Get-Service $ServiceName -ErrorAction SilentlyContinue

if ($Uninstall) {
    if ($existing) {
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Write-Host "Service $ServiceName removed. Files in $InstallDir were left in place."
    } else {
        Write-Host "Service $ServiceName is not installed."
    }
    return
}

if ($existing) {
    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    # Stop-Service can return before the process has exited and released the exe.
    Get-Process 'Manajoy.GameClass.Bridge' -ErrorAction SilentlyContinue | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
}

# The service runs as LocalSystem, so the folder must not stay under a non-admin user's control
# (someone could pre-create it and plant files). Take ownership and replace the ACL before copying.
New-Item $InstallDir -ItemType Directory -Force | Out-Null
icacls $InstallDir /setowner '*S-1-5-32-544' /T /C /Q | Out-Null
Set-FolderAcl $InstallDir

# Copy the exe + settings unless we are already running from the install folder.
$source = $PSScriptRoot
if ((Resolve-Path $source).Path.TrimEnd('\') -ne (Resolve-Path $InstallDir).Path.TrimEnd('\')) {
    Copy-Item (Join-Path $source 'Manajoy.GameClass.Bridge.exe') $InstallDir -Force
    if (-not (Test-Path (Join-Path $InstallDir 'appsettings.json'))) {
        Copy-Item (Join-Path $source 'appsettings.json') $InstallDir -Force
    }
}

# Re-apply after copying so every file inherits only the Administrators/SYSTEM ACL.
Set-FolderAcl $InstallDir

$exe = Join-Path $InstallDir 'Manajoy.GameClass.Bridge.exe'
if ($existing) {
    sc.exe config $ServiceName binPath= "`"$exe`"" start= delayed-auto | Out-Null
} else {
    sc.exe create $ServiceName binPath= "`"$exe`"" start= delayed-auto DisplayName= 'Manajoy GameClass Bridge (read-only)' | Out-Null
    sc.exe description $ServiceName 'Reads the GameClass database (read-only) and serves PC status on a local HTTP API.' | Out-Null
    sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
}

function Save-Protected([securestring]$Secret, [string]$FileName) {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secret))
    Add-Type -AssemblyName System.Security
    $protected = [Security.Cryptography.ProtectedData]::Protect(
        [Text.Encoding]::UTF8.GetBytes($plain), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    [IO.File]::WriteAllBytes((Join-Path $InstallDir $FileName), $protected)
}

# Only non-secret settings go into the service environment; existing entries are kept unless replaced.
$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$environment = [ordered]@{}
foreach ($line in @((Get-ItemProperty $serviceKey -ErrorAction SilentlyContinue).Environment)) {
    if ($line -match '^([^=]+)=(.*)$') { $environment[$Matches[1]] = $Matches[2] }
}

if ($SqlUser) {
    if (-not $SqlPassword) {
        $SqlPassword = Read-Host "Password for SQL login '$SqlUser'" -AsSecureString
    }
    Save-Protected $SqlPassword 'bridge.secret'
    $environment['Bridge__SqlLogin__UserName'] = $SqlUser
}

if ($PushUrl) {
    if (-not $PushKey) {
        $PushKey = Read-Host 'Bridge key from Manajoy (X-Bridge-Key)' -AsSecureString
    }
    Save-Protected $PushKey 'push.secret'
    $environment['Bridge__Push__Url'] = $PushUrl
}

if ($DisablePush) {
    $environment.Remove('Bridge__Push__Url')
    Remove-Item (Join-Path $InstallDir 'push.secret') -Force -ErrorAction SilentlyContinue
}

if ($environment.Count -gt 0) {
    New-ItemProperty -Path $serviceKey -Name Environment -PropertyType MultiString -Force `
        -Value @($environment.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) | Out-Null
} else {
    Remove-ItemProperty -Path $serviceKey -Name Environment -ErrorAction SilentlyContinue
}

Start-Service $ServiceName
Start-Sleep -Seconds 8

try {
    $health = Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 10
    Write-Host ("Bridge status: {0}, PCs: {1}, DB provider: {2}" -f $health.health.status, $health.health.pc_count, $health.health.db_provider)
    if ($health.push.enabled) {
        Start-Sleep -Seconds 5
        $push = (Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 10).push
        Write-Host ("Push: last success {0}, last error {1}" -f $push.last_success_at, $push.last_error)
    }
} catch {
    Write-Warning "Service started but /health did not answer yet: $($_.Exception.Message). See $InstallDir\logs."
}
