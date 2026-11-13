$ErrorActionPreference = 'Stop'
$original = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../src/Manajoy.GameClass.Bridge/Setup/update-runner.ps1') -Raw
$original = $original.Replace('Global\ManajoyGameClassUpdate', ('ManajoyUpdateTest-' + [guid]::NewGuid().ToString('N')))
$ast = [System.Management.Automation.Language.Parser]::ParseInput($original, [ref]$null, [ref]$null)
$registry = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Installer-Registry' }, $true)
# Windows registry is unavailable on Linux. Exercise the actual handoff/backup/restore control flow with service/process mocks.
$source = $original.Remove($registry.Extent.StartOffset, $registry.Extent.EndOffset - $registry.Extent.StartOffset).Insert($registry.Extent.StartOffset, 'function Installer-Registry([bool]$restore) {}')
$testLocation = (Get-Location).Path
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$null, [ref]$null)
$version = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Installed-Version' }, $true)
$source = $source.Remove($version.Extent.StartOffset, $version.Extent.EndOffset - $version.Extent.StartOffset).Insert($version.Extent.StartOffset, 'function Installed-Version([string]$path) { if ($global:ManajoyUpdateScenario -eq "wrong-version") { return "0.0.0" }; return $global:ManajoyUpdateFixture.expectedVersion }')
function Get-Acl { param($LiteralPath) return 'fixture-acl' }
function Set-Acl { param($LiteralPath, $AclObject) if ($AclObject -ne 'fixture-acl') { throw 'Original permissions were not restored.' } }
function Get-Service { param($Name, $ErrorAction) [pscustomobject]@{Status='Stopped'} }
function Get-Process { param($Id, $ErrorAction) return $null }
function Start-Service { param($Name) throw 'A stopped service must stay stopped.' }
function Start-Process {
 param($FilePath, $ArgumentList, [switch]$Wait, [switch]$PassThru, $WorkingDirectory)
 if ($FilePath -eq $global:ManajoyUpdateFixture.installer) {
   Set-Content -LiteralPath (Join-Path $global:ManajoyUpdateFixture.installDir 'Manajoy.GameClass.Bridge.exe') -Value 'partial update'
   Set-Content -LiteralPath (Join-Path $global:ManajoyUpdateFixture.installDir 'new.dll') -Value 'new'
   Remove-Item -LiteralPath (Join-Path $global:ManajoyUpdateFixture.installDir 'old.dll')
   if ($global:ManajoyUpdateScenario -eq 'installer-fails') { Set-Content -LiteralPath (Join-Path $global:ManajoyUpdateFixture.installDir 'bridge.secret') -Value 'changed' }
   $code = if ($global:ManajoyUpdateScenario -eq 'installer-fails') {1} elseif ($global:ManajoyUpdateScenario -eq 'restart-required') {3010} else {0}
   return [pscustomobject]@{ExitCode=$code}
 }
 $global:ManajoyUpdateReopened = $FilePath
}
foreach ($scenario in @('installer-fails', 'corrupt-download', 'wrong-version', 'success', 'restart-required')) {
 $global:ManajoyUpdateScenario = $scenario
 $root = Join-Path ([IO.Path]::GetTempPath()) ('manajoy-runner-' + [guid]::NewGuid().ToString('N'))
 $stage = Join-Path $root 'stage'
 $app = Join-Path $root 'app'
 New-Item -ItemType Directory -Path $stage,$app -Force | Out-Null
 foreach ($name in @('Manajoy.GameClass.Bridge.exe','old.dll','bridge.secret','appsettings.json','layout.json')) { Set-Content -LiteralPath (Join-Path $app $name) -Value ('original-' + $name) }
 $before = @{}; Get-ChildItem $app -File | ForEach-Object { $before[$_.Name] = (Get-FileHash $_.FullName).Hash }
 $installer = Join-Path $stage 'installer.exe'; Set-Content -LiteralPath $installer -Value 'fixture installer'
 $global:ManajoyUpdateFixture = @{ installer=$installer;installDir=$app;parentId=999999;expectedVersion='99.0.0';sha256=(Get-FileHash $installer).Hash;size=(Get-Item $installer).Length;healthUrl='http://127.0.0.1:8787/health';resultPath=(Join-Path $root 'last-result.json') }
 if ($scenario -eq 'corrupt-download') { $global:ManajoyUpdateFixture.sha256 = ('a' * 64) }
 $plan = Join-Path $stage 'plan.json'; $global:ManajoyUpdateFixture | ConvertTo-Json | Set-Content -LiteralPath $plan
 $runner = Join-Path $stage 'test-runner.ps1'; Set-Content -LiteralPath $runner -Value $source
 & $runner -PlanPath $plan
 $result = Get-Content -LiteralPath $global:ManajoyUpdateFixture.resultPath -Raw | ConvertFrom-Json
 $expected = if ($scenario -in @('installer-fails','wrong-version')) {'rolled_back'} elseif ($scenario -eq 'success') {'updated'} elseif ($scenario -eq 'restart-required') {'restart_required'} else {'failed'}
 if ($result.state -ne $expected) { throw "$scenario result: $($result | ConvertTo-Json -Compress)" }
 if ($scenario -notin @('success','restart-required')) {
  foreach ($name in $before.Keys) { if ((Get-FileHash (Join-Path $app $name)).Hash -ne $before[$name]) { throw "$scenario failed to preserve $name" } }
  if (Test-Path (Join-Path $app 'new.dll')) { throw 'New payload file was not rolled back.' }
 }
 foreach ($name in @('bridge.secret','appsettings.json','layout.json')) {
  if ((Get-FileHash (Join-Path $app $name)).Hash -ne $before[$name]) { throw "$scenario lost $name" }
 }
 if (Test-Path $installer) { throw 'Installer was not cleaned.' }
 if ($scenario -eq 'success' -and (Test-Path $stage)) { throw 'Successful staging directory was not cleaned.' }
 Write-Output "PASS $scenario : program, secrets, settings and layout handled correctly"
 Set-Location -LiteralPath $testLocation
 Remove-Item -LiteralPath $root -Recurse -Force
}

Remove-Variable -Scope Global -Name ManajoyUpdateFixture,ManajoyUpdateScenario,ManajoyUpdateReopened -ErrorAction SilentlyContinue
