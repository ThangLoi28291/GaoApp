BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004122812_AddPendingSalesReturnRestock'
)
BEGIN
    CREATE TABLE [SalesReturnRestockFragments] (
        [Id] int NOT NULL IDENTITY,
        [SalesReturnLineId] int NOT NULL,
        [SourceValuationEntryId] int NOT NULL,
        [AllocationReversalId] int NULL,
        [BaseQuantity] decimal(18,4) NOT NULL,
        [InventoryTransactionId] int NULL,
        [CompletedAtUtc] datetime2 NULL,
        [CompletedByUserId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_SalesReturnRestockFragments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SalesReturnRestockFragments_Completion] CHECK (([InventoryTransactionId] IS NULL AND [CompletedAtUtc] IS NULL AND [CompletedByUserId] IS NULL) OR ([InventoryTransactionId] IS NOT NULL AND [CompletedAtUtc] IS NOT NULL AND [CompletedByUserId] IS NOT NULL)),
        CONSTRAINT [CK_SalesReturnRestockFragments_Quantity] CHECK ([BaseQuantity] > 0),
        CONSTRAINT [FK_SalesReturnRestockFragments_InventoryTransactions_InventoryTransactionId] FOREIGN KEY ([InventoryTransactionId]) REFERENCES [InventoryTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SalesReturnRestockFragments_InventoryValuationEntries_SourceValuationEntryId] FOREIGN KEY ([SourceValuationEntryId]) REFERENCES [InventoryValuationEntries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SalesReturnRestockFragments_OrderLegalEntityAllocationReversals_AllocationReversalId] FOREIGN KEY ([AllocationReversalId]) REFERENCES [OrderLegalEntityAllocationReversals] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SalesReturnRestockFragments_SalesReturnLines_SalesReturnLineId] FOREIGN KEY ([SalesReturnLineId]) REFERENCES [SalesReturnLines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SalesReturnRestockFragments_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004122812_AddPendingSalesReturnRestock'
)
BEGIN
    CREATE INDEX [IX_SalesReturnRestockFragments_AllocationReversalId] ON [SalesReturnRestockFragments] ([AllocationReversalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004122812_AddPendingSalesReturnRestock'
)
BEGIN
    CREATE INDEX [IX_SalesReturnRestockFragments_InventoryTransactionId] ON [SalesReturnRestockFragments] ([InventoryTransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004122812_AddPendingSalesReturnRestock'
)
BEGIN
    CREATE INDEX [IX_SalesReturnRestockFragments_SalesReturnLineId] ON [SalesReturnRestockFragments] ([SalesReturnLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004122812_AddPendingSalesReturnRestock'
)
BEGIN
    CREATE INDEX [IX_SalesReturnRestockFragments_SourceValuationEntryId] ON [SalesReturnRestockFragments] ([SourceValuationEntryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004122812_AddPendingSalesReturnRestock'
)
BEGIN
    CREATE INDEX [IX_SalesReturnRestockFragments_StoreId_CompletedAtUtc_IsDeleted] ON [SalesReturnRestockFragments] ([StoreId], [CompletedAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004122812_AddPendingSalesReturnRestock'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SalesReturnRestockFragments_StoreId_SalesReturnLineId_SourceValuationEntryId] ON [SalesReturnRestockFragments] ([StoreId], [SalesReturnLineId], [SourceValuationEntryId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004122812_AddPendingSalesReturnRestock'
)
BEGIN
    CREATE INDEX [IX_SalesReturnRestockFragments_StoreId_SourceValuationEntryId_IsDeleted] ON [SalesReturnRestockFragments] ([StoreId], [SourceValuationEntryId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004122812_AddPendingSalesReturnRestock'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004122812_AddPendingSalesReturnRestock', N'8.0.29');
END;
GO

COMMIT;
GO

