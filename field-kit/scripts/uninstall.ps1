# Manajoy GameClass Bridge - complete removal (run "As Administrator").
#
#   powershell -ExecutionPolicy Bypass -File .\uninstall.ps1             # service + files
#   powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -DropLogin  # also the gcbridge_ro SQL login
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Manajoy\GameClassBridge'),
    [switch]$DropLogin,
    [string]$Server = '(local)',
    [string]$Database = 'GameClass',
    [string]$Login = 'gcbridge_ro',
    [string]$AdminUser = 'sa',
    [securestring]$AdminPassword,
    [switch]$WindowsAuth
)

$ErrorActionPreference = 'Stop'
$service = Get-Service ManajoyGameClassBridge -ErrorAction SilentlyContinue
if ($service) {
    Stop-Service ManajoyGameClassBridge -Force -ErrorAction SilentlyContinue
    Get-Process 'Manajoy.GameClass.Bridge' -ErrorAction SilentlyContinue | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
    sc.exe delete ManajoyGameClassBridge | Out-Null
    Write-Host 'Service removed.'
}
if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Removed $InstallDir"
    $parent = Split-Path $InstallDir
    if ((Test-Path $parent) -and -not (Get-ChildItem $parent -Force)) { Remove-Item $parent -Force }
}

if ($DropLogin) {
    if ($Login -notmatch '^[A-Za-z][A-Za-z0-9_]{2,30}$') { throw "Login name '$Login' is not allowed" }
    if (-not $WindowsAuth -and -not $PSBoundParameters.ContainsKey('AdminPassword')) {
        $AdminPassword = Read-Host "Password for SQL login '$AdminUser' (empty: press Enter)" -AsSecureString
    }
    $cs = "Driver={SQL Server};Server={$Server};Database={$Database};APP=Manajoy.GameClass.Setup;"
    if ($WindowsAuth) { $cs += 'Trusted_Connection=Yes;' }
    else {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($AdminPassword))
        $cs += "UID={$AdminUser};PWD={" + $plain.Replace('}', '}}') + '};'
    }
    $conn = New-Object System.Data.Odbc.OdbcConnection $cs
    $conn.Open()
    try {
        foreach ($sql in "IF USER_ID('$Login') IS NOT NULL EXEC sp_revokedbaccess '$Login'",
                         "IF EXISTS (SELECT * FROM master.dbo.syslogins WHERE name = '$Login') EXEC master.dbo.sp_droplogin '$Login'") {
            $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; [void]$cmd.ExecuteNonQuery()
        }
    } finally { $conn.Close() }
    Write-Host "SQL login '$Login' removed."
}
Write-Host 'Manajoy GameClass Bridge is fully removed. GameClass itself was not touched.'
