BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909015242_AddPosQrInstallmentLinks'
)
BEGIN
    ALTER TABLE [PosPaymentQrRequests] ADD [ClientRequestId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909015242_AddPosQrInstallmentLinks'
)
BEGIN
    ALTER TABLE [PosPaymentQrRequests] ADD [PaymentId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909015242_AddPosQrInstallmentLinks'
)
BEGIN
    ALTER TABLE [PosPaymentQrRequests] ADD [PrintClaimedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909015242_AddPosQrInstallmentLinks'
)
BEGIN
    CREATE INDEX [IX_PosPaymentQrRequests_PaymentId] ON [PosPaymentQrRequests] ([PaymentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909015242_AddPosQrInstallmentLinks'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PosPaymentQrRequests_StoreId_OrderId_ClientRequestId] ON [PosPaymentQrRequests] ([StoreId], [OrderId], [ClientRequestId]) WHERE [ClientRequestId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909015242_AddPosQrInstallmentLinks'
)
BEGIN
    ALTER TABLE [PosPaymentQrRequests] ADD CONSTRAINT [FK_PosPaymentQrRequests_OrderPayments_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [OrderPayments] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909015242_AddPosQrInstallmentLinks'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260909015242_AddPosQrInstallmentLinks', N'8.0.29');
END;
GO

COMMIT;
GO
