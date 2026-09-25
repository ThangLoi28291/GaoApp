# Maintainer test harness only. Never accepts arbitrary target names or clears a database.
param([ValidateSet('GaoAppMigrationReview_20260923','GaoAppMigrationReview_20260923_v2')][string]$Target='GaoAppMigrationReview_20260923_v2')
$ErrorActionPreference='Stop'
$root=Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) '.artifacts\migration-review-db'
[void][IO.Directory]::CreateDirectory($root)
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']='.\SQLEXPRESS';$cs['Initial Catalog']='master';$cs['Integrated Security']=$true;$cs['Encrypt']=$true;$cs['TrustServerCertificate']=$true
$c=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
function Exec([string]$sql){$cmd=$c.CreateCommand();$cmd.CommandTimeout=1800;$cmd.CommandText=$sql;[void]$cmd.ExecuteNonQuery();$cmd.Dispose()}
try{
 $c.Open()
 $data=(Join-Path $root ($target+'.mdf')).Replace("'","''")
 $log=(Join-Path $root ($target+'_log.ldf')).Replace("'","''")
 Exec "IF DB_ID(N'$target') IS NOT NULL THROW 55200,'Rehearsal database already exists; never reset it automatically.',1;
 CREATE DATABASE [$target] ON PRIMARY(NAME=N'GaoAppMigrationReviewData',FILENAME=N'$data',SIZE=128MB,FILEGROWTH=256MB) LOG ON(NAME=N'GaoAppMigrationReviewLog',FILENAME=N'$log',SIZE=128MB,FILEGROWTH=256MB);
 ALTER DATABASE [$target] SET RECOVERY SIMPLE;"
 $c.ChangeDatabase($target)
 $sql=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'SchemaTool/FullSchema.sql'))
 foreach($batch in [regex]::Split($sql,'(?im)^GO\s*$')){if($batch.Trim()){Exec $batch}}
 # Foundation copied from TEST only, with provider configuration intentionally not connected.
 foreach($table in @('Stores','LegalEntities','Warehouses','RewardSettings')){
  $cmd=$c.CreateCommand();$cmd.CommandText="SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.$table') AND is_computed=0 AND system_type_id<>189 ORDER BY column_id;"
  $reader=$cmd.ExecuteReader();$cols=[Collections.Generic.List[string]]::new();while($reader.Read()){$cols.Add([string]$reader[0])};$reader.Close()
  $names=($cols | ForEach-Object {'['+$_+']'}) -join ','
  $select=($cols | ForEach-Object {if($table -eq 'LegalEntities' -and $_ -in @('DefaultWarehouseId','InvoiceProviderSettingId')){'NULL'}else{'['+$_+']'}}) -join ','
  Exec "SET IDENTITY_INSERT dbo.[$table] ON; INSERT dbo.[$table]($names) SELECT $select FROM GaoAppDb.dbo.[$table]; SET IDENTITY_INSERT dbo.[$table] OFF;"
 }
 Exec 'UPDATE dbo.LegalEntities SET DefaultWarehouseId=1 WHERE Id=1;'
 Write-Output "REHEARSAL_INITIALIZED: $target"
}finally{$c.Dispose()}
