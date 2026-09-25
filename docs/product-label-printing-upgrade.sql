BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE TABLE [ProductLabelPrinters] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [WindowsPrinterName] nvarchar(220) NOT NULL,
        [Dpi] int NOT NULL,
        [PrintableWidthMm] decimal(8,2) NOT NULL,
        [OffsetXmm] decimal(8,2) NOT NULL,
        [OffsetYmm] decimal(8,2) NOT NULL,
        [Enabled] bit NOT NULL,
        [LastSeenAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_ProductLabelPrinters] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductLabelPrinters_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE TABLE [ProductLabelTasks] (
        [Id] int NOT NULL IDENTITY,
        [StockDocumentId] int NOT NULL,
        [DocumentNo] nvarchar(50) NOT NULL,
        [SourceHash] nvarchar(64) NOT NULL,
        [LinesJson] nvarchar(max) NOT NULL,
        [TemplateId] int NULL,
        [Completed] bit NOT NULL,
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
        CONSTRAINT [PK_ProductLabelTasks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductLabelTasks_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductLabelTasks_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE TABLE [ProductLabelTemplates] (
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
        CONSTRAINT [PK_ProductLabelTemplates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductLabelTemplates_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE TABLE [ProductLabelJobs] (
        [Id] int NOT NULL IDENTITY,
        [TaskId] int NULL,
        [PrinterId] int NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [Status] int NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [ResultJson] nvarchar(max) NOT NULL,
        [Quantity] int NOT NULL,
        [IsReprint] bit NOT NULL,
        [Reason] nvarchar(300) NOT NULL,
        [RequestedByName] nvarchar(200) NOT NULL,
        [SentAtUtc] datetime2 NULL,
        [ConfirmedAtUtc] datetime2 NULL,
        [ConfirmedByUserId] int NULL,
        [ConfirmedByName] nvarchar(200) NULL,
        [SpoolJobId] int NULL,
        [Error] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_ProductLabelJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductLabelJobs_ProductLabelPrinters_PrinterId] FOREIGN KEY ([PrinterId]) REFERENCES [ProductLabelPrinters] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductLabelJobs_ProductLabelTasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [ProductLabelTasks] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductLabelJobs_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE INDEX [IX_ProductLabelJobs_PrinterId] ON [ProductLabelJobs] ([PrinterId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE INDEX [IX_ProductLabelJobs_StoreId_PrinterId_Status_Id] ON [ProductLabelJobs] ([StoreId], [PrinterId], [Status], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductLabelJobs_StoreId_RequestId] ON [ProductLabelJobs] ([StoreId], [RequestId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductLabelJobs_StoreId_TaskId] ON [ProductLabelJobs] ([StoreId], [TaskId]) WHERE [TaskId] IS NOT NULL AND [Status] IN (0, 1, 2, 3)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE INDEX [IX_ProductLabelJobs_TaskId] ON [ProductLabelJobs] ([TaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductLabelPrinters_StoreId_WindowsPrinterName] ON [ProductLabelPrinters] ([StoreId], [WindowsPrinterName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE INDEX [IX_ProductLabelTasks_StockDocumentId] ON [ProductLabelTasks] ([StockDocumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductLabelTasks_StoreId_StockDocumentId] ON [ProductLabelTasks] ([StoreId], [StockDocumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    CREATE INDEX [IX_ProductLabelTemplates_StoreId_IsDeleted] ON [ProductLabelTemplates] ([StoreId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910040620_AddProductLabelPrinting'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260910040620_AddProductLabelPrinting', N'8.0.29');
END;
GO

COMMIT;
GO
