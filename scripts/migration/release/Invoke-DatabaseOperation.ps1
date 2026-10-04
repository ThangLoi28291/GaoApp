[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet('Inspect','TargetReady','BackupTarget','BackupSource','VerifySourceBackup','VerifyTargetBackup')][string]$Operation,
 [Parameter(Mandatory=$true)][string]$Server,
 [Parameter(Mandatory=$true)][string]$ExpectedServer,
 [string]$SourceDatabase='DataGaoStore',[string]$TargetDatabase='GaoAppDb',
 [string]$BackupDirectory,
 [string]$BackupFile,
 [string]$NotBeforeUtc,
 [Parameter(Mandatory=$true)][string]$OutputDirectory,
 [switch]$CheckSetup
)
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Backup-Progress.ps1')
foreach($name in @($SourceDatabase,$TargetDatabase)){if($name -notmatch '^[A-Za-z0-9_-]{1,128}$'){throw 'Invalid database name.'}}
if($SourceDatabase -eq $TargetDatabase -or $TargetDatabase -in @('master','model','msdb','tempdb')){throw 'Invalid source/target.'}
if($CheckSetup){Initialize-GaoBackupProgress;Write-Output 'DATABASE_OPERATION_SETUP_PASS: progress helper compiled; no SQL connection.';return}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new report directory.'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']=$Server; $cs['Initial Catalog']=$TargetDatabase; $cs['Integrated Security']=$true
$cs['Encrypt']=$true; $cs['TrustServerCertificate']=$true; $cs['Application Name']='GaoAppCutoverPreflight';$cs['Connect Timeout']=15
$connection=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
$manifest=[ordered]@{Operation=$Operation;Server=$Server;ExpectedServer=$ExpectedServer;SourceDatabase=$SourceDatabase;TargetDatabase=$TargetDatabase;StartedAtUtc=[datetime]::UtcNow.ToString('o');Status='STARTED'}
function Cmd([string]$text){$c=$connection.CreateCommand();$c.CommandText=$text;$c.CommandTimeout=3600;return $c}
function Read([string]$text){$c=Cmd $text;$a=[System.Data.SqlClient.SqlDataAdapter]::new($c);$d=[System.Data.DataSet]::new();try{[void]$a.Fill($d);return ,$d}finally{$a.Dispose();$c.Dispose()}}
try{
 $connection.Open()
 $c=Cmd "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'));"
 try{$actual=[string]$c.ExecuteScalar()}finally{$c.Dispose()}
 if($actual -ne $ExpectedServer){throw "Wrong SQL instance: $actual. Expected $ExpectedServer."}
 if($Operation -eq 'Inspect'){
  $data=Read @"
SET NOCOUNT ON;
SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) ServerName,ORIGINAL_LOGIN() LoginName,
 CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) Edition,CONVERT(nvarchar(30),SERVERPROPERTY('ProductVersion')) ProductVersion,SYSDATETIMEOFFSET() InspectedAt;
SELECT d.name,d.state_desc,d.user_access_desc,d.is_read_only,d.recovery_model_desc,f.type_desc,
 CAST(f.size/128.0 AS decimal(18,2)) AllocatedMB,f.physical_name
FROM sys.databases d JOIN sys.master_files f ON f.database_id=d.database_id
WHERE d.name IN(N'$SourceDatabase',N'$TargetDatabase') ORDER BY d.name,f.type;
SELECT fixed_drive_path,CAST(free_space_in_bytes/1073741824.0 AS decimal(18,2)) FreeGB FROM sys.dm_os_enumerate_fixed_drives;
SELECT CAST((total_log_size_in_bytes-used_log_space_in_bytes)/1073741824.0 AS decimal(18,2)) TargetFreeAllocatedLogGB,
 CAST(used_log_space_in_bytes/1048576.0 AS decimal(18,2)) TargetUsedLogMB FROM sys.dm_db_log_space_usage;
SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC;
SELECT s.name SchemaName,t.name TableName,SUM(p.rows) ApproximateRows FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN(0,1) GROUP BY s.name,t.name ORDER BY s.name,t.name;
SELECT (SELECT COUNT_BIG(*) FROM [$SourceDatabase].dbo.ProductDetail) ProductDetails,
 (SELECT COUNT_BIG(*) FROM [$SourceDatabase].dbo.[Order]) Orders,
 (SELECT COUNT_BIG(*) FROM [$SourceDatabase].dbo.InvoiceHead) InvoiceHeads,
 (SELECT COUNT_BIG(*) FROM [$SourceDatabase].dbo.InvoiceDetail) InvoiceDetails,
 (SELECT MAX(CreatedDate) FROM [$SourceDatabase].dbo.Product) LastProductDate,
 (SELECT MAX(IssuedDate) FROM [$SourceDatabase].dbo.InvoiceHead) LastIssuedDate;
