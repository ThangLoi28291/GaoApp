# Copy prerequisites into the isolated rehearsal only; never accepts a production target.
param([ValidateSet('GaoAppMigrationReview_20260923','GaoAppMigrationReview_20260923_v2')][string]$Target='GaoAppMigrationReview_20260923_v2')
$ErrorActionPreference='Stop'
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']='.\SQLEXPRESS';$cs['Initial Catalog']=$Target
$cs['Integrated Security']=$true;$cs['Encrypt']=$true;$cs['TrustServerCertificate']=$true
$c=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
$tx=$null
try{
 $c.Open();$tx=$c.BeginTransaction()
 foreach($table in @('Roles','Permissions','RolePermissions','Users','UserInStores','POSTerminals')){
  $cmd=$c.CreateCommand();$cmd.Transaction=$tx;$cmd.CommandTimeout=120
  $cmd.CommandText="SELECT name,is_identity FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.$table') AND is_computed=0 AND system_type_id<>189 ORDER BY column_id;"
  $r=$cmd.ExecuteReader();$cols=[Collections.Generic.List[string]]::new();$identity=$false
  while($r.Read()){$cols.Add('['+[string]$r[0]+']');if([bool]$r[1]){$identity=$true}};$r.Close()
  $names=$cols -join ','
  $cmd.CommandText="IF EXISTS(SELECT 1 FROM dbo.[$table]) THROW 55500,'Rehearsal prerequisite table is not empty.',1;"
  if($identity){$cmd.CommandText+="SET IDENTITY_INSERT dbo.[$table] ON;"}
  $cmd.CommandText+="INSERT dbo.[$table]($names) SELECT $names FROM GaoAppDb.dbo.[$table];"
  if($identity){$cmd.CommandText+="SET IDENTITY_INSERT dbo.[$table] OFF;"}
  [void]$cmd.ExecuteNonQuery();Write-Output "Copied prerequisite $table"
 }
 $tx.Commit();$tx=$null
}finally{if($null -ne $tx){$tx.Rollback()};$c.Dispose()}
