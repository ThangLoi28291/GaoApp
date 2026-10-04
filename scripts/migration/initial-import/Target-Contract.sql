-- Read-only contract for the reviewed September 28 schema. Never repairs or upgrades SQL.
SET NOCOUNT ON;
IF ISNULL((SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory),N'') <> N'20260928200000_AddStoreReceiptDefault'
    THROW 55600, 'Unsupported target schema. Inspect and review a new package; do not bypass this guard.', 1;
IF OBJECT_ID(N'dbo.AutoInvoiceSettings',N'U') IS NULL
 OR OBJECT_ID(N'dbo.AutoInvoiceOperations',N'U') IS NULL
 OR OBJECT_ID(N'dbo.AutoInvoiceOperationSources',N'U') IS NULL
 OR OBJECT_ID(N'dbo.AutoInvoiceWorkerStates',N'U') IS NULL
 OR OBJECT_ID(N'dbo.InvoiceBuyerSelfServiceRequests',N'U') IS NULL
 OR COL_LENGTH(N'dbo.Orders',N'InvoiceIssuanceRoute') IS NULL
 OR COL_LENGTH(N'dbo.InvoiceHeads',N'LastIssuanceRelevantChangeAtUtc') IS NULL
 OR COL_LENGTH(N'dbo.Stores',N'ReceiptTemplateKey') IS NULL
 OR ISNULL(COLUMNPROPERTY(OBJECT_ID(N'dbo.Orders'),N'ListSortAtUtc','IsComputed'),0) <> 1
    THROW 55601, 'Target schema objects/columns are incomplete.', 1;
IF EXISTS(SELECT 1 FROM dbo.AutoInvoiceSettings WHERE IsEnabled=1 AND IsDeleted=0)
    THROW 55602, 'Pause automatic invoice issuance in GaoApp before preparing the target. Settings are preserved, not overwritten.', 1;
IF EXISTS(SELECT 1 FROM dbo.AutoInvoiceWorkerStates
          WHERE IsDeleted=0 AND IsRunning=1 AND LastHeartbeatAtUtc >= DATEADD(MINUTE,-3,SYSUTCDATETIME()))
    THROW 55603, 'Auto invoice worker recently active. Stop its Windows Service, wait for in-flight work to finish, then retry.', 1;
IF NOT EXISTS(SELECT 1 FROM dbo.Stores WHERE Id=1 AND IsDeleted=0)
 OR NOT EXISTS(SELECT 1 FROM dbo.LegalEntities WHERE Id=1 AND IsDeleted=0)
 OR NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE Id=1 AND StoreId=1 AND LegalEntityId=1 AND IsDeleted=0)
    THROW 55604, 'Expected Store/LegalEntity/Warehouse mapping 1/1/1 is missing.', 1;
