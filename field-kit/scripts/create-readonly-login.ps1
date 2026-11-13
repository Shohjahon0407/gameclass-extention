# Manajoy GameClass Bridge - creates the READ-ONLY SQL login the bridge uses.
#
# This is the only change the kit makes inside SQL Server: one login (default gcbridge_ro) that is a
# member of db_datareader in the GameClass database, with EXECUTE denied on every GameClass
# procedure (GameClass grants those to "public"). The script then proves it cannot write. Needs an administrator login once (often "sa" with an EMPTY password on GameClass
# installs) or Windows authentication.
#
#   powershell -ExecutionPolicy Bypass -File .\create-readonly-login.ps1                 # asks for sa password
#   powershell -ExecutionPolicy Bypass -File .\create-readonly-login.ps1 -WindowsAuth
#
# Called from setup.ps1 with -PassThru: returns the new password as a SecureString instead of printing it.
[CmdletBinding()]
param(
    [string]$Server = '(local)',
    [string]$Database = 'GameClass',
    [string]$Login = 'gcbridge_ro',
    [string]$AdminUser = 'sa',
    [securestring]$AdminPassword,
    [switch]$WindowsAuth,
    [switch]$ResetPassword,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'

function Get-Plain([securestring]$s) {
    if (-not $s) { return '' }
    [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
}

function Quote-Odbc([string]$value) { '{' + $value.Replace('}', '}}') + '}' }

function Open-Sql([string]$db, [string]$user, [string]$password, [bool]$trusted) {
    $cs = "Driver={SQL Server};Server=$(Quote-Odbc $Server);Database=$(Quote-Odbc $db);APP=Manajoy.GameClass.Setup;"
    if ($trusted) { $cs += 'Trusted_Connection=Yes;' } else { $cs += "UID=$(Quote-Odbc $user);PWD=$(Quote-Odbc $password);" }
    $c = New-Object System.Data.Odbc.OdbcConnection $cs
    $c.ConnectionTimeout = 10
    $c.Open()
    return $c
}

# ODBC parameters are positional ('?'), so values are passed as an ordered array.
function New-Command($conn, [string]$sql, [object[]]$values) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $i = 0
    foreach ($v in $values) { [void]$cmd.Parameters.AddWithValue("p$i", $v); $i++ }
    return $cmd
}

function Invoke-Scalar($conn, [string]$sql, [object[]]$values = @()) { (New-Command $conn $sql $values).ExecuteScalar() }

function Invoke-NonQuery($conn, [string]$sql, [object[]]$values = @()) { [void](New-Command $conn $sql $values).ExecuteNonQuery() }

if ($Login -notmatch '^[A-Za-z][A-Za-z0-9_]{2,30}$') { throw "Login name '$Login' is not allowed" }

# --- 1. Connect as administrator -----------------------------------------------------------
if (-not $WindowsAuth -and -not $PSBoundParameters.ContainsKey('AdminPassword')) {
    $AdminPassword = Read-Host "Password for SQL login '$AdminUser' (GameClass default: empty - just press Enter)" -AsSecureString
}
try {
    $admin = Open-Sql $Database $AdminUser (Get-Plain $AdminPassword) $WindowsAuth.IsPresent
} catch {
    throw "Could not connect as administrator: $($_.Exception.Message)`nTry -WindowsAuth, or ask for the 'sa' password."
}

try {
    $isSysAdmin = [int](Invoke-Scalar $admin "SELECT IS_SRVROLEMEMBER('sysadmin')")
    if ($isSysAdmin -ne 1) {
        $isSecAdmin = [int](Invoke-Scalar $admin "SELECT IS_SRVROLEMEMBER('securityadmin')")
        if ($isSecAdmin -ne 1) { throw "This login cannot create logins (needs sysadmin or securityadmin)." }
    }

    # --- 2. Create (or reset) the login with a random password --------------------------------
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] 24
    $rng.GetBytes($bytes)
    $password = -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })

    $exists = [int](Invoke-Scalar $admin 'SELECT COUNT(*) FROM master.dbo.syslogins WHERE name = ?' @($Login))
    if ($exists -gt 0) {
        if (-not $ResetPassword) {
            throw "Login '$Login' already exists. Re-run with -ResetPassword to give it a new password."
        }
        Invoke-NonQuery $admin 'EXEC master.dbo.sp_password @old = NULL, @new = ?, @loginame = ?' @($password, $Login)
        Write-Host "Password of '$Login' was reset."
    } else {
        Invoke-NonQuery $admin 'EXEC master.dbo.sp_addlogin @loginame = ?, @passwd = ?, @defdb = ?' @($Login, $password, $Database)
        Write-Host "Login '$Login' created."
    }

    $hasUser = [int](Invoke-Scalar $admin 'SELECT COUNT(*) FROM sysusers WHERE name = ?' @($Login))
    if ($hasUser -eq 0) {
        Invoke-NonQuery $admin 'EXEC sp_grantdbaccess @loginame = ?' @($Login)
    }
    Invoke-NonQuery $admin "EXEC sp_addrolemember @rolename = 'db_datareader', @membername = ?" @($Login)

    # GameClass grants EXECUTE on its own procedures to "public" - including writers such as
    # SessionsInsert, SessionsStop, AccountsMoneyPay, RegistryUpdate and UsersCreate - and every
    # database user inherits that. DENY overrides the public grant for this login only.
    $routines = New-Object System.Collections.Generic.List[string]
    $reader = (New-Command $admin "SELECT name FROM sysobjects WHERE type IN ('P', 'FN') AND OBJECTPROPERTY(id, 'IsMSShipped') = 0" @()).ExecuteReader()
    try { while ($reader.Read()) { $routines.Add($reader.GetString(0)) } } finally { $reader.Close() }
    foreach ($routine in $routines) {
        Invoke-NonQuery $admin ("DENY EXECUTE ON [{0}] TO [{1}]" -f $routine.Replace(']', ']]'), $Login)
    }
    Write-Host "EXECUTE denied on $($routines.Count) GameClass procedures/functions."
} finally {
    $admin.Close()
}

