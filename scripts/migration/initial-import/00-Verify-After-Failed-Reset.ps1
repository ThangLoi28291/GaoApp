[CmdletBinding()]
param(
    [string]$Server='.\SQLEXPRESS',
    [ValidateSet('GaoAppDb')][string]$TargetDatabase='GaoAppDb',
    [Parameter(Mandatory=$true)][string]$FailedRunDirectory,
    [string]$OutputDirectory,
    [switch]$CheckSetup
)
# Read-only verification: SELECT queries and shared locks; no data or schema changes.
# Keep GaoApp stopped until this finishes. Baseline files are never overwritten.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path -Parent $PSCommandPath
. (Join-Path $root 'Reset-Hash.ps1')
Initialize-GaoResetHasher
$failure=Get-Content -LiteralPath (Join-Path $FailedRunDirectory 'manifest.json') -Raw | ConvertFrom-Json
$baselinePath=Join-Path $FailedRunDirectory 'before-hashes.json'
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
if($failure.Mode -ne 'DRYRUN_ALWAYS_ROLLBACK' -or $failure.Status -ne 'FAILED' -or
   $failure.TargetDatabase -ne $TargetDatabase -or $failure.Server -ne $Server){
    throw 'The failed run does not match this TEST database/server.'
}
$expected=@{}
foreach($property in $baseline.PSObject.Properties){
    if($property.Name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or $property.Value -notmatch '^\d+:[0-9A-F]{64}$'){
        throw 'Invalid baseline hash entry.'
    }
    $expected[$property.Name]=[string]$property.Value
}
if($expected.Count -eq 0){throw 'No baseline hashes were found.'}
if([string]::IsNullOrWhiteSpace($OutputDirectory)){
    $OutputDirectory=Join-Path $root ('evidence/reset-recovery-verify-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
if($CheckSetup){
    Write-Output "SETUP_CHECK_PASS; PowerShell $($PSVersionTable.PSVersion); C# helper compiled; $($expected.Count) baseline table hashes validated."
    Write-Output 'No SQL connection, backup access, or report directory created.'
    return
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new output directory.'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']=$Server; $cs['Initial Catalog']=$TargetDatabase
$cs['Integrated Security']=$true; $cs['Encrypt']=$true; $cs['TrustServerCertificate']=$true
$cs['Application Name']='GaoStoreResetRecoveryReadOnly'; $cs['Connect Timeout']=15
$connection=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
$transaction=$null
$manifest=[ordered]@{
    Mode='READ_ONLY_POST_FAILURE_VERIFY'; Server=$Server; TargetDatabase=$TargetDatabase
    FailedRunDirectory=[IO.Path]::GetFullPath($FailedRunDirectory)
    BaselineSha256=(Get-FileHash -LiteralPath $baselinePath -Algorithm SHA256).Hash
    ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    HasherSha256=(Get-FileHash -LiteralPath (Join-Path $root 'Reset-Hash.ps1') -Algorithm SHA256).Hash
    StartedAtUtc=[datetime]::UtcNow.ToString('o'); Status='STARTED'; DataOrSchemaWrites=$false
}
function Command([string]$sql){
    $cmd=$connection.CreateCommand(); $cmd.Transaction=$transaction
    $cmd.CommandText=$sql; $cmd.CommandTimeout=3600
    return $cmd
}
function Read-Rows([string]$sql){
    $cmd=Command $sql; $reader=$null
    try{
        $reader=$cmd.ExecuteReader()
        do{
            while($reader.Read()){
                $row=[ordered]@{}
                for($i=0;$i -lt $reader.FieldCount;$i++){
                    $row[$reader.GetName($i)]=if($reader.IsDBNull($i)){$null}else{$reader.GetValue($i)}
                }
                [pscustomobject]$row
            }
        }while($reader.NextResult())
    }finally{if($null -ne $reader){$reader.Dispose()};$cmd.Dispose()}
}
function Execute([string]$sql){
    $cmd=Command $sql
    try{[void]$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}
}
function Save-Report([string]$name,[object]$value){
    ConvertTo-Json -InputObject $value -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory ($name+'.json')) -Encoding UTF8
}
try{
    $connection.Open()
    $environment=@(Read-Rows 'SELECT @@SERVERNAME AS ServerName,DB_NAME() AS DatabaseName,SYSDATETIMEOFFSET() AS InspectedAt;')
    if($environment[0].ServerName -ne $failure.Server -or $environment[0].DatabaseName -ne $TargetDatabase){throw 'Connected to an unexpected server/database.'}
    Save-Report 'environment' $environment
    $drives=@(Read-Rows 'SELECT fixed_drive_path AS Drive, drive_type_desc AS DriveType, CAST(free_space_in_bytes/1073741824.0 AS decimal(18,2)) AS FreeGB FROM sys.dm_os_enumerate_fixed_drives ORDER BY fixed_drive_path;')
    Save-Report 'drives' $drives
    $drives | Format-Table -AutoSize | Out-String | Write-Output
    $log=@(Read-Rows @'
SELECT d.state_desc,d.recovery_model_desc,d.log_reuse_wait_desc,
       CAST(l.total_log_size_in_bytes/1048576.0 AS decimal(18,2)) AS TotalLogMB,
       CAST(l.used_log_space_in_bytes/1048576.0 AS decimal(18,2)) AS UsedLogMB
FROM sys.databases d CROSS JOIN sys.dm_db_log_space_usage l WHERE d.database_id=DB_ID();
'@)
    Save-Report 'log-state' $log
    # SQL's read-only error-log procedure can explain why auto-growth failed.
    # Failure to read diagnostic logs does not prevent the data comparison.
    try{
        $windowStart=[datetimeoffset]::Parse([string]$failure.StartedAtUtc).LocalDateTime.AddMinutes(-5)
        $windowEnd=[datetimeoffset]::Parse([string]$failure.FinishedAtUtc).LocalDateTime.AddMinutes(10)
        $errorLog=@(Read-Rows "EXEC master.sys.sp_readerrorlog 0,1,N'GaoAppDb';" | Where-Object {
            [datetime]$_.LogDate -ge $windowStart -and [datetime]$_.LogDate -le $windowEnd
        })
        Save-Report 'sql-error-log-at-failure' $errorLog
    }catch{
        $manifest['ErrorLogReadWarning']=$_.Exception.Message
        Write-Output 'SQL error log unavailable; continuing with the data comparison.'
    }
    $active=@(Read-Rows @'
SELECT r.session_id,r.command,r.status,r.percent_complete,s.program_name
FROM sys.dm_exec_requests r LEFT JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id
WHERE r.session_id<>@@SPID AND
 (s.program_name=N'GaoStoreResetDryRunAlwaysRollback' OR (r.database_id=DB_ID() AND r.command LIKE N'%ROLLBACK%'));
'@)
    if($active.Count){Save-Report 'active-reset-or-rollback' $active;throw 'Reset/rollback is still active. Wait before verifying.'}
    Execute 'SET LOCK_TIMEOUT 15000;'
    $transaction=$connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    $tables=@(Read-Rows 'SELECT s.name AS SchemaName,t.name AS TableName FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name;')
    if($tables.Count -ne $expected.Count){throw 'Table inventory differs from the baseline; no data was changed by this verifier.'}
    foreach($table in $tables){
        if($table.SchemaName -ne 'dbo' -or -not $expected.ContainsKey($table.TableName)){throw 'Table inventory differs from the baseline.'}
        # Shared table locks prevent writes while all baseline rows are compared.
        # Only names validated against the baseline above are interpolated.
        Execute "DECLARE @n bigint; SELECT @n=COUNT_BIG(*) FROM dbo.[$($table.TableName)] WITH(TABLOCK,HOLDLOCK);"
    }
    $actual=[ordered]@{}; $differences=[Collections.Generic.List[object]]::new()
    $index=0
    foreach($table in $tables){
        $index++; $name=$table.TableName
        Write-Output "Verify $index/$($tables.Count): $name"
        $query="SELECT HASHBYTES('SHA2_256',(SELECT t.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)) h FROM dbo.[$name] t ORDER BY h;"
        $hash=[GaoResetHasher]::Read($connection,$transaction,$query)
        $actual[$name]=$hash
        if($hash -cne $expected[$name]){
            $differences.Add([pscustomobject]@{Table=$name;Before=$expected[$name];Current=$hash})
        }
    }
    Save-Report 'current-hashes' $actual
    Save-Report 'differences' @($differences.ToArray())
    Execute 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'
    $transaction.Dispose();$transaction=$null
    $manifest['TablesCompared']=$tables.Count
    $manifest['DifferenceCount']=$differences.Count
    $manifest['Status']=if($differences.Count){'DATA_DIFF_REQUIRES_REVIEW'}else{'ALL_TABLES_MATCH_BEFORE_FAILED_DRYRUN'}
    Write-Output $manifest.Status
    if($differences.Count){$differences | Select-Object Table | Format-Table | Out-String | Write-Output}
}catch{
    $caught=$_; $manifest['Status']='FAILED'; $manifest['Error']=$caught.Exception.Message
    throw $caught
}finally{
    if($null -ne $transaction){
        try{if($null -ne $transaction.Connection){Execute 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'}}
        catch{$manifest['ReadTransactionReleaseError']=$_.Exception.Message}
        $transaction.Dispose()
    }
    $connection.Dispose()
    $manifest['FinishedAtUtc']=[datetime]::UtcNow.ToString('o')
    Save-Report 'manifest' $manifest
    Write-Output "Reports: $OutputDirectory"
}