"@
  $index=0
  foreach($table in $data.Tables){$index++;$table | Select-Object -Property $table.Columns.ColumnName | Export-Csv -LiteralPath (Join-Path $OutputDirectory ('report-{0:00}.csv' -f $index)) -NoTypeInformation -Encoding UTF8;if($table.Rows.Count -le 8){$table|Format-Table -AutoSize|Out-String -Width 220|Write-Output}else{Write-Output "Report $index : $($table.Rows.Count) rows"}}
  $manifest['Status']='INSPECTION_FINISHED_READ_ONLY'
 }elseif($Operation -eq 'TargetReady'){
  $c=Cmd ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'scripts/migration/initial-import/Target-Contract.sql')))
  try{[void]$c.ExecuteNonQuery()}finally{$c.Dispose()}
  $manifest['Status']='TARGET_CONTRACT_PASS_READ_ONLY'
 }elseif($Operation -in @('BackupSource','BackupTarget')){
  $database=if($Operation -eq 'BackupSource'){$SourceDatabase}else{$TargetDatabase}
  if([string]::IsNullOrWhiteSpace($BackupDirectory)){
   $c=Cmd "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath'));"
   try{$BackupDirectory=[string]$c.ExecuteScalar()}finally{$c.Dispose()}
  }
  if($BackupDirectory -notmatch '^[A-Za-z]:\\' -or $BackupDirectory.Contains("`r") -or $BackupDirectory.Contains("`n")){throw 'Set an existing server-local BackupDirectory. SQL Server must be able to write there.'}
  $file=Join-Path $BackupDirectory ($database+'-before-cutover-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N')+'.bak')
  $manifest['BackupFile']=$file
  Write-Output "Backup starting: $file"
  # Express does not support producing compressed backups. Do not add COMPRESSION.
  $c=Cmd "BACKUP DATABASE [$database] TO DISK=@file WITH COPY_ONLY,CHECKSUM,NOINIT,STATS=5;"
  $c.CommandTimeout=7200
  [void]$c.Parameters.Add('@file',[System.Data.SqlDbType]::NVarChar,4000);$c.Parameters['@file'].Value=$file
  try{Invoke-GaoBackupCommand -Command $c -Stage 'BACKUP'}finally{$c.Dispose()}
  $manifest['Status']='BACKUP_CREATED_UNVERIFIED'
  Write-Output "BackupFile: $file"
 }else{
  $database=if($Operation -eq 'VerifySourceBackup'){$SourceDatabase}else{$TargetDatabase}
  if($BackupFile -notmatch '^[A-Za-z]:\\.+\.bak$'){throw 'Provide an existing server-local full .bak filename.'}
  $manifest['BackupFile']=$BackupFile
  $c=Cmd 'RESTORE HEADERONLY FROM DISK=@file;'
  [void]$c.Parameters.AddWithValue('@file',$BackupFile)
  $headers=[System.Data.DataTable]::new();$adapter=[System.Data.SqlClient.SqlDataAdapter]::new($c)
  try{[void]$adapter.Fill($headers)}finally{$adapter.Dispose();$c.Dispose()}
  if($headers.Rows.Count -ne 1){throw 'Expected exactly one backup set in a uniquely named file; do not guess FILE position.'}
  $h=$headers.Rows[0]
  if($h.DatabaseName -ne $database -or $h.ServerName -ne $ExpectedServer -or $h.BackupType -ne 1 -or
     -not $h.HasBackupChecksums -or $h.IsDamaged -or -not $h.IsCopyOnly -or $h.BackupFinishDate -is [DBNull]){
    throw 'Expected completed COPY_ONLY full backup with checksums from this database/server.'
  }
  if([string]::IsNullOrWhiteSpace($NotBeforeUtc)){throw 'Backup verification requires the current reset preview timestamp.'}
  $c=Cmd 'SELECT DATEPART(TZOFFSET,SYSDATETIMEOFFSET());'
  try{$offset=[TimeSpan]::FromMinutes([int]$c.ExecuteScalar())}finally{$c.Dispose()}
  $start=[DateTimeOffset]::new([datetime]::SpecifyKind([datetime]$h.BackupStartDate,[DateTimeKind]::Unspecified),$offset)
  if($start.UtcDateTime -lt [DateTimeOffset]::Parse($NotBeforeUtc).UtcDateTime){throw 'Backup predates the current reset preview. Create a fresh cutover backup after stopping writers.'}
  $manifest['BackupSetGuid']=[string]$h.BackupSetGUID
  $manifest['BackupStartDate']=([datetime]$h.BackupStartDate).ToString('o')
  $manifest['BackupFinishDate']=([datetime]$h.BackupFinishDate).ToString('o')
  $c=Cmd 'RESTORE VERIFYONLY FROM DISK=@file WITH FILE=1,CHECKSUM,STATS=5;'
  $c.CommandTimeout=7200
  [void]$c.Parameters.AddWithValue('@file',$BackupFile)
  try{Invoke-GaoBackupCommand -Command $c -Stage 'VERIFYONLY'}finally{$c.Dispose()}
  $manifest['Status']='BACKUP_VERIFYONLY_PASS'
 }
 Write-Output $manifest.Status
}catch{$manifest['Status']='FAILED';$manifest['Error']=$_.Exception.Message;throw}
finally{$connection.Dispose();$manifest['FinishedAtUtc']=[datetime]::UtcNow.ToString('o');$manifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding UTF8;Write-Output "Reports: $([IO.Path]::GetFullPath($OutputDirectory))"}
