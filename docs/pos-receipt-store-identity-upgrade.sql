BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910012000_AddStoreReceiptIdentity'
)
BEGIN
    ALTER TABLE [Stores] ADD [ReceiptName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910012000_AddStoreReceiptIdentity'
)
BEGIN
    ALTER TABLE [Stores] ADD [ReceiptAddress] nvarchar(300) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910012000_AddStoreReceiptIdentity'
)
BEGIN
    ALTER TABLE [Stores] ADD [ReceiptPhone] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910012000_AddStoreReceiptIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260910012000_AddStoreReceiptIdentity', N'8.0.29');
END;
GO

COMMIT;
GO
