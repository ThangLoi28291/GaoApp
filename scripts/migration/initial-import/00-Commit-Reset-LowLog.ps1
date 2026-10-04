[CmdletBinding()]
param(
    [string]$Server = '.\SQLEXPRESS',
    [string]$ExpectedServer,
    [ValidateSet('GaoAppDb')][string]$TargetDatabase = 'GaoAppDb',
    [string]$PreviewDirectory,
    [string]$BackupFile,
    [string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$SuccessfulDryRunDirectory,
    [switch]$AllowCommit,
    [switch]$SkipTargetBackup,
    [switch]$CheckSetup
)
# TEST target preparation: explicit COMMIT, backed by a successful matching DRYRUN.
# It verifies the backup, truncates CLEAR tables inside a transaction,
# checks preserved data, commits, then fingerprints the committed result.
# It never connects to DataGaoStore and never deletes physical image files.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSCommandPath
. (Join-Path $root 'Reset-Truncate-Plan.ps1')
. (Join-Path $root 'Reset-Hash.ps1')
if ([string]::IsNullOrWhiteSpace($PreviewDirectory)) { throw 'Specify the successful reset preview directory.' }
$preview = Get-Content -LiteralPath (Join-Path $PreviewDirectory 'manifest.json') -Raw | ConvertFrom-Json
[object[]]$previewTables = Get-Content -LiteralPath (Join-Path $PreviewDirectory 'tables.json') -Raw | ConvertFrom-Json
[object[]]$previewFks = Get-Content -LiteralPath (Join-Path $PreviewDirectory 'foreign-keys.json') -Raw | ConvertFrom-Json
[object[]]$previewSchema = Get-Content -LiteralPath (Join-Path $PreviewDirectory 'schema.json') -Raw | ConvertFrom-Json
[object[]]$previewEnvironment = Get-Content -LiteralPath (Join-Path $PreviewDirectory 'environment.json') -Raw | ConvertFrom-Json
$planPath = Join-Path $root 'TABLE-PLAN.json'
$planHash = (Get-FileHash -LiteralPath $planPath -Algorithm SHA256).Hash
if ($preview.Status -ne 'PREVIEW_READY_FOR_REVIEW' -or $preview.TargetDatabase -ne $TargetDatabase -or $preview.PlanSha256 -ne $planHash) {
    throw 'Preview is not successful or the target/table plan has changed.'
}
$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
$actions = @{}
foreach ($entry in $plan.Tables) { $actions[[string]$entry.Table] = [string]$entry.Action }
$clearTables=@($previewTables | Where-Object Action -eq 'CLEAR' | Sort-Object Table | ForEach-Object Table)
if($clearTables.Count -eq 0){throw 'No CLEAR tables in the reviewed plan.'}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root ('evidence/reset-lowlog-commit-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
Initialize-GaoResetHasher
if(-not $CheckSetup -and -not $AllowCommit){throw 'Actual TEST cleanup requires -AllowCommit. Use -CheckSetup for offline validation.'}
$approval=Get-Content -LiteralPath (Join-Path $SuccessfulDryRunDirectory 'manifest.json') -Raw | ConvertFrom-Json
$allowImportedTestReset=($preview.PSObject.Properties.Name -contains 'AllowImportedTestReset' -and [bool]$preview.AllowImportedTestReset)
if ($approval.ExpectedServer -ne $ExpectedServer -or [bool]$approval.AllowImportedTestReset -ne $allowImportedTestReset) { throw 'Expected server or reviewed TEST reset scope differs from DRYRUN.' }
$dryRunPath=Join-Path $root '00-DryRun-Reset-LowLog.ps1'
$approvedWaiver=($approval.PSObject.Properties.Name -contains 'TargetBackupWaivedByUser' -and [bool]$approval.TargetBackupWaivedByUser)
if($approvedWaiver -ne [bool]$SkipTargetBackup){throw 'Target backup policy differs from the reviewed DRYRUN.'}
$expectedBackupStatus=if($SkipTargetBackup){'WAIVED_BY_USER'}else{'PASS'}
if($approval.Status -ne 'DRYRUN_PASS_ROLLED_BACK' -or $approval.Mode -ne 'DRYRUN_ALWAYS_ROLLBACK' -or
   $approval.Strategy -ne 'TRUNCATE_WITH_TRANSACTIONAL_FK_RESTORE' -or
   $approval.Server -ne $Server -or $approval.TargetDatabase -ne $TargetDatabase -or
   $approval.AllTargetRowsRestored -ne 'PASS' -or $approval.IdentityStateRestored -ne 'PASS' -or
   $approval.ForeignKeyDefinitionsRestored -ne 'PASS' -or $approval.EmptyAndPreservedChecks -ne 'PASS' -or
   $approval.BackupVerifyOnly -ne $expectedBackupStatus -or -not $approval.RollbackCompleted){throw 'A successful matching low-log DRYRUN is required.'}
if($approval.ScriptSha256 -ne (Get-FileHash -LiteralPath $dryRunPath -Algorithm SHA256).Hash -or
   $approval.PlannerSha256 -ne (Get-FileHash -LiteralPath (Join-Path $root 'Reset-Truncate-Plan.ps1') -Algorithm SHA256).Hash -or
   $approval.HasherSha256 -ne (Get-FileHash -LiteralPath (Join-Path $root 'Reset-Hash.ps1') -Algorithm SHA256).Hash -or
   $approval.PlanSha256 -ne $planHash -or
   $approval.PreviewManifestSha256 -ne (Get-FileHash -LiteralPath (Join-Path $PreviewDirectory 'manifest.json') -Algorithm SHA256).Hash){
    throw 'Reviewed DRYRUN source/plan/preview changed. Run and review DRYRUN again.'
}
$approvedBefore=Get-Content -LiteralPath (Join-Path $SuccessfulDryRunDirectory 'before-hashes.json') -Raw | ConvertFrom-Json
$approvedAfterRollback=Get-Content -LiteralPath (Join-Path $SuccessfulDryRunDirectory 'after-rollback-hashes.json') -Raw | ConvertFrom-Json
$expectedBefore=@{}
foreach($property in $approvedBefore.PSObject.Properties){
    if($property.Name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or $property.Value -notmatch '^\d+:[0-9A-F]{64}$'){throw 'Invalid DRYRUN baseline.'}
    if($approvedAfterRollback.PSObject.Properties[$property.Name].Value -cne $property.Value){throw 'DRYRUN before/after row hashes do not match.'}
    $expectedBefore[$property.Name]=[string]$property.Value
}
if($expectedBefore.Count -ne $previewTables.Count -or @($approvedAfterRollback.PSObject.Properties).Count -ne $expectedBefore.Count){throw 'Incomplete baseline table hashes.'}
[object[]]$approvedFks=Get-Content -LiteralPath (Join-Path $SuccessfulDryRunDirectory 'foreign-keys-before.json') -Raw | ConvertFrom-Json
[object[]]$approvedIdentities=Get-Content -LiteralPath (Join-Path $SuccessfulDryRunDirectory 'identities-before.json') -Raw | ConvertFrom-Json
if([string]::IsNullOrWhiteSpace($BackupFile)){$BackupFile=[string]$approval.BackupFile}
if ($CheckSetup) {
    Write-Output 'SETUP_CHECK_PASS (C# helper compiled; no SQL connection, backup access, or report directory created)'
    Write-Output "PowerShell: $($PSVersionTable.PSVersion)"
    Write-Output "Successful DRYRUN validated; $($expectedBefore.Count) baseline tables; $($clearTables.Count) truncate candidates."
    Write-Output "Reports: $OutputDirectory"
    return
}
if (-not $SkipTargetBackup -and ([string]::IsNullOrWhiteSpace($BackupFile) -or $BackupFile -notmatch '^[A-Za-z]:\\.+\.bak$')) {
    throw 'Specify the full server-local .bak filename, not just its directory.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Output directory already exists. Use a new directory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$cs = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']=$Server; $cs['Initial Catalog']=$TargetDatabase
$cs['Integrated Security']=$true; $cs['Encrypt']=$true; $cs['TrustServerCertificate']=$true
$cs['Application Name']='GaoStoreResetLowLogCommit'; $cs['Connect Timeout']=15
$connection = [System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
$transaction = $null
$commitAttempted=$false
$commitConfirmed=$false
$manifest = [ordered]@{
    Mode='COMMIT_TEST_RESET'; Server=$Server; TargetDatabase=$TargetDatabase
    ExpectedServer=$ExpectedServer; AllowImportedTestReset=$allowImportedTestReset
    Strategy='TRUNCATE_WITH_TRANSACTIONAL_FK_RESTORE'
    StartedAtUtc=[datetime]::UtcNow.ToString('o'); Status='STARTED'
    PlanSha256=$planHash
    ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    PlannerSha256=(Get-FileHash -LiteralPath (Join-Path $root 'Reset-Truncate-Plan.ps1') -Algorithm SHA256).Hash
    HasherSha256=(Get-FileHash -LiteralPath (Join-Path $root 'Reset-Hash.ps1') -Algorithm SHA256).Hash
    PreviewManifestSha256=(Get-FileHash -LiteralPath (Join-Path $PreviewDirectory 'manifest.json') -Algorithm SHA256).Hash
    BackupFile=$BackupFile; PhysicalFilesDeleted=$false; SourceDatabaseAccessed=$false
    TargetBackupWaivedByUser=[bool]$SkipTargetBackup
    ApprovedDryRunScriptSha256=$approval.ScriptSha256
    ApprovedDryRunManifestSha256=(Get-FileHash -LiteralPath (Join-Path $SuccessfulDryRunDirectory 'manifest.json') -Algorithm SHA256).Hash
    ApprovedBaselineSha256=(Get-FileHash -LiteralPath (Join-Path $SuccessfulDryRunDirectory 'before-hashes.json') -Algorithm SHA256).Hash
}
function Command([string]$sql) {
    $cmd=$connection.CreateCommand(); $cmd.Transaction=$transaction
    $cmd.CommandText=$sql; $cmd.CommandTimeout=3600
    return $cmd
}
function Execute([string]$sql) {
    $cmd=Command $sql
    try { [void]$cmd.ExecuteNonQuery() } finally { $cmd.Dispose() }
}
function Read-Rows([string]$sql, [hashtable]$parameters=@{}) {
    $cmd=Command $sql; $reader=$null
    foreach ($key in $parameters.Keys) { [void]$cmd.Parameters.AddWithValue($key,$parameters[$key]) }
    try {
        $reader=$cmd.ExecuteReader()
        do {
            while ($reader.Read()) {
                $row=[ordered]@{}
                for ($i=0;$i -lt $reader.FieldCount;$i++) {
                    $row[$reader.GetName($i)]=if($reader.IsDBNull($i)){$null}else{$reader.GetValue($i)}
                }
                [pscustomobject]$row
            }
        } while ($reader.NextResult())
    } finally { if($null -ne $reader){$reader.Dispose()}; $cmd.Dispose() }
}
function Scalar([string]$sql) {
    $cmd=Command $sql
    try { return $cmd.ExecuteScalar() } finally { $cmd.Dispose() }
}
function Save-Report([string]$name, [object]$value) {
    ConvertTo-Json -InputObject $value -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory ($name+'.json')) -Encoding UTF8
}
function Identifier([string]$name) { return '['+$name.Replace(']',']]')+']' }
function Canonical-Fks([object[]]$rows) {
    return (($rows | ForEach-Object {
        @($_.ConstraintName,$_.ChildSchema,$_.ChildTable,$_.ChildColumn,$_.ParentSchema,$_.ParentTable,$_.ParentColumn,
          $_.ChildNullable,$_.ColumnOrdinal,$_.IsDisabled,$_.IsNotTrusted,$_.DeleteAction) -join '|'
    } | Sort-Object) -join "`n")
}
function Hash-Table([string]$table, [string]$where='') {
    $quoted=Identifier $table
    return [GaoResetHasher]::Read($connection,$transaction,"SELECT HASHBYTES('SHA2_256',(SELECT t.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)) h FROM dbo.$quoted t $where ORDER BY h;")
}
function Read-FullForeignKeys {
    Read-Rows @'
SELECT fk.name AS ConstraintName,cs.name AS ChildSchema,ct.name AS ChildTable,cc.name AS ChildColumn,
       ps.name AS ParentSchema,pt.name AS ParentTable,pc.name AS ParentColumn,cc.is_nullable AS ChildNullable,
       fkc.constraint_column_id AS ColumnOrdinal,fk.is_disabled AS IsDisabled,fk.is_not_trusted AS IsNotTrusted,
       fk.delete_referential_action_desc AS DeleteAction,fk.update_referential_action_desc AS UpdateAction,
       fk.is_not_for_replication AS NotForReplication,fk.is_system_named AS IsSystemNamed,
       fk.key_index_id AS ReferencedIndexId
FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.tables ct ON ct.object_id=fk.parent_object_id JOIN sys.schemas cs ON cs.schema_id=ct.schema_id
JOIN sys.columns cc ON cc.object_id=ct.object_id AND cc.column_id=fkc.parent_column_id
JOIN sys.tables pt ON pt.object_id=fk.referenced_object_id JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
JOIN sys.columns pc ON pc.object_id=pt.object_id AND pc.column_id=fkc.referenced_column_id
ORDER BY cs.name,fk.name,fkc.constraint_column_id;
'@
}
function Read-Identities {
    Read-Rows @'
SELECT s.name AS SchemaName,t.name AS TableName,c.name AS ColumnName,
       CONVERT(nvarchar(100),c.seed_value) AS SeedValue,
       CONVERT(nvarchar(100),c.increment_value) AS IncrementValue,
       CONVERT(nvarchar(100),c.last_value) AS LastValue,c.is_not_for_replication AS NotForReplication
FROM sys.identity_columns c JOIN sys.tables t ON t.object_id=c.object_id
JOIN sys.schemas s ON s.schema_id=t.schema_id ORDER BY s.name,t.name,c.name;
'@
}
function Canonical-FullFks([object[]]$rows){
    return ConvertTo-Json -InputObject @($rows | Sort-Object ChildSchema,ConstraintName,ColumnOrdinal) -Depth 8 -Compress
}
function Record-LogUsage([string]$stage){
    $usage=@(Read-Rows 'SELECT total_log_size_in_bytes AS TotalBytes,used_log_space_in_bytes AS UsedBytes FROM sys.dm_db_log_space_usage;')
    Save-Report ('log-'+$stage) $usage
    Write-Output ("Log {0}: {1:N1} MB used" -f $stage,($usage[0].UsedBytes/1MB))
}
try {
    $connection.Open()
    Execute ([IO.File]::ReadAllText((Join-Path $root 'Target-Contract.sql')))
    $actualServer=[string](Scalar 'SELECT CONVERT(nvarchar(128),SERVERPROPERTY(''ServerName''));')
    if ($actualServer -ne $previewEnvironment[0].ServerName -or $preview.Server -ne $Server -or ($ExpectedServer -and $actualServer -ne $ExpectedServer)) {
        throw 'SQL Server does not match the reviewed preview/expected instance.'
    }
    # Reuse already allocated log. Do not allocate 64 GiB or modify growth settings.
    $availableLog=[long](Scalar 'SELECT total_log_size_in_bytes-used_log_space_in_bytes FROM sys.dm_db_log_space_usage;')
    if ($availableLog -lt 4GB) { throw 'Less than 4 GiB of existing log is free; inspect activity before starting.' }
    $volumeFree=[long](Scalar "SELECT MIN(v.available_bytes) FROM sys.database_files f CROSS APPLY sys.dm_os_volume_stats(DB_ID(),f.file_id) v;")
    if($volumeFree -lt 8GB){throw 'A database volume has less than 8 GiB headroom. Inspect disk space before starting.'}
    $manifest['LogCapacityPreflight']='PASS'
    Record-LogUsage 'before'
    if($SkipTargetBackup){
        $manifest['BackupVerifyOnly']='WAIVED_BY_USER'
        Write-Output 'TARGET_BACKUP_WAIVED_BY_USER. No target backup was verified; preserved-data and FK checks remain required.'
    }else{
    Write-Output 'Checking backup header and VERIFYONLY; no deletions have started.'
    $headers=@(Read-Rows 'RESTORE HEADERONLY FROM DISK=@p WITH FILE=1;' @{'@p'=$BackupFile})
    $header=@($headers | Where-Object Position -eq 1)
    if ($header.Count -ne 1 -or $headers.Count -ne 1) { throw 'Use a fresh backup file containing exactly one backup set.' }
    $h=$header[0]
    if([string]$h.BackupSetGUID -ne [string]$approval.BackupSetGuid){throw 'Backup set differs from the verified DRYRUN backup.'}
    if ($h.DatabaseName -ne $TargetDatabase -or $h.ServerName -ne $actualServer -or $h.BackupType -ne 1 -or
        -not $h.HasBackupChecksums -or $h.IsDamaged -or -not $h.IsCopyOnly) {
        throw 'Backup must be an undamaged COPY_ONLY full backup with checksums of this TEST database/server.'
    }
    # Compare local SQL server timestamps on the same inspected instance.
    $serverOffsetMinutes=[int](Scalar 'SELECT DATEPART(TZOFFSET,SYSDATETIMEOFFSET());')
    $previewStartedUtc=[datetimeoffset]::Parse([string]$preview.StartedAtUtc).UtcDateTime
    $backupStartedUtc=([datetime]$h.BackupStartDate).AddMinutes(-$serverOffsetMinutes)
    if ($backupStartedUtc -lt $previewStartedUtc) { throw 'Create a fresh backup after the reviewed PREVIEW.' }
    $cmd=Command 'RESTORE VERIFYONLY FROM DISK=@p WITH FILE=1,CHECKSUM;'
    [void]$cmd.Parameters.AddWithValue('@p',$BackupFile)
    try { [void]$cmd.ExecuteNonQuery() } finally { $cmd.Dispose() }
    $manifest['BackupVerifyOnly']='PASS'
    $manifest['BackupFinishDate']=([datetime]$h.BackupFinishDate).ToString('o')
    $manifest['BackupSetGuid']=[string]$h.BackupSetGUID
    }
    Write-Output 'Starting transaction; stop GaoApp and keep it stopped until all checks finish.'
    $transaction=$connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    Execute @'
SET XACT_ABORT ON; SET LOCK_TIMEOUT 15000;
DECLARE @r int;
EXEC @r=sys.sp_getapplock @Resource=N'GSTORE-INITIAL-IMPORT-V2',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
IF @r<0 THROW 55220,'Another migration is running.',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE is_ms_shipped=0 AND is_disabled=0)
    THROW 55222,'Enabled trigger requires review.',1;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1)
    THROW 55223,'Check constraint is disabled or not trusted.',1;
IF EXISTS(SELECT 1 FROM sys.server_triggers WHERE is_disabled=0)
    THROW 55224,'Server DDL trigger requires review before transactional FK changes.',1;
'@
    if (-not $allowImportedTestReset) {
        Execute "IF OBJECT_ID(N'dbo.GaoStoreMigrationRunsV2',N'U') IS NOT NULL EXEC(N'IF EXISTS(SELECT 1 FROM dbo.GaoStoreMigrationRunsV2) THROW 55221,''Migration receipts exist. Reset is blocked.'',1;');
IF OBJECT_ID(N'dbo.GaoStoreProductImageRunsV1',N'U') IS NOT NULL EXEC(N'IF EXISTS(SELECT 1 FROM dbo.GaoStoreProductImageRunsV1) THROW 55221,''Image receipts exist. Reset is blocked.'',1;');"
    }
    $tables=@(Read-Rows 'SELECT s.name AS [Schema],t.name AS [Table],t.temporal_type,t.is_memory_optimized,t.is_tracked_by_cdc,t.is_published,t.is_merge_published,t.is_schema_published,t.is_node,t.is_edge,t.ledger_type FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name;')
    $currentNames=($tables | Sort-Object Schema,Table | ForEach-Object { $_.Schema+'.'+$_.Table }) -join '|'
    $previewNames=($previewTables | Sort-Object Schema,Table | ForEach-Object { $_.Schema+'.'+$_.Table }) -join '|'
    if ($currentNames -cne $previewNames) { throw 'Table inventory has changed since PREVIEW.' }
    foreach ($table in $tables) {
        if ($table.Schema -ne 'dbo' -or $table.temporal_type -ne 0 -or $table.is_memory_optimized -or $table.is_tracked_by_cdc -or
            $table.is_published -or $table.is_merge_published -or $table.is_schema_published -or $table.is_node -or $table.is_edge -or $table.ledger_type -ne 0) { throw 'Unsupported table requires review.' }
        if (-not $actions.ContainsKey($table.Table) -and $table.Table -ne 'GaoStoreMigrationRunsV2') { throw 'Unclassified table.' }
        $q=Identifier $table.Table
        Execute "DECLARE @n bigint; SELECT @n=COUNT_BIG(*) FROM dbo.$q WITH(TABLOCKX,HOLDLOCK);"
    }
    $schema=@(Read-Rows 'SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC;')
    if (($schema | ConvertTo-Json -Compress) -cne ($previewSchema | ConvertTo-Json -Compress)) { throw 'Schema history changed after PREVIEW.' }
    if ([long](Scalar 'SELECT COUNT_BIG(*) FROM dbo.Stores;') -ne 1 -or [long](Scalar 'SELECT COUNT_BIG(*) FROM dbo.Stores WHERE Id=1 AND IsDeleted=0;') -ne 1) { throw 'Expected a single active StoreId=1.' }
    if ([long](Scalar 'SELECT COUNT_BIG(*) FROM dbo.LegalEntities WHERE Id=1 AND StoreId=1;') -ne 1 -or
        [long](Scalar 'SELECT COUNT_BIG(*) FROM dbo.Warehouses WHERE Id=1 AND StoreId=1;') -ne 1) { throw 'Required LegalEntity/Warehouse ID 1 is missing.' }
    $fks=@(Read-FullForeignKeys)
    if((Canonical-FullFks $fks) -cne (Canonical-FullFks $approvedFks)){throw 'Full FK metadata differs from the successful DRYRUN.'}
    if ((Canonical-Fks $fks) -cne (Canonical-Fks $previewFks)) { throw 'Foreign keys changed since PREVIEW.' }
    foreach ($fk in $fks) {
        if ($fk.IsDisabled -or $fk.IsNotTrusted) { throw 'Foreign key must be enabled and trusted.' }
        if ($actions[$fk.ChildTable] -eq 'KEEP' -and $actions[$fk.ParentTable] -in @('CLEAR','SELECTIVE')) { throw 'Retained data references a reset table.' }
    }
    $indexedViewTables=@(Read-Rows 'SELECT DISTINCT t.name AS TableName FROM sys.sql_expression_dependencies d JOIN sys.views v ON v.object_id=d.referencing_id JOIN sys.indexes i ON i.object_id=v.object_id AND i.index_id>0 JOIN sys.tables t ON t.object_id=d.referenced_id;')
    if(@($indexedViewTables | Where-Object { $actions[$_.TableName] -eq 'CLEAR' }).Count){throw 'A CLEAR table participates in an indexed view; do not truncate.'}
    $fkPlan=@(Get-ResetForeignKeyPlan -Tables $previewTables -ForeignKeys $fks)
    $specialFkMetadata=@(Read-Rows 'SELECT fk.name AS ConstraintName FROM sys.foreign_keys fk WHERE EXISTS(SELECT 1 FROM sys.extended_properties p WHERE p.class=1 AND p.major_id=fk.object_id) OR EXISTS(SELECT 1 FROM sys.database_permissions p WHERE p.class=1 AND p.major_id=fk.object_id);')
    if(@($specialFkMetadata | Where-Object { $_.ConstraintName -in @($fkPlan | ForEach-Object Name) }).Count){throw 'FK has additional metadata/permissions requiring review.'}
    Save-Report 'foreign-keys-before' $fks
    Save-Report 'foreign-key-plan' $fkPlan
    $identitiesBefore=@(Read-Identities)
    if((ConvertTo-Json -InputObject $identitiesBefore -Compress) -cne (ConvertTo-Json -InputObject $approvedIdentities -Compress)){throw 'Identity state changed since DRYRUN.'}
    Save-Report 'identities-before' $identitiesBefore
    Write-Output 'Fingerprinting every target table before the rehearsal. Large tables can take several minutes.'
    $before=[ordered]@{}
    foreach ($table in $tables) {
        Write-Output "Before: $($table.Table)"
        $before[$table.Table]=Hash-Table $table.Table
    }
    Save-Report 'before-hashes' $before
    foreach($table in $tables){
        if(-not $expectedBefore.ContainsKey($table.Table) -or $before[$table.Table] -cne $expectedBefore[$table.Table]){
            throw "Data changed since the successful DRYRUN: $($table.Table). No cleanup was started."
        }
    }
    $manifest['MatchesApprovedDryRun']='PASS'
    # Select only product-linked metadata. Keep every unlinked asset, uncertain path,
    # or asset whose filename/path appears in ANY text column of a retained table.
    Execute @'
SELECT m.Id,m.StoragePath,
       RIGHT(REPLACE(m.StoragePath,N'\',N'/'),CHARINDEX(N'/',REVERSE(REPLACE(m.StoragePath,N'\',N'/'))+N'/')-1) AS FileName
INTO #ResetMediaCandidates
FROM dbo.MediaAssets m WHERE EXISTS(SELECT 1 FROM dbo.ProductImages pi WHERE pi.MediaAssetId=m.Id);
DELETE FROM #ResetMediaCandidates WHERE StoragePath IS NULL OR LEN(FileName)<4;
CREATE UNIQUE CLUSTERED INDEX IX_ResetMediaCandidate ON #ResetMediaCandidates(Id);
'@
    $textColumns=@(Read-Rows @'
SELECT t.name AS TableName,c.name AS ColumnName FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
WHERE s.name=N'dbo' AND c.system_type_id IN (35,99,167,175,231,239);
'@)
    foreach ($col in $textColumns) {
        if ($actions[$col.TableName] -ne 'KEEP') { continue }
        $qt=Identifier $col.TableName; $qc=Identifier $col.ColumnName
        # Filename matching also catches slash-escaped JSON. Overmatches preserve assets.
        Execute "DELETE m FROM #ResetMediaCandidates m WHERE EXISTS(SELECT 1 FROM dbo.$qt k WHERE CHARINDEX(m.FileName,CONVERT(nvarchar(max),k.$qc))>0 OR CHARINDEX(m.StoragePath,CONVERT(nvarchar(max),k.$qc))>0);"
    }
    # Refuse unknown relational consumers of media; do not infer how to cascade.
    if (@($fks | Where-Object { $_.ParentTable -eq 'MediaAssets' -and $_.ChildTable -ne 'ProductImages' }).Count) { throw 'Unreviewed MediaAssets reference.' }
    $mediaCount=[long](Scalar 'SELECT COUNT_BIG(*) FROM #ResetMediaCandidates;')
    $mediaExpected=Hash-Table 'MediaAssets' 'WHERE NOT EXISTS(SELECT 1 FROM #ResetMediaCandidates c WHERE c.Id=t.Id)'
    $manifest['MediaMetadataSelected']=$mediaCount
    if($mediaCount -ne [long]$approval.MediaMetadataSelected){throw 'Selected media count changed since the approved DRYRUN.'}
    Write-Output "Media metadata selected: $mediaCount; physical files are not touched."
    # Only FKs between CLEAR tables are removed; preserved table FKs are untouched.
    # All DDL is in the same transaction as TRUNCATE; failures roll back before commit.
    foreach($fk in $fkPlan){Execute $fk.DropSql}
    $deleted=[ordered]@{}
    foreach ($name in $clearTables) {
        Write-Output "COMMIT preparation truncate: $name"
        $qt=Identifier $name
        if([long](Scalar 'SELECT total_log_size_in_bytes-used_log_space_in_bytes FROM sys.dm_db_log_space_usage;') -lt 2GB){throw 'Log reserve low; stop and roll back.'}
        $deleted[$name]=[long](($before[$name] -split ':')[0])
        Execute "TRUNCATE TABLE dbo.$qt;"
        Save-Report 'deleted-row-counts' $deleted
    }
    Execute 'DELETE m FROM dbo.MediaAssets m JOIN #ResetMediaCandidates c ON c.Id=m.Id;'
    foreach($fk in $fkPlan){Execute $fk.CreateSql}
    $fksRecreated=@(Read-FullForeignKeys)
    if((Canonical-FullFks $fksRecreated) -cne (Canonical-FullFks $fks)){throw 'Foreign key definitions differ after recreation.'}
    Save-Report 'foreign-keys-recreated' $fksRecreated
    Record-LogUsage 'after-truncate-and-fk-restore'
    Save-Report 'deleted-row-counts' $deleted
    foreach ($name in $clearTables) {
        $qt=Identifier $name
        if ([long](Scalar "SELECT COUNT_BIG(*) FROM dbo.$qt;") -ne 0) { throw "CLEAR table is not empty: $name" }
    }
    foreach ($table in $tables) {
        $name=$table.Table
        if ($actions[$name] -eq 'KEEP') {
            if ((Hash-Table $name) -cne $before[$name]) { throw "Preserved data changed: $name" }
        }
    }
    if ((Hash-Table 'MediaAssets') -cne $mediaExpected) { throw 'Media preservation check failed.' }
    $constraintErrors=@(Read-Rows 'DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS, ALL_ERRORMSGS;')
    if ($constraintErrors.Count) { Save-Report 'constraint-errors' $constraintErrors; throw 'Constraint validation failed.' }
    $manifest['EmptyAndPreservedChecks']='PASS'
    $expectedCommitted=[ordered]@{}
    foreach($table in $tables){
        $name=$table.Table
        $expectedCommitted[$name]=if($name -eq 'MediaAssets'){$mediaExpected}elseif($actions[$name] -eq 'CLEAR'){
            '0:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855'
        }else{$before[$name]}
    }
    $identitiesForCommit=@(Read-Identities)
    Save-Report 'expected-committed-hashes' $expectedCommitted
    Save-Report 'expected-committed-identities' $identitiesForCommit
    $manifest['Status']='READY_TO_COMMIT'
    $manifest['CommitAttempted']=$true
    Save-Report 'manifest' $manifest
    Write-Output 'All checks passed. Committing the TEST reset now.'
    $commitAttempted=$true
    Execute 'COMMIT TRANSACTION;'
    $commitConfirmed=$true
    $manifest['CommitConfirmed']=$true
    $manifest['Status']='COMMITTED_VERIFYING'
    Save-Report 'manifest' $manifest
    $transaction.Dispose(); $transaction=$null
    # Verify the actual committed result; keep GaoApp stopped between transactions.
    $transaction=$connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    foreach ($table in $tables) {
        $qt=Identifier $table.Table
        Execute "DECLARE @n bigint; SELECT @n=COUNT_BIG(*) FROM dbo.$qt WITH(TABLOCKX,HOLDLOCK);"
    }
    $restored=[ordered]@{}
    foreach ($table in $tables) {
        Write-Output "After commit: $($table.Table)"
        $restored[$table.Table]=Hash-Table $table.Table
        if ($restored[$table.Table] -cne $expectedCommitted[$table.Table]) { throw "Post-commit data differs: $($table.Table). Reset is committed; keep GaoApp stopped and review the report." }
    }
    Save-Report 'after-commit-hashes' $restored
    $identitiesAfter=@(Read-Identities)
    Save-Report 'identities-after-commit' $identitiesAfter
    if((ConvertTo-Json -InputObject $identitiesAfter -Compress) -cne (ConvertTo-Json -InputObject $identitiesForCommit -Compress)){throw 'Identity state differs after commit.'}
    $fksAfter=@(Read-FullForeignKeys)
    Save-Report 'foreign-keys-after-commit' $fksAfter
    if((Canonical-FullFks $fksAfter) -cne (Canonical-FullFks $fks)){throw 'FK definitions differ after commit.'}
    Execute 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'
    $transaction.Dispose(); $transaction=$null
    $manifest['AllCommittedRowsVerified']='PASS'
    $manifest['IdentityStateVerified']='PASS'
    $manifest['ForeignKeyDefinitionsVerified']='PASS'
    Record-LogUsage 'after-commit'
    $manifest['Status']='RESET_COMMIT_PASS'
    Write-Output $manifest.Status
} catch {
    $failure=$_
    $manifest['Status']=if($commitConfirmed){'COMMITTED_POSTCHECK_FAILED'}elseif($commitAttempted){'COMMIT_OUTCOME_REQUIRES_REVIEW'}else{'FAILED_BEFORE_COMMIT'}
    $manifest['Error']=$failure.Exception.Message
    if ($null -ne $transaction) {
        try {
            if ($null -ne $transaction.Connection) {
                Execute 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'
                if($commitConfirmed){$manifest['VerificationTransactionReleased']=$true}else{$manifest['RollbackCompleted']=$true}
            }
            else { $manifest['RollbackState']='SQL_TRANSACTION_ALREADY_ENDED_DATA_NOT_YET_VERIFIED' }
        } catch { $manifest['RollbackError']=$_.Exception.Message }
        $transaction.Dispose(); $transaction=$null
    }
    throw $failure
} finally {
    $connection.Dispose()
    $manifest['FinishedAtUtc']=[datetime]::UtcNow.ToString('o')
    Save-Report 'manifest' $manifest
    Write-Output "Reports: $OutputDirectory"
}
