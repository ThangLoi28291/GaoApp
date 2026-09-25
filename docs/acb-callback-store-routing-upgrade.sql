BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909062834_AddAcbCallbackStoreRouting'
)
BEGIN
    CREATE TABLE [AcbCallbackRoutes] (
        [Id] int NOT NULL IDENTITY,
        [Host] nvarchar(253) NOT NULL,
        [TargetStoreId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_AcbCallbackRoutes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AcbCallbackRoutes_Stores_TargetStoreId] FOREIGN KEY ([TargetStoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909062834_AddAcbCallbackStoreRouting'
)
BEGIN
    CREATE TABLE [AcbCallbackRouteChanges] (
        [Id] bigint NOT NULL IDENTITY,
        [RouteId] int NOT NULL,
        [PreviousStoreId] int NULL,
        [TargetStoreId] int NULL,
        [ActorUserId] int NOT NULL,
        [ChangedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AcbCallbackRouteChanges] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AcbCallbackRouteChanges_AcbCallbackRoutes_RouteId] FOREIGN KEY ([RouteId]) REFERENCES [AcbCallbackRoutes] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909062834_AddAcbCallbackStoreRouting'
)
BEGIN
    CREATE INDEX [IX_AcbCallbackRouteChanges_RouteId_ChangedAtUtc] ON [AcbCallbackRouteChanges] ([RouteId], [ChangedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909062834_AddAcbCallbackStoreRouting'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AcbCallbackRoutes_Host] ON [AcbCallbackRoutes] ([Host]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909062834_AddAcbCallbackStoreRouting'
)
BEGIN
    CREATE INDEX [IX_AcbCallbackRoutes_TargetStoreId] ON [AcbCallbackRoutes] ([TargetStoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909062834_AddAcbCallbackStoreRouting'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260909062834_AddAcbCallbackStoreRouting', N'8.0.29');
END;
GO

COMMIT;
GO
