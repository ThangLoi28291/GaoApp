BEGIN TRANSACTION;
GO

ALTER TABLE [InvoiceHeads] ADD [LegacySourceId] bigint NULL;
GO

ALTER TABLE [InvoiceHeads] ADD [LegacySnapshotJson] nvarchar(max) NULL;
GO

ALTER TABLE [InvoiceHeads] ADD [LegacyImportedHash] binary(32) NULL;
GO

CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_LegacySourceId] ON [InvoiceHeads] ([StoreId], [LegacySourceId]) WHERE [LegacySourceId] IS NOT NULL;
GO

ALTER TABLE [InvoiceDetails] ADD [LegacySourceId] bigint NULL;
GO

ALTER TABLE [InvoiceDetails] ADD [LegacySnapshotJson] nvarchar(max) NULL;
GO

ALTER TABLE [InvoiceDetails] ADD [LegacyImportedHash] binary(32) NULL;
GO

CREATE UNIQUE INDEX [IX_InvoiceDetails_StoreId_LegacySourceId] ON [InvoiceDetails] ([StoreId], [LegacySourceId]) WHERE [LegacySourceId] IS NOT NULL;
GO

ALTER TABLE [InvoiceHeads] ADD [LegacyOrderCategoryId] bigint NULL;
GO

ALTER TABLE [InvoiceHeads] ADD [LegacyMergeId] nvarchar(15) NULL;
GO

ALTER TABLE [InvoiceHeads] ADD [LegacyReadOnly] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

ALTER TABLE [InvoiceDetails] ADD [LegacyUnitFactor] decimal(18,4) NULL;
GO

DROP INDEX [IX_InvoiceHeads_OrderId] ON [InvoiceHeads];
GO

DROP INDEX [IX_InvoiceHeads_StoreId_OrderId] ON [InvoiceHeads];
GO

DROP INDEX [IX_InvoiceHeads_StoreId_OrderId_LegalEntityId] ON [InvoiceHeads];
GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceHeads]') AND [c].[name] = N'OrderId');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceHeads] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [InvoiceHeads] ALTER COLUMN [OrderId] int NULL;
GO

CREATE INDEX [IX_InvoiceHeads_OrderId] ON [InvoiceHeads] ([OrderId]);
GO

CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_OrderId] ON [InvoiceHeads] ([StoreId], [OrderId]) WHERE [OrderId] IS NOT NULL AND [IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NULL;
GO

CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_OrderId_LegalEntityId] ON [InvoiceHeads] ([StoreId], [OrderId], [LegalEntityId]) WHERE [OrderId] IS NOT NULL AND [IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NOT NULL;
GO

ALTER TABLE [InvoiceHeads] ADD CONSTRAINT [CK_InvoiceHeads_LegacyOrder] CHECK ([OrderId] IS NOT NULL OR ([LegacySourceId] IS NOT NULL AND [LegacyReadOnly] = 1));
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260923160000_AddLegacyInvoiceImport', N'8.0.29');
GO

COMMIT;
GO
