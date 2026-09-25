BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909024821_AddAcbConfirmationAudit'
)
BEGIN
    ALTER TABLE [AcbQrSessions] ADD [ConfirmationCallbackReceiptId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909024821_AddAcbConfirmationAudit'
)
BEGIN
    ALTER TABLE [AcbQrSessions] ADD [ConfirmationSource] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909024821_AddAcbConfirmationAudit'
)
BEGIN
    ALTER TABLE [AcbQrSessions] ADD [ConfirmedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909024821_AddAcbConfirmationAudit'
)
BEGIN
    ALTER TABLE [AcbQrSessions] ADD [ConfirmedByUserId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909024821_AddAcbConfirmationAudit'
)
BEGIN
    CREATE INDEX [IX_AcbQrSessions_ConfirmationCallbackReceiptId] ON [AcbQrSessions] ([ConfirmationCallbackReceiptId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909024821_AddAcbConfirmationAudit'
)
BEGIN
    ALTER TABLE [AcbQrSessions] ADD CONSTRAINT [FK_AcbQrSessions_AcbCallbackReceipts_ConfirmationCallbackReceiptId] FOREIGN KEY ([ConfirmationCallbackReceiptId]) REFERENCES [AcbCallbackReceipts] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909024821_AddAcbConfirmationAudit'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260909024821_AddAcbConfirmationAudit', N'8.0.29');
END;
GO

COMMIT;
GO
