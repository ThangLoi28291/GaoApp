# TEST ONLY. Modifies temporary staged rows, always ROLLBACK; never alters source.
[CmdletBinding()]
param([string]$Server='.\SQLEXPRESS',[string]$SourceDatabase='DataGaoStore',[string]$TargetDatabase='GaoAppDb',
 [string]$OutputPath=(Join-Path $PSScriptRoot 'rerun-test-result.json'))
$ErrorActionPreference='Stop'
$sql=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'InvoiceInputStockMigration.sql'))
$sql=[regex]::Replace($sql,'(?s)-- SETTINGS BEGIN.*?-- SETTINGS END','-- Test supplies parameters; Mode is always DRYRUN.')
$fixture=@'
CREATE INDEX IX_Raw_Code ON #Raw(Code);
DECLARE @FixtureUpdateKey nvarchar(200)=(SELECT TOP(1) r.LegacySourceKey FROM #Raw r JOIN dbo.InvoiceInputStockSupplementalMovements t ON t.LegacySourceKey=r.LegacySourceKey AND t.StoreId=@StoreId AND t.IsDeleted=0 WHERE r.MovementType=2 AND r.RawQuantity>0 ORDER BY r.LegacySourceKey);
DECLARE @FixtureRetireKey nvarchar(200)=(SELECT TOP(1) r.LegacySourceKey FROM #Raw r JOIN dbo.InvoiceInputStockSupplementalMovements t ON t.LegacySourceKey=r.LegacySourceKey AND t.StoreId=@StoreId AND t.IsDeleted=0 WHERE r.MovementType=4 ORDER BY r.LegacySourceKey);
DECLARE @FixtureNewKey nvarchar(200)=CONCAT(@Prefix,N'I|fixture-new');
IF @FixtureUpdateKey IS NULL OR @FixtureRetireKey IS NULL THROW 51000,N'Run fixture after TEST COMMIT and VERIFY.',1;
IF EXISTS(SELECT 1 FROM dbo.InvoiceInputStockSupplementalMovements WHERE StoreId=@StoreId AND LegacySourceKey=@FixtureNewKey) THROW 51000,N'Fixture key already exists.',1;
INSERT #Raw SELECT @FixtureNewKey,Code,LocalAt,2,MovementType,N'Rollback-only new source row',LegacyOrderId,LegacyInvoiceNumber,LegacyInvoiceSymbol FROM #Raw WHERE LegacySourceKey=@FixtureUpdateKey;
UPDATE #Raw SET RawQuantity=RawQuantity+1 WHERE LegacySourceKey=@FixtureUpdateKey;
DELETE #Raw WHERE LegacySourceKey=@FixtureRetireKey;
'@
$sql=$sql.Replace('CREATE INDEX IX_Raw_Code ON #Raw(Code);',$fixture)
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']=$Server;$cs['Initial Catalog']=$TargetDatabase;$cs['Integrated Security']=$true
$cs['Encrypt']=$true;$cs['TrustServerCertificate']=$true;$cs['Application Name']='GaoAppInvoiceStockRollbackFixture'
$conn=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
try {
 $conn.Open();$cmd=$conn.CreateCommand();$cmd.CommandTimeout=600;$cmd.CommandText=$sql
 foreach($pair in @(@('@Mode','DRYRUN'),@('@SourceDatabase',$SourceDatabase),@('@ExpectedTargetDatabase',$TargetDatabase))) {
  [void]$cmd.Parameters.Add($pair[0],[System.Data.SqlDbType]::NVarChar,128);$cmd.Parameters[$pair[0]].Value=$pair[1]
 }
 foreach($pair in @(@('@StoreId',1),@('@WarehouseId',1))) {
  [void]$cmd.Parameters.Add($pair[0],[System.Data.SqlDbType]::Int);$cmd.Parameters[$pair[0]].Value=$pair[1]
 }
 [void]$cmd.Parameters.Add('@CutoffExclusive',[System.Data.SqlDbType]::Date);$cmd.Parameters['@CutoffExclusive'].Value=[DBNull]::Value
 foreach($pair in @(@('@AllowCommit',$false),@('@AllowRetire',$true))) {
  [void]$cmd.Parameters.Add($pair[0],[System.Data.SqlDbType]::Bit);$cmd.Parameters[$pair[0]].Value=$pair[1]
 }
 $data=[System.Data.DataSet]::new();$adapter=[System.Data.SqlClient.SqlDataAdapter]::new($cmd);[void]$adapter.Fill($data)
 $plan=$null;$verified=$false;$rolledBack=$false
 foreach($table in $data.Tables){
  if($table.Rows.Count -eq 0){continue}
  $report=[string]$table.Rows[0]['Report']
  if($report -eq 'PLAN'){$plan=$table.Rows[0]}
  if($report -eq 'EXACT_VERIFY_PASS'){$verified=$true}
  if($report -eq 'DRYRUN_ROLLED_BACK'){$rolledBack=$true}
 }
 if($null -eq $plan -or $plan.InsertRows -ne 1 -or $plan.UpdateRows -ne 1 -or $plan.RetireRows -ne 1 -or -not $verified -or -not $rolledBack){throw 'Expected exactly 1 insert, 1 update, 1 retirement, exact verification and rollback.'}
 $cmd.Parameters.Clear();$cmd.CommandText="SELECT COUNT_BIG(*) FROM dbo.InvoiceInputStockSupplementalMovements WHERE LegacySourceKey=N'GSTORE-IIS-V1|I|fixture-new';"
 if([long]$cmd.ExecuteScalar() -ne 0){throw 'Fixture row persisted.'}
 $result=[pscustomobject]@{Result='RERUN_DELTA_ROLLBACK_PASS';InsertRows=1;UpdateRows=1;RetireRows=1;ExactVerification=$verified;RolledBack=$rolledBack;CompletedAtUtc=[datetime]::UtcNow.ToString('o')}
 $result|ConvertTo-Json|Set-Content -LiteralPath $OutputPath -Encoding utf8
 $result|Format-List
}finally{$conn.Dispose()}
