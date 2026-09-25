BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914154923_AddPOSShiftCashReceipt'
)
BEGIN
    ALTER TABLE [POSShifts] ADD [CashReceiptNote] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914154923_AddPOSShiftCashReceipt'
)
BEGIN
    ALTER TABLE [POSShifts] ADD [CashReceivedAmount] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914154923_AddPOSShiftCashReceipt'
)
BEGIN
    ALTER TABLE [POSShifts] ADD [CashReceivedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914154923_AddPOSShiftCashReceipt'
)
BEGIN
    ALTER TABLE [POSShifts] ADD [CashReceivedByUserId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914154923_AddPOSShiftCashReceipt'
)
BEGIN
    CREATE INDEX [IX_POSShifts_StoreId_CashReceivedAtUtc] ON [POSShifts] ([StoreId], [CashReceivedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914154923_AddPOSShiftCashReceipt'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260914154923_AddPOSShiftCashReceipt', N'8.0.29');
END;
GO

COMMIT;
GO
