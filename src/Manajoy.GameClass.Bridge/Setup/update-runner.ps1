param([Parameter(Mandatory = $true)][string]$PlanPath)
# Runs elevated, outside Program Files, after the panel has exited. Windows PowerShell 5.1 compatible.
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanPath -Raw -Encoding UTF8 | ConvertFrom-Json
$stage = Split-Path -Parent $PlanPath
$backup = Join-Path $stage 'backup'
$serviceName = 'ManajoyGameClassBridge'
$exe = Join-Path $plan.installDir 'Manajoy.GameClass.Bridge.exe'
$mutex = New-Object Threading.Mutex($false, 'Global\ManajoyGameClassUpdate')
$ownsMutex = $false
$wasRunning = $false
$backedUp = $false
$changed = $false
$metadata = @{}
$permissions = @{}
$result = 'failed'
$detail = ''

function Save-Result {
    $value = @{ state = $result; version = $plan.expectedVersion; detail = $detail; at = [DateTime]::UtcNow.ToString('o') }
    $temporary = $plan.resultPath + '.new'
    $value | ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $plan.resultPath -Force
}

function App-Files([string]$root) {
    # Logs/output can be large and are never program payload. All configuration and secrets ARE backed up.
    Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object {
        $relative = $_.FullName.Substring($root.TrimEnd('\').Length + 1)
        $relative -notmatch '^(logs|output)\\'
    }
}

function Installer-Registry([bool]$restore) {
    $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry32)
    try {
        foreach ($path in @('Software\Manajoy\GameClassBridge', 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F3C2A51-6D4B-4E7A-9C1F-3B2E5D7A9C41}_is1')) {
            $key = $hive.OpenSubKey($path, $restore)
            if (-not $key) { continue }
            try {
                if (-not $restore) {
                    $values = @{}
                    foreach ($name in $key.GetValueNames()) { $values[$name] = @{ value = $key.GetValue($name); kind = $key.GetValueKind($name) } }
                    $metadata[$path] = $values
                } elseif ($metadata.ContainsKey($path)) {
                    $values = $metadata[$path]
                    foreach ($name in $key.GetValueNames()) { if (-not $values.ContainsKey($name)) { $key.DeleteValue($name) } }
                    foreach ($name in $values.Keys) { $key.SetValue($name, $values[$name].value, $values[$name].kind) }
                }
            } finally { $key.Dispose() }
        }
    } finally { $hive.Dispose() }
}

function Stop-Bridge {
    $svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -ErrorAction Stop
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(45))
    }
}

function Installed-Version([string]$path) {
    return [Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion.Split('+')[0]
}

try {
    $ownsMutex = $mutex.WaitOne(0)
    if (-not $ownsMutex) { exit 2 }
    Set-Content -LiteralPath (Join-Path $stage 'runner.ready') -Value 'ready' -Encoding ASCII
    # Abort rather than overwrite files while the old panel still holds them.
    $parent = Get-Process -Id $plan.parentId -ErrorAction SilentlyContinue
    if ($parent -and -not $parent.WaitForExit(45000)) { throw 'The settings window did not exit.' }
    $file = Get-Item -LiteralPath $plan.installer
    if ($file.Length -ne $plan.size -or (Get-FileHash -LiteralPath $plan.installer -Algorithm SHA256).Hash -ne $plan.sha256) {
        throw 'Installer checksum mismatch.'
    }
    $svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $svc) { throw 'The bridge service is not installed.' }
    $wasRunning = $svc.Status -ne 'Stopped'
    Stop-Bridge
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $originalFiles = @(App-Files $plan.installDir)
    foreach ($file in $originalFiles) {
        $relative = $file.FullName.Substring($plan.installDir.TrimEnd('\').Length + 1)
        $permissions[$relative] = Get-Acl -LiteralPath $file.FullName
        $target = Join-Path $backup $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
    Installer-Registry $false
    $backedUp = $true
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/RESTARTEXITCODE=3010', '/SP-', '/AUTOUPDATE=1',
        ('/DIR="' + $plan.installDir + '"'), ('/LOG="' + (Join-Path $stage 'installer.log') + '"'))
    $changed = $true
    $installer = Start-Process -FilePath $plan.installer -ArgumentList $arguments -Wait -PassThru
    if ($installer.ExitCode -eq 3010) {
        $result = 'restart_required'
    } else {
        if ($installer.ExitCode -ne 0) { throw ('Installer exit code ' + $installer.ExitCode) }
        $actual = Installed-Version $exe
        if ([version]$actual -ne [version]$plan.expectedVersion) { throw 'Installed version does not match the release.' }
        if ($wasRunning) {
            Start-Service -Name $serviceName
            (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(45))
            # A DB outage may give HTTP 503: verify the running version, not the club's database availability.
            $healthy = $false
            for ($attempt = 0; $attempt -lt 30; $attempt++) {
                $response = $null
                try {
                    $request = [Net.HttpWebRequest]::Create($plan.healthUrl)
                    $request.Timeout = 2000
                    $request.AllowAutoRedirect = $false
                    try { $response = $request.GetResponse() } catch [Net.WebException] { $response = $_.Exception.Response }
                    if ($response) {
                        $reader = New-Object IO.StreamReader($response.GetResponseStream())
                        try { $health = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                        if ($health.version -eq $plan.expectedVersion) { $healthy = $true; break }
                    }
                } catch { } finally { if ($response) { $response.Dispose() } }
                Start-Sleep -Seconds 2
            }
            if (-not $healthy) { throw 'The updated service did not become ready.' }
        }
        $result = 'updated'
    }
} catch {
    $detail = $_.Exception.Message
    if ($backedUp -and $changed) {
        try {
            Stop-Bridge
            # Remove only files introduced by the attempted update; restore the exact original payload/data.
            foreach ($file in @(App-Files $plan.installDir)) {
                $relative = $file.FullName.Substring($plan.installDir.TrimEnd('\').Length + 1)
                if (-not (Test-Path -LiteralPath (Join-Path $backup $relative))) { Remove-Item -LiteralPath $file.FullName -Force }
            }
            foreach ($file in @(App-Files $backup)) {
                $relative = $file.FullName.Substring($backup.Length + 1)
                $target = Join-Path $plan.installDir $relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $target -Force
                Set-Acl -LiteralPath $target -AclObject $permissions[$relative]
            }
            Installer-Registry $true
            if ($wasRunning) {
                Start-Service -Name $serviceName
                (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(45))
            }
            $result = 'rolled_back'
        } catch { $detail += '; restore failed: ' + $_.Exception.Message }
    } elseif ($wasRunning) {
        try { Start-Service -Name $serviceName } catch { }
    }
} finally {
    if ($ownsMutex) {
        try { Save-Result } catch { }
        $mutex.ReleaseMutex()
        # Avoid deleting arbitrary downloaded files: only this updater-owned staging folder is cleaned.
        if ($result -eq 'updated') {
            Set-Location -LiteralPath $plan.installDir
            try { Remove-Item -LiteralPath $stage -Recurse -Force } catch { }
        } else {
            try { Remove-Item -LiteralPath $plan.installer -Force } catch { }
        }
        if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe -WorkingDirectory $plan.installDir }
    }
    $mutex.Dispose()
}
