BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    ALTER TABLE [Warehouses] ADD [LegalEntityId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    ALTER TABLE [Stores] ADD [IsMultiLegalEntityEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    ALTER TABLE [Stores] ADD [MultiLegalEntityActivatedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    ALTER TABLE [Warehouses] ADD CONSTRAINT [AK_Warehouses_StoreId_Id] UNIQUE ([StoreId], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    ALTER TABLE [InvoiceProviderSettings] ADD CONSTRAINT [AK_InvoiceProviderSettings_StoreId_Id] UNIQUE ([StoreId], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    CREATE TABLE [LegalEntities] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [LegalName] nvarchar(300) NOT NULL,
        [TaxCode] nvarchar(50) NULL,
        [Address] nvarchar(1200) NULL,
        [Phone] nvarchar(30) NULL,
        [Email] nvarchar(320) NULL,
        [DefaultWarehouseId] int NULL,
        [InvoiceProviderSettingId] int NULL,
        [SalePriority] int NOT NULL,
        [IsDefaultForPurchase] bit NOT NULL DEFAULT CAST(0 AS bit),
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [Note] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_LegalEntities] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_LegalEntities_StoreId_Id] UNIQUE ([StoreId], [Id]),
        CONSTRAINT [CK_LegalEntities_SalePriority_Positive] CHECK ([SalePriority] > 0),
        CONSTRAINT [FK_LegalEntities_InvoiceProviderSettings_StoreId_InvoiceProviderSettingId] FOREIGN KEY ([StoreId], [InvoiceProviderSettingId]) REFERENCES [InvoiceProviderSettings] ([StoreId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LegalEntities_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_LegalEntities_Warehouses_StoreId_DefaultWarehouseId] FOREIGN KEY ([StoreId], [DefaultWarehouseId]) REFERENCES [Warehouses] ([StoreId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    INSERT INTO [LegalEntities]
    (
        [Code],
        [Name],
        [LegalName],
        [TaxCode],
        [Address],
        [Phone],
        [Email],
        [DefaultWarehouseId],
        [InvoiceProviderSettingId],
        [SalePriority],
        [IsDefaultForPurchase],
        [IsActive],
        [Note],
        [CreatedAtUtc],
        [CreatedBy],
        [UpdatedAtUtc],
        [UpdatedBy],
        [IsDeleted],
        [DeletedAtUtc],
        [DeletedBy],
        [StoreId]
    )
    SELECT
        N'PRIMARY',
        s.[Name],
        s.[Name],
        NULL,
        NULL,
        NULL,
        NULL,
        NULL,
        providerSetting.[Id],
        1,
        CASE WHEN s.[IsDeleted] = 0 AND s.[IsActive] = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
        CASE WHEN s.[IsDeleted] = 0 AND s.[IsActive] = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
        N'Bootstrap tự động Phase 22.1 từ Store hiện hữu. Multi LegalEntity vẫn tắt.',
        SYSUTCDATETIME(),
        NULL,
        NULL,
        NULL,
        s.[IsDeleted],
        s.[DeletedAtUtc],
        s.[DeletedBy],
        s.[Id]
    FROM [Stores] s
    OUTER APPLY
    (
        SELECT
            CASE WHEN COUNT_BIG(*) = 1 THEN MAX(ips.[Id]) ELSE NULL END AS [Id]
        FROM [InvoiceProviderSettings] ips
        WHERE ips.[StoreId] = s.[Id]
          AND ips.[IsDeleted] = 0
          AND ips.[IsActive] = 1
          AND UPPER(ips.[ProviderCode]) = N'VIETTEL'
    ) providerSetting
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM [LegalEntities] existing
        WHERE existing.[StoreId] = s.[Id]
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    UPDATE w
    SET w.[LegalEntityId] = le.[Id]
    FROM [Warehouses] w
    INNER JOIN [LegalEntities] le
        ON le.[StoreId] = w.[StoreId]
       AND le.[Code] = N'PRIMARY';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    UPDATE le
    SET le.[DefaultWarehouseId] = selectedWarehouse.[Id]
    FROM [LegalEntities] le
    OUTER APPLY
    (
        SELECT TOP (1) w.[Id]
        FROM [Warehouses] w
        WHERE w.[StoreId] = le.[StoreId]
          AND w.[LegalEntityId] = le.[Id]
        ORDER BY
            w.[IsDeleted] ASC,
            w.[IsDefault] DESC,
            w.[IsActive] DESC,
            w.[Id] ASC
    ) selectedWarehouse
    WHERE le.[Code] = N'PRIMARY';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND [c].[name] = N'LegalEntityId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Warehouses] ALTER COLUMN [LegalEntityId] int NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    CREATE INDEX [IX_Warehouses_StoreId_LegalEntityId] ON [Warehouses] ([StoreId], [LegalEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_LegalEntities_StoreId] ON [LegalEntities] ([StoreId]) WHERE [IsDefaultForPurchase] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_LegalEntities_StoreId_Code] ON [LegalEntities] ([StoreId], [Code]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_LegalEntities_StoreId_DefaultWarehouseId] ON [LegalEntities] ([StoreId], [DefaultWarehouseId]) WHERE [DefaultWarehouseId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    CREATE INDEX [IX_LegalEntities_StoreId_InvoiceProviderSettingId] ON [LegalEntities] ([StoreId], [InvoiceProviderSettingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    CREATE INDEX [IX_LegalEntities_StoreId_IsActive_IsDeleted] ON [LegalEntities] ([StoreId], [IsActive], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_LegalEntities_StoreId_SalePriority] ON [LegalEntities] ([StoreId], [SalePriority]) WHERE [IsActive] = 1 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_LegalEntities_StoreId_TaxCode] ON [LegalEntities] ([StoreId], [TaxCode]) WHERE [TaxCode] IS NOT NULL AND [TaxCode] <> '''' AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    ALTER TABLE [Warehouses] ADD CONSTRAINT [FK_Warehouses_LegalEntities_StoreId_LegalEntityId] FOREIGN KEY ([StoreId], [LegalEntityId]) REFERENCES [LegalEntities] ([StoreId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718080343_Phase221LegalEntityFoundation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260718080343_Phase221LegalEntityFoundation', N'8.0.29');
END;
GO

COMMIT;
GO

