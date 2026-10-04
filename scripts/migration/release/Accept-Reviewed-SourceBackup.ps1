[CmdletBinding()]
param(
 [string]$PackageRoot='C:\GaoMigration-20260929',
 [switch]$ConfirmSourceUnchanged,
 [switch]$ConfirmManualVerifyOnly,
 [switch]$CheckSetup
)
# Companion for the reviewed 20260929 package only. No original package files change.
# Accepts one specific SOURCE backup with the operator's SSMS VERIFYONLY evidence.
# Never creates a backup, runs an import/reset, or approves a TARGET backup.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$expectedPackageHash='FA43A454BB4CA6064A323C4A74C7B13E57B7B2A3896878D88C307319FD7AB983'
$expectedServer='WIN-HU6RO2EMIJF\SQLEXPRESS'
$backupFile='C:\Data\DataGaoStore.bak'
$backupGuid='138e0955-5375-4cf2-b66f-82f73e490411'

function Assert-ReviewedHeader([object[]]$Headers) {
 if($Headers.Count -ne 1){throw 'Expected exactly one backup set; do not guess FILE position.'}
 $h=$Headers[0]
 if($h.DatabaseName -cne 'DataGaoStore' -or $h.ServerName -ine $expectedServer -or
    [int]$h.BackupType -ne 1 -or [int]$h.Position -ne 1 -or
    $h.IsDamaged -ne $false -or $h.IsCopyOnly -ne $true -or
    $h.HasBackupChecksums -ne $false -or
    ([string]$h.BackupSetGUID).ToLowerInvariant() -cne $backupGuid -or
    [decimal]$h.BackupSize -ne 5358673920 -or
    ([datetime]$h.BackupStartDate).ToString('yyyy-MM-dd HH:mm:ss') -cne '2026-09-28 22:27:26' -or
    ([datetime]$h.BackupFinishDate).ToString('yyyy-MM-dd HH:mm:ss') -cne '2026-09-28 23:21:53'){
  throw 'Backup header differs from the specific SSMS evidence reviewed on 2026-09-29. Nothing accepted.'
 }
}
if($CheckSetup){
 $fixture=[pscustomobject]@{DatabaseName='DataGaoStore';ServerName=$expectedServer;BackupType=1;Position=1;IsDamaged=$false;IsCopyOnly=$true;HasBackupChecksums=$false;BackupSetGUID=$backupGuid;BackupSize=5358673920;BackupStartDate=[datetime]'2026-09-28T22:27:26';BackupFinishDate=[datetime]'2026-09-28T23:21:53'}
 Assert-ReviewedHeader @($fixture)
 $checks=1
 foreach($change in @(@('DatabaseName','GaoAppDb'),@('ServerName','OTHER\SQL'),@('BackupType',2),@('Position',2),@('IsDamaged',$true),@('IsCopyOnly',$false),@('BackupSetGUID',[guid]::Empty.ToString()),@('HasBackupChecksums',$true),@('BackupSize',1),@('BackupFinishDate',[datetime]'2026-09-29T01:00:00'))){
  $bad=$fixture|ConvertTo-Json|ConvertFrom-Json;$bad.($change[0])=$change[1];$blocked=$false
  try{Assert-ReviewedHeader @($bad)}catch{$blocked=$true}
  if(-not $blocked){throw ('Offline rejection failed: '+$change[0])};$checks++
 }
 foreach($badSet in @(@(),@($fixture,$fixture))){
  $blocked=$false;try{Assert-ReviewedHeader $badSet}catch{$blocked=$true}
  if(-not $blocked){throw 'Offline backup-set count rejection failed.'};$checks++
 }
 Write-Output "SOURCE_BACKUP_COMPANION_OFFLINE_PASS: $checks checks; no SQL connection or file writes."
 return
}
if(-not $ConfirmSourceUnchanged -or -not $ConfirmManualVerifyOnly){
 throw 'Requires the operator confirmations already reviewed: source unchanged since backup finish and SSMS VERIFYONLY FILE=1 passed. Do not use if either is untrue.'
}
$root=[IO.Path]::GetFullPath($PackageRoot)
$manifestPath=Join-Path $root 'checksums.json'
if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -cne $expectedPackageHash){throw 'This companion is bound to the reviewed 373-file package. No state changed.'}
& (Join-Path $root 'Verify-Package.ps1') -PackageRoot $root
$config=Get-Content -LiteralPath (Join-Path $root 'config.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($config.Server -ine $expectedServer -or $config.ExpectedServer -ine $expectedServer -or
   $config.SourceDatabase -cne 'DataGaoStore' -or $config.TargetDatabase -cne 'GaoAppDb' -or
   $config.RunName -notmatch '^[A-Za-z0-9_-]{1,60}$'){throw 'Unexpected migration configuration.'}
