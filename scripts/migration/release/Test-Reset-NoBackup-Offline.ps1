param([Parameter(Mandatory=$true)][string]$CompanionRoot)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$bundle=[IO.Path]::GetFullPath($CompanionRoot)
$fixture=Join-Path $repo ('.artifacts/reset-no-backup-test-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$old=Join-Path $repo '.artifacts/release-offline-bcadc9b253ce47a0b1fd94b5d4c6b4eb'
Copy-Item -LiteralPath (Join-Path $old 'preview') -Destination (Join-Path $fixture 'preview') -Recurse
Copy-Item -LiteralPath (Join-Path $old 'fake-approval') -Destination (Join-Path $fixture 'approval') -Recurse
$preview=Join-Path $fixture 'preview';$approvalDir=Join-Path $fixture 'approval'
$checks=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:checks++;Write-Output "PASS: $name"}
foreach($file in Get-ChildItem -LiteralPath $bundle -Filter '*.ps1'){
 $tokens=$null;$errors=$null;$null=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
 Check ($errors.Count -eq 0) ('PS5 syntax: '+$file.Name)
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $bundle 'Invoke-Reset-Without-TargetBackup.ps1') -CheckSetup
Check ($LASTEXITCODE -eq 0) 'Companion checksum verification without SQL'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $bundle '00-DryRun-Reset-LowLog.ps1') -PreviewDirectory $preview -SkipTargetBackup -CheckSetup
Check ($LASTEXITCODE -eq 0) 'DRYRUN compiles hasher without backup or SQL'
$approval=Get-Content -LiteralPath (Join-Path $approvalDir 'manifest.json') -Raw|ConvertFrom-Json
$approval.ScriptSha256=(Get-FileHash -LiteralPath (Join-Path $bundle '00-DryRun-Reset-LowLog.ps1')).Hash
$approval|Add-Member NoteProperty TargetBackupWaivedByUser $true -Force
$approval.BackupVerifyOnly='WAIVED_BY_USER';$approval.BackupFile=''
function Write-Approval {$approval|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $approvalDir 'manifest.json') -Encoding UTF8}
Write-Approval
$base=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $bundle '00-Commit-Reset-LowLog.ps1'),'-PreviewDirectory',$preview,'-SuccessfulDryRunDirectory',$approvalDir,'-Server','WIN-HU6RO2EMIJF\SQLEXPRESS','-ExpectedServer','WIN-HU6RO2EMIJF\SQLEXPRESS','-CheckSetup')
& powershell.exe @base -SkipTargetBackup
Check ($LASTEXITCODE -eq 0) 'Matching waived DRYRUN accepted for COMMIT setup only'
function Expect-Blocked([bool]$waive,[string]$name){
 $args2=$base;if($waive){$args2+='-SkipTargetBackup'}
 $blocked=$false;try{& powershell.exe @args2 2>&1|Out-Null;$blocked=$LASTEXITCODE -ne 0}catch{$blocked=$true}
 Check $blocked $name
}
Expect-Blocked $false 'Waived DRYRUN cannot be represented as backup-verified'
$approval.BackupVerifyOnly='PASS';Write-Approval
Expect-Blocked $true 'Waiver requires explicit WAIVED_BY_USER, never fabricated PASS'
$approval.TargetBackupWaivedByUser=$false;Write-Approval
Expect-Blocked $true 'Cannot change backup policy between dry-run and commit'
& powershell.exe @base
Check ($LASTEXITCODE -eq 0) 'Original backup-required setup still supported'
$approval.TargetBackupWaivedByUser=$true;$approval.BackupVerifyOnly='WAIVED_BY_USER';$approval.AllTargetRowsRestored='FAIL';Write-Approval
Expect-Blocked $true 'Backup waiver never bypasses rollback/content verification'
$approval.AllTargetRowsRestored='PASS';$approval.ScriptSha256='CHANGED';Write-Approval
Expect-Blocked $true 'Backup waiver never bypasses script fingerprint'
Write-Output "RESET_NO_BACKUP_OFFLINE_PASS: $checks checks; no database connection."
