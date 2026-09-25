# Maintainer only: rerun our step 3 test in the isolated database created this session.
# Never shipped to an operator. No target/source parameters and no production defaults.
$ErrorActionPreference='Stop'
$cs=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$cs['Data Source']='.\SQLEXPRESS';$cs['Initial Catalog']='GaoAppMigrationReview_20260923_v2'
$cs['Integrated Security']=$true;$cs['Encrypt']=$true;$cs['TrustServerCertificate']=$true
$c=[System.Data.SqlClient.SqlConnection]::new($cs.ConnectionString)
try {
 $c.Open();$cmd=$c.CreateCommand();$cmd.CommandTimeout=600
 $cmd.CommandText=@'
SET XACT_ABORT ON;
IF DB_NAME()<>N'GaoAppMigrationReview_20260923_v2' THROW 55510,'Wrong rehearsal database.',1;
BEGIN TRAN;
IF NOT EXISTS(SELECT 1 FROM dbo.GaoStoreMigrationRunsV2 WHERE PackageId=N'GSTORE-03-SALES-V1' AND SourceDatabase=N'DataGaoStore' AND SqlSha256='4A4E463782F11470D5293D8E3C2BC49A3FFAF55D0C3BD39070734ED01EDD8464')
 THROW 55511,'Expected prior rehearsal receipt is missing.',1;
IF EXISTS(SELECT 1 FROM dbo.Orders WHERE OrderNumber NOT LIKE N'LEGACY-%')
 OR EXISTS(SELECT 1 FROM dbo.InvoiceHeads) OR EXISTS(SELECT 1 FROM dbo.StockDocument)
 OR EXISTS(SELECT 1 FROM dbo.InventoryTransactions) OR EXISTS(SELECT 1 FROM dbo.InvoiceInputStockSupplementalMovements)
 THROW 55512,'Rehearsal has runtime or downstream records; do not reset.',1;
DELETE dbo.SalesReturnPayments;
DELETE dbo.SalesReturnLines;
DELETE dbo.SalesReturns;
DELETE dbo.POSShiftCashTransactions;
DELETE dbo.OrderPayments;
DELETE dbo.OrderLines;
DELETE dbo.Orders;
DELETE dbo.POSShifts;
DELETE dbo.GaoStoreMigrationRunsV2 WHERE PackageId=N'GSTORE-03-SALES-V1';
COMMIT;
'@
 [void]$cmd.ExecuteNonQuery();Write-Output 'OWN_REHEARSAL_STEP3_RESET; products/customers/archive and reference databases unchanged.'
} finally { $c.Dispose() }
