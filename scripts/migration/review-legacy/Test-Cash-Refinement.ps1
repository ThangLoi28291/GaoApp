# Maintainer-only differential test in our rehearsal, not an operator migration.
$ErrorActionPreference='Stop'
$sourceFile=Join-Path $PSScriptRoot '../initial-import/03-sales/Migration.sql'
$migration=Get-Content $sourceFile -Raw
$stage=$migration.Substring(0,$migration.IndexOf('-- Generic insertion')).Replace('__SOURCE__','DataGaoStore')
$sql=@'
SET NOCOUNT ON; SET XACT_ABORT ON;
IF DB_NAME()<>N'GaoAppMigrationReview_20260923_v2' THROW 55520,'Wrong rehearsal.',1;
SELECT * INTO #CashBefore FROM dbo.POSShiftCashTransactions;
BEGIN TRAN;
EXEC sys.sp_set_session_context @key=N'GSTORE_INITIAL_IMPORT',@value=1;
DECLARE @Mode varchar(12)='DRYRUN';
'@ + $stage + @'
UPDATE t SET Note=s.Note,CreatedAtUtc=s.CreatedAtUtc
FROM dbo.POSShiftCashTransactions t JOIN #StagePOSShiftCashTransactions s
ON TRY_CONVERT(bigint,SUBSTRING(t.Note,14,CHARINDEX(' | ',t.Note+' | ')-14))=
   TRY_CONVERT(bigint,SUBSTRING(s.Note,14,CHARINDEX(' | ',s.Note+' | ')-14));
DECLARE @CheckTables TABLE(Name sysname);
INSERT @CheckTables VALUES('POSShifts'),('Orders'),('OrderLines'),('OrderPayments'),
 ('POSShiftCashTransactions'),('SalesReturns'),('SalesReturnLines'),('SalesReturnPayments');
DECLARE @CheckName sysname,@CheckColumns nvarchar(max),@CheckSql nvarchar(max);
DECLARE cash_check CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @CheckTables;
OPEN cash_check; FETCH NEXT FROM cash_check INTO @CheckName;
WHILE @@FETCH_STATUS=0
BEGIN
 SELECT @CheckColumns=STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(name)),N',') WITHIN GROUP(ORDER BY column_id)
 FROM tempdb.sys.columns WHERE object_id=OBJECT_ID(N'tempdb..#Stage'+@CheckName);
 SET @CheckSql=N'IF (SELECT COUNT_BIG(*) FROM dbo.'+QUOTENAME(@CheckName)+N')<>(SELECT COUNT_BIG(*) FROM '+QUOTENAME(N'#Stage'+@CheckName)+N')
 OR EXISTS(SELECT '+@CheckColumns+N' FROM '+QUOTENAME(N'#Stage'+@CheckName)+N' EXCEPT SELECT '+@CheckColumns+N' FROM dbo.'+QUOTENAME(@CheckName)+N')
 OR EXISTS(SELECT '+@CheckColumns+N' FROM dbo.'+QUOTENAME(@CheckName)+N' EXCEPT SELECT '+@CheckColumns+N' FROM '+QUOTENAME(N'#Stage'+@CheckName)+N')
 THROW 55521,''Differential stage mismatch: '+@CheckName+N''',1;';
 EXEC sys.sp_executesql @CheckSql;
 FETCH NEXT FROM cash_check INTO @CheckName;
END;
CLOSE cash_check; DEALLOCATE cash_check;
SELECT N'ALL_EIGHT_STAGES_EXACT_PASS_AFTER_CASH_REFINEMENT' Report;
IF EXISTS(SELECT POSShiftId,Type,Amount,Reason,Note,CreatedByUserId FROM dbo.POSShiftCashTransactions
 EXCEPT SELECT POSShiftId,Type,Amount,Reason,Note,CreatedByUserId FROM GaoAppDb.dbo.POSShiftCashTransactions WHERE Note LIKE N'LegacyCashId=%')
 OR EXISTS(SELECT POSShiftId,Type,Amount,Reason,Note,CreatedByUserId FROM GaoAppDb.dbo.POSShiftCashTransactions WHERE Note LIKE N'LegacyCashId=%'
 EXCEPT SELECT POSShiftId,Type,Amount,Reason,Note,CreatedByUserId FROM dbo.POSShiftCashTransactions)
 OR EXISTS(SELECT 1 FROM dbo.POSShiftCashTransactions n JOIN GaoAppDb.dbo.POSShiftCashTransactions t ON t.Note=n.Note
 WHERE ABS(DATEDIFF_BIG(MICROSECOND,n.CreatedAtUtc,t.CreatedAtUtc))>1000)
 THROW 55523,'Cash data/note/time differs from reference TEST.',1;
SELECT N'CASH_REFERENCE_TEST_EXACT_PASS_WITH_1MS_TIME_TOLERANCE' Report;
ROLLBACK;
IF EXISTS(SELECT * FROM #CashBefore EXCEPT SELECT * FROM dbo.POSShiftCashTransactions)
 OR EXISTS(SELECT * FROM dbo.POSShiftCashTransactions EXCEPT SELECT * FROM #CashBefore)
 THROW 55522,'Cash rollback did not restore original rows.',1;
SELECT N'CASH_REFINEMENT_ROLLBACK_VERIFIED' Report,COUNT_BIG(*) CashRows FROM dbo.POSShiftCashTransactions;
'@
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']='.\SQLEXPRESS';$cs['Initial Catalog']='GaoAppMigrationReview_20260923_v2'
$cs['Integrated Security']=$true;$cs['Encrypt']=$true;$cs['TrustServerCertificate']=$true
$c=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
try {
 $c.Open();$cmd=$c.CreateCommand();$cmd.CommandTimeout=1800;$cmd.CommandText=$sql
 $ds=[System.Data.DataSet]::new();$a=[System.Data.SqlClient.SqlDataAdapter]::new($cmd);[void]$a.Fill($ds)
 foreach($table in $ds.Tables){if($table.Rows.Count -eq 1){$table | Format-Table -AutoSize | Out-String -Width 220 | Write-Output}}
 $result=@{Status='CASH_REFINEMENT_DRYRUN_PASS';SqlSha256=(Get-FileHash $sourceFile -Algorithm SHA256).Hash;Target='GaoAppMigrationReview_20260923_v2';AllEightStagesCompared=$true;ReferenceCashCompared=$true;RollbackRestoredOriginalRows=$true}
 $result | ConvertTo-Json | Set-Content .artifacts/migration-review/v2/cash-refinement-dryrun.json -Encoding utf8
} finally {$c.Dispose()}
