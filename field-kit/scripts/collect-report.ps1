# Manajoy GameClass Bridge - collects a diagnostic zip for Manajoy (no passwords or keys inside).
#
#   powershell -ExecutionPolicy Bypass -File .\collect-report.ps1
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Manajoy\GameClassBridge'),
    [string]$ApiBase = 'http://127.0.0.1:8787',
    [string]$OutDir = [Environment]::GetFolderPath('Desktop')
)

$ErrorActionPreference = 'Continue'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path $env:TEMP "manajoy-bridge-report-$stamp"
New-Item $work -ItemType Directory -Force | Out-Null

function Save-Api([string]$path, [string]$file) {
    try {
        $r = Invoke-WebRequest "$ApiBase$path" -UseBasicParsing -TimeoutSec 15
        [IO.File]::WriteAllText((Join-Path $work $file), $r.Content, (New-Object Text.UTF8Encoding($false)))
    } catch {
        $body = if ($_.Exception.Response) { (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } else { $_.Exception.Message }
        Set-Content (Join-Path $work $file) "ERROR: $body" -Encoding UTF8
    }
}

Save-Api '/health' 'health.json'
Save-Api '/api/v1/snapshot' 'snapshot.json'
Save-Api '/api/v1/events?since=0&limit=500' 'events.json'

$info = @(
    "PC: $env:COMPUTERNAME   time: $(Get-Date -Format o)"
    (Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, OSArchitecture | Format-List | Out-String)
    (Get-Service ManajoyGameClassBridge, MSSQLSERVER -ErrorAction SilentlyContinue | Select-Object Name, Status, StartType | Format-Table | Out-String)
    (Get-Process Manajoy.GameClass.Bridge, GCServer -ErrorAction SilentlyContinue | Select-Object Name, Id, @{n='WorkingSetMB';e={[math]::Round($_.WorkingSet64/1MB,1)}}, StartTime | Format-Table | Out-String)
    "Bridge files:"
    (Get-ChildItem $InstallDir -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -ne '.secret' } |
        Select-Object @{n='Path';e={$_.FullName.Substring($InstallDir.Length)}}, Length, LastWriteTime | Format-Table -AutoSize | Out-String -Width 200)
) -join [Environment]::NewLine
Set-Content (Join-Path $work 'system.txt') $info -Encoding UTF8

Copy-Item (Join-Path $InstallDir 'appsettings.json') $work -ErrorAction SilentlyContinue
foreach ($sub in 'logs', 'output') {
    $files = Get-ChildItem (Join-Path $InstallDir $sub) -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -ne '.secret' -and $_.Name -ne 'snapshot.json' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 3
    if ($files) {
        New-Item (Join-Path $work $sub) -ItemType Directory -Force | Out-Null
        $files | Copy-Item -Destination (Join-Path $work $sub)
    }
}
Get-ChildItem $PSScriptRoot -Filter 'preflight-*.txt' -ErrorAction SilentlyContinue | Copy-Item -Destination $work

# Secrets never leave the PC: the DPAPI files are excluded above; double-check nothing slipped in.
$leak = Get-ChildItem $work -Recurse -File | Where-Object { $_.Extension -eq '.secret' -or (Select-String -Path $_.FullName -Pattern 'PWD=|Password=' -Quiet) }
if ($leak) { $leak | Remove-Item -Force; Write-Warning "Removed $($leak.Count) file(s) that looked like they contained secrets." }

$zip = Join-Path $OutDir "manajoy-bridge-report-$env:COMPUTERNAME-$stamp.zip"
Compress-Archive -Path (Join-Path $work '*') -DestinationPath $zip -Force
Remove-Item $work -Recurse -Force
Write-Host "Report: $zip"
