[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceDatabase,
    [Parameter(Mandatory)][string]$TargetDatabase,
    [Parameter(Mandatory)][string]$BackupPath,
    [Parameter(Mandatory)][string]$RetainedKeyRingPath,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference='Stop'
# Writes a pending worksheet only. No SQL, directory copy, key inspection,
# service/control command, migration, deletion, restore or deployment occurs.
if($SourceDatabase -notmatch '^[A-Za-z][A-Za-z0-9_]{1,127}$' -or $TargetDatabase -notmatch '^[A-Za-z][A-Za-z0-9_]{1,127}$' -or
    $SourceDatabase.Equals($TargetDatabase,[StringComparison]::OrdinalIgnoreCase) -or
    $SourceDatabase -in @('Main','GaoAppDb','master','model','msdb','tempdb') -or $TargetDatabase -in @('Main','GaoAppDb','master','model','msdb','tempdb')) {throw 'RECOVERY_DATABASE_SCOPE_INVALID'}
if(-not [IO.Path]::IsPathFullyQualified($BackupPath) -or -not [IO.Path]::IsPathFullyQualified($RetainedKeyRingPath)){throw 'RECOVERY_PATHS_MUST_BE_EXPLICIT_ABSOLUTE'}
if(Test-Path -LiteralPath $OutputPath){throw 'REFUSE_OVERWRITE_EXISTING_EVIDENCE'}
$plan=[ordered]@{
    status='PENDING_NOT_EXECUTED'; published=$false; sourceDatabase=$SourceDatabase; targetDatabase=$TargetDatabase
    backup=[ordered]@{path=$BackupPath;sha256=$null;bytes=$null;backupPointUtc=$null;sqlOperator=$null;actualRestoreEvidence=$null}
    retainedKeys=[ordered]@{samePrivatePath=$RetainedKeyRingPath;sameWindowsIdentityAndProfileConfirmed=$false;copyOrExportAuthorized=$false;privateContentsInspected=$false}
    steps=@('Keep new intake stopped; preserve separately hosted bank/invoice workers and all protected liabilities.','Record supported schema/history and exact source/financial/physical identities at backup point.','Have an authorized SQL operator rehearse backup/restore to a different owned target with server-accessible approved paths.','Use the SAME retained key root/application name/service identity/profile; never regenerate on failure.','Validate original guest token and committed command replay, late-credit processing, stock/cashflow/refund budgets and unresolved attention.','Record post-backup events/RPO gap and actual RTO; reconcile before any later reopening.','Prefer forward repair after durable financial activity; never delete orders/receipts or waive migration Down guards.')
    observedRpo=$null; observedRto=$null; postBackupEventsReconciled=$false; ownerApproval=$null; releaseAuthorized=$false
}
$json=$plan|ConvertTo-Json -Depth 8
$stream=[IO.File]::Open([IO.Path]::GetFullPath($OutputPath),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$bytes=[Text.Encoding]::UTF8.GetBytes($json);$stream.Write($bytes)}finally{$stream.Dispose()}
Write-Output 'PENDING_RECOVERY_WORKSHEET_CREATED_NO_OPERATION_EXECUTED'
