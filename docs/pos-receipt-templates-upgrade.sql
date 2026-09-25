BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910002000_AddPosReceiptTemplates'
)
BEGIN
    CREATE TABLE [PosReceiptTemplates] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [DefinitionJson] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_PosReceiptTemplates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PosReceiptTemplates_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910002000_AddPosReceiptTemplates'
)
BEGIN
    CREATE INDEX [IX_PosReceiptTemplates_StoreId_IsDeleted] ON [PosReceiptTemplates] ([StoreId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910002000_AddPosReceiptTemplates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260910002000_AddPosReceiptTemplates', N'8.0.29');
END;
GO

COMMIT;
GO
