# TEST only: staged source additions/updates and a target-edit conflict. Always rollback.
[CmdletBinding()]
param([string]$Server='.\SQLEXPRESS',[string]$TargetDatabase='GaoAppDb',[string]$SourceDatabase='DataGaoStore',
 [string]$OutputPath=(Join-Path $PSScriptRoot 'rerun-test.json'))
$ErrorActionPreference='Stop'
$base=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'InvoiceMigration.sql'))
$base=[regex]::Replace($base,'(?s)-- SETTINGS BEGIN.*?-- SETTINGS END','-- Typed parameters supplied by fixture.')
$marker=' EXEC sys.sp_executesql @SourceSql;'
$fixture=@'
 EXEC sys.sp_executesql @SourceSql;
 DECLARE @Existing bigint=(SELECT MIN(LegacySourceId) FROM dbo.InvoiceHeads WHERE StoreId=@StoreId AND LegacyReadOnly=0 AND ProviderStatus=0 AND LegacySourceId IS NOT NULL);
 DECLARE @NewHead bigint=1800000000,@NewDetail bigint=1800000001;
 IF @Existing IS NULL OR EXISTS(SELECT 1 FROM #SourceHead WHERE LegacyId=@NewHead) OR EXISTS(SELECT 1 FROM #SourceDetail WHERE LegacyId=@NewDetail)
     THROW 51000,N'Fixture prerequisites not met.',1;
 UPDATE #SourceHead SET SourceJson=JSON_MODIFY(SourceJson,'$.Note',N'Rollback-only changed source note') WHERE LegacyId=@Existing;
 INSERT #SourceHead SELECT @NewHead,JSON_MODIFY(JSON_MODIFY(SourceJson,'$.Id',@NewHead),'$.Note',N'Rollback-only new source header') FROM #SourceHead WHERE LegacyId=@Existing;
 INSERT #SourceDetail SELECT TOP(1) @NewDetail,JSON_MODIFY(JSON_MODIFY(SourceJson,'$.ID',@NewDetail),'$.OrderID',@NewHead) FROM #SourceDetail ORDER BY LegacyId;
'@
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']=$Server;$cs['Initial Catalog']=$TargetDatabase;$cs['Integrated Security']=$true
$cs['Encrypt']=$true;$cs['TrustServerCertificate']=$true;$cs['Application Name']='GaoAppInvoiceMigrationRollbackFixture'
$conn=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
try {
 $conn.Open();$cmd=$conn.CreateCommand();$cmd.CommandTimeout=600
 foreach($key in @('Mode','SourceDatabase','ExpectedTargetDatabase')){[void]$cmd.Parameters.Add('@'+$key,[System.Data.SqlDbType]::NVarChar,128)}
 $cmd.Parameters['@Mode'].Value='DRYRUN';$cmd.Parameters['@SourceDatabase'].Value=$SourceDatabase;$cmd.Parameters['@ExpectedTargetDatabase'].Value=$TargetDatabase
 foreach($key in @('StoreId','LegalEntityId','WarehouseId')){[void]$cmd.Parameters.Add('@'+$key,[System.Data.SqlDbType]::Int);$cmd.Parameters['@'+$key].Value=1}
 [void]$cmd.Parameters.Add('@AllowCommit',[System.Data.SqlDbType]::Bit);$cmd.Parameters['@AllowCommit'].Value=$false
 $cmd.CommandText=$base.Replace($marker,$fixture)
 $ds=[System.Data.DataSet]::new();$adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd);[void]$adapter.Fill($ds)
 $plan=$null;$verified=$false;$rolledBack=$false
 foreach($table in $ds.Tables){if($table.Rows.Count -gt 0 -and $table.Columns.Contains('Report')){
   $report=[string]$table.Rows[0]['Report'];if($report -eq 'PLAN'){$plan=$table.Rows[0]}
   if($report -eq 'EXACT_VERIFY_PASS'){$verified=$true};if($report -eq 'DRYRUN_ROLLED_BACK'){$rolledBack=$true}
 }}
 if($null -eq $plan -or $plan.InsertHeads -ne 1 -or $plan.UpdateHeads -ne 1 -or $plan.InsertDetails -ne 1 -or $plan.UpdateDetails -ne 0 -or !$verified -or !$rolledBack){throw 'Unexpected fixture write plan or verification.'}
 $editFixture=@'
 EXEC sys.sp_executesql @SourceSql;
 UPDATE dbo.InvoiceHeads SET Note=N'Rollback-only GaoApp user edit'
 WHERE Id=(SELECT MIN(Id) FROM dbo.InvoiceHeads WHERE StoreId=@StoreId AND LegacySourceId IS NOT NULL);
'@
 $cmd.CommandText=$base.Replace($marker,$editFixture);$ds=[System.Data.DataSet]::new();$adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd)
 $protected=$false
 try{[void]$adapter.Fill($ds)}catch{if($_.Exception.ToString().Contains('imported row edited in GaoApp')){$protected=$true}else{throw}}
 if(!$protected){throw 'Target edit was not protected.'}
 $cmd.Parameters.Clear();$cmd.CommandText="SELECT COUNT_BIG(*) FROM dbo.InvoiceHeads WHERE LegacySourceId=1800000000 OR Note=N'Rollback-only GaoApp user edit';"
 if([long]$cmd.ExecuteScalar() -ne 0){throw 'Fixture changes did not roll back.'}
 $result=[pscustomobject]@{Result='INVOICE_RERUN_ROLLBACK_PASS';InsertHeads=1;UpdateHeads=1;InsertDetails=1;UpdateDetails=0;ExactVerification=$verified;RolledBack=$rolledBack;TargetEditProtected=$protected;CompletedAtUtc=[datetime]::UtcNow.ToString('o')}
 $result|ConvertTo-Json|Set-Content -LiteralPath $OutputPath -Encoding utf8;$result|Format-List
}finally{$conn.Dispose()}