# --- 3. Prove the login can read but cannot write -----------------------------------------
$ro = Open-Sql $Database $Login $password $false
try {
    $checks = [ordered]@{
        'can read Computers'      = ([int](Invoke-Scalar $ro 'SELECT COUNT(*) FROM Computers') -ge 0)
        'db_datareader'           = ([int](Invoke-Scalar $ro "SELECT IS_MEMBER('db_datareader')") -eq 1)
        'NOT db_datawriter'       = ([int](Invoke-Scalar $ro "SELECT IS_MEMBER('db_datawriter')") -eq 0)
        'NOT db_owner'            = ([int](Invoke-Scalar $ro "SELECT IS_MEMBER('db_owner')") -eq 0)
        'NOT sysadmin'            = ([int](Invoke-Scalar $ro "SELECT IS_SRVROLEMEMBER('sysadmin')") -eq 0)
    }
    # A write that would change no rows: SQL Server still checks the permission and must refuse it.
    try {
        Invoke-NonQuery $ro 'UPDATE Registry SET [value] = [value] WHERE 1 = 0'
        $checks['write is refused'] = $false
    } catch {
        $checks['write is refused'] = ($_.Exception.Message -match 'permission|denied|229')
    }
    # PERMISSIONS() bit 32 = EXECUTE for the current login.
    $executable = [int](Invoke-Scalar $ro "SELECT COUNT(*) FROM sysobjects WHERE type IN ('P', 'FN') AND OBJECTPROPERTY(id, 'IsMSShipped') = 0 AND (PERMISSIONS(id) & 32) = 32")
    $checks['no GameClass procedure can be executed'] = ($executable -eq 0)
    if ([int](Invoke-Scalar $ro "SELECT COUNT(*) FROM sysobjects WHERE name = 'SessionsUpdateTimeCost' AND type = 'P'") -eq 1) {
        # Would update no row even if it ran (segment id 0 does not exist).
        try {
            Invoke-NonQuery $ro 'EXEC SessionsUpdateTimeCost @idSessionsAdd = 0, @TimeCost = 0'
            $checks['procedure write is refused'] = $false
        } catch {
            $checks['procedure write is refused'] = ($_.Exception.Message -match 'permission|denied|229')
        }
    }
} finally {
    $ro.Close()
}

$checks.GetEnumerator() | ForEach-Object { Write-Host ("  [{0}] {1}" -f $(if ($_.Value) { 'OK' } else { 'FAIL' }), $_.Key) }
if ($checks.Values -contains $false) { throw 'Read-only verification failed - do not use this login.' }

if ($PassThru) {
    return (ConvertTo-SecureString $password -AsPlainText -Force)
}
Write-Host ''
Write-Host "Login '$Login' is read-only. Its password (shown once, give it to install-service.ps1):"
Write-Host "  $password"
