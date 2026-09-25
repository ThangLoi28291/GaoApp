BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908192844_AddAcbQrNotificationReconciliation'
)
BEGIN
    DROP INDEX [IX_AcbCallbackReceipts_StoreId_ClientRequestId_Page] ON [AcbCallbackReceipts];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908192844_AddAcbQrNotificationReconciliation'
)
BEGIN
    ALTER TABLE [AcbCallbackReceipts] ADD [RequestCode] nvarchar(30) NOT NULL DEFAULT N'TRANSACTION_UPDATE';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908192844_AddAcbQrNotificationReconciliation'
)
BEGIN
    ALTER TABLE [AcbCallbackReceipts] ADD [TotalPages] int NOT NULL DEFAULT 1;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908192844_AddAcbQrNotificationReconciliation'
)
BEGIN
    CREATE TABLE [AcbQrNotificationItems] (
        [Id] int NOT NULL IDENTITY,
        [ReceiptId] int NOT NULL,
        [Position] int NOT NULL,
        [ProviderOrderId] nvarchar(300) NOT NULL,
        [RequestCode] nvarchar(30) NOT NULL,
        [BusinessDate] date NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [TransactionStatus] nvarchar(30) NOT NULL,
        [DebitOrCredit] nvarchar(10) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_AcbQrNotificationItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AcbQrNotificationItems_AcbCallbackReceipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [AcbCallbackReceipts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AcbQrNotificationItems_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908192844_AddAcbQrNotificationReconciliation'
)
BEGIN

    UPDATE r SET TotalPages = COALESCE(TRY_CONVERT(int, JSON_VALUE(j.Payload, '$.requestParameters.request.requestParams.pagination.totalPage')), 1)
    FROM AcbCallbackReceipts r
    CROSS APPLY (SELECT CASE WHEN ISJSON(r.PayloadJson) = 1 THEN r.PayloadJson ELSE '{}' END AS Payload) j;

    INSERT INTO AcbQrNotificationItems
    (ReceiptId, Position, ProviderOrderId, RequestCode, BusinessDate, Amount, TransactionStatus, DebitOrCredit, Content,
     CreatedAtUtc, CreatedBy, IsDeleted, StoreId)
    SELECT r.Id, CONVERT(int, t.[key]), COALESCE(v.ProviderOrderId, ''), r.RequestCode, TRY_CONVERT(date, v.BusinessDate, 23),
     TRY_CONVERT(decimal(18,2), v.Amount), v.TransactionStatus, COALESCE(v.DebitOrCredit, ''), COALESCE(v.Content, ''),
     r.CreatedAtUtc, r.CreatedBy, r.IsDeleted, r.StoreId
    FROM AcbCallbackReceipts r
    CROSS APPLY OPENJSON(CASE WHEN ISJSON(r.PayloadJson) = 1 THEN r.PayloadJson ELSE '{}' END,
     '$.requestParameters.request.requestParams.transactions') t
    CROSS APPLY OPENJSON(t.value) WITH (
     ProviderOrderId nvarchar(max) '$.transactionEntityAttribute.custom4',
     BusinessDate nvarchar(100) '$.effectiveDate', Amount nvarchar(100) '$.amount',
     TransactionStatus nvarchar(100) '$.transactionStatus', DebitOrCredit nvarchar(100) '$.debitOrCredit',
     Content nvarchar(max) '$.transactionContent') v
    WHERE TRY_CONVERT(date, v.BusinessDate, 23) IS NOT NULL AND TRY_CONVERT(decimal(18,2), v.Amount) IS NOT NULL
     AND LEN(COALESCE(v.ProviderOrderId, '')) <= 300 AND LEN(COALESCE(v.DebitOrCredit, '')) <= 10
     AND v.TransactionStatus IN ('COMPLETED', 'ERRORCORRECTED');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908192844_AddAcbQrNotificationReconciliation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AcbCallbackReceipts_StoreId_RequestCode_ClientRequestId_Page] ON [AcbCallbackReceipts] ([StoreId], [RequestCode], [ClientRequestId], [Page]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908192844_AddAcbQrNotificationReconciliation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AcbQrNotificationItems_ReceiptId_Position] ON [AcbQrNotificationItems] ([ReceiptId], [Position]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908192844_AddAcbQrNotificationReconciliation'
)
BEGIN
    CREATE INDEX [IX_AcbQrNotificationItems_StoreId_BusinessDate_RequestCode_ProviderOrderId] ON [AcbQrNotificationItems] ([StoreId], [BusinessDate], [RequestCode], [ProviderOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908192844_AddAcbQrNotificationReconciliation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260908192844_AddAcbQrNotificationReconciliation', N'8.0.29');
END;
GO

COMMIT;
GO
