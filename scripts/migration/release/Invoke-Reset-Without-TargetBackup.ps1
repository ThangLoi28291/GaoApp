[CmdletBinding()]
param(
 [ValidateSet('DRYRUN','COMMIT')][string]$Mode='DRYRUN',
 [string]$PackageRoot='C:\GaoMigration-20260929',
 [switch]$AllowCommit,
 [switch]$CheckSetup
)
# Separate companion entry point, explicitly requested by the operator.
# Does not modify the original 373-file package. Only the target-backup requirement
# is waived; preview, source evidence, hashes, FK/KEEP checks and rollback remain.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$here=Split-Path -Parent $PSCommandPath
& (Join-Path $here 'Verify-Package.ps1') -PackageRoot $here
if($CheckSetup){Write-Output 'NO_TARGET_BACKUP_SETUP_PASS: companion integrity verified; no SQL or state writes.';return}
if($Mode -eq 'COMMIT' -and -not $AllowCommit){throw 'Use -AllowCommit only after reviewing this companion DRYRUN.'}
$root=[IO.Path]::GetFullPath($PackageRoot)
$expectedHash='FA43A454BB4CA6064A323C4A74C7B13E57B7B2A3896878D88C307319FD7AB983'
if((Get-FileHash -LiteralPath (Join-Path $root 'checksums.json')).Hash -cne $expectedHash){throw 'Use the reviewed 373-file migration package.'}
& (Join-Path $root 'Verify-Package.ps1') -PackageRoot $root
$config=Get-Content -LiteralPath (Join-Path $root 'config.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($config.Server -ine 'WIN-HU6RO2EMIJF\SQLEXPRESS' -or $config.ExpectedServer -ine $config.Server -or
   $config.SourceDatabase -cne 'DataGaoStore' -or $config.TargetDatabase -cne 'GaoAppDb' -or
   $config.TargetContainsOnlyTestBusinessData -ne $true -or $config.RunName -notmatch '^[A-Za-z0-9_-]{1,60}$'){
 throw 'Unexpected server/database or target is not declared TEST.'
}
$runs=Join-Path $root ('runs/'+$config.RunName);$statePath=Join-Path $runs 'state.json'
$stateHash=(Get-FileHash -LiteralPath $statePath).Hash
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
if($state.ResetCommitted -ne $false -or $state.Server -ine $config.Server -or
   $state.SourceDatabase -cne $config.SourceDatabase -or $state.TargetDatabase -cne $config.TargetDatabase -or
   $state.ReviewedPackageSha256 -cne $expectedHash){throw 'Reset already committed or state differs from reviewed package.'}
$source=Get-Content -LiteralPath $state.SourceBackupEvidence -Raw -Encoding UTF8|ConvertFrom-Json
if($source.Status -cne 'SOURCE_BACKUP_ACCEPTED_MANUAL_VERIFY' -or $source.HeaderMatched -cne 'PASS' -or
   $source.Server -ine $config.Server -or $source.SourceDatabase -cne 'DataGaoStore' -or
   $source.SourceUnchangedConfirmedByOperator -ne $true -or $source.BackupFile -cne $state.VerifiedSourceBackup -or
   $source.PackageSha256 -cne $expectedHash){throw 'The accepted SOURCE backup evidence is missing or mismatched.'}
if((Get-FileHash -LiteralPath (Join-Path $here 'TABLE-PLAN.json')).Hash -cne
   (Get-FileHash -LiteralPath (Join-Path $root 'scripts/migration/initial-import/TABLE-PLAN.json')).Hash){throw 'Keep/clear plan changed.'}
$output=Join-Path $runs ('Reset-no-target-backup-'+$Mode.ToLowerInvariant()+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$auditPath=$output+'-operator.json'
@{Mode=$Mode;TargetBackupWaivedByUser=$true;Reason='Operator explicitly requested skipping GaoAppDb backup in migration chat';SourceBackupEvidence=$state.SourceBackupEvidence;CompanionSha256=(Get-FileHash -LiteralPath (Join-Path $here 'checksums.json')).Hash;StartedAtUtc=[datetime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath $auditPath -Encoding UTF8
function Save-State([string]$suffix){
 if((Get-FileHash -LiteralPath $statePath).Hash -cne $script:stateHash){throw 'State changed concurrently. Review SQL results before retrying; do not rerun reset blindly.'}
 $temp=$output+'-'+$suffix+'.pending.json';$before=$output+'-'+$suffix+'.state-before.json'
 [IO.File]::WriteAllText($temp,($state|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
 [IO.File]::Replace($temp,$statePath,$before)
 $script:stateHash=(Get-FileHash -LiteralPath $statePath).Hash
}
$arguments=@('-Server',$config.Server,'-ExpectedServer',$config.ExpectedServer,'-TargetDatabase',$config.TargetDatabase,'-PreviewDirectory',$state.ResetPreviewDirectory,'-OutputDirectory',$output,'-SkipTargetBackup')
if($Mode -eq 'DRYRUN'){
 $state.PSObject.Properties.Remove('ResetDryRunDirectory');Save-State 'before-run'
 $file=Join-Path $here '00-DryRun-Reset-LowLog.ps1'
}else{
 $file=Join-Path $here '00-Commit-Reset-LowLog.ps1'
 $arguments+=@('-SuccessfulDryRunDirectory',$state.ResetDryRunDirectory,'-AllowCommit')
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $file @arguments
if($LASTEXITCODE -ne 0){throw 'Reset failed. Preserve reports and review; no success registered.'}
$report=Get-Content -LiteralPath (Join-Path $output 'manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
$expected=if($Mode -eq 'DRYRUN'){'DRYRUN_PASS_ROLLED_BACK'}else{'RESET_COMMIT_PASS'}
if($report.Status -cne $expected -or $report.BackupVerifyOnly -cne 'WAIVED_BY_USER' -or $report.TargetBackupWaivedByUser -ne $true){throw 'Unexpected reset report; inspect actual SQL outcome.'}
if($Mode -eq 'DRYRUN'){$state|Add-Member NoteProperty ResetDryRunDirectory $output -Force}
else{$state|Add-Member NoteProperty ResetCommitted $true -Force}
$state|Add-Member NoteProperty TargetBackupWaivedByUser $true -Force
$state|Add-Member NoteProperty TargetBackupWaiverEvidence $auditPath -Force
$state|Add-Member NoteProperty LastSuccessfulStep ('ResetWithoutTargetBackup/'+$Mode) -Force
Save-State 'result'
Write-Output "STEP_FINISHED: ResetWithoutTargetBackup / $Mode"
