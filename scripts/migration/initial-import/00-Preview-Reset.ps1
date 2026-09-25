[CmdletBinding()]
param(
    [string]$Server = '.\SQLEXPRESS',
    [ValidateSet('GaoAppDb')][string]$TargetDatabase = 'GaoAppDb',
    [string]$OutputDirectory,
    [switch]$AllowImportedTestReset,
    [switch]$CheckSetup
)
# Operator-only PREVIEW: SELECT queries only. No reset/import/schema/backup commands.
# Reports are written locally. This does not prove that a backup can be restored.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptDirectory = Split-Path -Parent $PSCommandPath
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $scriptDirectory ('evidence/reset-preview-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$planPath = Join-Path $scriptDirectory 'TABLE-PLAN.json'
$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
$actions = @{}
foreach ($entry in $plan.Tables) {
    if ($actions.ContainsKey($entry.Table)) { throw "Duplicate table in plan: $($entry.Table)" }
    if ($entry.Table -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw 'Invalid table name in plan.' }
    if ($entry.Action -notin @('KEEP','CLEAR','SELECTIVE')) { throw 'Unknown plan action.' }
    $actions[$entry.Table] = [string]$entry.Action
}
if ($CheckSetup) {
    Write-Output 'SETUP_CHECK_PASS (no SQL connection; no report directory created)'
    Write-Output "PowerShell: $($PSVersionTable.PSVersion)"
    Write-Output "Plan tables: $($actions.Count)"
    Write-Output "Reports: $OutputDirectory"
    return
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory; existing evidence is not overwritten.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$builder['Data Source'] = $Server
$builder['Initial Catalog'] = $TargetDatabase
$builder['Integrated Security'] = $true
$builder['Encrypt'] = $true
$builder['TrustServerCertificate'] = $true
$builder['Application Name'] = 'GaoStoreResetPreviewReadOnly'
$builder['Connect Timeout'] = 15
$connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
$manifest = [ordered]@{
    Mode = 'PREVIEW_ONLY'; TargetDatabase = $TargetDatabase; Server = $Server
    SourceAssumption = 'DataGaoStore is the fixed rehearsal snapshot selected by the operator.'
    PlanSha256 = (Get-FileHash -LiteralPath $planPath -Algorithm SHA256).Hash
    ScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    StartedAtUtc = [datetime]::UtcNow.ToString('o'); Status = 'STARTED'
    DatabaseWrites = $false
    AllowImportedTestReset = [bool]$AllowImportedTestReset
}
function Read-Rows([string]$sql) {
    $command = $connection.CreateCommand()
    $command.CommandText = $sql
    $command.CommandTimeout = 300
    $reader = $null
    try {
        $reader = $command.ExecuteReader()
        while ($reader.Read()) {
            $row = [ordered]@{}
            for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                $row[$reader.GetName($i)] = if ($reader.IsDBNull($i)) { $null } else { $reader.GetValue($i) }
            }
            [pscustomobject]$row
        }
    } finally {
        if ($null -ne $reader) { $reader.Dispose() }
        $command.Dispose()
    }
}
function Save-Report([string]$name, [object[]]$rows) {
    ConvertTo-Json -InputObject @($rows) -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory ($name + '.json')) -Encoding UTF8
}
try {
    $connection.Open()
    $environment = @(Read-Rows 'SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName, ORIGINAL_LOGIN() AS LoginName, SYSDATETIMEOFFSET() AS InspectedAt;')
    Save-Report 'environment' $environment
    $tables = @(Read-Rows @'
SELECT s.name AS SchemaName, t.name AS TableName, t.temporal_type AS TemporalType,
       t.is_memory_optimized AS MemoryOptimized, t.is_tracked_by_cdc AS TrackedByCdc,
       COALESCE(SUM(p.rows),0) AS ApproximateRows
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
LEFT JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1)
WHERE t.is_ms_shipped=0
GROUP BY s.name,t.name,t.temporal_type,t.is_memory_optimized,t.is_tracked_by_cdc
ORDER BY s.name,t.name;
'@)
    $blockers = [Collections.Generic.List[string]]::new()
    $inventory = @($tables | ForEach-Object {
        $action = 'UNCLASSIFIED'
        if ($_.SchemaName -eq 'dbo' -and $actions.ContainsKey($_.TableName)) { $action = $actions[$_.TableName] }
        elseif ($_.SchemaName -eq 'dbo' -and $_.TableName -eq 'GaoStoreMigrationRunsV2') { $action = 'KEEP_JOURNAL' }
        if ($action -eq 'UNCLASSIFIED') { $blockers.Add("Unclassified table: $($_.SchemaName).$($_.TableName)") }
        if ($action -in @('CLEAR','SELECTIVE') -and ($_.TemporalType -ne 0 -or $_.MemoryOptimized -or $_.TrackedByCdc)) {
            $blockers.Add("Special table requires review: $($_.TableName)")
        }
        [pscustomobject]@{ Schema=$_.SchemaName; Table=$_.TableName; Action=$action; ApproximateRows=$_.ApproximateRows }
    })
    Save-Report 'tables' $inventory
    foreach ($entry in $plan.Tables) {
        if ($entry.Table -in @('LegacyReturnArchives','GaoStoreMigrationRunsV2','GaoStoreProductImageRunsV1')) { continue } # Optional until the corresponding schema/import exists.
        if (@($tables | Where-Object { $_.SchemaName -eq 'dbo' -and $_.TableName -eq $entry.Table }).Count -ne 1) {
            $blockers.Add("Expected table missing: dbo.$($entry.Table)")
        }
    }
    $foreignKeys = @(Read-Rows @'
SELECT fk.name AS ConstraintName, cs.name AS ChildSchema, ct.name AS ChildTable,
       cc.name AS ChildColumn, ps.name AS ParentSchema, pt.name AS ParentTable,
       pc.name AS ParentColumn, cc.is_nullable AS ChildNullable,
       fkc.constraint_column_id AS ColumnOrdinal, fk.is_disabled AS IsDisabled,
       fk.is_not_trusted AS IsNotTrusted, fk.delete_referential_action_desc AS DeleteAction
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.tables ct ON ct.object_id=fk.parent_object_id
JOIN sys.schemas cs ON cs.schema_id=ct.schema_id
JOIN sys.columns cc ON cc.object_id=ct.object_id AND cc.column_id=fkc.parent_column_id
JOIN sys.tables pt ON pt.object_id=fk.referenced_object_id
JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
JOIN sys.columns pc ON pc.object_id=pt.object_id AND pc.column_id=fkc.referenced_column_id
ORDER BY fk.name,fkc.constraint_column_id;
'@)
    Save-Report 'foreign-keys' $foreignKeys
    foreach ($fk in $foreignKeys) {
        if ($actions[$fk.ChildTable] -eq 'KEEP' -and $actions[$fk.ParentTable] -in @('CLEAR','SELECTIVE')) {
            $blockers.Add("Retained table references reset table: $($fk.ConstraintName)")
        }
        if ($fk.IsDisabled -or $fk.IsNotTrusted) { $blockers.Add("Foreign key not enabled/trusted: $($fk.ConstraintName)") }
    }
    $triggers = @(Read-Rows @'
SELECT tr.name AS TriggerName, OBJECT_SCHEMA_NAME(tr.parent_id) AS ParentSchema,
       OBJECT_NAME(tr.parent_id) AS ParentTable, tr.is_disabled AS IsDisabled
FROM sys.triggers tr WHERE tr.is_ms_shipped=0 ORDER BY tr.name;
'@)
    Save-Report 'triggers' $triggers
    foreach ($trigger in $triggers) {
        if (-not $trigger.IsDisabled) { $blockers.Add("Enabled trigger requires review: $($trigger.TriggerName)") }
    }
    if (@($tables | Where-Object { $_.TableName -eq '__EFMigrationsHistory' -and $_.SchemaName -eq 'dbo' }).Count) {
        $schema = @(Read-Rows 'SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC;')
        Save-Report 'schema' $schema
        if ($schema.Count -eq 0 -or $schema[0].MigrationId -notin @('20260923160000_AddLegacyInvoiceImport','20260923180000_AddLegacyReturnArchive')) {
            $blockers.Add('Unexpected schema version.')
        }
    }
    if (@($tables | Where-Object { $_.TableName -eq 'GaoStoreMigrationRunsV2' -and $_.SchemaName -eq 'dbo' }).Count) {
        $journal = @(Read-Rows 'SELECT PackageId, SourceDatabase, SqlSha256, CommittedAtUtc FROM dbo.GaoStoreMigrationRunsV2 ORDER BY CommittedAtUtc;')
        Save-Report 'journal' $journal
        if ($journal.Count -and -not $AllowImportedTestReset) { $blockers.Add('Migration receipts exist. Only an explicitly reviewed TEST target may use -AllowImportedTestReset for a fresh import.') }
    }
    if (@($tables | Where-Object { $_.TableName -eq 'GaoStoreProductImageRunsV1' -and $_.SchemaName -eq 'dbo' }).Count) {
        $imageJournal = @(Read-Rows 'SELECT StoreId,SourceDatabase,PreviewManifestSha256,CommittedAtUtc FROM dbo.GaoStoreProductImageRunsV1 ORDER BY StoreId;')
        Save-Report 'image-journal' $imageJournal
        if ($imageJournal.Count -and -not $AllowImportedTestReset) { $blockers.Add('Image receipts exist. Fresh TEST reset must explicitly include them.') }
    }
    # Report only counts of media paths referenced from preserved text/configuration.
    # No passwords, API keys, customer data, or configuration values are exported.
    if ($actions['MediaAssets'] -eq 'SELECTIVE' -and @($tables | Where-Object { $_.TableName -eq 'MediaAssets' }).Count) {
        $media = @(Read-Rows @'
SELECT COUNT_BIG(*) AS TotalMedia,
       COALESCE(SUM(CONVERT(bigint,CASE WHEN EXISTS_LINK.Id IS NULL THEN 0 ELSE 1 END)),0) AS LinkedToProductImages
FROM dbo.MediaAssets m
OUTER APPLY (SELECT TOP (1) pi.Id FROM dbo.ProductImages pi WHERE pi.MediaAssetId=m.Id) EXISTS_LINK;
'@)
        Save-Report 'media-summary' $media
        $textColumns = @(Read-Rows @'
SELECT s.name AS SchemaName,t.name AS TableName,c.name AS ColumnName
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id
WHERE c.system_type_id IN (35,99,167,175,231,239) AND s.name=N'dbo';
'@)
        $mediaReferences = @(
            foreach ($col in $textColumns) {
                if ($actions[$col.TableName] -ne 'KEEP') { continue }
                $tableIdentifier = '[' + $col.TableName.Replace(']',']]') + ']'
                $columnIdentifier = '[' + $col.ColumnName.Replace(']',']]') + ']'
                $match = @(Read-Rows "SELECT COUNT_BIG(*) AS MatchedMedia FROM dbo.MediaAssets m WHERE NULLIF(m.StoragePath,N'') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.$tableIdentifier k WHERE CHARINDEX(REPLACE(m.StoragePath,N'\',N'/'), REPLACE(CONVERT(nvarchar(max),k.$columnIdentifier),N'\',N'/'))>0);")
                if ($match[0].MatchedMedia -gt 0) {
                    [pscustomobject]@{Table=$col.TableName; Column=$col.ColumnName; MatchedMedia=$match[0].MatchedMedia}
                }
            }
        )
        Save-Report 'media-retained-text-references' $mediaReferences
    }
    $manifest['Blockers'] = @($blockers.ToArray())
    $manifest['Status'] = if ($blockers.Count) { 'PREVIEW_BLOCKED' } else { 'PREVIEW_READY_FOR_REVIEW' }
    $manifest['Note'] = 'Metadata counts only. Media matching is diagnostic, not authorization to delete. No cleanup has run.'
    $inventory | Group-Object Action | Select-Object Name,Count | Format-Table -AutoSize | Out-String | Write-Output
    Write-Output $manifest.Status
    foreach ($blocker in $blockers) { Write-Output "BLOCKER: $blocker" }
    Write-Output "Reports: $OutputDirectory"
} catch {
    $manifest['Status'] = 'FAILED'
    $manifest['Error'] = $_.Exception.Message
    throw
} finally {
    $connection.Dispose()
    $manifest['FinishedAtUtc'] = [datetime]::UtcNow.ToString('o')
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding UTF8
}
