BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072417_AddAdminMenuVisibility'
)
BEGIN
    CREATE TABLE [AdminMenuVisibilitySettings] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] int NULL,
        [UserInStoreId] int NULL,
        [HiddenMenuIdsJson] nvarchar(max) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_AdminMenuVisibilitySettings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AdminMenuVisibilitySettings_Target] CHECK (([RoleId] IS NOT NULL AND [UserInStoreId] IS NULL) OR ([RoleId] IS NULL AND [UserInStoreId] IS NOT NULL)),
        CONSTRAINT [FK_AdminMenuVisibilitySettings_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AdminMenuVisibilitySettings_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_AdminMenuVisibilitySettings_UserInStores_UserInStoreId] FOREIGN KEY ([UserInStoreId]) REFERENCES [UserInStores] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072417_AddAdminMenuVisibility'
)
BEGIN
    CREATE INDEX [IX_AdminMenuVisibilitySettings_RoleId] ON [AdminMenuVisibilitySettings] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072417_AddAdminMenuVisibility'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AdminMenuVisibilitySettings_StoreId_RoleId] ON [AdminMenuVisibilitySettings] ([StoreId], [RoleId]) WHERE [RoleId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072417_AddAdminMenuVisibility'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AdminMenuVisibilitySettings_StoreId_UserInStoreId] ON [AdminMenuVisibilitySettings] ([StoreId], [UserInStoreId]) WHERE [UserInStoreId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072417_AddAdminMenuVisibility'
)
BEGIN
    CREATE INDEX [IX_AdminMenuVisibilitySettings_UserInStoreId] ON [AdminMenuVisibilitySettings] ([UserInStoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001072417_AddAdminMenuVisibility'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001072417_AddAdminMenuVisibility', N'8.0.29');
END;
GO

COMMIT;
GO

