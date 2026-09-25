[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet('01-products','02-customers','03-sales','03-return-archive','04-inventory')][string]$Package,
 [Parameter(Mandatory=$true)][string]$SourceDatabase,
 [Parameter(Mandatory=$true)][string]$TargetDatabase,
 [string]$Server='.\SQLEXPRESS',
 [ValidateSet('PREVIEW','DRYRUN','COMMIT','VERIFY')][string]$Mode='PREVIEW',
 [switch]$AllowCommit,
 [string]$OutputDirectory,
 [switch]$CheckSetup
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$scriptDirectory=Split-Path -Parent $PSCommandPath
if([string]::IsNullOrWhiteSpace($OutputDirectory)){
 $OutputDirectory=Join-Path $scriptDirectory ('evidence/'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
foreach($dbName in @($SourceDatabase,$TargetDatabase)){
 if($dbName -notmatch '^[\p{L}\p{N}_-]{1,128}$'){throw 'Database names must contain only letters, digits, underscore or hyphen.'}
}
if($SourceDatabase -eq $TargetDatabase){throw 'Source and target must be different databases.'}
if($TargetDatabase -in @('master','model','msdb','tempdb')){throw 'System database cannot be the target.'}
if($Mode -eq 'COMMIT' -and !$AllowCommit){throw 'COMMIT requires -AllowCommit after reviewing DRYRUN.'}
$folder=Join-Path $scriptDirectory $Package
$config=Get-Content -LiteralPath (Join-Path $folder 'package.json') -Raw | ConvertFrom-Json
$sqlPath=Join-Path $folder 'Migration.sql'
$sql=[IO.File]::ReadAllText($sqlPath).Replace('__SOURCE__',$SourceDatabase).Replace('__TARGET__',$TargetDatabase)
$packageHash=(Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash
# Streaming hashes: include every row (including duplicates), every column and NULL.
# Sort by the fixed-size row hash, then hash the complete byte stream. No CHECKSUM collisions.
if(-not ('GaoStoreTableHasherV2' -as [type])){
 $hashTypeSource=@'
using System;
using System.Data.Common;
using System.Security.Cryptography;
public static class GaoStoreTableHasherV2 {
 public static string Read(DbConnection c,DbTransaction t,string query) {
  using(var cmd=c.CreateCommand()) {
   cmd.Transaction=t; cmd.CommandText=query; cmd.CommandTimeout=1800;
   using(var reader=cmd.ExecuteReader()) using(var sha=SHA256.Create()) {
    long count=0;
    while(reader.Read()) { byte[] b=(byte[])reader[0]; sha.TransformBlock(b,0,b.Length,b,0); count++; }
    sha.TransformFinalBlock(new byte[0],0,0);
    return count.ToString()+":"+BitConverter.ToString(sha.Hash).Replace("-","");
   }
  }
 }
}
'@
 if($PSVersionTable.PSEdition -eq 'Desktop'){
  Add-Type -TypeDefinition $hashTypeSource -ReferencedAssemblies @('System.dll','System.Core.dll','System.Data.dll','System.Xml.dll')
 }else{
  Add-Type -TypeDefinition $hashTypeSource
 }
}
if($CheckSetup){
 Write-Output "SETUP_CHECK_PASS: $Package; PowerShell $($PSVersionTable.PSVersion); C# helper compiled."
 Write-Output "SQL SHA256: $packageHash"
 Write-Output "Source tables: $($config.SourceTables.Count); target tables: $($config.TargetTables.Count)"
 Write-Output 'No SQL connection or report directory created.'
 return
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory; existing evidence is not overwritten.'}
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']=$Server; $cs['Initial Catalog']=$TargetDatabase
$cs['Integrated Security']=$true; $cs['Encrypt']=$true; $cs['TrustServerCertificate']=$true
$cs['Application Name']='GaoStoreReviewedInitialImportV2'; $cs['Connect Timeout']=15
$connection=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
$transaction=$null
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$watch=[Diagnostics.Stopwatch]::StartNew()
$manifest=[ordered]@{Package=$Package;Mode=$Mode;Server=$Server;SourceDatabase=$SourceDatabase;TargetDatabase=$TargetDatabase;SqlSha256=$packageHash;RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;StartedAtUtc=[datetime]::UtcNow.ToString('o');Status='STARTED'}
function Command([string]$text){
 $cmd=$connection.CreateCommand();$cmd.Transaction=$transaction;$cmd.CommandTimeout=1800;$cmd.CommandText=$text;return $cmd
}
function Execute([string]$text){$cmd=Command $text;try{[void]$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}}
function Scalar([string]$text){$cmd=Command $text;try{return $cmd.ExecuteScalar()}finally{$cmd.Dispose()}}
function Hash-Tables([string]$database,[object[]]$tables){
 $hashes=[ordered]@{}
 foreach($name in $tables){
  if($name -notmatch '^[A-Za-z_][A-Za-z_0-9]*$'){throw 'Unexpected table identifier in package.'}
  $query="SELECT HASHBYTES('SHA2_256',(SELECT t.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)) h FROM [$database].dbo.[$name] t ORDER BY h;"
  $hashes[$name]=[GaoStoreTableHasherV2]::Read($connection,$transaction,$query)
 }
 return ($hashes | ConvertTo-Json -Compress)
}
try{
 $connection.Open()
 $transaction=$connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
 Execute "SET XACT_ABORT ON; SET LOCK_TIMEOUT 15000;
 DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'GSTORE-INITIAL-IMPORT-V2',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
 IF @r<0 THROW 55103,'Another initial migration is running.',1;
 IF (SELECT COUNT_BIG(*) FROM dbo.Stores WITH(UPDLOCK,HOLDLOCK))<>1 OR NOT EXISTS(SELECT 1 FROM dbo.Stores WHERE Id=1 AND IsDeleted=0)
 THROW 55104,'This package supports a single StoreId=1 only. Do not guess another mapping.',1;
 EXEC sys.sp_set_session_context @key=N'GSTORE_INITIAL_IMPORT',@value=1;"
 # Acquire shared table locks before fingerprinting/staging, so source writes cannot interleave.
 foreach($name in $config.SourceTables){Execute "DECLARE @n bigint; SELECT @n=COUNT_BIG(*) FROM [$SourceDatabase].dbo.[$name] WITH(TABLOCK,HOLDLOCK);"}
 foreach($name in $config.TargetTables){Execute "DECLARE @n bigint; SELECT @n=COUNT_BIG(*) FROM dbo.[$name] WITH(TABLOCKX,HOLDLOCK);"}
 Write-Output "Checking $Package ($Mode): $SourceDatabase -> $TargetDatabase"
 $sourceHash=Hash-Tables $SourceDatabase $config.SourceTables
 $manifest['SourceHashes']=$sourceHash | ConvertFrom-Json
 $hasJournal=[bool](Scalar "SELECT CASE WHEN OBJECT_ID(N'dbo.GaoStoreMigrationRunsV2',N'U') IS NULL THEN 0 ELSE 1 END;")
 $prior=$null
 if($hasJournal){
  $cmd=Command 'SELECT SourceDatabase,SqlSha256,SourceHashes,TargetHashes FROM dbo.GaoStoreMigrationRunsV2 WHERE PackageId=@PackageId;'
  [void]$cmd.Parameters.AddWithValue('@PackageId',[string]$config.PackageId)
  $data=[System.Data.DataTable]::new();$adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd);[void]$adapter.Fill($data)
  if($data.Rows.Count -eq 1){$prior=$data.Rows[0]}
 }
 if($null -ne $prior){
  if($prior.SourceDatabase -ne $SourceDatabase -or $prior.SqlSha256 -ne $packageHash -or $prior.SourceHashes -cne $sourceHash){throw 'Previously imported package/source changed. Initial import does not refresh a live target. Use a fresh rehearsal/cutover target.'}
  $currentHash=Hash-Tables $TargetDatabase $config.TargetTables
  if($prior.TargetHashes -cne $currentHash){
   $imageExtensionVerified=$false
   if($Package -eq '01-products' -and [int](Scalar "SELECT CASE WHEN OBJECT_ID(N'dbo.GaoStoreProductImageRunsV1',N'U') IS NULL THEN 0 ELSE 1 END;") -eq 1){
    . (Join-Path $scriptDirectory '../product-images/Image-Receipt.ps1')
    $cmd=Command 'SELECT * FROM dbo.GaoStoreProductImageRunsV1 WHERE StoreId=1;'
    $receiptTable=[System.Data.DataTable]::new();$adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd)
    try{[void]$adapter.Fill($receiptTable)}finally{$adapter.Dispose();$cmd.Dispose()}
    if($receiptTable.Rows.Count -eq 1){
     $cmd=Command "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ProductVariant') AND name NOT IN(N'PrimaryProductImageId',N'RowVersion') ORDER BY column_id;"
     $columns=[System.Data.DataTable]::new();$adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd)
     try{[void]$adapter.Fill($columns)}finally{$adapter.Dispose();$cmd.Dispose()}
     $projection=($columns.Rows | ForEach-Object {'t.['+([string]$_.name).Replace(']',']]')+']'}) -join ','
     $otherHash=[GaoStoreTableHasherV2]::Read($connection,$transaction,"SELECT HASHBYTES('SHA2_256',(SELECT $projection FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)) h FROM dbo.ProductVariant t ORDER BY h;")
     $imageHashes=Hash-Tables $TargetDatabase @('MediaAssets','ProductImages') | ConvertFrom-Json
     $imageSqlHash=(Get-FileHash -LiteralPath (Join-Path $scriptDirectory '../product-images/Apply-Images.sql')).Hash
     $imageExtensionVerified=Test-ImageCatalogueExtension -Expected ($prior.TargetHashes | ConvertFrom-Json) -Current ($currentHash | ConvertFrom-Json) -Receipt $receiptTable.Rows[0] -SourceDatabase $SourceDatabase -ImageSqlHash $imageSqlHash -OtherVariantColumnsHash $otherHash -MediaAssetsHash $imageHashes.MediaAssets -ProductImagesHash $imageHashes.ProductImages
     if($imageExtensionVerified){$manifest['VerifiedImageExtension']=$receiptTable.Rows[0].PreviewManifestSha256}
    }
   }
   if(-not $imageExtensionVerified){throw 'Target differs from the committed import. No overwrite was performed; review downstream/runtime changes.'}
  }
  # SqlTransaction.Rollback() uses an internal short timeout. Large DRYRUNs need
  # the explicit command timeout so rollback completion is actually observed.
  Execute 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'
  $transaction.Dispose();$transaction=$null
  $manifest['Status']=if($Mode -eq 'VERIFY'){'VERIFY_PASS'}else{'ALREADY_IMPORTED_NO_CHANGE'}
  Write-Output $manifest.Status
 }else{
  if($Mode -eq 'VERIFY'){throw 'No V2 commit receipt exists for this package. Old imports are not silently adopted.'}
  foreach($name in $config.TargetTables){
   if([long](Scalar "SELECT COUNT_BIG(*) FROM dbo.[$name];") -ne 0){throw "Target dbo.$name is not empty. This initial-import package never clears existing data."}
  }
  # Product import is allowed only before all business transactions.
  if($Package -eq '01-products'){
   foreach($name in @('Orders','InvoiceHeads','StockDocument','SalesReturns','InvoiceInputStockSupplementalMovements','InventoryTransactions')){
    if([long](Scalar "SELECT COUNT_BIG(*) FROM dbo.[$name];") -ne 0){throw "Product initial import requires no business transactions: $name is populated."}
   }
  }
  $cmd=Command $sql
  [void]$cmd.Parameters.Add('@Mode',[System.Data.SqlDbType]::VarChar,12);$cmd.Parameters['@Mode'].Value=$Mode
  $ds=[System.Data.DataSet]::new();$adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd)
  [void]$adapter.Fill($ds)
  $i=0
  foreach($table in $ds.Tables){
   $i++;$path=Join-Path $OutputDirectory ('report-{0:00}.csv' -f $i)
   $table | Select-Object -Property $table.Columns.ColumnName | Export-Csv -LiteralPath $path -Encoding utf8 -NoTypeInformation
   if($table.Rows.Count -le 2){$table | Format-Table -AutoSize | Out-String -Width 230 | Write-Output}
   else{Write-Output "Report $i : $($table.Rows.Count) rows"}
  }
  if($Mode -ne 'PREVIEW'){
   $targetHash=Hash-Tables $TargetDatabase $config.TargetTables
   $manifest['TargetHashes']=$targetHash | ConvertFrom-Json
  }
  if($Mode -eq 'COMMIT'){
   Execute "IF OBJECT_ID(N'dbo.GaoStoreMigrationRunsV2',N'U') IS NULL CREATE TABLE dbo.GaoStoreMigrationRunsV2(PackageId nvarchar(100) NOT NULL PRIMARY KEY,SourceDatabase sysname NOT NULL,SqlSha256 char(64) NOT NULL,SourceHashes nvarchar(max) NOT NULL,TargetHashes nvarchar(max) NOT NULL,CommittedAtUtc datetime2 NOT NULL);"
   $cmd=Command 'INSERT dbo.GaoStoreMigrationRunsV2 VALUES(@Id,@Source,@Sql,@SourceHashes,@TargetHashes,SYSUTCDATETIME());'
   foreach($entry in @(@('@Id',[string]$config.PackageId),@('@Source',$SourceDatabase),@('@Sql',$packageHash),@('@SourceHashes',$sourceHash),@('@TargetHashes',$targetHash))){[void]$cmd.Parameters.AddWithValue($entry[0],$entry[1])}
   [void]$cmd.ExecuteNonQuery()
   Execute 'COMMIT TRANSACTION;'
   $transaction.Dispose();$transaction=$null;$manifest['Status']='COMMIT_PASS'
  }else{
   Execute 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'
   $transaction.Dispose();$transaction=$null;$manifest['Status']=$Mode+'_PASS_ROLLED_BACK'
  }
  Write-Output $manifest.Status
 }
}catch{
 $failure=$_
 if($null -ne $transaction){
  try{if($null -ne $transaction.Connection){Execute 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'}}
  catch{$manifest['RollbackError']=$_.Exception.Message}
  $transaction.Dispose();$transaction=$null
 }
 $manifest['Status']='FAILED';$manifest['Error']=$failure.Exception.Message
 throw $failure
}finally{
 $connection.Dispose();$watch.Stop();$manifest['ElapsedMilliseconds']=$watch.ElapsedMilliseconds
 $manifest['FinishedAtUtc']=[datetime]::UtcNow.ToString('o')
 $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding utf8
 Write-Output "Reports: $OutputDirectory"
}