$runs=Join-Path $root ('runs/'+$config.RunName)
$statePath=Join-Path $runs 'state.json'
$stateHash=(Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
if($state.Server -ine $expectedServer -or $state.SourceDatabase -cne 'DataGaoStore' -or
   $state.TargetDatabase -cne 'GaoAppDb' -or $state.ResetCommitted -ne $false -or
   $state.ReviewedPackageSha256 -cne $expectedPackageHash){throw 'Run is not a pending reset reviewed against this package.'}
$previewPath=Join-Path $state.ResetPreviewDirectory 'manifest.json'
$preview=Get-Content -LiteralPath $previewPath -Raw -Encoding UTF8|ConvertFrom-Json
if($preview.Status -cne 'PREVIEW_READY_FOR_REVIEW' -or $preview.Server -ine $expectedServer -or
   $preview.TargetDatabase -cne 'GaoAppDb' -or -not $preview.AllowImportedTestReset -or
   $preview.PlanSha256 -cne (Get-FileHash -LiteralPath (Join-Path $root 'scripts/migration/initial-import/TABLE-PLAN.json') -Algorithm SHA256).Hash){throw 'Current reset preview is missing, blocked or changed.'}
$output=Join-Path $runs ('AcceptSourceBackup-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($output)
$receipt=[ordered]@{
 Status='STARTED';Server=$expectedServer;SourceDatabase='DataGaoStore';TargetDatabase='GaoAppDb';BackupFile=$backupFile
 BackupSetGuid=$backupGuid;HasBackupChecksums=$false;StartedAtUtc=[datetime]::UtcNow.ToString('o')
 PackageSha256=$expectedPackageHash;ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
 PreviewManifestSha256=(Get-FileHash -LiteralPath $previewPath -Algorithm SHA256).Hash
 VerificationMethod='Operator SSMS RESTORE VERIFYONLY WITH FILE=1, STATS=5; screenshot reviewed in migration chat'
 ManualVerifyCompletedAt='2026-09-29T01:14:13.4358783+07:00'
 ManualVerifyResult='The backup set on file 1 is valid.'
 SourceUnchangedSince='2026-09-28T23:21:53+07:00';SourceUnchangedConfirmedByOperator=$true
 Limitations='No backup checksums. No restore drill. Source freeze is an operator attestation, not automatically proven.'
 SqlWrites=$false;TargetBackupApproved=$false;AutomaticVerifyOnlyExecuted=$false
}
$connection=$null
try{
 $cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
 $cs['Data Source']=$expectedServer;$cs['Initial Catalog']='master';$cs['Integrated Security']=$true
 $cs['Encrypt']=$true;$cs['TrustServerCertificate']=$true;$cs['Connect Timeout']=15
 $cs['Application Name']='GaoAppReviewedSourceBackupHeaderOnly'
 $connection=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString);$connection.Open()
 $cmd=$connection.CreateCommand();$cmd.CommandTimeout=30;$cmd.CommandText="SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'));"
 try{if([string]$cmd.ExecuteScalar() -ine $expectedServer){throw 'Connected to an unexpected SQL instance.'}}finally{$cmd.Dispose()}
 $cmd=$connection.CreateCommand();$cmd.CommandTimeout=120;$cmd.CommandText='RESTORE HEADERONLY FROM DISK=@file;'
 [void]$cmd.Parameters.AddWithValue('@file',$backupFile)
 $table=[System.Data.DataTable]::new();$adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd)
 try{[void]$adapter.Fill($table)}finally{$adapter.Dispose();$cmd.Dispose()}
 $headers=@(foreach($row in $table.Rows){
  $record=[ordered]@{};foreach($column in $table.Columns){$record[$column.ColumnName]=$row[$column.ColumnName]};[pscustomobject]$record
 })
 Assert-ReviewedHeader $headers
 $receipt['HeaderMatched']='PASS'
 # Save explicit evidence first. It must never claim this tool ran VERIFYONLY.
 $receipt['Status']='SOURCE_BACKUP_ACCEPTED_MANUAL_VERIFY'
 $receiptPath=Join-Path $output 'manifest.json'
 $receipt|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $receiptPath -Encoding UTF8
 if((Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash -cne $stateHash){throw 'Run state changed concurrently. Stop other migration commands and review.'}
 $state|Add-Member NoteProperty VerifiedSourceBackup $backupFile -Force
 $state|Add-Member NoteProperty SourceBackupEvidence $receiptPath -Force
 $state|Add-Member NoteProperty SourceBackupVerificationMethod 'MANUAL_SSMS_VERIFYONLY_HEADER_MATCHED' -Force
 $state|Add-Member NoteProperty LastSuccessfulStep 'AcceptReviewedSourceBackup/MANUAL_EVIDENCE' -Force
 $state.PSObject.Properties.Remove('ResetDryRunDirectory') # Any older dry-run approval must be renewed.
 $tempPath=Join-Path $output 'state.pending.json'
 [IO.File]::WriteAllText($tempPath,($state|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
 [IO.File]::Replace($tempPath,$statePath,(Join-Path $output 'state.before.json'))
 $receipt['StateRegistered']=$true
 Write-Output 'SOURCE_BACKUP_ACCEPTED_MANUAL_VERIFY'
 Write-Output 'No new backup, no database writes. Target backup requirements remain unchanged.'
}catch{$receipt['Status']='FAILED';$receipt['Error']=$_.Exception.Message;throw}
finally{
 if($null -ne $connection){$connection.Dispose()}
 $receipt['FinishedAtUtc']=[datetime]::UtcNow.ToString('o')
 $receipt|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding UTF8
 Write-Output "Reports: $output"
}
