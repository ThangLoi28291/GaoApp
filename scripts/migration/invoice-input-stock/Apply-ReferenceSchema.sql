-- Equivalent to EF migration 20260923140000_AddInvoiceStockLegacyDocumentReferences.
-- Run on the reviewed target before deploying the updated application / v2 importer.
-- Supply @ExpectedTargetDatabase sysname as a command parameter.
SET XACT_ABORT ON;
IF DB_NAME()<>@ExpectedTargetDatabase THROW 51000,N'Wrong target database.',1;
IF @@TRANCOUNT<>0 THROW 51000,N'Ambient transaction is not supported.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20260921100000_AddInvoiceInputStockSupplementalMovements')
    THROW 51000,N'Apply the supplemental-movement migration first.',1;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult=sys.sp_getapplock @Resource=N'GSTORE-IIS-V1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
    IF @LockResult<0 THROW 51000,N'Could not acquire migration lock.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20260923140000_AddInvoiceStockLegacyDocumentReferences')
    BEGIN
        IF COL_LENGTH(N'dbo.InvoiceInputStockSupplementalMovements',N'LegacyOrderId') IS NOT NULL
            OR COL_LENGTH(N'dbo.InvoiceInputStockSupplementalMovements',N'LegacyInvoiceNumber') IS NOT NULL
            OR COL_LENGTH(N'dbo.InvoiceInputStockSupplementalMovements',N'LegacyInvoiceSymbol') IS NOT NULL
            THROW 51000,N'Untracked reference schema found; review before applying.',1;
        ALTER TABLE dbo.InvoiceInputStockSupplementalMovements ADD
            LegacyOrderId bigint NULL, LegacyInvoiceNumber nvarchar(200) NULL, LegacyInvoiceSymbol nvarchar(200) NULL;
        INSERT dbo.__EFMigrationsHistory(MigrationId,ProductVersion)
        SELECT N'20260923140000_AddInvoiceStockLegacyDocumentReferences',ProductVersion
        FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20260921100000_AddInvoiceInputStockSupplementalMovements';
    END;
    IF COL_LENGTH(N'dbo.InvoiceInputStockSupplementalMovements',N'LegacyOrderId')<>8
        OR COL_LENGTH(N'dbo.InvoiceInputStockSupplementalMovements',N'LegacyInvoiceNumber')<>400
        OR COL_LENGTH(N'dbo.InvoiceInputStockSupplementalMovements',N'LegacyInvoiceSymbol')<>400
        OR COL_LENGTH(N'dbo.InvoiceInputStockSupplementalMovements',N'LegacyOrderId') IS NULL
        OR COL_LENGTH(N'dbo.InvoiceInputStockSupplementalMovements',N'LegacyInvoiceNumber') IS NULL
        OR COL_LENGTH(N'dbo.InvoiceInputStockSupplementalMovements',N'LegacyInvoiceSymbol') IS NULL
        THROW 51000,N'Reference schema verification failed.',1;
    COMMIT;
    SELECT N'REFERENCE_SCHEMA_PASS' Result,DB_NAME() TargetDatabase;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
