# Manajoy GameClass Bridge - preflight check (READ-ONLY: changes nothing).
# Checks that this PC can run the bridge: Windows version/bitness, SQL Server / MSDE,
# GameClass server, ODBC driver, free port and disk. Writes preflight-<PC>-<date>.txt.
#
#   powershell -ExecutionPolicy Bypass -File .\preflight.ps1
[CmdletBinding()]
param(
    [int]$Port = 8787,
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = 'Continue'
$results = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$Level, [string]$Check, [string]$Detail) {
    $results.Add([pscustomobject]@{ Level = $Level; Check = $Check; Detail = $Detail })
}

# --- Windows ---------------------------------------------------------------------------
$os = Get-CimInstance Win32_OperatingSystem
$build = [int]$os.BuildNumber
$is64 = [Environment]::Is64BitOperatingSystem
if ($build -ge 14393) {
    Add-Result 'OK' 'Windows' "$($os.Caption) build $build"
} else {
    # .NET 8 (the bridge runtime) needs Windows 10 1607+ / Server 2012+ class OS builds.
    Add-Result 'RED' 'Windows' "$($os.Caption) build $build - bridge (.NET 8) needs Windows 10 1607+ / 11 / Server 2016+"
}
Add-Result 'OK' 'Bitness' $(if ($is64) { '64-bit -> use bridge\x64' } else { '32-bit -> use bridge\x86' })

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Add-Result 'OK' 'Administrator' 'PowerShell is elevated'
} else {
    Add-Result 'YELLOW' 'Administrator' 'Not elevated - setup.ps1 must be run "As Administrator"'
}

# --- SQL Server / MSDE ---------------------------------------------------------------------
$sqlServices = @(Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'MSSQLSERVER' -or $_.Name -like 'MSSQL$*' })
if ($sqlServices.Count -eq 0) {
    Add-Result 'RED' 'SQL Server' 'No MSSQLSERVER / MSSQL$<instance> service on this PC - GameClass database may be on another PC'
} else {
    foreach ($svc in $sqlServices) {
        $level = if ($svc.Status -eq 'Running') { 'OK' } else { 'RED' }
        Add-Result $level 'SQL service' "$($svc.Name): $($svc.Status) ($($svc.StartType))"
    }
}

$sqlVersion = $null
foreach ($key in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\MSSQLServer\MSSQLServer\CurrentVersion',
                 'HKLM:\SOFTWARE\Microsoft\MSSQLServer\MSSQLServer\CurrentVersion') {
    $v = (Get-ItemProperty $key -ErrorAction SilentlyContinue).CurrentVersion
    if ($v) { $sqlVersion = $v; break }
}
if ($sqlVersion) {
    $major = [int]($sqlVersion.Split('.')[0])
    $note = if ($major -le 8) { 'SQL Server 2000 / MSDE - bridge uses the built-in "SQL Server" ODBC driver' } else { 'newer SQL Server' }
    Add-Result 'OK' 'SQL version' "$sqlVersion ($note)"
} else {
    Add-Result 'YELLOW' 'SQL version' 'Not found in registry (named instance or newer SQL Server?)'
}

$odbc = Get-ItemProperty 'HKLM:\SOFTWARE\ODBC\ODBCINST.INI\SQL Server' -ErrorAction SilentlyContinue
if ($odbc) {
    Add-Result 'OK' 'ODBC driver' "SQL Server ($($odbc.Driver))"
} else {
    Add-Result 'RED' 'ODBC driver' 'Built-in "SQL Server" ODBC driver not registered (64-bit view)'
}

# --- GameClass -----------------------------------------------------------------------------
$gcServer = Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\GameClass\Server' -ErrorAction SilentlyContinue
if (-not $gcServer) { $gcServer = Get-ItemProperty 'HKLM:\SOFTWARE\GameClass\Server' -ErrorAction SilentlyContinue }
if ($gcServer) {
    Add-Result 'OK' 'GameClass server' "version $($gcServer.CurrentVersion) in $($gcServer.InstallDirectory)"
} else {
    Add-Result 'YELLOW' 'GameClass server' 'Not installed on this PC (bridge must run where the GameClass SQL database is reachable)'
}

$gcRunning = Get-Process GCServer -ErrorAction SilentlyContinue
if ($gcRunning) {
    Add-Result 'OK' 'GCServer.exe' "running (PID $($gcRunning.Id -join ','))"
} else {
    Add-Result 'YELLOW' 'GCServer.exe' 'Not running now - live states will show "unknown" until an operator logs in to GameClass'
}

$client = Get-Service srvGCCL -ErrorAction SilentlyContinue
if ($client) {
    Add-Result 'INFO' 'GameClass client' "srvGCCL is installed here ($($client.Status)) - normal on a client PC, unusual on the operator PC"
}

$culture = (Get-Culture).Name
$systemLocale = (Get-WinSystemLocale).Name
if ($systemLocale -like 'ru-*' -and $culture -notlike 'ru-*') {
    Add-Result 'YELLOW' 'Locale' "System locale $systemLocale but user format $culture - GameClass may show 'BOF or EOF' errors (Set-Culture ru-RU fixes it)"
} else {
    Add-Result 'OK' 'Locale' "system $systemLocale, user $culture"
}

# --- Bridge prerequisites -------------------------------------------------------------------
$busy = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue
$existing = Get-Service ManajoyGameClassBridge -ErrorAction SilentlyContinue
if ($busy -and -not $existing) {
    $owner = (Get-Process -Id $busy[0].OwningProcess -ErrorAction SilentlyContinue).ProcessName
    Add-Result 'RED' "Port $Port" "already used by $owner - set Bridge:HttpUrls to another port"
} else {
    Add-Result 'OK' "Port $Port" $(if ($existing) { 'used by an existing Manajoy bridge' } else { 'free' })
}
if ($existing) {
    Add-Result 'INFO' 'Existing bridge' "service ManajoyGameClassBridge is $($existing.Status) - setup.ps1 will update it"
}

$drive = Get-PSDrive ($env:ProgramFiles.Substring(0, 1)) -ErrorAction SilentlyContinue
if ($drive -and $drive.Free -lt 500MB) {
    Add-Result 'RED' 'Disk' ("only {0:N0} MB free on {1}:" -f ($drive.Free / 1MB), $drive.Name)
} elseif ($drive) {
    Add-Result 'OK' 'Disk' ("{0:N1} GB free on {1}:" -f ($drive.Free / 1GB), $drive.Name)
}

# --- Summary --------------------------------------------------------------------------------
$verdict = if ($results.Level -contains 'RED') { 'RED - do not install, see RED lines' }
           elseif ($results.Level -contains 'YELLOW') { 'YELLOW - can install, read YELLOW lines' }
           else { 'GREEN - ready to install' }

$report = @(
    "Manajoy GameClass Bridge preflight  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  PC: $env:COMPUTERNAME"
    ($results | Format-Table -AutoSize -Wrap | Out-String -Width 200).TrimEnd()
    ''
    "VERDICT: $verdict"
) -join [Environment]::NewLine

Write-Host $report
try {
    $file = Join-Path $OutDir ("preflight-{0}-{1}.txt" -f $env:COMPUTERNAME, (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Set-Content -Path $file -Value $report -Encoding UTF8
    Write-Host "Saved: $file"
} catch {
    Write-Warning "Could not save the report: $($_.Exception.Message)"
}

if ($verdict -like 'RED*') { exit 2 } elseif ($verdict -like 'YELLOW*') { exit 1 } else { exit 0 }
