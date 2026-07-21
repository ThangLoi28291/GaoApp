IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] int NOT NULL IDENTITY,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [Stores] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [SubDomain] nvarchar(60) NOT NULL,
        [SubDomainNormalized] nvarchar(60) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] varbinary(max) NOT NULL,
        CONSTRAINT [PK_Stores] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] int NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] int NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] int NOT NULL,
        [RoleId] int NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] int NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [Attribute] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Status] bit NOT NULL DEFAULT CAST(1 AS bit),
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_Attribute] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Attribute_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [Brands] (
        [Id] int NOT NULL IDENTITY,
        [Description] nvarchar(300) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_Brands] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Brands_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [Category] (
        [Id] int NOT NULL IDENTITY,
        [ParentId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [StoreId] int NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [SortOrder] int NOT NULL DEFAULT 0,
        CONSTRAINT [PK_Category] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Category_Category_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [Category] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Category_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [Suppliers] (
        [Id] int NOT NULL IDENTITY,
        [Phone] nvarchar(30) NULL,
        [Email] nvarchar(200) NULL,
        [Address] nvarchar(300) NULL,
        [ContactName] nvarchar(150) NULL,
        [TaxCode] nvarchar(50) NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_Suppliers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Suppliers_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [Taxes] (
        [Id] int NOT NULL IDENTITY,
        [Rate] decimal(5,2) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [StoreId] int NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_Taxes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Taxes_Rate_0_100] CHECK ([Rate] >= 0 AND [Rate] <= 100),
        CONSTRAINT [FK_Taxes_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [Unit] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [IsBase] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_Unit] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Unit_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [UserStores] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [RoleId] int NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_UserStores] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserStores_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [AttributeValue] (
        [Id] int NOT NULL IDENTITY,
        [AttributeId] int NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Status] bit NOT NULL DEFAULT CAST(1 AS bit),
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_AttributeValue] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AttributeValue_Attribute_AttributeId] FOREIGN KEY ([AttributeId]) REFERENCES [Attribute] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AttributeValue_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [Product] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [Alias] nvarchar(250) NOT NULL,
        [CategoryId] int NOT NULL,
        [SupplierId] int NOT NULL,
        [BaseUnitId] int NOT NULL,
        [BrandId] int NULL,
        [TaxId] int NULL,
        [BasePrice] decimal(18,2) NOT NULL,
        [Description] nvarchar(500) NULL,
        [Content] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_Product] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Product_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Product_Category_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Category] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Product_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_Product_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Product_Taxes_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [Taxes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Product_Unit_BaseUnitId] FOREIGN KEY ([BaseUnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [ProductVariant] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [Code] nvarchar(80) NOT NULL,
        [Barcode] nvarchar(80) NULL,
        [CostPrice] decimal(18,2) NOT NULL,
        [Price] decimal(18,2) NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_ProductVariant] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductVariant_Product_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Product] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProductVariant_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE TABLE [ProductVariantAttributeValue] (
        [Id] int NOT NULL IDENTITY,
        [VariantId] int NOT NULL,
        [AttributeId] int NOT NULL,
        [AttributeValueId] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_ProductVariantAttributeValue] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductVariantAttributeValue_AttributeValue_AttributeValueId] FOREIGN KEY ([AttributeValueId]) REFERENCES [AttributeValue] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProductVariantAttributeValue_Attribute_AttributeId] FOREIGN KEY ([AttributeId]) REFERENCES [Attribute] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProductVariantAttributeValue_ProductVariant_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProductVariantAttributeValue_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Attribute_StoreId_Code] ON [Attribute] ([StoreId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Attribute_StoreId_Name] ON [Attribute] ([StoreId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_AttributeValue_AttributeId] ON [AttributeValue] ([AttributeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AttributeValue_StoreId_AttributeId_Code] ON [AttributeValue] ([StoreId], [AttributeId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AttributeValue_StoreId_AttributeId_Name] ON [AttributeValue] ([StoreId], [AttributeId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Brands_StoreId_Code] ON [Brands] ([StoreId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Brands_StoreId_Name] ON [Brands] ([StoreId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_Category_ParentId] ON [Category] ([ParentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Category_StoreId_Code] ON [Category] ([StoreId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Category_StoreId_Name] ON [Category] ([StoreId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_Product_BaseUnitId] ON [Product] ([BaseUnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_Product_BrandId] ON [Product] ([BrandId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_Product_CategoryId] ON [Product] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Product_StoreId_Alias] ON [Product] ([StoreId], [Alias]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_Product_SupplierId] ON [Product] ([SupplierId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_Product_TaxId] ON [Product] ([TaxId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_ProductVariant_ProductId] ON [ProductVariant] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductVariant_StoreId_Barcode] ON [ProductVariant] ([StoreId], [Barcode]) WHERE [Barcode] IS NOT NULL AND [Barcode] <> ''''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVariant_StoreId_Code] ON [ProductVariant] ([StoreId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_ProductVariantAttributeValue_AttributeId] ON [ProductVariantAttributeValue] ([AttributeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_ProductVariantAttributeValue_AttributeValueId] ON [ProductVariantAttributeValue] ([AttributeValueId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE INDEX [IX_ProductVariantAttributeValue_StoreId] ON [ProductVariantAttributeValue] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVariantAttributeValue_VariantId_AttributeId] ON [ProductVariantAttributeValue] ([VariantId], [AttributeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVariantAttributeValue_VariantId_AttributeValueId] ON [ProductVariantAttributeValue] ([VariantId], [AttributeValueId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Stores_SubDomainNormalized] ON [Stores] ([SubDomainNormalized]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Suppliers_StoreId_Code] ON [Suppliers] ([StoreId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Suppliers_StoreId_Name] ON [Suppliers] ([StoreId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Taxes_StoreId_Code] ON [Taxes] ([StoreId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Taxes_StoreId_Name] ON [Taxes] ([StoreId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Unit_StoreId_Code] ON [Unit] ([StoreId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Unit_StoreId_Name] ON [Unit] ([StoreId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserStores_StoreId_UserId] ON [UserStores] ([StoreId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170044_Add_Productv'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251220170044_Add_Productv', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariant] DROP CONSTRAINT [FK_ProductVariant_Product_ProductId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariantAttributeValue] DROP CONSTRAINT [FK_ProductVariantAttributeValue_AttributeValue_AttributeValueId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariantAttributeValue] DROP CONSTRAINT [FK_ProductVariantAttributeValue_Attribute_AttributeId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariantAttributeValue] DROP CONSTRAINT [FK_ProductVariantAttributeValue_ProductVariant_VariantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    DROP INDEX [IX_ProductVariantAttributeValue_StoreId] ON [ProductVariantAttributeValue];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    DROP INDEX [IX_ProductVariantAttributeValue_VariantId_AttributeId] ON [ProductVariantAttributeValue];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    DROP INDEX [IX_ProductVariantAttributeValue_VariantId_AttributeValueId] ON [ProductVariantAttributeValue];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    DROP INDEX [IX_ProductVariant_StoreId_Barcode] ON [ProductVariant];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    DROP INDEX [IX_ProductVariant_StoreId_Code] ON [ProductVariant];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductVariant]') AND [c].[name] = N'Code');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [ProductVariant] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [ProductVariant] DROP COLUMN [Code];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductVariant]') AND [c].[name] = N'SortOrder');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [ProductVariant] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [ProductVariant] DROP COLUMN [SortOrder];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductVariant]') AND [c].[name] = N'Barcode');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [ProductVariant] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [ProductVariant] ALTER COLUMN [Barcode] nvarchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD [Sku] nvarchar(60) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    DROP INDEX [IX_Product_StoreId_Alias] ON [Product];
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Product]') AND [c].[name] = N'Alias');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Product] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [Product] ALTER COLUMN [Alias] nvarchar(200) NOT NULL;
    CREATE UNIQUE INDEX [IX_Product_StoreId_Alias] ON [Product] ([StoreId], [Alias]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVariantAttributeValue_StoreId_VariantId_AttributeId] ON [ProductVariantAttributeValue] ([StoreId], [VariantId], [AttributeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    CREATE INDEX [IX_ProductVariantAttributeValue_VariantId] ON [ProductVariantAttributeValue] ([VariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductVariant_StoreId_Barcode] ON [ProductVariant] ([StoreId], [Barcode]) WHERE [Barcode] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVariant_StoreId_Sku] ON [ProductVariant] ([StoreId], [Sku]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD CONSTRAINT [FK_ProductVariant_Product_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Product] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariantAttributeValue] ADD CONSTRAINT [FK_ProductVariantAttributeValue_AttributeValue_AttributeValueId] FOREIGN KEY ([AttributeValueId]) REFERENCES [AttributeValue] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariantAttributeValue] ADD CONSTRAINT [FK_ProductVariantAttributeValue_Attribute_AttributeId] FOREIGN KEY ([AttributeId]) REFERENCES [Attribute] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariantAttributeValue] ADD CONSTRAINT [FK_ProductVariantAttributeValue_ProductVariant_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251220170832_Add_Product_ProductVariant'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251220170832_Add_Product_ProductVariant', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    ALTER TABLE [ProductVariant] DROP CONSTRAINT [FK_ProductVariant_Product_ProductId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    DROP INDEX [IX_ProductVariant_StoreId_Barcode] ON [ProductVariant];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    DECLARE @var4 sysname;
    SELECT @var4 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Attribute]') AND [c].[name] = N'RowVersion');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Attribute] DROP CONSTRAINT [' + @var4 + '];');
    ALTER TABLE [Attribute] DROP COLUMN [RowVersion];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    ALTER TABLE [Attribute] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    DECLARE @var5 sysname;
    SELECT @var5 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AttributeValue]') AND [c].[name] = N'RowVersion');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [AttributeValue] DROP CONSTRAINT [' + @var5 + '];');
    ALTER TABLE [AttributeValue] DROP COLUMN [RowVersion];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    ALTER TABLE [AttributeValue] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    DECLARE @var6 sysname;
    SELECT @var6 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Category]') AND [c].[name] = N'RowVersion');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [Category] DROP CONSTRAINT [' + @var6 + '];');
    ALTER TABLE [Category] DROP COLUMN [RowVersion];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    ALTER TABLE [Category] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    DECLARE @var7 sysname;
    SELECT @var7 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Stores]') AND [c].[name] = N'RowVersion');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [Stores] DROP CONSTRAINT [' + @var7 + '];');
    ALTER TABLE [Stores] DROP COLUMN [RowVersion];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    ALTER TABLE [Stores] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    DECLARE @var8 sysname;
    SELECT @var8 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Taxes]') AND [c].[name] = N'RowVersion');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [Taxes] DROP CONSTRAINT [' + @var8 + '];');
    ALTER TABLE [Taxes] DROP COLUMN [RowVersion];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    ALTER TABLE [Taxes] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    DECLARE @var9 sysname;
    SELECT @var9 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UserStores]') AND [c].[name] = N'RowVersion');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [UserStores] DROP CONSTRAINT [' + @var9 + '];');
    ALTER TABLE [UserStores] DROP COLUMN [RowVersion];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    ALTER TABLE [UserStores] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD CONSTRAINT [FK_ProductVariant_Product_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Product] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221111616_FixRowVersionColumns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251221111616_FixRowVersionColumns', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221123158_Add_ProductVariantBarcodeHistory'
)
BEGIN
    CREATE TABLE [ProductVariantBarcodeHistory] (
        [Id] int NOT NULL IDENTITY,
        [ProductVariantId] int NOT NULL,
        [OldBarcode] nvarchar(32) NOT NULL,
        [NewBarcode] nvarchar(32) NOT NULL,
        [ChangedAtUtc] datetime2 NOT NULL,
        [ChangedBy] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ProductVariantBarcodeHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductVariantBarcodeHistory_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221123158_Add_ProductVariantBarcodeHistory'
)
BEGIN
    CREATE INDEX [IX_ProductVariantBarcodeHistory_OldBarcode] ON [ProductVariantBarcodeHistory] ([OldBarcode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221123158_Add_ProductVariantBarcodeHistory'
)
BEGIN
    CREATE INDEX [IX_ProductVariantBarcodeHistory_ProductVariantId] ON [ProductVariantBarcodeHistory] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221123158_Add_ProductVariantBarcodeHistory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251221123158_Add_ProductVariantBarcodeHistory', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221163109_Fix_ProductVariant_Unique_Filter_IsDeleted'
)
BEGIN
    DROP INDEX [IX_ProductVariant_StoreId_Sku] ON [ProductVariant];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221163109_Fix_ProductVariant_Unique_Filter_IsDeleted'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductVariant_StoreId_Barcode] ON [ProductVariant] ([StoreId], [Barcode]) WHERE [Barcode] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221163109_Fix_ProductVariant_Unique_Filter_IsDeleted'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductVariant_StoreId_Sku] ON [ProductVariant] ([StoreId], [Sku]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251221163109_Fix_ProductVariant_Unique_Filter_IsDeleted'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251221163109_Fix_ProductVariant_Unique_Filter_IsDeleted', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222034201_Add_TempFields_To_MediaAsset'
)
BEGIN
    CREATE TABLE [MediaAssets] (
        [Id] int NOT NULL IDENTITY,
        [StoragePath] nvarchar(260) NOT NULL,
        [OriginalFileName] nvarchar(260) NULL,
        [ContentType] nvarchar(100) NULL,
        [SizeBytes] bigint NOT NULL,
        [Sha256] nvarchar(64) NULL,
        [IsTemp] bit NOT NULL,
        [TempToken] nvarchar(80) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_MediaAssets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MediaAssets_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222034201_Add_TempFields_To_MediaAsset'
)
BEGIN
    CREATE TABLE [ProductImages] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [MediaAssetId] int NOT NULL,
        [IsPrimary] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [AltText] nvarchar(200) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_ProductImages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductImages_MediaAssets_MediaAssetId] FOREIGN KEY ([MediaAssetId]) REFERENCES [MediaAssets] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductImages_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222034201_Add_TempFields_To_MediaAsset'
)
BEGIN
    CREATE INDEX [IX_MediaAssets_StoreId_Id] ON [MediaAssets] ([StoreId], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222034201_Add_TempFields_To_MediaAsset'
)
BEGIN
    CREATE INDEX [IX_MediaAssets_StoreId_IsDeleted] ON [MediaAssets] ([StoreId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222034201_Add_TempFields_To_MediaAsset'
)
BEGIN
    CREATE INDEX [IX_ProductImages_MediaAssetId] ON [ProductImages] ([MediaAssetId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222034201_Add_TempFields_To_MediaAsset'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductImages_StoreId_ProductId] ON [ProductImages] ([StoreId], [ProductId]) WHERE [IsPrimary] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222034201_Add_TempFields_To_MediaAsset'
)
BEGIN
    CREATE INDEX [IX_ProductImages_StoreId_ProductId_IsPrimary] ON [ProductImages] ([StoreId], [ProductId], [IsPrimary]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222034201_Add_TempFields_To_MediaAsset'
)
BEGIN
    CREATE INDEX [IX_ProductImages_StoreId_ProductId_SortOrder] ON [ProductImages] ([StoreId], [ProductId], [SortOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222034201_Add_TempFields_To_MediaAsset'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251222034201_Add_TempFields_To_MediaAsset', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222154258_Add_TempFields_To_image'
)
BEGIN
    CREATE INDEX [IX_ProductImages_ProductId] ON [ProductImages] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222154258_Add_TempFields_To_image'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MediaAssets_StoreId_TempToken] ON [MediaAssets] ([StoreId], [TempToken]) WHERE [TempToken] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222154258_Add_TempFields_To_image'
)
BEGIN
    ALTER TABLE [ProductImages] ADD CONSTRAINT [FK_ProductImages_Product_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Product] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222154258_Add_TempFields_To_image'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251222154258_Add_TempFields_To_image', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222160811_Add_TempFields_To_imagefix'
)
BEGIN

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductImages_StoreId_ProductId' AND object_id = OBJECT_ID('dbo.ProductImages'))
    BEGIN
        DROP INDEX [IX_ProductImages_StoreId_ProductId] ON [dbo].[ProductImages];
    END

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222160811_Add_TempFields_To_imagefix'
)
BEGIN

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductImages_StoreId_ProductId_SortOrder' AND object_id = OBJECT_ID('dbo.ProductImages'))
    BEGIN
        CREATE INDEX [IX_ProductImages_StoreId_ProductId_SortOrder]
        ON [dbo].[ProductImages]([StoreId],[ProductId],[SortOrder]);
    END

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222160811_Add_TempFields_To_imagefix'
)
BEGIN

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductImages_StoreId_ProductId_IsPrimary' AND object_id = OBJECT_ID('dbo.ProductImages'))
    BEGIN
        CREATE INDEX [IX_ProductImages_StoreId_ProductId_IsPrimary]
        ON [dbo].[ProductImages]([StoreId],[ProductId],[IsPrimary]);
    END

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222160811_Add_TempFields_To_imagefix'
)
BEGIN

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ProductImages_Primary_PerProduct' AND object_id = OBJECT_ID('dbo.ProductImages'))
    BEGIN
        CREATE UNIQUE INDEX [UX_ProductImages_Primary_PerProduct]
        ON [dbo].[ProductImages]([StoreId],[ProductId])
        WHERE [IsPrimary] = 1;
    END

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222160811_Add_TempFields_To_imagefix'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251222160811_Add_TempFields_To_imagefix', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222161318_Fix_ProductImages_Primary_FilteredIndex'
)
BEGIN

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'UX_ProductImages_Primary_PerProduct'
          AND object_id = OBJECT_ID('dbo.ProductImages')
    )
    BEGIN
        DROP INDEX [UX_ProductImages_Primary_PerProduct] ON [dbo].[ProductImages];
    END

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222161318_Fix_ProductImages_Primary_FilteredIndex'
)
BEGIN

    CREATE UNIQUE INDEX [UX_ProductImages_Primary_PerProduct]
    ON [dbo].[ProductImages]([StoreId],[ProductId])
    WHERE [IsPrimary] = 1;

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222161318_Fix_ProductImages_Primary_FilteredIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251222161318_Fix_ProductImages_Primary_FilteredIndex', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251222173240_Fix_ProductImages_Indexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251222173240_Fix_ProductImages_Indexes', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251224023332_Add_MediaCreate'
)
BEGIN
    ALTER TABLE [MediaAssets] ADD [ExpireAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251224023332_Add_MediaCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251224023332_Add_MediaCreate', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251230080800_AddVariantPrimaryProductImage'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD [PrimaryProductImageId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251230080800_AddVariantPrimaryProductImage'
)
BEGIN
    CREATE INDEX [IX_ProductVariant_PrimaryProductImageId] ON [ProductVariant] ([PrimaryProductImageId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251230080800_AddVariantPrimaryProductImage'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductImages_StoreId_ProductId] ON [ProductImages] ([StoreId], [ProductId]) WHERE [IsPrimary] = 1 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251230080800_AddVariantPrimaryProductImage'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD CONSTRAINT [FK_ProductVariant_ProductImages_PrimaryProductImageId] FOREIGN KEY ([PrimaryProductImageId]) REFERENCES [ProductImages] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251230080800_AddVariantPrimaryProductImage'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251230080800_AddVariantPrimaryProductImage', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251231163031_barcode'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251231163031_barcode', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251231163558_barcode_1'
)
BEGIN
    DROP INDEX [IX_ProductVariant_StoreId_Sku] ON [ProductVariant];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251231163558_barcode_1'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVariant_StoreId_Sku] ON [ProductVariant] ([StoreId], [Sku]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251231163558_barcode_1'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251231163558_barcode_1', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260304081712_Phase4_Orders'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD [StoreId] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260304081712_Phase4_Orders'
)
BEGIN
    CREATE INDEX [IX_ProductVariantBarcodeHistory_StoreId] ON [ProductVariantBarcodeHistory] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260304081712_Phase4_Orders'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD CONSTRAINT [FK_ProductVariantBarcodeHistory_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260304081712_Phase4_Orders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260304081712_Phase4_Orders', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305150554_AddOrderTimeColumns'
)
BEGIN
    ALTER TABLE [Orders] ADD [CompletedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305150554_AddOrderTimeColumns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260305150554_AddOrderTimeColumns', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305151648_AddOrderTimeColumns1'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260305151648_AddOrderTimeColumns1', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305152400_AddOrderNumberSequences'
)
BEGIN
    CREATE TABLE [OrderNumberSequences] (
        [Id] int NOT NULL IDENTITY,
        [DateKey] nvarchar(8) NOT NULL,
        [LastNumber] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_OrderNumberSequences] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderNumberSequences_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305152400_AddOrderNumberSequences'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrderNumberSequences_StoreId_DateKey] ON [OrderNumberSequences] ([StoreId], [DateKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305152400_AddOrderNumberSequences'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260305152400_AddOrderNumberSequences', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305160557_Add_POSShifts'
)
BEGIN
    CREATE TABLE [POSShifts] (
        [Id] int NOT NULL IDENTITY,
        [OpenedByUserId] int NOT NULL,
        [OpenedAtUtc] datetime2 NOT NULL,
        [Status] int NOT NULL DEFAULT 1,
        [ShiftCode] nvarchar(30) NULL,
        [OpeningCash] decimal(18,2) NOT NULL,
        [CashSalesTotal] decimal(18,2) NOT NULL,
        [NonCashSalesTotal] decimal(18,2) NOT NULL,
        [CashInTotal] decimal(18,2) NOT NULL,
        [CashOutTotal] decimal(18,2) NOT NULL,
        [ClosingCashExpected] decimal(18,2) NOT NULL,
        [ClosingCashActual] decimal(18,2) NULL,
        [ClosedByUserId] int NULL,
        [ClosedAtUtc] datetime2 NULL,
        [Note] nvarchar(300) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_POSShifts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_POSShifts_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305160557_Add_POSShifts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShifts_StoreId_Status] ON [POSShifts] ([StoreId], [Status]) WHERE [Status] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305160557_Add_POSShifts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260305160557_Add_POSShifts', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305162815_Add_Order_POSShiftresume'
)
BEGIN
    ALTER TABLE [Orders] ADD [POSShiftId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305162815_Add_Order_POSShiftresume'
)
BEGIN

            INSERT INTO POSShifts
                (StoreId, OpenedByUserId, OpenedAtUtc, Status,
                 OpeningCash, CashSalesTotal, NonCashSalesTotal, CashInTotal, CashOutTotal, ClosingCashExpected,
                 ShiftCode, Note, IsDeleted)
            SELECT DISTINCT
                o.StoreId,
                0,
                SYSUTCDATETIME(),
                2,
                0,0,0,0,0,0,
                CONCAT('LEGACY-', o.StoreId),
                N'Shift hệ thống để gắn đơn cũ trước khi có POSShift',
                0
            FROM Orders o
            WHERE NOT EXISTS (
                SELECT 1 FROM POSShifts s
                WHERE s.StoreId = o.StoreId AND s.ShiftCode = CONCAT('LEGACY-', o.StoreId)
            );
        
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305162815_Add_Order_POSShiftresume'
)
BEGIN

            UPDATE o
            SET o.POSShiftId = s.Id
            FROM Orders o
            JOIN POSShifts s
              ON s.StoreId = o.StoreId
             AND s.ShiftCode = CONCAT('LEGACY-', o.StoreId)
            WHERE o.POSShiftId IS NULL;
        
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305162815_Add_Order_POSShiftresume'
)
BEGIN
    DECLARE @var10 sysname;
    SELECT @var10 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'POSShiftId');
    IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT [' + @var10 + '];');
    ALTER TABLE [Orders] ALTER COLUMN [POSShiftId] int NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305162815_Add_Order_POSShiftresume'
)
BEGIN
    CREATE INDEX [IX_Orders_StoreId_POSShiftId] ON [Orders] ([StoreId], [POSShiftId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305162815_Add_Order_POSShiftresume'
)
BEGIN
    CREATE INDEX [IX_Orders_POSShiftId] ON [Orders] ([POSShiftId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305162815_Add_Order_POSShiftresume'
)
BEGIN
    ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_POSShifts_POSShiftId] FOREIGN KEY ([POSShiftId]) REFERENCES [POSShifts] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305162815_Add_Order_POSShiftresume'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260305162815_Add_Order_POSShiftresume', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    ALTER TABLE [OrderLines] DROP CONSTRAINT [FK_OrderLines_Orders_OrderId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    ALTER TABLE [OrderPayments] DROP CONSTRAINT [FK_OrderPayments_Orders_OrderId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    ALTER TABLE [Orders] DROP CONSTRAINT [FK_Orders_Customers_CustomerId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    DROP INDEX [IX_POSShifts_StoreId_Status] ON [POSShifts];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN

    IF EXISTS (
        SELECT 1
        FROM sys.indexes i
        JOIN sys.tables t ON i.object_id = t.object_id
        WHERE i.name = 'IX_Orders_CompletedAtUtc' AND t.name = 'Orders'
    )
    BEGIN
        DROP INDEX [IX_Orders_CompletedAtUtc] ON [Orders];
    END

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    DROP INDEX [IX_Orders_StoreId_OrderNumber] ON [Orders];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    DROP INDEX [IX_Orders_StoreId_POSShiftId] ON [Orders];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    DROP INDEX [IX_MediaAssets_StoreId_TempToken] ON [MediaAssets];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    DECLARE @var11 sysname;
    SELECT @var11 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShifts]') AND [c].[name] = N'Status');
    IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [POSShifts] DROP CONSTRAINT [' + @var11 + '];');
    ALTER TABLE [POSShifts] ALTER COLUMN [Status] tinyint NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    DECLARE @var12 sysname;
    SELECT @var12 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'Status');
    IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT [' + @var12 + '];');
    ALTER TABLE [Orders] ALTER COLUMN [Status] tinyint NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    DECLARE @var13 sysname;
    SELECT @var13 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'PaymentStatus');
    IF @var13 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT [' + @var13 + '];');
    ALTER TABLE [Orders] ALTER COLUMN [PaymentStatus] tinyint NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShifts_StoreId_ShiftCode] ON [POSShifts] ([StoreId], [ShiftCode]) WHERE [ShiftCode] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    CREATE INDEX [IX_POSShifts_StoreId_Status] ON [POSShifts] ([StoreId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    CREATE INDEX [IX_Orders_StoreId_CompletedAtUtc] ON [Orders] ([StoreId], [CompletedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_StoreId_OrderNumber] ON [Orders] ([StoreId], [OrderNumber]) WHERE [OrderNumber] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    CREATE INDEX [IX_Orders_StoreId_POSShiftId_Status] ON [Orders] ([StoreId], [POSShiftId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    ALTER TABLE [OrderLines] ADD CONSTRAINT [FK_OrderLines_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    ALTER TABLE [OrderPayments] ADD CONSTRAINT [FK_OrderPayments_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260305165309_Add_Order_POSShiftresume2'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260305165309_Add_Order_POSShiftresume2', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260306143356_Add_POSShiftCashTransactions'
)
BEGIN
    CREATE TABLE [POSShiftCashTransactions] (
        [Id] int NOT NULL IDENTITY,
        [POSShiftId] int NOT NULL,
        [Type] tinyint NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Note] nvarchar(300) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_POSShiftCashTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_POSShiftCashTransactions_POSShifts_POSShiftId] FOREIGN KEY ([POSShiftId]) REFERENCES [POSShifts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_POSShiftCashTransactions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260306143356_Add_POSShiftCashTransactions'
)
BEGIN
    CREATE INDEX [IX_POSShiftCashTransactions_POSShiftId] ON [POSShiftCashTransactions] ([POSShiftId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260306143356_Add_POSShiftCashTransactions'
)
BEGIN
    CREATE INDEX [IX_POSShiftCashTransactions_StoreId_POSShiftId_CreatedAtUtc] ON [POSShiftCashTransactions] ([StoreId], [POSShiftId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260306143356_Add_POSShiftCashTransactions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260306143356_Add_POSShiftCashTransactions', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    DROP INDEX [IX_POSShiftCashTransactions_StoreId_POSShiftId_CreatedAtUtc] ON [POSShiftCashTransactions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    EXEC sp_rename N'[POSShifts].[Note]', N'OpenNote', N'COLUMN';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    DECLARE @var14 sysname;
    SELECT @var14 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShifts]') AND [c].[name] = N'OpeningCash');
    IF @var14 IS NOT NULL EXEC(N'ALTER TABLE [POSShifts] DROP CONSTRAINT [' + @var14 + '];');
    ALTER TABLE [POSShifts] ADD DEFAULT 0.0 FOR [OpeningCash];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    DECLARE @var15 sysname;
    SELECT @var15 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShifts]') AND [c].[name] = N'NonCashSalesTotal');
    IF @var15 IS NOT NULL EXEC(N'ALTER TABLE [POSShifts] DROP CONSTRAINT [' + @var15 + '];');
    ALTER TABLE [POSShifts] ADD DEFAULT 0.0 FOR [NonCashSalesTotal];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    DECLARE @var16 sysname;
    SELECT @var16 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShifts]') AND [c].[name] = N'ClosingCashExpected');
    IF @var16 IS NOT NULL EXEC(N'ALTER TABLE [POSShifts] DROP CONSTRAINT [' + @var16 + '];');
    ALTER TABLE [POSShifts] ADD DEFAULT 0.0 FOR [ClosingCashExpected];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    DECLARE @var17 sysname;
    SELECT @var17 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShifts]') AND [c].[name] = N'CashSalesTotal');
    IF @var17 IS NOT NULL EXEC(N'ALTER TABLE [POSShifts] DROP CONSTRAINT [' + @var17 + '];');
    ALTER TABLE [POSShifts] ADD DEFAULT 0.0 FOR [CashSalesTotal];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    DECLARE @var18 sysname;
    SELECT @var18 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShifts]') AND [c].[name] = N'CashOutTotal');
    IF @var18 IS NOT NULL EXEC(N'ALTER TABLE [POSShifts] DROP CONSTRAINT [' + @var18 + '];');
    ALTER TABLE [POSShifts] ADD DEFAULT 0.0 FOR [CashOutTotal];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    DECLARE @var19 sysname;
    SELECT @var19 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShifts]') AND [c].[name] = N'CashInTotal');
    IF @var19 IS NOT NULL EXEC(N'ALTER TABLE [POSShifts] DROP CONSTRAINT [' + @var19 + '];');
    ALTER TABLE [POSShifts] ADD DEFAULT 0.0 FOR [CashInTotal];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    ALTER TABLE [POSShifts] ADD [CloseNote] nvarchar(300) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    DECLARE @var20 sysname;
    SELECT @var20 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShiftCashTransactions]') AND [c].[name] = N'Type');
    IF @var20 IS NOT NULL EXEC(N'ALTER TABLE [POSShiftCashTransactions] DROP CONSTRAINT [' + @var20 + '];');
    ALTER TABLE [POSShiftCashTransactions] ALTER COLUMN [Type] int NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    DECLARE @var21 sysname;
    SELECT @var21 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShiftCashTransactions]') AND [c].[name] = N'Note');
    IF @var21 IS NOT NULL EXEC(N'ALTER TABLE [POSShiftCashTransactions] DROP CONSTRAINT [' + @var21 + '];');
    ALTER TABLE [POSShiftCashTransactions] ALTER COLUMN [Note] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    ALTER TABLE [POSShiftCashTransactions] ADD [CreatedByUserId] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    ALTER TABLE [POSShiftCashTransactions] ADD [Reason] nvarchar(300) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    CREATE INDEX [IX_POSShifts_StoreId_ClosedAtUtc] ON [POSShifts] ([StoreId], [ClosedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    CREATE INDEX [IX_POSShifts_StoreId_OpenedAtUtc] ON [POSShifts] ([StoreId], [OpenedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    EXEC(N'ALTER TABLE [POSShifts] ADD CONSTRAINT [CK_POSShifts_CashInTotal_NonNegative] CHECK ([CashInTotal] >= 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    EXEC(N'ALTER TABLE [POSShifts] ADD CONSTRAINT [CK_POSShifts_CashOutTotal_NonNegative] CHECK ([CashOutTotal] >= 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    EXEC(N'ALTER TABLE [POSShifts] ADD CONSTRAINT [CK_POSShifts_CashSalesTotal_NonNegative] CHECK ([CashSalesTotal] >= 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    EXEC(N'ALTER TABLE [POSShifts] ADD CONSTRAINT [CK_POSShifts_ClosingCashActual_NonNegative] CHECK ([ClosingCashActual] IS NULL OR [ClosingCashActual] >= 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    EXEC(N'ALTER TABLE [POSShifts] ADD CONSTRAINT [CK_POSShifts_ClosingCashExpected_NonNegative] CHECK ([ClosingCashExpected] >= 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    EXEC(N'ALTER TABLE [POSShifts] ADD CONSTRAINT [CK_POSShifts_NonCashSalesTotal_NonNegative] CHECK ([NonCashSalesTotal] >= 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    EXEC(N'ALTER TABLE [POSShifts] ADD CONSTRAINT [CK_POSShifts_OpeningCash_NonNegative] CHECK ([OpeningCash] >= 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    CREATE INDEX [IX_POSShiftCashTransactions_CreatedAtUtc] ON [POSShiftCashTransactions] ([CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    CREATE INDEX [IX_POSShiftCashTransactions_StoreId_POSShiftId] ON [POSShiftCashTransactions] ([StoreId], [POSShiftId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    CREATE INDEX [IX_POSShiftCashTransactions_StoreId_Type] ON [POSShiftCashTransactions] ([StoreId], [Type]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309153213_EditPOSShiftCashTransaction'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260309153213_EditPOSShiftCashTransaction', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309174417_AddHoldFieldsToOrder'
)
BEGIN
    ALTER TABLE [Orders] ADD [HeldAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309174417_AddHoldFieldsToOrder'
)
BEGIN
    ALTER TABLE [Orders] ADD [HoldCode] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309174417_AddHoldFieldsToOrder'
)
BEGIN
    ALTER TABLE [Orders] ADD [HoldNote] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260309174417_AddHoldFieldsToOrder'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260309174417_AddHoldFieldsToOrder', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260310024731_AddCurrentOrderToPOSShift'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260310024731_AddCurrentOrderToPOSShift', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260310154149_Add_OrderDiscount_To_Order'
)
BEGIN
    ALTER TABLE [Orders] ADD [OrderDiscount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260310154149_Add_OrderDiscount_To_Order'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260310154149_Add_OrderDiscount_To_Order', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311051816_AddPOSAuditLogs'
)
BEGIN
    CREATE TABLE [POSAuditLogs] (
        [Id] int NOT NULL IDENTITY,
        [Action] nvarchar(50) NOT NULL,
        [OrderId] int NULL,
        [UserId] int NULL,
        [Note] nvarchar(500) NULL,
        [MetadataJson] nvarchar(max) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_POSAuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_POSAuditLogs_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311051816_AddPOSAuditLogs'
)
BEGIN
    CREATE INDEX [IX_POSAuditLogs_StoreId] ON [POSAuditLogs] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311051816_AddPOSAuditLogs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260311051816_AddPOSAuditLogs', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE TABLE [Warehouses] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Location] nvarchar(255) NULL,
        [Note] nvarchar(500) NULL,
        [IsDefault] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_Warehouses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Warehouses_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE TABLE [InventoryBalances] (
        [Id] int NOT NULL IDENTITY,
        [WarehouseId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [OnHandQty] decimal(18,3) NOT NULL DEFAULT 0.0,
        [ReservedQty] decimal(18,3) NOT NULL DEFAULT 0.0,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InventoryBalances] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryBalances_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryBalances_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_InventoryBalances_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE TABLE [InventoryTransactions] (
        [Id] int NOT NULL IDENTITY,
        [WarehouseId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [TransactionType] int NOT NULL,
        [ReferenceType] int NOT NULL,
        [ReferenceId] nvarchar(100) NULL,
        [QuantityChange] decimal(18,3) NOT NULL,
        [BeforeQty] decimal(18,3) NOT NULL,
        [AfterQty] decimal(18,3) NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InventoryTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryTransactions_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryTransactions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_InventoryTransactions_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE INDEX [IX_InventoryBalances_ProductVariantId] ON [InventoryBalances] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE INDEX [IX_InventoryBalances_StoreId_ProductVariantId] ON [InventoryBalances] ([StoreId], [ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE INDEX [IX_InventoryBalances_StoreId_WarehouseId] ON [InventoryBalances] ([StoreId], [WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InventoryBalances_StoreId_WarehouseId_ProductVariantId] ON [InventoryBalances] ([StoreId], [WarehouseId], [ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE INDEX [IX_InventoryBalances_WarehouseId] ON [InventoryBalances] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_ProductVariantId] ON [InventoryTransactions] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_StoreId_ReferenceType_ReferenceId] ON [InventoryTransactions] ([StoreId], [ReferenceType], [ReferenceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_StoreId_TransactionType_OccurredAtUtc] ON [InventoryTransactions] ([StoreId], [TransactionType], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc] ON [InventoryTransactions] ([StoreId], [WarehouseId], [ProductVariantId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_WarehouseId] ON [InventoryTransactions] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Warehouses_StoreId_Code] ON [Warehouses] ([StoreId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Warehouses_StoreId_Name] ON [Warehouses] ([StoreId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311095320_Phase51_InventoryCore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260311095320_Phase51_InventoryCore', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311105357_Phase52_ProductUnitConversion_And_Barcode'
)
BEGIN
    CREATE TABLE [ProductUnitConversion] (
        [Id] int NOT NULL IDENTITY,
        [ProductVariantId] int NOT NULL,
        [UnitId] int NOT NULL,
        [Factor] decimal(18,4) NOT NULL,
        [IsBaseUnit] bit NOT NULL DEFAULT CAST(0 AS bit),
        [IsDefaultForSale] bit NOT NULL DEFAULT CAST(0 AS bit),
        [Price] decimal(18,2) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [SortOrder] int NOT NULL DEFAULT 0,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_ProductUnitConversion] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductUnitConversion_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductUnitConversion_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_ProductUnitConversion_Unit_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311105357_Phase52_ProductUnitConversion_And_Barcode'
)
BEGIN
    CREATE TABLE [ProductVariantUnitBarcode] (
        [Id] int NOT NULL IDENTITY,
        [ProductUnitConversionId] int NOT NULL,
        [Barcode] nvarchar(64) NOT NULL,
        [BarcodeType] int NOT NULL,
        [IsPrimary] bit NOT NULL DEFAULT CAST(1 AS bit),
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [Note] nvarchar(250) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_ProductVariantUnitBarcode] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductVariantUnitBarcode_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311105357_Phase52_ProductUnitConversion_And_Barcode'
)
BEGIN
    CREATE INDEX [IX_ProductUnitConversion_ProductVariantId] ON [ProductUnitConversion] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311105357_Phase52_ProductUnitConversion_And_Barcode'
)
BEGIN
    CREATE INDEX [IX_ProductUnitConversion_StoreId_ProductVariantId_IsActive] ON [ProductUnitConversion] ([StoreId], [ProductVariantId], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311105357_Phase52_ProductUnitConversion_And_Barcode'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductUnitConversion_StoreId_ProductVariantId_UnitId] ON [ProductUnitConversion] ([StoreId], [ProductVariantId], [UnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311105357_Phase52_ProductUnitConversion_And_Barcode'
)
BEGIN
    CREATE INDEX [IX_ProductUnitConversion_UnitId] ON [ProductUnitConversion] ([UnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311105357_Phase52_ProductUnitConversion_And_Barcode'
)
BEGIN
    CREATE INDEX [IX_ProductVariantUnitBarcode_ProductUnitConversionId] ON [ProductVariantUnitBarcode] ([ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311105357_Phase52_ProductUnitConversion_And_Barcode'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVariantUnitBarcode_StoreId_Barcode] ON [ProductVariantUnitBarcode] ([StoreId], [Barcode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311105357_Phase52_ProductUnitConversion_And_Barcode'
)
BEGIN
    CREATE INDEX [IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId_IsActive] ON [ProductVariantUnitBarcode] ([StoreId], [ProductUnitConversionId], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311105357_Phase52_ProductUnitConversion_And_Barcode'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260311105357_Phase52_ProductUnitConversion_And_Barcode', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311154459_Phase53_POS_And_Inventory'
)
BEGIN
    ALTER TABLE [Warehouses] ADD [AllowNegativeInventory] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311154459_Phase53_POS_And_Inventory'
)
BEGIN
    ALTER TABLE [POSShifts] ADD [WarehouseId] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311154459_Phase53_POS_And_Inventory'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [ReferenceLineId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311154459_Phase53_POS_And_Inventory'
)
BEGIN
    CREATE INDEX [IX_POSShifts_WarehouseId] ON [POSShifts] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311154459_Phase53_POS_And_Inventory'
)
BEGIN
    ALTER TABLE [POSShifts] ADD CONSTRAINT [FK_POSShifts_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311154459_Phase53_POS_And_Inventory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260311154459_Phase53_POS_And_Inventory', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [POSShifts] DROP CONSTRAINT [FK_POSShifts_Warehouses_WarehouseId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [BarcodeSource] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [BaseQuantity] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [BaseUnitId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [BaseUnitName] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [Multiplier] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [ScannedBarcode] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [SellingUnitId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [SellingUnitName] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    CREATE INDEX [IX_OrderLines_VariantId] ON [OrderLines] ([VariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [OrderLines] ADD CONSTRAINT [FK_OrderLines_ProductVariant_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    ALTER TABLE [POSShifts] ADD CONSTRAINT [FK_POSShifts_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260311180134_Phase53_POS_And_barcode'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260311180134_Phase53_POS_And_barcode', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE TABLE [StockDocument] (
        [Id] int NOT NULL IDENTITY,
        [DocumentNo] nvarchar(50) NOT NULL,
        [Type] int NOT NULL,
        [Status] int NOT NULL,
        [DocumentDate] datetime2 NOT NULL,
        [WarehouseId] int NOT NULL,
        [SupplierId] int NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [Note] nvarchar(1000) NULL,
        [ConfirmedAtUtc] datetime2 NULL,
        [ConfirmedByUserId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_StockDocument] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockDocument_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_StockDocument_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockDocument_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE TABLE [StockDocumentLine] (
        [Id] int NOT NULL IDENTITY,
        [StockDocumentId] int NOT NULL,
        [LineNo] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [UnitId] int NULL,
        [UnitNameSnapshot] nvarchar(100) NULL,
        [Factor] decimal(18,4) NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [BaseQuantity] decimal(18,3) NOT NULL,
        [UnitCost] decimal(18,2) NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        [ProductNameSnapshot] nvarchar(250) NOT NULL,
        [SkuSnapshot] nvarchar(100) NULL,
        [BarcodeSnapshot] nvarchar(100) NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_StockDocumentLine] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockDocumentLine_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockDocumentLine_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_StockDocumentLine_Unit_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockDocument_StoreId_DocumentNo] ON [StockDocument] ([StoreId], [DocumentNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE INDEX [IX_StockDocument_StoreId_Type_Status_DocumentDate] ON [StockDocument] ([StoreId], [Type], [Status], [DocumentDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE INDEX [IX_StockDocument_StoreId_WarehouseId_DocumentDate] ON [StockDocument] ([StoreId], [WarehouseId], [DocumentDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE INDEX [IX_StockDocument_SupplierId] ON [StockDocument] ([SupplierId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE INDEX [IX_StockDocument_WarehouseId] ON [StockDocument] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE INDEX [IX_StockDocumentLine_ProductVariantId] ON [StockDocumentLine] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE INDEX [IX_StockDocumentLine_StockDocumentId] ON [StockDocumentLine] ([StockDocumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockDocumentLine_StockDocumentId_LineNo] ON [StockDocumentLine] ([StockDocumentId], [LineNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    CREATE INDEX [IX_StockDocumentLine_UnitId] ON [StockDocumentLine] ([UnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312043956_AddStockDocumentPhase54'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260312043956_AddStockDocumentPhase54', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312073456_UpgradeStockDocumentApprovalWorkflow'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [ApprovalNote] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312073456_UpgradeStockDocumentApprovalWorkflow'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [ApprovedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312073456_UpgradeStockDocumentApprovalWorkflow'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [ApprovedByUserId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312073456_UpgradeStockDocumentApprovalWorkflow'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [SubmittedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312073456_UpgradeStockDocumentApprovalWorkflow'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [SubmittedByUserId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312073456_UpgradeStockDocumentApprovalWorkflow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260312073456_UpgradeStockDocumentApprovalWorkflow', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313143651_NegativeInventoryLogPhase55'
)
BEGIN
    CREATE TABLE [NegativeInventoryLog] (
        [Id] int NOT NULL IDENTITY,
        [WarehouseId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [BeforeQty] decimal(18,3) NOT NULL,
        [QuantityChange] decimal(18,3) NOT NULL,
        [AfterQty] decimal(18,3) NOT NULL,
        [TransactionType] int NOT NULL,
        [ReferenceType] int NOT NULL,
        [ReferenceId] nvarchar(100) NULL,
        [ReferenceLineId] int NULL,
        [Note] nvarchar(500) NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_NegativeInventoryLog] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NegativeInventoryLog_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_NegativeInventoryLog_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_NegativeInventoryLog_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313143651_NegativeInventoryLogPhase55'
)
BEGIN
    CREATE INDEX [IX_NegativeInventoryLog_ProductVariantId] ON [NegativeInventoryLog] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313143651_NegativeInventoryLogPhase55'
)
BEGIN
    CREATE INDEX [IX_NegativeInventoryLog_Store_Warehouse_Variant_OccurredAt] ON [NegativeInventoryLog] ([StoreId], [WarehouseId], [ProductVariantId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313143651_NegativeInventoryLogPhase55'
)
BEGIN
    CREATE INDEX [IX_NegativeInventoryLog_WarehouseId] ON [NegativeInventoryLog] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313143651_NegativeInventoryLogPhase55'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260313143651_NegativeInventoryLogPhase55', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    CREATE TABLE [StockCountDocument] (
        [Id] int NOT NULL IDENTITY,
        [WarehouseId] int NOT NULL,
        [DocumentNo] nvarchar(50) NOT NULL,
        [DocumentDate] datetime2 NOT NULL,
        [Status] int NOT NULL,
        [Note] nvarchar(1000) NULL,
        [ConfirmedAtUtc] datetime2 NULL,
        [ConfirmedByUserId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_StockCountDocument] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockCountDocument_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_StockCountDocument_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    CREATE TABLE [StockCountLine] (
        [Id] int NOT NULL IDENTITY,
        [StockCountDocumentId] int NOT NULL,
        [LineNo] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [UnitId] int NOT NULL,
        [UnitNameSnapshot] nvarchar(100) NULL,
        [Factor] decimal(18,3) NOT NULL,
        [SystemQtyBase] decimal(18,3) NOT NULL,
        [CountedQty] decimal(18,3) NOT NULL,
        [CountedQtyBase] decimal(18,3) NOT NULL,
        [DifferenceQtyBase] decimal(18,3) NOT NULL,
        [ProductNameSnapshot] nvarchar(250) NOT NULL,
        [SkuSnapshot] nvarchar(100) NULL,
        [BarcodeSnapshot] nvarchar(100) NULL,
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
        CONSTRAINT [PK_StockCountLine] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockCountLine_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockCountLine_StockCountDocument_StockCountDocumentId] FOREIGN KEY ([StockCountDocumentId]) REFERENCES [StockCountDocument] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_StockCountLine_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_StockCountLine_Unit_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockCountDocument_StoreId_DocumentNo] ON [StockCountDocument] ([StoreId], [DocumentNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    CREATE INDEX [IX_StockCountDocument_StoreId_WarehouseId_Status_DocumentDate] ON [StockCountDocument] ([StoreId], [WarehouseId], [Status], [DocumentDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    CREATE INDEX [IX_StockCountDocument_WarehouseId] ON [StockCountDocument] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    CREATE INDEX [IX_StockCountLine_ProductVariantId] ON [StockCountLine] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockCountLine_StockCountDocumentId_LineNo] ON [StockCountLine] ([StockCountDocumentId], [LineNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    CREATE INDEX [IX_StockCountLine_StockCountDocumentId_ProductVariantId] ON [StockCountLine] ([StockCountDocumentId], [ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    CREATE INDEX [IX_StockCountLine_StoreId] ON [StockCountLine] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    CREATE INDEX [IX_StockCountLine_UnitId] ON [StockCountLine] ([UnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314061609_AddStockCountDocumentAndLine'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260314061609_AddStockCountDocumentAndLine', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314174617_AddInventoryPhase5Indexes'
)
BEGIN
    CREATE INDEX [IX_StockDocument_Status] ON [StockDocument] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260314174617_AddInventoryPhase5Indexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260314174617_AddInventoryPhase5Indexes', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE TABLE [StockTransferDocument] (
        [Id] int NOT NULL IDENTITY,
        [DocumentNo] nvarchar(50) NOT NULL,
        [DocumentDate] datetime2 NOT NULL,
        [FromWarehouseId] int NOT NULL,
        [ToWarehouseId] int NOT NULL,
        [Status] int NOT NULL,
        [Note] nvarchar(1000) NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [SubmittedByUserId] int NULL,
        [ApprovedAtUtc] datetime2 NULL,
        [ApprovedByUserId] int NULL,
        [ConfirmedAtUtc] datetime2 NULL,
        [ConfirmedByUserId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_StockTransferDocument] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockTransferDocument_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_StockTransferDocument_Warehouses_FromWarehouseId] FOREIGN KEY ([FromWarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockTransferDocument_Warehouses_ToWarehouseId] FOREIGN KEY ([ToWarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE TABLE [StockTransferLine] (
        [Id] int NOT NULL IDENTITY,
        [StockTransferDocumentId] int NOT NULL,
        [LineNo] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [UnitId] int NOT NULL,
        [UnitNameSnapshot] nvarchar(100) NOT NULL,
        [Factor] decimal(18,4) NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [BaseQuantity] decimal(18,3) NOT NULL,
        [ProductNameSnapshot] nvarchar(250) NOT NULL,
        [SkuSnapshot] nvarchar(60) NULL,
        [BarcodeSnapshot] nvarchar(32) NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_StockTransferLine] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockTransferLine_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockTransferLine_StockTransferDocument_StockTransferDocumentId] FOREIGN KEY ([StockTransferDocumentId]) REFERENCES [StockTransferDocument] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_StockTransferLine_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE INDEX [IX_StockTransferDocument_FromWarehouseId] ON [StockTransferDocument] ([FromWarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE INDEX [IX_StockTransferDocument_StoreId_DocumentDate_Status] ON [StockTransferDocument] ([StoreId], [DocumentDate], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockTransferDocument_StoreId_DocumentNo] ON [StockTransferDocument] ([StoreId], [DocumentNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE INDEX [IX_StockTransferDocument_StoreId_FromWarehouseId_DocumentDate] ON [StockTransferDocument] ([StoreId], [FromWarehouseId], [DocumentDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE INDEX [IX_StockTransferDocument_StoreId_ToWarehouseId_DocumentDate] ON [StockTransferDocument] ([StoreId], [ToWarehouseId], [DocumentDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE INDEX [IX_StockTransferDocument_ToWarehouseId] ON [StockTransferDocument] ([ToWarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE INDEX [IX_StockTransferLine_ProductVariantId] ON [StockTransferLine] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockTransferLine_StockTransferDocumentId_LineNo] ON [StockTransferLine] ([StockTransferDocumentId], [LineNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    CREATE INDEX [IX_StockTransferLine_StoreId_ProductVariantId] ON [StockTransferLine] ([StoreId], [ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315071304_AddStockTransferDocument'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260315071304_AddStockTransferDocument', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315122531_AddInventoryReservation'
)
BEGIN
    ALTER TABLE [Orders] ADD [HasReservation] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315122531_AddInventoryReservation'
)
BEGIN
    ALTER TABLE [Orders] ADD [ReservedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315122531_AddInventoryReservation'
)
BEGIN
    CREATE TABLE [InventoryReservations] (
        [Id] int NOT NULL IDENTITY,
        [WarehouseId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [ReferenceType] int NOT NULL,
        [ReferenceId] nvarchar(50) NOT NULL,
        [ReferenceLineId] int NULL,
        [ReservedQty] decimal(18,3) NOT NULL DEFAULT 0.0,
        [Status] int NOT NULL,
        [Note] nvarchar(500) NULL,
        [ReservedAtUtc] datetime2 NOT NULL,
        [ReleasedAtUtc] datetime2 NULL,
        [ReleaseNote] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InventoryReservations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryReservations_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryReservations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_InventoryReservations_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315122531_AddInventoryReservation'
)
BEGIN
    CREATE INDEX [IX_InventoryReservations_ProductVariantId] ON [InventoryReservations] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315122531_AddInventoryReservation'
)
BEGIN
    CREATE INDEX [IX_InventoryReservations_StoreId_ReferenceType_ReferenceId] ON [InventoryReservations] ([StoreId], [ReferenceType], [ReferenceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315122531_AddInventoryReservation'
)
BEGIN
    CREATE INDEX [IX_InventoryReservations_StoreId_ReferenceType_ReferenceId_ReferenceLineId_WarehouseId_ProductVariantId_Status] ON [InventoryReservations] ([StoreId], [ReferenceType], [ReferenceId], [ReferenceLineId], [WarehouseId], [ProductVariantId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315122531_AddInventoryReservation'
)
BEGIN
    CREATE INDEX [IX_InventoryReservations_StoreId_WarehouseId_ProductVariantId_Status] ON [InventoryReservations] ([StoreId], [WarehouseId], [ProductVariantId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315122531_AddInventoryReservation'
)
BEGIN
    CREATE INDEX [IX_InventoryReservations_WarehouseId] ON [InventoryReservations] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260315122531_AddInventoryReservation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260315122531_AddInventoryReservation', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    ALTER TABLE [UserStores] DROP CONSTRAINT [FK_UserStores_Stores_StoreId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    ALTER TABLE [UserStores] DROP CONSTRAINT [PK_UserStores];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    EXEC sp_rename N'[UserStores]', N'UserInStores';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    EXEC sp_rename N'[UserInStores].[IX_UserStores_StoreId_UserId]', N'IX_UserInStores_StoreId_UserId', N'INDEX';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    ALTER TABLE [UserInStores] ADD CONSTRAINT [PK_UserInStores] PRIMARY KEY ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE TABLE [Permissions] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(150) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [GroupName] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(100) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [IsSystemRole] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Roles_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] int NOT NULL,
        [PermissionId] int NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE INDEX [IX_UserInStores_RoleId] ON [UserInStores] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE INDEX [IX_UserInStores_StoreId_RoleId] ON [UserInStores] ([StoreId], [RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE INDEX [IX_UserInStores_UserId_StoreId_IsActive] ON [UserInStores] ([UserId], [StoreId], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Code] ON [Permissions] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RolePermissions_RoleId_PermissionId] ON [RolePermissions] ([RoleId], [PermissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_StoreId_Code] ON [Roles] ([StoreId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    CREATE INDEX [IX_Roles_StoreId_Name] ON [Roles] ([StoreId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    ALTER TABLE [UserInStores] ADD CONSTRAINT [FK_UserInStores_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    ALTER TABLE [UserInStores] ADD CONSTRAINT [FK_UserInStores_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316094525_RolePermission'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260316094525_RolePermission', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316120318_AddUserAndUserInStore'
)
BEGIN
    DROP INDEX [IX_UserInStores_UserId_StoreId_IsActive] ON [UserInStores];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316120318_AddUserAndUserInStore'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] int NOT NULL IDENTITY,
        [UserName] nvarchar(100) NOT NULL,
        [FullName] nvarchar(200) NULL,
        [Email] nvarchar(200) NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [IsActive] bit NOT NULL,
        [IsHostAdmin] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316120318_AddUserAndUserInStore'
)
BEGIN
    CREATE INDEX [IX_UserInStores_StoreId_IsActive] ON [UserInStores] ([StoreId], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316120318_AddUserAndUserInStore'
)
BEGIN
    CREATE INDEX [IX_UserInStores_UserId] ON [UserInStores] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316120318_AddUserAndUserInStore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]) WHERE [Email] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316120318_AddUserAndUserInStore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_UserName] ON [Users] ([UserName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316120318_AddUserAndUserInStore'
)
BEGIN
    ALTER TABLE [UserInStores] ADD CONSTRAINT [FK_UserInStores_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316120318_AddUserAndUserInStore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260316120318_AddUserAndUserInStore', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316152149_Addauditlog'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [StoreId] int NULL,
        [ActorUserId] int NULL,
        [ActorUserName] nvarchar(200) NULL,
        [Module] int NOT NULL,
        [ActionType] int NOT NULL,
        [EntityName] nvarchar(150) NULL,
        [EntityId] nvarchar(100) NULL,
        [EntityDisplay] nvarchar(300) NULL,
        [Summary] nvarchar(1000) NULL,
        [OldValuesJson] nvarchar(max) NULL,
        [NewValuesJson] nvarchar(max) NULL,
        [ChangedColumnsJson] nvarchar(max) NULL,
        [TraceId] nvarchar(100) NULL,
        [IpAddress] nvarchar(100) NULL,
        [UserAgent] nvarchar(2000) NULL,
        [Path] nvarchar(500) NULL,
        [IsSuccess] bit NOT NULL,
        [ErrorMessage] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316152149_Addauditlog'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityName_EntityId_CreatedAtUtc] ON [AuditLogs] ([EntityName], [EntityId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316152149_Addauditlog'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_StoreId_ActorUserId_CreatedAtUtc] ON [AuditLogs] ([StoreId], [ActorUserId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316152149_Addauditlog'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_StoreId_CreatedAtUtc] ON [AuditLogs] ([StoreId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316152149_Addauditlog'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_StoreId_Module_ActionType_CreatedAtUtc] ON [AuditLogs] ([StoreId], [Module], [ActionType], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316152149_Addauditlog'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TraceId] ON [AuditLogs] ([TraceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316152149_Addauditlog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260316152149_Addauditlog', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316160134_FixAuditLogStoreRequired'
)
BEGIN
    DROP INDEX [IX_AuditLogs_StoreId_ActorUserId_CreatedAtUtc] ON [AuditLogs];
    DROP INDEX [IX_AuditLogs_StoreId_CreatedAtUtc] ON [AuditLogs];
    DROP INDEX [IX_AuditLogs_StoreId_Module_ActionType_CreatedAtUtc] ON [AuditLogs];
    DECLARE @var22 sysname;
    SELECT @var22 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AuditLogs]') AND [c].[name] = N'StoreId');
    IF @var22 IS NOT NULL EXEC(N'ALTER TABLE [AuditLogs] DROP CONSTRAINT [' + @var22 + '];');
    EXEC(N'UPDATE [AuditLogs] SET [StoreId] = 0 WHERE [StoreId] IS NULL');
    ALTER TABLE [AuditLogs] ALTER COLUMN [StoreId] int NOT NULL;
    ALTER TABLE [AuditLogs] ADD DEFAULT 0 FOR [StoreId];
    CREATE INDEX [IX_AuditLogs_StoreId_ActorUserId_CreatedAtUtc] ON [AuditLogs] ([StoreId], [ActorUserId], [CreatedAtUtc]);
    CREATE INDEX [IX_AuditLogs_StoreId_CreatedAtUtc] ON [AuditLogs] ([StoreId], [CreatedAtUtc]);
    CREATE INDEX [IX_AuditLogs_StoreId_Module_ActionType_CreatedAtUtc] ON [AuditLogs] ([StoreId], [Module], [ActionType], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316160134_FixAuditLogStoreRequired'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_StoreId] ON [AuditLogs] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316160134_FixAuditLogStoreRequired'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260316160134_FixAuditLogStoreRequired', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariantUnitBarcode] DROP CONSTRAINT [FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    DROP INDEX [IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId_IsActive] ON [ProductVariantUnitBarcode];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    DROP INDEX [IX_ProductVariant_StoreId_Barcode] ON [ProductVariant];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    DECLARE @var23 sysname;
    SELECT @var23 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductVariant]') AND [c].[name] = N'Barcode');
    IF @var23 IS NOT NULL EXEC(N'ALTER TABLE [ProductVariant] DROP CONSTRAINT [' + @var23 + '];');
    ALTER TABLE [ProductVariant] DROP COLUMN [Barcode];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    DECLARE @var24 sysname;
    SELECT @var24 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductVariant]') AND [c].[name] = N'IsActive');
    IF @var24 IS NOT NULL EXEC(N'ALTER TABLE [ProductVariant] DROP CONSTRAINT [' + @var24 + '];');
    ALTER TABLE [ProductVariant] ADD DEFAULT CAST(1 AS bit) FOR [IsActive];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    CREATE INDEX [IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId] ON [ProductVariantUnitBarcode] ([StoreId], [ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    CREATE INDEX [IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId_IsPrimary] ON [ProductVariantUnitBarcode] ([StoreId], [ProductUnitConversionId], [IsPrimary]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    CREATE INDEX [IX_ProductUnitConversion_StoreId_ProductVariantId_IsBaseUnit] ON [ProductUnitConversion] ([StoreId], [ProductVariantId], [IsBaseUnit]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    CREATE INDEX [IX_ProductUnitConversion_StoreId_ProductVariantId_IsDefaultForSale] ON [ProductUnitConversion] ([StoreId], [ProductVariantId], [IsDefaultForSale]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariantUnitBarcode] ADD CONSTRAINT [FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316174756_FixRemoveBarCodeOfProductVariant'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260316174756_FixRemoveBarCodeOfProductVariant', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317100755_editProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD [IsLocked] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317100755_editProductVariant'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260317100755_editProductVariant', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317103851_editProductVariant1'
)
BEGIN
    DECLARE @var25 sysname;
    SELECT @var25 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductVariant]') AND [c].[name] = N'IsLocked');
    IF @var25 IS NOT NULL EXEC(N'ALTER TABLE [ProductVariant] DROP CONSTRAINT [' + @var25 + '];');
    ALTER TABLE [ProductVariant] DROP COLUMN [IsLocked];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317103851_editProductVariant1'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260317103851_editProductVariant1', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317135046_FixProductVariant'
)
BEGIN
    DROP INDEX [IX_ProductVariant_StoreId_Sku] ON [ProductVariant];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317135046_FixProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD [ProductVariantName] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317135046_FixProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD [ProductVariantNameNormalized] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317135046_FixProductVariant'
)
BEGIN
    CREATE INDEX [IX_ProductVariant_Store_ProductVariantName] ON [ProductVariant] ([StoreId], [IsDeleted], [IsActive], [ProductVariantName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317135046_FixProductVariant'
)
BEGIN
    CREATE INDEX [IX_ProductVariant_Store_ProductVariantNameNormalized] ON [ProductVariant] ([StoreId], [IsDeleted], [IsActive], [ProductVariantNameNormalized]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317135046_FixProductVariant'
)
BEGIN
    CREATE INDEX [IX_ProductVariant_Store_Sku] ON [ProductVariant] ([StoreId], [IsDeleted], [IsActive], [Sku]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317135046_FixProductVariant'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ProductVariant_Store_Product_Sku] ON [ProductVariant] ([StoreId], [ProductId], [Sku]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260317135046_FixProductVariant'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260317135046_FixProductVariant', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] DROP CONSTRAINT [FK_ProductVariantBarcodeHistory_ProductVariant_ProductVariantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantUnitBarcode] DROP CONSTRAINT [FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    DROP INDEX [IX_ProductVariantUnitBarcode_ProductUnitConversionId] ON [ProductVariantUnitBarcode];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    DROP INDEX [IX_ProductVariantUnitBarcode_StoreId_Barcode] ON [ProductVariantUnitBarcode];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    DROP INDEX [IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId_IsPrimary] ON [ProductVariantUnitBarcode];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    DROP INDEX [IX_ProductVariantBarcodeHistory_OldBarcode] ON [ProductVariantBarcodeHistory];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    DROP INDEX [IX_ProductVariantBarcodeHistory_ProductVariantId] ON [ProductVariantBarcodeHistory];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    DROP INDEX [IX_ProductVariantBarcodeHistory_StoreId] ON [ProductVariantBarcodeHistory];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    EXEC sp_rename N'[ProductVariantUnitBarcode].[IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId]', N'IX_ProductVariantUnitBarcode_Store_Conversion', N'INDEX';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    EXEC sp_rename N'[ProductVariantBarcodeHistory].[ChangedBy]', N'OldBarcodeId', N'COLUMN';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    DECLARE @var26 sysname;
    SELECT @var26 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductVariantBarcodeHistory]') AND [c].[name] = N'OldBarcode');
    IF @var26 IS NOT NULL EXEC(N'ALTER TABLE [ProductVariantBarcodeHistory] DROP CONSTRAINT [' + @var26 + '];');
    ALTER TABLE [ProductVariantBarcodeHistory] ALTER COLUMN [OldBarcode] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    DECLARE @var27 sysname;
    SELECT @var27 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductVariantBarcodeHistory]') AND [c].[name] = N'NewBarcode');
    IF @var27 IS NOT NULL EXEC(N'ALTER TABLE [ProductVariantBarcodeHistory] DROP CONSTRAINT [' + @var27 + '];');
    ALTER TABLE [ProductVariantBarcodeHistory] ALTER COLUMN [NewBarcode] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD [ActionType] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD [ChangedByUserId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD [ChangedByUserName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD [NewBarcodeId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD [ProductUnitConversionId] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD [Reason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    CREATE INDEX [IX_ProductVariantUnitBarcode_Conversion_Active_Primary] ON [ProductVariantUnitBarcode] ([ProductUnitConversionId], [IsActive], [IsPrimary]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ProductVariantUnitBarcode_Conversion_Primary_Active] ON [ProductVariantUnitBarcode] ([ProductUnitConversionId]) WHERE [IsDeleted] = 0 AND [IsActive] = 1 AND [IsPrimary] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ProductVariantUnitBarcode_Store_Barcode_Active] ON [ProductVariantUnitBarcode] ([StoreId], [Barcode]) WHERE [IsDeleted] = 0 AND [IsActive] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    CREATE INDEX [IX_ProductVariantBarcodeHistory_Conversion_ChangedAtUtc] ON [ProductVariantBarcodeHistory] ([ProductUnitConversionId], [ChangedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    CREATE INDEX [IX_ProductVariantBarcodeHistory_NewBarcodeId] ON [ProductVariantBarcodeHistory] ([NewBarcodeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    CREATE INDEX [IX_ProductVariantBarcodeHistory_OldBarcodeId] ON [ProductVariantBarcodeHistory] ([OldBarcodeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    CREATE INDEX [IX_ProductVariantBarcodeHistory_Store_NewBarcode] ON [ProductVariantBarcodeHistory] ([StoreId], [NewBarcode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    CREATE INDEX [IX_ProductVariantBarcodeHistory_Store_OldBarcode] ON [ProductVariantBarcodeHistory] ([StoreId], [OldBarcode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    CREATE INDEX [IX_ProductVariantBarcodeHistory_Variant_ChangedAtUtc] ON [ProductVariantBarcodeHistory] ([ProductVariantId], [ChangedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD CONSTRAINT [FK_ProductVariantBarcodeHistory_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD CONSTRAINT [FK_ProductVariantBarcodeHistory_ProductVariantUnitBarcode_NewBarcodeId] FOREIGN KEY ([NewBarcodeId]) REFERENCES [ProductVariantUnitBarcode] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD CONSTRAINT [FK_ProductVariantBarcodeHistory_ProductVariantUnitBarcode_OldBarcodeId] FOREIGN KEY ([OldBarcodeId]) REFERENCES [ProductVariantUnitBarcode] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantBarcodeHistory] ADD CONSTRAINT [FK_ProductVariantBarcodeHistory_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    ALTER TABLE [ProductVariantUnitBarcode] ADD CONSTRAINT [FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260320151950_editbarcodehistory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260320151950_editbarcodehistory', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260321123838_AddPOSTerminalAndShiftTerminal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260321123838_AddPOSTerminalAndShiftTerminal', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE TABLE [SalesReturns] (
        [Id] int NOT NULL IDENTITY,
        [ReturnNumber] nvarchar(30) NOT NULL,
        [OrderId] int NOT NULL,
        [POSShiftId] int NOT NULL,
        [Type] int NOT NULL,
        [Status] int NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Note] nvarchar(1000) NULL,
        [ReturnSubtotal] decimal(18,2) NOT NULL,
        [RefundTotal] decimal(18,2) NOT NULL,
        [CreatedByUserId] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CompletedByUserId] int NULL,
        [CompletedAtUtc] datetime2 NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_SalesReturns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SalesReturns_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SalesReturns_POSShifts_POSShiftId] FOREIGN KEY ([POSShiftId]) REFERENCES [POSShifts] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SalesReturns_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE TABLE [SalesReturnLines] (
        [Id] int NOT NULL IDENTITY,
        [SalesReturnId] int NOT NULL,
        [OrderLineId] int NOT NULL,
        [ProductId] int NOT NULL,
        [VariantId] int NOT NULL,
        [ItemName] nvarchar(250) NOT NULL,
        [UnitName] nvarchar(100) NULL,
        [ReturnQuantity] decimal(18,2) NOT NULL,
        [ReturnBaseQuantity] decimal(18,2) NOT NULL,
        [RefundUnitAmount] decimal(18,2) NOT NULL,
        [RefundLineTotal] decimal(18,2) NOT NULL,
        [Action] int NOT NULL,
        [Reason] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_SalesReturnLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SalesReturnLines_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SalesReturnLines_SalesReturns_SalesReturnId] FOREIGN KEY ([SalesReturnId]) REFERENCES [SalesReturns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SalesReturnLines_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE TABLE [SalesReturnPayments] (
        [Id] int NOT NULL IDENTITY,
        [SalesReturnId] int NOT NULL,
        [Method] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [ReferenceCode] nvarchar(100) NULL,
        [Provider] nvarchar(50) NULL,
        [Note] nvarchar(500) NULL,
        [PaidAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_SalesReturnPayments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SalesReturnPayments_SalesReturns_SalesReturnId] FOREIGN KEY ([SalesReturnId]) REFERENCES [SalesReturns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SalesReturnPayments_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE INDEX [IX_SalesReturnLines_OrderLineId] ON [SalesReturnLines] ([OrderLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE INDEX [IX_SalesReturnLines_SalesReturnId] ON [SalesReturnLines] ([SalesReturnId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE INDEX [IX_SalesReturnLines_StoreId] ON [SalesReturnLines] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE INDEX [IX_SalesReturnPayments_SalesReturnId] ON [SalesReturnPayments] ([SalesReturnId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE INDEX [IX_SalesReturnPayments_StoreId] ON [SalesReturnPayments] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE INDEX [IX_SalesReturns_OrderId] ON [SalesReturns] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE INDEX [IX_SalesReturns_POSShiftId] ON [SalesReturns] ([POSShiftId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE INDEX [IX_SalesReturns_StoreId_OrderId] ON [SalesReturns] ([StoreId], [OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE INDEX [IX_SalesReturns_StoreId_POSShiftId] ON [SalesReturns] ([StoreId], [POSShiftId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SalesReturns_StoreId_ReturnNumber] ON [SalesReturns] ([StoreId], [ReturnNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322140435_Phase514_SalesReturnRefund'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260322140435_Phase514_SalesReturnRefund', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322155419_Phase514_SalesReturnRefundedit'
)
BEGIN
    ALTER TABLE [POSShifts] ADD [RefundCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260322155419_Phase514_SalesReturnRefundedit'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260322155419_Phase514_SalesReturnRefundedit', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260323104654_Phase514_SalesReturnRefundcountvoid'
)
BEGIN
    ALTER TABLE [POSShifts] ADD [VoidCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260323104654_Phase514_SalesReturnRefundcountvoid'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260323104654_Phase514_SalesReturnRefundcountvoid', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    ALTER TABLE [Orders] ADD [HasInventoryIssue] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    ALTER TABLE [Orders] ADD [InventoryIssueApprovedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    ALTER TABLE [Orders] ADD [InventoryIssueOpenedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    ALTER TABLE [Orders] ADD [InventoryResolutionStatus] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE TABLE [OrderInventoryIssues] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Status] int NOT NULL,
        [Severity] int NOT NULL,
        [OpenedAtUtc] datetime2 NOT NULL,
        [DueAtUtc] datetime2 NOT NULL,
        [ReadyForApprovalAtUtc] datetime2 NULL,
        [ApprovedAtUtc] datetime2 NULL,
        [ApprovedByUserId] int NULL,
        [RejectedAtUtc] datetime2 NULL,
        [RejectedByUserId] int NULL,
        [ReasonType] int NOT NULL,
        [InternalNote] nvarchar(1000) NULL,
        [IsOverdue] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_OrderInventoryIssues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderInventoryIssues_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssues_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_OrderInventoryIssues_Users_ApprovedByUserId] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssues_Users_RejectedByUserId] FOREIGN KEY ([RejectedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE TABLE [OrderInventoryIssueActions] (
        [Id] int NOT NULL IDENTITY,
        [OrderInventoryIssueId] int NOT NULL,
        [ActionType] int NOT NULL,
        [ActorUserId] int NULL,
        [ActionAtUtc] datetime2 NOT NULL,
        [ReferenceType] int NOT NULL,
        [ReferenceId] int NULL,
        [Note] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_OrderInventoryIssueActions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderInventoryIssueActions_OrderInventoryIssues_OrderInventoryIssueId] FOREIGN KEY ([OrderInventoryIssueId]) REFERENCES [OrderInventoryIssues] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueActions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_OrderInventoryIssueActions_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE TABLE [OrderInventoryIssueLines] (
        [Id] int NOT NULL IDENTITY,
        [OrderInventoryIssueId] int NOT NULL,
        [OrderId] int NOT NULL,
        [OrderLineId] int NOT NULL,
        [ProductId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [ProductUnitConversionId] int NULL,
        [BarcodeId] int NULL,
        [OrderedQty] decimal(18,3) NOT NULL,
        [StockBefore] decimal(18,3) NOT NULL,
        [StockAfter] decimal(18,3) NOT NULL,
        [NegativeQty] decimal(18,3) NOT NULL,
        [ProvisionalUnitCost] decimal(18,2) NULL,
        [ProvisionalCostAmount] decimal(18,2) NULL,
        [RevaluationAmount] decimal(18,2) NULL,
        [IsResolved] bit NOT NULL,
        [ResolvedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_OrderInventoryIssueLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderInventoryIssueLines_OrderInventoryIssues_OrderInventoryIssueId] FOREIGN KEY ([OrderInventoryIssueId]) REFERENCES [OrderInventoryIssues] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueLines_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueLines_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueLines_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueLines_ProductVariantUnitBarcode_BarcodeId] FOREIGN KEY ([BarcodeId]) REFERENCES [ProductVariantUnitBarcode] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueLines_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueLines_Product_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Product] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueLines_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_Orders_StoreId_HasInventoryIssue_InventoryResolutionStatus_IsDeleted] ON [Orders] ([StoreId], [HasInventoryIssue], [InventoryResolutionStatus], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_Orders_StoreId_InventoryIssueOpenedAtUtc_IsDeleted] ON [Orders] ([StoreId], [InventoryIssueOpenedAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueActions_ActorUserId] ON [OrderInventoryIssueActions] ([ActorUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueActions_OrderInventoryIssueId_ActionAtUtc_IsDeleted] ON [OrderInventoryIssueActions] ([OrderInventoryIssueId], [ActionAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueActions_StoreId_ActionType_ActionAtUtc_IsDeleted] ON [OrderInventoryIssueActions] ([StoreId], [ActionType], [ActionAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueActions_StoreId_ReferenceType_ReferenceId_IsDeleted] ON [OrderInventoryIssueActions] ([StoreId], [ReferenceType], [ReferenceId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLines_BarcodeId] ON [OrderInventoryIssueLines] ([BarcodeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLines_OrderId_IsDeleted] ON [OrderInventoryIssueLines] ([OrderId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLines_OrderInventoryIssueId_IsDeleted] ON [OrderInventoryIssueLines] ([OrderInventoryIssueId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLines_OrderLineId_IsDeleted] ON [OrderInventoryIssueLines] ([OrderLineId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLines_ProductId] ON [OrderInventoryIssueLines] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLines_ProductUnitConversionId] ON [OrderInventoryIssueLines] ([ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLines_ProductVariantId] ON [OrderInventoryIssueLines] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLines_StoreId_ProductUnitConversionId_IsResolved_IsDeleted] ON [OrderInventoryIssueLines] ([StoreId], [ProductUnitConversionId], [IsResolved], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLines_StoreId_ProductVariantId_IsResolved_IsDeleted] ON [OrderInventoryIssueLines] ([StoreId], [ProductVariantId], [IsResolved], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssues_ApprovedByUserId] ON [OrderInventoryIssues] ([ApprovedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrderInventoryIssues_OrderId] ON [OrderInventoryIssues] ([OrderId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssues_RejectedByUserId] ON [OrderInventoryIssues] ([RejectedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrderInventoryIssues_StoreId_Code] ON [OrderInventoryIssues] ([StoreId], [Code]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssues_StoreId_DueAtUtc_Status_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [DueAtUtc], [Status], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssues_StoreId_IsOverdue_Status_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [IsOverdue], [Status], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssues_StoreId_OpenedAtUtc_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [OpenedAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssues_StoreId_Status_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [Status], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325024020_AddOrderInventoryIssueFlow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260325024020_AddOrderInventoryIssueFlow', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [SalesReturnLines] DROP CONSTRAINT [FK_SalesReturnLines_OrderLines_OrderLineId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [SalesReturnLines] DROP CONSTRAINT [FK_SalesReturnLines_SalesReturns_SalesReturnId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DROP INDEX [IX_SalesReturnLines_StoreId] ON [SalesReturnLines];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DROP INDEX [IX_InventoryTransactions_StoreId_ReferenceType_ReferenceId] ON [InventoryTransactions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DROP INDEX [IX_InventoryTransactions_StoreId_TransactionType_OccurredAtUtc] ON [InventoryTransactions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DROP INDEX [IX_InventoryTransactions_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc] ON [InventoryTransactions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [StockTransferLine] ADD [IsProvisionalCost] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [StockTransferLine] ADD [LineCostTotal] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [StockTransferLine] ADD [UnitCostSnapshot] decimal(18,6) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [StockCountLine] ADD [IsProvisionalCost] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [StockCountLine] ADD [LineCostTotal] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [StockCountLine] ADD [UnitCostSnapshot] decimal(18,6) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DECLARE @var28 sysname;
    SELECT @var28 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesReturnLines]') AND [c].[name] = N'ReturnQuantity');
    IF @var28 IS NOT NULL EXEC(N'ALTER TABLE [SalesReturnLines] DROP CONSTRAINT [' + @var28 + '];');
    ALTER TABLE [SalesReturnLines] ALTER COLUMN [ReturnQuantity] decimal(18,3) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DECLARE @var29 sysname;
    SELECT @var29 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesReturnLines]') AND [c].[name] = N'ReturnBaseQuantity');
    IF @var29 IS NOT NULL EXEC(N'ALTER TABLE [SalesReturnLines] DROP CONSTRAINT [' + @var29 + '];');
    ALTER TABLE [SalesReturnLines] ALTER COLUMN [ReturnBaseQuantity] decimal(18,3) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DECLARE @var30 sysname;
    SELECT @var30 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesReturnLines]') AND [c].[name] = N'RefundUnitAmount');
    IF @var30 IS NOT NULL EXEC(N'ALTER TABLE [SalesReturnLines] DROP CONSTRAINT [' + @var30 + '];');
    ALTER TABLE [SalesReturnLines] ADD DEFAULT 0.0 FOR [RefundUnitAmount];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DECLARE @var31 sysname;
    SELECT @var31 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesReturnLines]') AND [c].[name] = N'RefundLineTotal');
    IF @var31 IS NOT NULL EXEC(N'ALTER TABLE [SalesReturnLines] DROP CONSTRAINT [' + @var31 + '];');
    ALTER TABLE [SalesReturnLines] ADD DEFAULT 0.0 FOR [RefundLineTotal];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DECLARE @var32 sysname;
    SELECT @var32 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesReturnLines]') AND [c].[name] = N'Action');
    IF @var32 IS NOT NULL EXEC(N'ALTER TABLE [SalesReturnLines] DROP CONSTRAINT [' + @var32 + '];');
    ALTER TABLE [SalesReturnLines] ADD DEFAULT 1 FOR [Action];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [SalesReturnLines] ADD [IsProvisionalCost] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [SalesReturnLines] ADD [LineCostTotal] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [SalesReturnLines] ADD [UnitCostSnapshot] decimal(18,6) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [GrossProfit] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [IsProvisionalCost] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [LineCostTotal] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [UnitCostSnapshot] decimal(18,6) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DECLARE @var33 sysname;
    SELECT @var33 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InventoryTransactions]') AND [c].[name] = N'ReferenceId');
    IF @var33 IS NOT NULL EXEC(N'ALTER TABLE [InventoryTransactions] DROP CONSTRAINT [' + @var33 + '];');
    EXEC(N'UPDATE [InventoryTransactions] SET [ReferenceId] = N'''' WHERE [ReferenceId] IS NULL');
    ALTER TABLE [InventoryTransactions] ALTER COLUMN [ReferenceId] nvarchar(64) NOT NULL;
    ALTER TABLE [InventoryTransactions] ADD DEFAULT N'' FOR [ReferenceId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DECLARE @var34 sysname;
    SELECT @var34 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InventoryTransactions]') AND [c].[name] = N'QuantityChange');
    IF @var34 IS NOT NULL EXEC(N'ALTER TABLE [InventoryTransactions] DROP CONSTRAINT [' + @var34 + '];');
    ALTER TABLE [InventoryTransactions] ALTER COLUMN [QuantityChange] decimal(18,4) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DECLARE @var35 sysname;
    SELECT @var35 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InventoryTransactions]') AND [c].[name] = N'Note');
    IF @var35 IS NOT NULL EXEC(N'ALTER TABLE [InventoryTransactions] DROP CONSTRAINT [' + @var35 + '];');
    ALTER TABLE [InventoryTransactions] ALTER COLUMN [Note] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DECLARE @var36 sysname;
    SELECT @var36 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InventoryTransactions]') AND [c].[name] = N'BeforeQty');
    IF @var36 IS NOT NULL EXEC(N'ALTER TABLE [InventoryTransactions] DROP CONSTRAINT [' + @var36 + '];');
    ALTER TABLE [InventoryTransactions] ALTER COLUMN [BeforeQty] decimal(18,4) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    DECLARE @var37 sysname;
    SELECT @var37 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InventoryTransactions]') AND [c].[name] = N'AfterQty');
    IF @var37 IS NOT NULL EXEC(N'ALTER TABLE [InventoryTransactions] DROP CONSTRAINT [' + @var37 + '];');
    ALTER TABLE [InventoryTransactions] ALTER COLUMN [AfterQty] decimal(18,4) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [AfterInventoryValue] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [BeforeInventoryValue] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [CostFinalizedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [CostSourceType] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [IsProvisionalCost] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [RunningAverageUnitCostAfter] decimal(18,6) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [TotalCost] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [UnitCostSnapshot] decimal(18,6) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [WarehouseId1] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryBalances] ADD [AverageUnitCost] decimal(18,6) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryBalances] ADD [InventoryValue] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryBalances] ADD [LastInboundAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryBalances] ADD [LastInboundUnitCost] decimal(18,6) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryBalances] ADD [LastValuationAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE TABLE [InventoryValuationEntries] (
        [Id] int NOT NULL IDENTITY,
        [InventoryTransactionId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [EntryType] int NOT NULL,
        [ReferenceType] int NOT NULL,
        [ReferenceId] nvarchar(64) NOT NULL,
        [ReferenceLineId] int NULL,
        [Quantity] decimal(18,4) NOT NULL,
        [UnitCost] decimal(18,6) NOT NULL DEFAULT 0.0,
        [Amount] decimal(18,4) NOT NULL DEFAULT 0.0,
        [RunningQtyAfter] decimal(18,4) NOT NULL DEFAULT 0.0,
        [RunningValueAfter] decimal(18,4) NOT NULL DEFAULT 0.0,
        [RunningAverageUnitCostAfter] decimal(18,6) NOT NULL DEFAULT 0.0,
        [CostSourceType] int NOT NULL,
        [IsProvisional] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CostFinalizedAtUtc] datetime2 NULL,
        [RevaluationOfEntryId] int NULL,
        [Note] nvarchar(1000) NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InventoryValuationEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryValuationEntries_InventoryTransactions_InventoryTransactionId] FOREIGN KEY ([InventoryTransactionId]) REFERENCES [InventoryTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryValuationEntries_InventoryValuationEntries_RevaluationOfEntryId] FOREIGN KEY ([RevaluationOfEntryId]) REFERENCES [InventoryValuationEntries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryValuationEntries_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryValuationEntries_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_InventoryValuationEntries_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_SalesReturnLines_StoreId_OrderLineId] ON [SalesReturnLines] ([StoreId], [OrderLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_SalesReturnLines_StoreId_SalesReturnId] ON [SalesReturnLines] ([StoreId], [SalesReturnId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_SalesReturnLines_StoreId_VariantId] ON [SalesReturnLines] ([StoreId], [VariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_StoreId_ReferenceType_ReferenceId_ReferenceLineId_TransactionType] ON [InventoryTransactions] ([StoreId], [ReferenceType], [ReferenceId], [ReferenceLineId], [TransactionType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc_Id] ON [InventoryTransactions] ([StoreId], [WarehouseId], [ProductVariantId], [OccurredAtUtc], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_WarehouseId1] ON [InventoryTransactions] ([WarehouseId1]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_InventoryTransactionId] ON [InventoryValuationEntries] ([InventoryTransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_ProductVariantId] ON [InventoryValuationEntries] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_RevaluationOfEntryId] ON [InventoryValuationEntries] ([RevaluationOfEntryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_StoreId_InventoryTransactionId] ON [InventoryValuationEntries] ([StoreId], [InventoryTransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_StoreId_ReferenceType_ReferenceId_ReferenceLineId_EntryType] ON [InventoryValuationEntries] ([StoreId], [ReferenceType], [ReferenceId], [ReferenceLineId], [EntryType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_StoreId_RevaluationOfEntryId] ON [InventoryValuationEntries] ([StoreId], [RevaluationOfEntryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc_Id] ON [InventoryValuationEntries] ([StoreId], [WarehouseId], [ProductVariantId], [OccurredAtUtc], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_WarehouseId] ON [InventoryValuationEntries] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD CONSTRAINT [FK_InventoryTransactions_Warehouses_WarehouseId1] FOREIGN KEY ([WarehouseId1]) REFERENCES [Warehouses] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [SalesReturnLines] ADD CONSTRAINT [FK_SalesReturnLines_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    ALTER TABLE [SalesReturnLines] ADD CONSTRAINT [FK_SalesReturnLines_SalesReturns_SalesReturnId] FOREIGN KEY ([SalesReturnId]) REFERENCES [SalesReturns] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325041900_Phase515_ProvisionalCostFoundation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260325041900_Phase515_ProvisionalCostFoundation', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325092236_Phase515_InventoryValuationCore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260325092236_Phase515_InventoryValuationCore', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325144927_AddReferenceSubKeyToInventoryTransaction'
)
BEGIN
    ALTER TABLE [InventoryTransactions] ADD [ReferenceSubKey] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325144927_AddReferenceSubKeyToInventoryTransaction'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260325144927_AddReferenceSubKeyToInventoryTransaction', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325153619_AddSourceTraceToInventoryValuationEntry'
)
BEGIN
    DROP INDEX [IX_InventoryValuationEntries_StoreId_ReferenceType_ReferenceId_ReferenceLineId_EntryType] ON [InventoryValuationEntries];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325153619_AddSourceTraceToInventoryValuationEntry'
)
BEGIN
    ALTER TABLE [InventoryValuationEntries] ADD [ReferenceSubKey] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325153619_AddSourceTraceToInventoryValuationEntry'
)
BEGIN
    ALTER TABLE [InventoryValuationEntries] ADD [SourceReferenceSubKey] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325153619_AddSourceTraceToInventoryValuationEntry'
)
BEGIN
    ALTER TABLE [InventoryValuationEntries] ADD [SourceValuationEntryId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325153619_AddSourceTraceToInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_SourceValuationEntryId] ON [InventoryValuationEntries] ([SourceValuationEntryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325153619_AddSourceTraceToInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_StoreId_ReferenceType_ReferenceId_ReferenceLineId_ReferenceSubKey_EntryType] ON [InventoryValuationEntries] ([StoreId], [ReferenceType], [ReferenceId], [ReferenceLineId], [ReferenceSubKey], [EntryType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325153619_AddSourceTraceToInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_StoreId_SourceValuationEntryId] ON [InventoryValuationEntries] ([StoreId], [SourceValuationEntryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325153619_AddSourceTraceToInventoryValuationEntry'
)
BEGIN
    ALTER TABLE [InventoryValuationEntries] ADD CONSTRAINT [FK_InventoryValuationEntries_InventoryValuationEntries_SourceValuationEntryId] FOREIGN KEY ([SourceValuationEntryId]) REFERENCES [InventoryValuationEntries] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325153619_AddSourceTraceToInventoryValuationEntry'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260325153619_AddSourceTraceToInventoryValuationEntry', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325174748_AddDocumentNumberSequence'
)
BEGIN
    CREATE TABLE [DocumentNumberSequences] (
        [Id] int NOT NULL IDENTITY,
        [SequenceType] int NOT NULL,
        [SequenceDate] date NOT NULL,
        [LastNumber] int NOT NULL DEFAULT 0,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_DocumentNumberSequences] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DocumentNumberSequences_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325174748_AddDocumentNumberSequence'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DocumentNumberSequences_StoreId_SequenceType_SequenceDate] ON [DocumentNumberSequences] ([StoreId], [SequenceType], [SequenceDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260325174748_AddDocumentNumberSequence'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260325174748_AddDocumentNumberSequence', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326045520_RemoveDefaultValueFromSalesReturnLineAction'
)
BEGIN
    DECLARE @var38 sysname;
    SELECT @var38 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesReturnLines]') AND [c].[name] = N'Action');
    IF @var38 IS NOT NULL EXEC(N'ALTER TABLE [SalesReturnLines] DROP CONSTRAINT [' + @var38 + '];');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326045520_RemoveDefaultValueFromSalesReturnLineAction'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260326045520_RemoveDefaultValueFromSalesReturnLineAction', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326093919_Phase515_PosFinalizePendingApproval'
)
BEGIN
    DECLARE @var39 sysname;
    SELECT @var39 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'UnitCostSnapshot');
    IF @var39 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var39 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [UnitCostSnapshot] decimal(18,6) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326093919_Phase515_PosFinalizePendingApproval'
)
BEGIN
    DECLARE @var40 sysname;
    SELECT @var40 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'LineCostTotal');
    IF @var40 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var40 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [LineCostTotal] decimal(18,4) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326093919_Phase515_PosFinalizePendingApproval'
)
BEGIN
    DECLARE @var41 sysname;
    SELECT @var41 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'GrossProfit');
    IF @var41 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var41 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [GrossProfit] decimal(18,4) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326093919_Phase515_PosFinalizePendingApproval'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [CostSnapshotNote] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326093919_Phase515_PosFinalizePendingApproval'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [ProvisionalUnitCost] decimal(18,4) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326093919_Phase515_PosFinalizePendingApproval'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260326093919_Phase515_PosFinalizePendingApproval', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326142135_Phase515_PosFinalizePendingApprovaluser'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueActions] ADD [OrderInventoryIssueLineId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326142135_Phase515_PosFinalizePendingApprovaluser'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueActions_OrderInventoryIssueLineId] ON [OrderInventoryIssueActions] ([OrderInventoryIssueLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326142135_Phase515_PosFinalizePendingApprovaluser'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueActions] ADD CONSTRAINT [FK_OrderInventoryIssueActions_OrderInventoryIssueLines_OrderInventoryIssueLineId] FOREIGN KEY ([OrderInventoryIssueLineId]) REFERENCES [OrderInventoryIssueLines] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326142135_Phase515_PosFinalizePendingApprovaluser'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260326142135_Phase515_PosFinalizePendingApprovaluser', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    ALTER TABLE [OrderInventoryIssues] ADD [AutoResolvedLineCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    ALTER TABLE [OrderInventoryIssues] ADD [LastAutoResolvedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLines] ADD [AutoDetectedCostResolved] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLines] ADD [AutoDetectedDocumentResolved] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLines] ADD [AutoDetectedInboundQty] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLines] ADD [AutoDetectedRevaluationAmount] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLines] ADD [AutoResolveNote] nvarchar(2000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLines] ADD [LastAutoResolvedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    CREATE TABLE [OrderInventoryIssueLineAllocations] (
        [Id] int NOT NULL IDENTITY,
        [OrderInventoryIssueId] int NOT NULL,
        [OrderInventoryIssueLineId] int NOT NULL,
        [SourceReferenceType] int NOT NULL,
        [SourceReferenceId] int NOT NULL,
        [SourceReferenceLineId] int NULL,
        [InventoryTransactionId] int NOT NULL,
        [AllocatedQuantity] decimal(18,3) NOT NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_OrderInventoryIssueLineAllocations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderInventoryIssueLineAllocations_InventoryTransactions_InventoryTransactionId] FOREIGN KEY ([InventoryTransactionId]) REFERENCES [InventoryTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueLineAllocations_OrderInventoryIssueLines_OrderInventoryIssueLineId] FOREIGN KEY ([OrderInventoryIssueLineId]) REFERENCES [OrderInventoryIssueLines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueLineAllocations_OrderInventoryIssues_OrderInventoryIssueId] FOREIGN KEY ([OrderInventoryIssueId]) REFERENCES [OrderInventoryIssues] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderInventoryIssueLineAllocations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLineAllocations_InventoryTransactionId] ON [OrderInventoryIssueLineAllocations] ([InventoryTransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLineAllocations_OrderInventoryIssueId_OrderInventoryIssueLineId_IsDeleted] ON [OrderInventoryIssueLineAllocations] ([OrderInventoryIssueId], [OrderInventoryIssueLineId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLineAllocations_OrderInventoryIssueLineId] ON [OrderInventoryIssueLineAllocations] ([OrderInventoryIssueLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLineAllocations_StoreId] ON [OrderInventoryIssueLineAllocations] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326162229_AddInventoryIssueAutoResolutionAllocation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260326162229_AddInventoryIssueAutoResolutionAllocation', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    ALTER TABLE [InventoryValuationEntries] ADD [InventoryCostLayerId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    ALTER TABLE [InventoryValuationEntries] ADD [InventoryCostLayerId1] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE TABLE [InventoryCostLayers] (
        [Id] int NOT NULL IDENTITY,
        [WarehouseId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [InventoryTransactionId] int NOT NULL,
        [InventoryValuationEntryId] int NOT NULL,
        [ReferenceType] int NOT NULL,
        [ReferenceId] nvarchar(64) NOT NULL,
        [ReferenceLineId] int NULL,
        [ReferenceSubKey] nvarchar(100) NULL,
        [OriginalQuantity] decimal(18,4) NOT NULL,
        [RemainingQuantity] decimal(18,4) NOT NULL,
        [UnitCost] decimal(18,6) NOT NULL,
        [IsProvisionalSource] bit NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
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
        CONSTRAINT [PK_InventoryCostLayers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryCostLayers_InventoryTransactions_InventoryTransactionId] FOREIGN KEY ([InventoryTransactionId]) REFERENCES [InventoryTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryCostLayers_InventoryValuationEntries_InventoryValuationEntryId] FOREIGN KEY ([InventoryValuationEntryId]) REFERENCES [InventoryValuationEntries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryCostLayers_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryCostLayers_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_InventoryCostLayers_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE TABLE [InventoryCostLayerAllocations] (
        [Id] int NOT NULL IDENTITY,
        [InventoryValuationEntryId] int NOT NULL,
        [InventoryCostLayerId] int NULL,
        [ReverseOfAllocationId] int NULL,
        [Quantity] decimal(18,4) NOT NULL,
        [UnitCost] decimal(18,6) NOT NULL,
        [Amount] decimal(18,4) NOT NULL,
        [IsProvisional] bit NOT NULL,
        [IsResolved] bit NOT NULL,
        [ResolvedAtUtc] datetime2 NULL,
        [ResolvedByInventoryCostLayerId] int NULL,
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
        CONSTRAINT [PK_InventoryCostLayerAllocations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryCostLayerAllocations_InventoryCostLayerAllocations_ReverseOfAllocationId] FOREIGN KEY ([ReverseOfAllocationId]) REFERENCES [InventoryCostLayerAllocations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryCostLayerAllocations_InventoryCostLayers_InventoryCostLayerId] FOREIGN KEY ([InventoryCostLayerId]) REFERENCES [InventoryCostLayers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryCostLayerAllocations_InventoryCostLayers_ResolvedByInventoryCostLayerId] FOREIGN KEY ([ResolvedByInventoryCostLayerId]) REFERENCES [InventoryCostLayers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryCostLayerAllocations_InventoryValuationEntries_InventoryValuationEntryId] FOREIGN KEY ([InventoryValuationEntryId]) REFERENCES [InventoryValuationEntries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryCostLayerAllocations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_InventoryCostLayerId1] ON [InventoryValuationEntries] ([InventoryCostLayerId1]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryCostLayerAllocations_CostLayerId] ON [InventoryCostLayerAllocations] ([InventoryCostLayerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryCostLayerAllocations_OpenProvisional] ON [InventoryCostLayerAllocations] ([StoreId], [IsProvisional], [IsResolved], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryCostLayerAllocations_ResolvedByLayerId] ON [InventoryCostLayerAllocations] ([ResolvedByInventoryCostLayerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryCostLayerAllocations_ReverseOfAllocationId] ON [InventoryCostLayerAllocations] ([ReverseOfAllocationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryCostLayerAllocations_ValuationEntryId] ON [InventoryCostLayerAllocations] ([InventoryValuationEntryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryCostLayers_FIFO] ON [InventoryCostLayers] ([StoreId], [WarehouseId], [ProductVariantId], [OccurredAtUtc], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryCostLayers_InventoryTransactionId] ON [InventoryCostLayers] ([InventoryTransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InventoryCostLayers_InventoryValuationEntryId] ON [InventoryCostLayers] ([InventoryValuationEntryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryCostLayers_Open] ON [InventoryCostLayers] ([StoreId], [WarehouseId], [ProductVariantId], [RemainingQuantity]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryCostLayers_ProductVariantId] ON [InventoryCostLayers] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    CREATE INDEX [IX_InventoryCostLayers_WarehouseId] ON [InventoryCostLayers] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    ALTER TABLE [InventoryValuationEntries] ADD CONSTRAINT [FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId1] FOREIGN KEY ([InventoryCostLayerId1]) REFERENCES [InventoryCostLayers] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327024554_EditInventoryValuationEntry'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260327024554_EditInventoryValuationEntry', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    ALTER TABLE [InventoryValuationEntries] DROP CONSTRAINT [FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId1];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    DROP INDEX [IX_InventoryValuationEntries_InventoryCostLayerId1] ON [InventoryValuationEntries];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    DECLARE @var42 sysname;
    SELECT @var42 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InventoryValuationEntries]') AND [c].[name] = N'InventoryCostLayerId1');
    IF @var42 IS NOT NULL EXEC(N'ALTER TABLE [InventoryValuationEntries] DROP CONSTRAINT [' + @var42 + '];');
    ALTER TABLE [InventoryValuationEntries] DROP COLUMN [InventoryCostLayerId1];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    DECLARE @var43 sysname;
    SELECT @var43 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInventoryIssueLineAllocations]') AND [c].[name] = N'InventoryTransactionId');
    IF @var43 IS NOT NULL EXEC(N'ALTER TABLE [OrderInventoryIssueLineAllocations] DROP CONSTRAINT [' + @var43 + '];');
    ALTER TABLE [OrderInventoryIssueLineAllocations] ALTER COLUMN [InventoryTransactionId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    DECLARE @var44 sysname;
    SELECT @var44 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInventoryIssueLineAllocations]') AND [c].[name] = N'AllocatedQuantity');
    IF @var44 IS NOT NULL EXEC(N'ALTER TABLE [OrderInventoryIssueLineAllocations] DROP CONSTRAINT [' + @var44 + '];');
    ALTER TABLE [OrderInventoryIssueLineAllocations] ALTER COLUMN [AllocatedQuantity] decimal(18,4) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLineAllocations] ADD [InventoryCostLayerAllocationId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLineAllocations] ADD [InventoryCostLayerId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLineAllocations_InventoryCostLayerAllocationId] ON [OrderInventoryIssueLineAllocations] ([InventoryCostLayerAllocationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssueLineAllocations_InventoryCostLayerId] ON [OrderInventoryIssueLineAllocations] ([InventoryCostLayerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_InventoryCostLayerId] ON [InventoryValuationEntries] ([InventoryCostLayerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_StoreId_InventoryCostLayerId] ON [InventoryValuationEntries] ([StoreId], [InventoryCostLayerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    CREATE INDEX [IX_InventoryValuationEntries_StoreId_WarehouseId_ProductVariantId_EntryType_IsProvisional_CostFinalizedAtUtc_OccurredAtUtc_Id] ON [InventoryValuationEntries] ([StoreId], [WarehouseId], [ProductVariantId], [EntryType], [IsProvisional], [CostFinalizedAtUtc], [OccurredAtUtc], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    ALTER TABLE [InventoryValuationEntries] ADD CONSTRAINT [FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId] FOREIGN KEY ([InventoryCostLayerId]) REFERENCES [InventoryCostLayers] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLineAllocations] ADD CONSTRAINT [FK_OrderInventoryIssueLineAllocations_InventoryCostLayerAllocations_InventoryCostLayerAllocationId] FOREIGN KEY ([InventoryCostLayerAllocationId]) REFERENCES [InventoryCostLayerAllocations] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLineAllocations] ADD CONSTRAINT [FK_OrderInventoryIssueLineAllocations_InventoryCostLayers_InventoryCostLayerId] FOREIGN KEY ([InventoryCostLayerId]) REFERENCES [InventoryCostLayers] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327032402_editfifo'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260327032402_editfifo', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327081048_editfifoinventorycostlayer'
)
BEGIN
    ALTER TABLE [InventoryCostLayers] ADD [RemainingOpenProvisionalQty] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327081048_editfifoinventorycostlayer'
)
BEGIN
    ALTER TABLE [InventoryCostLayers] ADD [ResolvedProvisionalQty] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327081048_editfifoinventorycostlayer'
)
BEGIN
    ALTER TABLE [InventoryCostLayerAllocations] ADD [ResolvedAmount] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327081048_editfifoinventorycostlayer'
)
BEGIN
    ALTER TABLE [InventoryCostLayerAllocations] ADD [ResolvedQuantity] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327081048_editfifoinventorycostlayer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260327081048_editfifoinventorycostlayer', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401035702_addpending'
)
BEGIN
    ALTER TABLE [OrderInventoryIssues] ADD [LastOverdueNotifiedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401035702_addpending'
)
BEGIN
    ALTER TABLE [OrderInventoryIssues] ADD [OverdueSinceUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401035702_addpending'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssues_StoreId_IsOverdue_LastOverdueNotifiedAtUtc_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [IsOverdue], [LastOverdueNotifiedAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401035702_addpending'
)
BEGIN
    CREATE INDEX [IX_OrderInventoryIssues_StoreId_IsOverdue_OverdueSinceUtc_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [IsOverdue], [OverdueSinceUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401035702_addpending'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260401035702_addpending', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401103815_Fix_InventoryTransaction_WarehouseRelationship'
)
BEGIN
    ALTER TABLE [InventoryTransactions] DROP CONSTRAINT [FK_InventoryTransactions_Warehouses_WarehouseId1];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401103815_Fix_InventoryTransaction_WarehouseRelationship'
)
BEGIN
    DROP INDEX [IX_InventoryTransactions_WarehouseId1] ON [InventoryTransactions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401103815_Fix_InventoryTransaction_WarehouseRelationship'
)
BEGIN
    DECLARE @var45 sysname;
    SELECT @var45 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InventoryTransactions]') AND [c].[name] = N'WarehouseId1');
    IF @var45 IS NOT NULL EXEC(N'ALTER TABLE [InventoryTransactions] DROP CONSTRAINT [' + @var45 + '];');
    ALTER TABLE [InventoryTransactions] DROP COLUMN [WarehouseId1];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401103815_Fix_InventoryTransaction_WarehouseRelationship'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260401103815_Fix_InventoryTransaction_WarehouseRelationship', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLines] DROP CONSTRAINT [FK_OrderInventoryIssueLines_Product_ProductId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Product] DROP CONSTRAINT [FK_Product_Brands_BrandId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Product] DROP CONSTRAINT [FK_Product_Category_CategoryId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Product] DROP CONSTRAINT [FK_Product_Stores_StoreId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Product] DROP CONSTRAINT [FK_Product_Suppliers_SupplierId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Product] DROP CONSTRAINT [FK_Product_Taxes_TaxId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Product] DROP CONSTRAINT [FK_Product_Unit_BaseUnitId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [ProductImages] DROP CONSTRAINT [FK_ProductImages_Product_ProductId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [ProductVariant] DROP CONSTRAINT [FK_ProductVariant_Product_ProductId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Product] DROP CONSTRAINT [PK_Product];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    EXEC sp_rename N'[Product]', N'Products';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    EXEC sp_rename N'[Products].[IX_Product_TaxId]', N'IX_Products_TaxId', N'INDEX';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    EXEC sp_rename N'[Products].[IX_Product_SupplierId]', N'IX_Products_SupplierId', N'INDEX';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    EXEC sp_rename N'[Products].[IX_Product_StoreId_Alias]', N'IX_Products_StoreId_Alias', N'INDEX';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    EXEC sp_rename N'[Products].[IX_Product_CategoryId]', N'IX_Products_CategoryId', N'INDEX';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    EXEC sp_rename N'[Products].[IX_Product_BrandId]', N'IX_Products_BrandId', N'INDEX';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    EXEC sp_rename N'[Products].[IX_Product_BaseUnitId]', N'IX_Products_BaseUnitId', N'INDEX';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var46 sysname;
    SELECT @var46 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'UnitCostSnapshot');
    IF @var46 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var46 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [UnitCostSnapshot] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var47 sysname;
    SELECT @var47 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'SellingUnitName');
    IF @var47 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var47 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [SellingUnitName] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var48 sysname;
    SELECT @var48 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'ScannedBarcode');
    IF @var48 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var48 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [ScannedBarcode] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var49 sysname;
    SELECT @var49 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'Quantity');
    IF @var49 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var49 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [Quantity] decimal(18,4) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var50 sysname;
    SELECT @var50 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'ProvisionalUnitCost');
    IF @var50 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var50 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [ProvisionalUnitCost] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var51 sysname;
    SELECT @var51 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'Multiplier');
    IF @var51 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var51 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [Multiplier] decimal(18,6) NOT NULL;
    ALTER TABLE [OrderLines] ADD DEFAULT 1.0 FOR [Multiplier];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var52 sysname;
    SELECT @var52 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'LineCostTotal');
    IF @var52 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var52 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [LineCostTotal] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var53 sysname;
    SELECT @var53 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'GrossProfit');
    IF @var53 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var53 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [GrossProfit] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var54 sysname;
    SELECT @var54 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'CostSnapshotNote');
    IF @var54 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var54 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [CostSnapshotNote] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var55 sysname;
    SELECT @var55 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'BaseUnitName');
    IF @var55 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var55 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [BaseUnitName] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var56 sysname;
    SELECT @var56 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderLines]') AND [c].[name] = N'BaseQuantity');
    IF @var56 IS NOT NULL EXEC(N'ALTER TABLE [OrderLines] DROP CONSTRAINT [' + @var56 + '];');
    ALTER TABLE [OrderLines] ALTER COLUMN [BaseQuantity] decimal(18,4) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var57 sysname;
    SELECT @var57 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInventoryIssueLines]') AND [c].[name] = N'StockBefore');
    IF @var57 IS NOT NULL EXEC(N'ALTER TABLE [OrderInventoryIssueLines] DROP CONSTRAINT [' + @var57 + '];');
    ALTER TABLE [OrderInventoryIssueLines] ALTER COLUMN [StockBefore] decimal(18,4) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var58 sysname;
    SELECT @var58 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInventoryIssueLines]') AND [c].[name] = N'StockAfter');
    IF @var58 IS NOT NULL EXEC(N'ALTER TABLE [OrderInventoryIssueLines] DROP CONSTRAINT [' + @var58 + '];');
    ALTER TABLE [OrderInventoryIssueLines] ALTER COLUMN [StockAfter] decimal(18,4) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var59 sysname;
    SELECT @var59 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInventoryIssueLines]') AND [c].[name] = N'ProvisionalUnitCost');
    IF @var59 IS NOT NULL EXEC(N'ALTER TABLE [OrderInventoryIssueLines] DROP CONSTRAINT [' + @var59 + '];');
    ALTER TABLE [OrderInventoryIssueLines] ALTER COLUMN [ProvisionalUnitCost] decimal(18,6) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var60 sysname;
    SELECT @var60 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInventoryIssueLines]') AND [c].[name] = N'OrderedQty');
    IF @var60 IS NOT NULL EXEC(N'ALTER TABLE [OrderInventoryIssueLines] DROP CONSTRAINT [' + @var60 + '];');
    ALTER TABLE [OrderInventoryIssueLines] ALTER COLUMN [OrderedQty] decimal(18,4) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var61 sysname;
    SELECT @var61 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInventoryIssueLines]') AND [c].[name] = N'NegativeQty');
    IF @var61 IS NOT NULL EXEC(N'ALTER TABLE [OrderInventoryIssueLines] DROP CONSTRAINT [' + @var61 + '];');
    ALTER TABLE [OrderInventoryIssueLines] ALTER COLUMN [NegativeQty] decimal(18,4) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var62 sysname;
    SELECT @var62 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InventoryBalances]') AND [c].[name] = N'ReservedQty');
    IF @var62 IS NOT NULL EXEC(N'ALTER TABLE [InventoryBalances] DROP CONSTRAINT [' + @var62 + '];');
    ALTER TABLE [InventoryBalances] ALTER COLUMN [ReservedQty] decimal(18,4) NOT NULL;
    ALTER TABLE [InventoryBalances] ADD DEFAULT 0.0 FOR [ReservedQty];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    DECLARE @var63 sysname;
    SELECT @var63 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InventoryBalances]') AND [c].[name] = N'OnHandQty');
    IF @var63 IS NOT NULL EXEC(N'ALTER TABLE [InventoryBalances] DROP CONSTRAINT [' + @var63 + '];');
    ALTER TABLE [InventoryBalances] ALTER COLUMN [OnHandQty] decimal(18,4) NOT NULL;
    ALTER TABLE [InventoryBalances] ADD DEFAULT 0.0 FOR [OnHandQty];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Products] ADD CONSTRAINT [PK_Products] PRIMARY KEY ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    CREATE INDEX [IX_OrderLines_StoreId_ProductId] ON [OrderLines] ([StoreId], [ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    CREATE INDEX [IX_OrderLines_StoreId_VariantId] ON [OrderLines] ([StoreId], [VariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [OrderInventoryIssueLines] ADD CONSTRAINT [FK_OrderInventoryIssueLines_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [ProductImages] ADD CONSTRAINT [FK_ProductImages_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Products] ADD CONSTRAINT [FK_Products_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Products] ADD CONSTRAINT [FK_Products_Category_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Category] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Products] ADD CONSTRAINT [FK_Products_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Products] ADD CONSTRAINT [FK_Products_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Products] ADD CONSTRAINT [FK_Products_Taxes_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [Taxes] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [Products] ADD CONSTRAINT [FK_Products_Unit_BaseUnitId] FOREIGN KEY ([BaseUnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD CONSTRAINT [FK_ProductVariant_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260401105545_Fix_OrderLine_And_SalesReturn_Precision', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401110802_Fix_SalesReturnPayment_Amount_Precision'
)
BEGIN
    DROP INDEX [IX_SalesReturnPayments_StoreId] ON [SalesReturnPayments];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401110802_Fix_SalesReturnPayment_Amount_Precision'
)
BEGIN
    DECLARE @var64 sysname;
    SELECT @var64 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShifts]') AND [c].[name] = N'NonCashRefundTotal');
    IF @var64 IS NOT NULL EXEC(N'ALTER TABLE [POSShifts] DROP CONSTRAINT [' + @var64 + '];');
    ALTER TABLE [POSShifts] ALTER COLUMN [NonCashRefundTotal] decimal(18,2) NOT NULL;
    ALTER TABLE [POSShifts] ADD DEFAULT 0.0 FOR [NonCashRefundTotal];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401110802_Fix_SalesReturnPayment_Amount_Precision'
)
BEGIN
    DECLARE @var65 sysname;
    SELECT @var65 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[POSShifts]') AND [c].[name] = N'CashRefundTotal');
    IF @var65 IS NOT NULL EXEC(N'ALTER TABLE [POSShifts] DROP CONSTRAINT [' + @var65 + '];');
    ALTER TABLE [POSShifts] ALTER COLUMN [CashRefundTotal] decimal(18,2) NOT NULL;
    ALTER TABLE [POSShifts] ADD DEFAULT 0.0 FOR [CashRefundTotal];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401110802_Fix_SalesReturnPayment_Amount_Precision'
)
BEGIN
    DECLARE @var66 sysname;
    SELECT @var66 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInventoryIssueLines]') AND [c].[name] = N'AutoDetectedInboundQty');
    IF @var66 IS NOT NULL EXEC(N'ALTER TABLE [OrderInventoryIssueLines] DROP CONSTRAINT [' + @var66 + '];');
    ALTER TABLE [OrderInventoryIssueLines] ALTER COLUMN [AutoDetectedInboundQty] decimal(18,4) NOT NULL;
    ALTER TABLE [OrderInventoryIssueLines] ADD DEFAULT 0.0 FOR [AutoDetectedInboundQty];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401110802_Fix_SalesReturnPayment_Amount_Precision'
)
BEGIN
    CREATE INDEX [IX_SalesReturnPayments_StoreId_PaidAtUtc] ON [SalesReturnPayments] ([StoreId], [PaidAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401110802_Fix_SalesReturnPayment_Amount_Precision'
)
BEGIN
    CREATE INDEX [IX_SalesReturnPayments_StoreId_SalesReturnId] ON [SalesReturnPayments] ([StoreId], [SalesReturnId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401110802_Fix_SalesReturnPayment_Amount_Precision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260401110802_Fix_SalesReturnPayment_Amount_Precision', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401111309_Fix_RolePermission_QueryFilter'
)
BEGIN
    ALTER TABLE [RolePermissions] DROP CONSTRAINT [FK_RolePermissions_Permissions_PermissionId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401111309_Fix_RolePermission_QueryFilter'
)
BEGIN
    ALTER TABLE [RolePermissions] DROP CONSTRAINT [FK_RolePermissions_Roles_RoleId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401111309_Fix_RolePermission_QueryFilter'
)
BEGIN
    ALTER TABLE [RolePermissions] ADD CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401111309_Fix_RolePermission_QueryFilter'
)
BEGIN
    ALTER TABLE [RolePermissions] ADD CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260401111309_Fix_RolePermission_QueryFilter'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260401111309_Fix_RolePermission_QueryFilter', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260402054656_StageA_LockAuditTenant'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260402054656_StageA_LockAuditTenant', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    DROP TABLE [AspNetRoleClaims];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    DROP TABLE [AspNetUserClaims];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    DROP TABLE [AspNetUserLogins];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    DROP TABLE [AspNetUserRoles];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    DROP TABLE [AspNetUserTokens];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    DROP TABLE [AspNetRoles];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    DROP TABLE [AspNetUsers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    CREATE TABLE [StoreBankAccounts] (
        [Id] int NOT NULL IDENTITY,
        [BankCode] nvarchar(30) NOT NULL,
        [BankName] nvarchar(100) NOT NULL,
        [AccountNumber] nvarchar(50) NOT NULL,
        [AccountName] nvarchar(200) NOT NULL,
        [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit),
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [QrRenderMode] int NOT NULL,
        [ConfirmMode] int NOT NULL,
        [ProviderCode] nvarchar(50) NOT NULL DEFAULT N'LOCAL',
        [NoteTemplate] nvarchar(500) NULL,
        [VietQrBankBin] nvarchar(100) NULL,
        [ApiClientId] nvarchar(500) NULL,
        [ApiSecretEncrypted] nvarchar(max) NULL,
        [CallbackSecret] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_StoreBankAccounts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StoreBankAccounts_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    CREATE TABLE [PosPaymentQrRequests] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [BankAccountId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Content] nvarchar(300) NOT NULL,
        [QrRenderMode] int NOT NULL,
        [ConfirmMode] int NOT NULL,
        [Status] int NOT NULL,
        [RequestCode] nvarchar(80) NOT NULL,
        [ProviderTransactionId] nvarchar(200) NULL,
        [QrDataUrl] nvarchar(max) NULL,
        [QrRawText] nvarchar(max) NULL,
        [ProviderRawResponseJson] nvarchar(max) NULL,
        [CallbackRawJson] nvarchar(max) NULL,
        [ExpireAtUtc] datetime2 NOT NULL,
        [PaidAtUtc] datetime2 NULL,
        [ManualConfirmedByUserId] int NULL,
        [ManualConfirmedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_PosPaymentQrRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PosPaymentQrRequests_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PosPaymentQrRequests_StoreBankAccounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [StoreBankAccounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PosPaymentQrRequests_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    CREATE INDEX [IX_PosPaymentQrRequests_BankAccountId] ON [PosPaymentQrRequests] ([BankAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    CREATE INDEX [IX_PosPaymentQrRequests_OrderId] ON [PosPaymentQrRequests] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    CREATE INDEX [IX_PosPaymentQrRequests_StoreId_OrderId_Status] ON [PosPaymentQrRequests] ([StoreId], [OrderId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PosPaymentQrRequests_StoreId_RequestCode] ON [PosPaymentQrRequests] ([StoreId], [RequestCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StoreBankAccounts_StoreId_AccountNumber] ON [StoreBankAccounts] ([StoreId], [AccountNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    CREATE INDEX [IX_StoreBankAccounts_StoreId_IsDefault] ON [StoreBankAccounts] ([StoreId], [IsDefault]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508160536_Add_POS_BankAccounts_QrRequests'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260508160536_Add_POS_BankAccounts_QrRequests', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260510155348_AddDisplayPromotions'
)
BEGIN
    CREATE TABLE [DisplayPromotions] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(250) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [MediaType] nvarchar(30) NOT NULL,
        [MediaUrl] nvarchar(500) NULL,
        [ButtonText] nvarchar(100) NULL,
        [BackgroundColor] nvarchar(30) NULL,
        [TextColor] nvarchar(30) NULL,
        [SortOrder] int NOT NULL,
        [DurationSeconds] int NOT NULL,
        [StartAt] datetime2 NULL,
        [EndAt] datetime2 NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_DisplayPromotions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DisplayPromotions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260510155348_AddDisplayPromotions'
)
BEGIN
    CREATE INDEX [IX_DisplayPromotions_StoreId_IsActive_SortOrder] ON [DisplayPromotions] ([StoreId], [IsActive], [SortOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260510155348_AddDisplayPromotions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260510155348_AddDisplayPromotions', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260510165446_AddDisplayPromotionPriority'
)
BEGIN
    ALTER TABLE [DisplayPromotions] ADD [CountdownToUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260510165446_AddDisplayPromotionPriority'
)
BEGIN
    ALTER TABLE [DisplayPromotions] ADD [IsFlashSale] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260510165446_AddDisplayPromotionPriority'
)
BEGIN
    ALTER TABLE [DisplayPromotions] ADD [IsFullscreen] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260510165446_AddDisplayPromotionPriority'
)
BEGIN
    ALTER TABLE [DisplayPromotions] ADD [Priority] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260510165446_AddDisplayPromotionPriority'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260510165446_AddDisplayPromotionPriority', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512070930_AddHasInputInvoiceToProductVariant'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD [HasInputInvoice] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512070930_AddHasInputInvoiceToProductVariant'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260512070930_AddHasInputInvoiceToProductVariant', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE TABLE [InputInvoiceHead] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceTemplateCode] nvarchar(50) NULL,
        [InvoiceSeries] nvarchar(50) NULL,
        [InvoiceNumber] nvarchar(50) NULL,
        [InvoiceDate] datetime2 NULL,
        [TaxAuthorityCode] nvarchar(100) NULL,
        [SellerTaxCode] nvarchar(50) NULL,
        [SellerName] nvarchar(300) NULL,
        [SellerAddress] nvarchar(500) NULL,
        [BuyerTaxCode] nvarchar(50) NULL,
        [BuyerName] nvarchar(300) NULL,
        [BuyerAddress] nvarchar(500) NULL,
        [TotalBeforeTax] decimal(18,2) NOT NULL,
        [TotalTaxAmount] decimal(18,2) NOT NULL,
        [TotalPaymentAmount] decimal(18,2) NOT NULL,
        [OriginalFileName] nvarchar(260) NULL,
        [XmlFilePath] nvarchar(500) NULL,
        [XmlHash] nvarchar(128) NULL,
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
        CONSTRAINT [PK_InputInvoiceHead] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InputInvoiceHead_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE TABLE [InputInvoiceDetail] (
        [Id] int NOT NULL IDENTITY,
        [InputInvoiceHeadId] int NOT NULL,
        [LineNo] int NOT NULL,
        [ItemName] nvarchar(500) NOT NULL,
        [UnitName] nvarchar(100) NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [LineAmount] decimal(18,2) NOT NULL,
        [VatRate] nvarchar(50) NULL,
        [VatAmount] decimal(18,2) NOT NULL,
        [Note] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_InputInvoiceDetail] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InputInvoiceDetail_InputInvoiceHead_InputInvoiceHeadId] FOREIGN KEY ([InputInvoiceHeadId]) REFERENCES [InputInvoiceHead] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE TABLE [StockDocumentInputInvoiceMap] (
        [Id] int NOT NULL IDENTITY,
        [StockDocumentId] int NOT NULL,
        [InputInvoiceHeadId] int NOT NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_StockDocumentInputInvoiceMap] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockDocumentInputInvoiceMap_InputInvoiceHead_InputInvoiceHeadId] FOREIGN KEY ([InputInvoiceHeadId]) REFERENCES [InputInvoiceHead] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockDocumentInputInvoiceMap_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockDocumentInputInvoiceMap_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE TABLE [StockDocumentLineInputInvoiceMap] (
        [Id] int NOT NULL IDENTITY,
        [StockDocumentId] int NOT NULL,
        [StockDocumentLineId] int NOT NULL,
        [InputInvoiceDetailId] int NULL,
        [UseInputInvoice] bit NOT NULL,
        [MatchStatus] int NOT NULL,
        [QuantityDifference] decimal(18,3) NOT NULL,
        [AmountDifference] decimal(18,2) NOT NULL,
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
        CONSTRAINT [PK_StockDocumentLineInputInvoiceMap] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockDocumentLineInputInvoiceMap_InputInvoiceDetail_InputInvoiceDetailId] FOREIGN KEY ([InputInvoiceDetailId]) REFERENCES [InputInvoiceDetail] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockDocumentLineInputInvoiceMap_StockDocumentLine_StockDocumentLineId] FOREIGN KEY ([StockDocumentLineId]) REFERENCES [StockDocumentLine] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockDocumentLineInputInvoiceMap_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockDocumentLineInputInvoiceMap_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE INDEX [IX_InputInvoiceDetail_InputInvoiceHeadId_LineNo] ON [InputInvoiceDetail] ([InputInvoiceHeadId], [LineNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE INDEX [IX_InputInvoiceHead_StoreId_SellerTaxCode_InvoiceTemplateCode_InvoiceSeries_InvoiceNumber] ON [InputInvoiceHead] ([StoreId], [SellerTaxCode], [InvoiceTemplateCode], [InvoiceSeries], [InvoiceNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE INDEX [IX_InputInvoiceHead_StoreId_XmlHash] ON [InputInvoiceHead] ([StoreId], [XmlHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE INDEX [IX_StockDocumentInputInvoiceMap_InputInvoiceHeadId] ON [StockDocumentInputInvoiceMap] ([InputInvoiceHeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE INDEX [IX_StockDocumentInputInvoiceMap_StockDocumentId] ON [StockDocumentInputInvoiceMap] ([StockDocumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_InputInvoiceHeadId] ON [StockDocumentInputInvoiceMap] ([StoreId], [StockDocumentId], [InputInvoiceHeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE INDEX [IX_StockDocumentLineInputInvoiceMap_InputInvoiceDetailId] ON [StockDocumentLineInputInvoiceMap] ([InputInvoiceDetailId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE INDEX [IX_StockDocumentLineInputInvoiceMap_StockDocumentId] ON [StockDocumentLineInputInvoiceMap] ([StockDocumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE INDEX [IX_StockDocumentLineInputInvoiceMap_StockDocumentLineId] ON [StockDocumentLineInputInvoiceMap] ([StockDocumentLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE INDEX [IX_StockDocumentLineInputInvoiceMap_StoreId_StockDocumentId_UseInputInvoice] ON [StockDocumentLineInputInvoiceMap] ([StoreId], [StockDocumentId], [UseInputInvoice]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockDocumentLineInputInvoiceMap_StoreId_StockDocumentLineId] ON [StockDocumentLineInputInvoiceMap] ([StoreId], [StockDocumentLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512105018_AddInputInvoiceXmlMapping'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260512105018_AddInputInvoiceXmlMapping', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE TABLE [InvoiceHeads] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [InvoiceNumber] nvarchar(50) NULL,
        [InvoiceDate] datetime2 NOT NULL,
        [BuyerName] nvarchar(250) NULL,
        [BuyerTaxCode] nvarchar(50) NULL,
        [BuyerAddress] nvarchar(500) NULL,
        [TotalQuantity] decimal(18,3) NOT NULL,
        [SubTotal] decimal(18,2) NOT NULL,
        [VatAmount] decimal(18,2) NOT NULL,
        [GrandTotal] decimal(18,2) NOT NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InvoiceHeads] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvoiceHeads_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InvoiceHeads_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE TABLE [InvoiceDetails] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceHeadId] int NOT NULL,
        [OrderLineId] int NULL,
        [ProductVariantId] int NULL,
        [SourceType] int NOT NULL,
        [ItemName] nvarchar(250) NOT NULL,
        [UnitName] nvarchar(100) NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [VatRate] decimal(5,2) NOT NULL,
        [VatAmount] decimal(18,2) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InvoiceDetails] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvoiceDetails_InvoiceHeads_InvoiceHeadId] FOREIGN KEY ([InvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_InvoiceDetails_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InvoiceDetails_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InvoiceDetails_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_InvoiceHeadId] ON [InvoiceDetails] ([InvoiceHeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_OrderLineId] ON [InvoiceDetails] ([OrderLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_ProductVariantId] ON [InvoiceDetails] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_StoreId_InvoiceHeadId] ON [InvoiceDetails] ([StoreId], [InvoiceHeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_StoreId_OrderLineId] ON [InvoiceDetails] ([StoreId], [OrderLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_StoreId_ProductVariantId] ON [InvoiceDetails] ([StoreId], [ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_StoreId_SourceType] ON [InvoiceDetails] ([StoreId], [SourceType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_OrderId] ON [InvoiceHeads] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_InvoiceDate] ON [InvoiceHeads] ([StoreId], [InvoiceDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_InvoiceNumber] ON [InvoiceHeads] ([StoreId], [InvoiceNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_OrderId] ON [InvoiceHeads] ([StoreId], [OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512155006_AddInvoiceHeadAndInvoiceDetail'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260512155006_AddInvoiceHeadAndInvoiceDetail', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260515075405_AddInvoiceLockFields'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [IsLocked] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260515075405_AddInvoiceLockFields'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [LockReason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260515075405_AddInvoiceLockFields'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [LockedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260515075405_AddInvoiceLockFields'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [LockedByUserId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260515075405_AddInvoiceLockFields'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_IsLocked] ON [InvoiceHeads] ([StoreId], [IsLocked]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260515075405_AddInvoiceLockFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260515075405_AddInvoiceLockFields', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260515151021_AddAdminMenu'
)
BEGIN
    CREATE TABLE [AdminMenuItems] (
        [Id] int NOT NULL IDENTITY,
        [ParentId] int NULL,
        [Title] nvarchar(120) NOT NULL,
        [Area] nvarchar(80) NULL,
        [Controller] nvarchar(120) NULL,
        [Action] nvarchar(120) NULL,
        [Url] nvarchar(300) NULL,
        [Icon] nvarchar(120) NULL,
        [PermissionCode] nvarchar(200) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [IsSystem] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_AdminMenuItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AdminMenuItems_AdminMenuItems_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [AdminMenuItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AdminMenuItems_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260515151021_AddAdminMenu'
)
BEGIN
    CREATE INDEX [IX_AdminMenuItems_ParentId] ON [AdminMenuItems] ([ParentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260515151021_AddAdminMenu'
)
BEGIN
    CREATE INDEX [IX_AdminMenuItems_StoreId_ParentId_SortOrder] ON [AdminMenuItems] ([StoreId], [ParentId], [SortOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260515151021_AddAdminMenu'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260515151021_AddAdminMenu', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE TABLE [InventoryAdjustmentDocuments] (
        [Id] int NOT NULL IDENTITY,
        [DocumentNo] nvarchar(50) NOT NULL,
        [DocumentDate] datetime2 NOT NULL,
        [WarehouseId] int NOT NULL,
        [AdjustmentType] int NOT NULL,
        [Status] int NOT NULL,
        [ReasonType] int NOT NULL,
        [Note] nvarchar(1000) NULL,
        [ApprovalNote] nvarchar(1000) NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [SubmittedByUserId] int NULL,
        [ApprovedAtUtc] datetime2 NULL,
        [ApprovedByUserId] int NULL,
        [RejectedAtUtc] datetime2 NULL,
        [RejectedByUserId] int NULL,
        [CancelledAtUtc] datetime2 NULL,
        [CancelledByUserId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InventoryAdjustmentDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryAdjustmentDocuments_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_InventoryAdjustmentDocuments_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE TABLE [InventoryAdjustmentLines] (
        [Id] int NOT NULL IDENTITY,
        [InventoryAdjustmentDocumentId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [UnitId] int NULL,
        [ProductUnitConversionId] int NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Factor] decimal(18,6) NOT NULL,
        [BaseQuantity] decimal(18,3) NOT NULL,
        [UnitCost] decimal(18,6) NULL,
        [ProvisionalUnitCost] decimal(18,6) NULL,
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
        CONSTRAINT [PK_InventoryAdjustmentLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryAdjustmentLines_InventoryAdjustmentDocuments_InventoryAdjustmentDocumentId] FOREIGN KEY ([InventoryAdjustmentDocumentId]) REFERENCES [InventoryAdjustmentDocuments] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_InventoryAdjustmentLines_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryAdjustmentLines_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryAdjustmentLines_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_InventoryAdjustmentLines_Unit_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE INDEX [IX_InventoryAdjustmentDocument_Store_Status_Date] ON [InventoryAdjustmentDocuments] ([StoreId], [Status], [DocumentDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE INDEX [IX_InventoryAdjustmentDocument_Store_Warehouse_Date] ON [InventoryAdjustmentDocuments] ([StoreId], [WarehouseId], [DocumentDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE INDEX [IX_InventoryAdjustmentDocuments_WarehouseId] ON [InventoryAdjustmentDocuments] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE UNIQUE INDEX [UX_InventoryAdjustmentDocument_Store_DocumentNo] ON [InventoryAdjustmentDocuments] ([StoreId], [DocumentNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE INDEX [IX_InventoryAdjustmentLine_Store_Document] ON [InventoryAdjustmentLines] ([StoreId], [InventoryAdjustmentDocumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE INDEX [IX_InventoryAdjustmentLine_Store_ProductVariant] ON [InventoryAdjustmentLines] ([StoreId], [ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE INDEX [IX_InventoryAdjustmentLines_InventoryAdjustmentDocumentId] ON [InventoryAdjustmentLines] ([InventoryAdjustmentDocumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE INDEX [IX_InventoryAdjustmentLines_ProductUnitConversionId] ON [InventoryAdjustmentLines] ([ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE INDEX [IX_InventoryAdjustmentLines_ProductVariantId] ON [InventoryAdjustmentLines] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    CREATE INDEX [IX_InventoryAdjustmentLines_UnitId] ON [InventoryAdjustmentLines] ([UnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260516162309_AddInventoryAdjustmentDocuments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260516162309_AddInventoryAdjustmentDocuments', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260517184840_AddDocumentTitleToStockDocument'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [DocumentTitle] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260517184840_AddDocumentTitleToStockDocument'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260517184840_AddDocumentTitleToStockDocument', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260518031024_AddDocumentNameToStockCountDocument'
)
BEGIN
    ALTER TABLE [StockCountDocument] ADD [DocumentName] nvarchar(250) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260518031024_AddDocumentNameToStockCountDocument'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260518031024_AddDocumentNameToStockCountDocument', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260529072508_AddPOSTerminalDevices'
)
BEGIN
    CREATE TABLE [POSTerminalDevices] (
        [Id] int NOT NULL IDENTITY,
        [TerminalId] int NOT NULL,
        [DeviceKey] nvarchar(100) NOT NULL,
        [DeviceName] nvarchar(150) NULL,
        [UserAgent] nvarchar(500) NULL,
        [LastIp] nvarchar(100) NULL,
        [LastSeenAtUtc] datetime2 NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_POSTerminalDevices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_POSTerminalDevices_POSTerminals_TerminalId] FOREIGN KEY ([TerminalId]) REFERENCES [POSTerminals] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_POSTerminalDevices_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260529072508_AddPOSTerminalDevices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_POSTerminalDevices_StoreId_DeviceKey] ON [POSTerminalDevices] ([StoreId], [DeviceKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260529072508_AddPOSTerminalDevices'
)
BEGIN
    CREATE INDEX [IX_POSTerminalDevices_StoreId_TerminalId_IsActive] ON [POSTerminalDevices] ([StoreId], [TerminalId], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260529072508_AddPOSTerminalDevices'
)
BEGIN
    CREATE INDEX [IX_POSTerminalDevices_TerminalId] ON [POSTerminalDevices] ([TerminalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260529072508_AddPOSTerminalDevices'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260529072508_AddPOSTerminalDevices', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260529082336_AddEmployeeInfoToUserInStore'
)
BEGIN
    ALTER TABLE [UserInStores] ADD [JoinedDate] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260529082336_AddEmployeeInfoToUserInStore'
)
BEGIN
    ALTER TABLE [UserInStores] ADD [Note] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260529082336_AddEmployeeInfoToUserInStore'
)
BEGIN
    ALTER TABLE [UserInStores] ADD [PhoneNumber] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260529082336_AddEmployeeInfoToUserInStore'
)
BEGIN
    ALTER TABLE [UserInStores] ADD [PositionName] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260529082336_AddEmployeeInfoToUserInStore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260529082336_AddEmployeeInfoToUserInStore', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    DECLARE @var67 sysname;
    SELECT @var67 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'IsActive');
    IF @var67 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT [' + @var67 + '];');
    ALTER TABLE [Customers] ADD DEFAULT CAST(1 AS bit) FOR [IsActive];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [Code] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [CustomerGroup] nvarchar(30) NOT NULL DEFAULT N'MEMBER';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [Email] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [HaveDebt] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [ImportedRewardAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [IsImportedFromOldSystem] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [OldCustomerId] bigint NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [TaxCode] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    CREATE INDEX [IX_Customers_StoreId_Code] ON [Customers] ([StoreId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    CREATE INDEX [IX_Customers_StoreId_OldCustomerId] ON [Customers] ([StoreId], [OldCustomerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    CREATE INDEX [IX_Customers_StoreId_TaxCode] ON [Customers] ([StoreId], [TaxCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075057_AddCustomerLegacyFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603075057_AddCustomerLegacyFields', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075427_Add-RewardSettings'
)
BEGIN
    CREATE TABLE [RewardSettings] (
        [Id] int NOT NULL IDENTITY,
        [MoneyPerPoint] decimal(18,2) NOT NULL,
        [PointsPerVoucher] int NOT NULL,
        [VoucherValue] decimal(18,2) NOT NULL,
        [IsEnabled] bit NOT NULL DEFAULT CAST(1 AS bit),
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_RewardSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RewardSettings_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075427_Add-RewardSettings'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RewardSettings_StoreId] ON [RewardSettings] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603075427_Add-RewardSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603075427_Add-RewardSettings', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603080145_AddCustomerRewardLedger'
)
BEGIN
    CREATE TABLE [CustomerRewardLedgers] (
        [Id] int NOT NULL IDENTITY,
        [CustomerId] int NOT NULL,
        [Type] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [OrderId] int NULL,
        [SalesReturnId] int NULL,
        [VoucherId] int NULL,
        [ReferenceCode] nvarchar(100) NULL,
        [Description] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_CustomerRewardLedgers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomerRewardLedgers_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CustomerRewardLedgers_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CustomerRewardLedgers_SalesReturns_SalesReturnId] FOREIGN KEY ([SalesReturnId]) REFERENCES [SalesReturns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CustomerRewardLedgers_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603080145_AddCustomerRewardLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardLedgers_CustomerId] ON [CustomerRewardLedgers] ([CustomerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603080145_AddCustomerRewardLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardLedgers_OrderId] ON [CustomerRewardLedgers] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603080145_AddCustomerRewardLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardLedgers_SalesReturnId] ON [CustomerRewardLedgers] ([SalesReturnId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603080145_AddCustomerRewardLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardLedgers_StoreId_CustomerId_CreatedAtUtc] ON [CustomerRewardLedgers] ([StoreId], [CustomerId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603080145_AddCustomerRewardLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardLedgers_StoreId_OrderId] ON [CustomerRewardLedgers] ([StoreId], [OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603080145_AddCustomerRewardLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardLedgers_StoreId_ReferenceCode] ON [CustomerRewardLedgers] ([StoreId], [ReferenceCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603080145_AddCustomerRewardLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardLedgers_StoreId_SalesReturnId] ON [CustomerRewardLedgers] ([StoreId], [SalesReturnId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603080145_AddCustomerRewardLedger'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603080145_AddCustomerRewardLedger', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603104911_CreateCustomerRewardVoucherTableManual'
)
BEGIN
    CREATE TABLE [CustomerRewardVouchers] (
        [Id] int NOT NULL IDENTITY,
        [StoreId] int NOT NULL,
        [CustomerId] int NOT NULL,
        [VoucherCode] nvarchar(50) NOT NULL,
        [Value] decimal(18,2) NOT NULL,
        [RequiredAmount] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [IssuedAtUtc] datetime2 NOT NULL,
        [UsedAtUtc] datetime2 NULL,
        [UsedOrderId] int NULL,
        [Description] nvarchar(500) NULL,
        [ReferenceCode] nvarchar(100) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CustomerRewardVouchers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomerRewardVouchers_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CustomerRewardVouchers_Orders_UsedOrderId] FOREIGN KEY ([UsedOrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CustomerRewardVouchers_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603104911_CreateCustomerRewardVoucherTableManual'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardVouchers_CustomerId] ON [CustomerRewardVouchers] ([CustomerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603104911_CreateCustomerRewardVoucherTableManual'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardVouchers_StoreId_CustomerId_Status] ON [CustomerRewardVouchers] ([StoreId], [CustomerId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603104911_CreateCustomerRewardVoucherTableManual'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardVouchers_StoreId_ReferenceCode] ON [CustomerRewardVouchers] ([StoreId], [ReferenceCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603104911_CreateCustomerRewardVoucherTableManual'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerRewardVouchers_StoreId_VoucherCode] ON [CustomerRewardVouchers] ([StoreId], [VoucherCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603104911_CreateCustomerRewardVoucherTableManual'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardVouchers_UsedOrderId] ON [CustomerRewardVouchers] ([UsedOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603104911_CreateCustomerRewardVoucherTableManual'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603104911_CreateCustomerRewardVoucherTableManual', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603114933_AddVoucherRelationToRewardLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardLedgers_StoreId_VoucherId] ON [CustomerRewardLedgers] ([StoreId], [VoucherId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603114933_AddVoucherRelationToRewardLedger'
)
BEGIN
    CREATE INDEX [IX_CustomerRewardLedgers_VoucherId] ON [CustomerRewardLedgers] ([VoucherId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603114933_AddVoucherRelationToRewardLedger'
)
BEGIN
    ALTER TABLE [CustomerRewardLedgers] ADD CONSTRAINT [FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId] FOREIGN KEY ([VoucherId]) REFERENCES [CustomerRewardVouchers] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603114933_AddVoucherRelationToRewardLedger'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603114933_AddVoucherRelationToRewardLedger', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603121644_AddRewardRuleFieldsToCatalog'
)
BEGIN
    ALTER TABLE [Products] ADD [IsRewardEligibleOverride] bit NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603121644_AddRewardRuleFieldsToCatalog'
)
BEGIN
    ALTER TABLE [Products] ADD [RewardBulkExcludeQuantity] decimal(18,4) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603121644_AddRewardRuleFieldsToCatalog'
)
BEGIN
    ALTER TABLE [Category] ADD [IsRewardEligible] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260603121644_AddRewardRuleFieldsToCatalog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260603121644_AddRewardRuleFieldsToCatalog', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604052737_AddOrderRewardVoucher'
)
BEGIN
    ALTER TABLE [Orders] ADD [VoucherDiscountTotal] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604052737_AddOrderRewardVoucher'
)
BEGIN
    CREATE TABLE [OrderRewardVouchers] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [VoucherId] int NOT NULL,
        [VoucherValue] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_OrderRewardVouchers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderRewardVouchers_CustomerRewardVouchers_VoucherId] FOREIGN KEY ([VoucherId]) REFERENCES [CustomerRewardVouchers] ([Id]),
        CONSTRAINT [FK_OrderRewardVouchers_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]),
        CONSTRAINT [FK_OrderRewardVouchers_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604052737_AddOrderRewardVoucher'
)
BEGIN
    CREATE INDEX [IX_OrderRewardVouchers_OrderId] ON [OrderRewardVouchers] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604052737_AddOrderRewardVoucher'
)
BEGIN
    CREATE INDEX [IX_OrderRewardVouchers_StoreId_OrderId] ON [OrderRewardVouchers] ([StoreId], [OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604052737_AddOrderRewardVoucher'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrderRewardVouchers_StoreId_VoucherId] ON [OrderRewardVouchers] ([StoreId], [VoucherId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604052737_AddOrderRewardVoucher'
)
BEGIN
    CREATE INDEX [IX_OrderRewardVouchers_VoucherId] ON [OrderRewardVouchers] ([VoucherId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604052737_AddOrderRewardVoucher'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260604052737_AddOrderRewardVoucher', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604073238_FixOrderRewardVoucherIndex'
)
BEGIN
    DROP INDEX [IX_OrderRewardVouchers_StoreId_VoucherId] ON [OrderRewardVouchers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604073238_FixOrderRewardVoucherIndex'
)
BEGIN
    CREATE INDEX [IX_OrderRewardVouchers_StoreId_VoucherId] ON [OrderRewardVouchers] ([StoreId], [VoucherId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604073238_FixOrderRewardVoucherIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260604073238_FixOrderRewardVoucherIndex', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604120734_AddUniqueOpenPOSShiftPerTerminal'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShifts_StoreId_TerminalId_Status] ON [POSShifts] ([StoreId], [TerminalId], [Status]) WHERE [Status] = 1 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604120734_AddUniqueOpenPOSShiftPerTerminal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260604120734_AddUniqueOpenPOSShiftPerTerminal', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604151002_AddPOSShiftCashDenominations'
)
BEGIN
    CREATE TABLE [POSShiftCashDenominations] (
        [Id] int NOT NULL IDENTITY,
        [POSShiftId] int NOT NULL,
        [EntryType] tinyint NOT NULL,
        [DenominationValue] int NOT NULL,
        [Quantity] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL DEFAULT 0.0,
        [Note] nvarchar(300) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_POSShiftCashDenominations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_POSShiftCashDenominations_Amount_NonNegative] CHECK ([Amount] >= 0),
        CONSTRAINT [CK_POSShiftCashDenominations_DenominationValue_NonNegative] CHECK ([DenominationValue] >= 0),
        CONSTRAINT [CK_POSShiftCashDenominations_Quantity_NonNegative] CHECK ([Quantity] >= 0),
        CONSTRAINT [FK_POSShiftCashDenominations_POSShifts_POSShiftId] FOREIGN KEY ([POSShiftId]) REFERENCES [POSShifts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_POSShiftCashDenominations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604151002_AddPOSShiftCashDenominations'
)
BEGIN
    CREATE INDEX [IX_POSShiftCashDenominations_POSShiftId] ON [POSShiftCashDenominations] ([POSShiftId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604151002_AddPOSShiftCashDenominations'
)
BEGIN
    CREATE INDEX [IX_POSShiftCashDenominations_StoreId_POSShiftId_EntryType] ON [POSShiftCashDenominations] ([StoreId], [POSShiftId], [EntryType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604151002_AddPOSShiftCashDenominations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShiftCashDenominations_StoreId_POSShiftId_EntryType_DenominationValue] ON [POSShiftCashDenominations] ([StoreId], [POSShiftId], [EntryType], [DenominationValue]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604151002_AddPOSShiftCashDenominations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260604151002_AddPOSShiftCashDenominations', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE TABLE [POSShiftHandoverSlips] (
        [Id] int NOT NULL IDENTITY,
        [SlipCode] nvarchar(50) NOT NULL,
        [BarcodeValue] nvarchar(100) NOT NULL,
        [Status] tinyint NOT NULL,
        [TerminalId] int NULL,
        [WarehouseId] int NOT NULL,
        [CreatedByUserId] int NOT NULL,
        [AssignedToUserId] int NULL,
        [OpeningCashTotal] decimal(18,2) NOT NULL DEFAULT 0.0,
        [UsedPOSShiftId] int NULL,
        [PrintedAtUtc] datetime2 NULL,
        [UsedAtUtc] datetime2 NULL,
        [UsedByUserId] int NULL,
        [CancelledAtUtc] datetime2 NULL,
        [CancelledByUserId] int NULL,
        [CancelReason] nvarchar(300) NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_POSShiftHandoverSlips] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_POSShiftHandoverSlips_OpeningCashTotal_NonNegative] CHECK ([OpeningCashTotal] >= 0),
        CONSTRAINT [FK_POSShiftHandoverSlips_POSShifts_UsedPOSShiftId] FOREIGN KEY ([UsedPOSShiftId]) REFERENCES [POSShifts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_POSShiftHandoverSlips_POSTerminals_TerminalId] FOREIGN KEY ([TerminalId]) REFERENCES [POSTerminals] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_POSShiftHandoverSlips_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_POSShiftHandoverSlips_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE TABLE [POSShiftHandoverSlipDenominations] (
        [Id] int NOT NULL IDENTITY,
        [POSShiftHandoverSlipId] int NOT NULL,
        [DenominationValue] int NOT NULL,
        [Quantity] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL DEFAULT 0.0,
        [Note] nvarchar(300) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_POSShiftHandoverSlipDenominations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_POSShiftHandoverSlipDenominations_Amount_NonNegative] CHECK ([Amount] >= 0),
        CONSTRAINT [CK_POSShiftHandoverSlipDenominations_DenominationValue_NonNegative] CHECK ([DenominationValue] >= 0),
        CONSTRAINT [CK_POSShiftHandoverSlipDenominations_Quantity_NonNegative] CHECK ([Quantity] >= 0),
        CONSTRAINT [FK_POSShiftHandoverSlipDenominations_POSShiftHandoverSlips_POSShiftHandoverSlipId] FOREIGN KEY ([POSShiftHandoverSlipId]) REFERENCES [POSShiftHandoverSlips] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_POSShiftHandoverSlipDenominations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftHandoverSlipDenominations_POSShiftHandoverSlipId] ON [POSShiftHandoverSlipDenominations] ([POSShiftHandoverSlipId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftHandoverSlipDenominations_StoreId_POSShiftHandoverSlipId] ON [POSShiftHandoverSlipDenominations] ([StoreId], [POSShiftHandoverSlipId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShiftHandoverSlipDenominations_StoreId_POSShiftHandoverSlipId_DenominationValue] ON [POSShiftHandoverSlipDenominations] ([StoreId], [POSShiftHandoverSlipId], [DenominationValue]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShiftHandoverSlips_StoreId_BarcodeValue] ON [POSShiftHandoverSlips] ([StoreId], [BarcodeValue]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftHandoverSlips_StoreId_CreatedAtUtc] ON [POSShiftHandoverSlips] ([StoreId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShiftHandoverSlips_StoreId_SlipCode] ON [POSShiftHandoverSlips] ([StoreId], [SlipCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftHandoverSlips_StoreId_Status] ON [POSShiftHandoverSlips] ([StoreId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftHandoverSlips_StoreId_TerminalId] ON [POSShiftHandoverSlips] ([StoreId], [TerminalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftHandoverSlips_StoreId_WarehouseId] ON [POSShiftHandoverSlips] ([StoreId], [WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftHandoverSlips_TerminalId] ON [POSShiftHandoverSlips] ([TerminalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftHandoverSlips_UsedPOSShiftId] ON [POSShiftHandoverSlips] ([UsedPOSShiftId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftHandoverSlips_WarehouseId] ON [POSShiftHandoverSlips] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604154338_AddPOSShiftHandoverSlips'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260604154338_AddPOSShiftHandoverSlips', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604175143_AddPOSShiftClosingSlips'
)
BEGIN
    CREATE TABLE [POSShiftClosingSlips] (
        [Id] int NOT NULL IDENTITY,
        [POSShiftId] int NOT NULL,
        [SlipCode] nvarchar(50) NOT NULL,
        [BarcodeValue] nvarchar(100) NOT NULL,
        [Status] tinyint NOT NULL,
        [OpenedByUserId] int NOT NULL,
        [ClosedByUserId] int NOT NULL,
        [OpenedAtUtc] datetime2 NOT NULL,
        [ClosedAtUtc] datetime2 NOT NULL,
        [OpeningCash] decimal(18,2) NOT NULL,
        [CashSalesTotal] decimal(18,2) NOT NULL,
        [NonCashSalesTotal] decimal(18,2) NOT NULL,
        [CashRefundTotal] decimal(18,2) NOT NULL,
        [NonCashRefundTotal] decimal(18,2) NOT NULL,
        [RefundCount] int NOT NULL,
        [VoidCount] int NOT NULL,
        [CashInTotal] decimal(18,2) NOT NULL,
        [CashOutTotal] decimal(18,2) NOT NULL,
        [ClosingCashExpected] decimal(18,2) NOT NULL,
        [ClosingCashActual] decimal(18,2) NOT NULL,
        [CashDifference] decimal(18,2) NOT NULL,
        [CloseNote] nvarchar(500) NULL,
        [PrintedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_POSShiftClosingSlips] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_POSShiftClosingSlips_POSShifts_POSShiftId] FOREIGN KEY ([POSShiftId]) REFERENCES [POSShifts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_POSShiftClosingSlips_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604175143_AddPOSShiftClosingSlips'
)
BEGIN
    CREATE TABLE [POSShiftClosingSlipDenominations] (
        [Id] int NOT NULL IDENTITY,
        [POSShiftClosingSlipId] int NOT NULL,
        [DenominationValue] int NOT NULL,
        [Quantity] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_POSShiftClosingSlipDenominations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_POSShiftClosingSlipDenominations_Amount_NonNegative] CHECK ([Amount] >= 0),
        CONSTRAINT [CK_POSShiftClosingSlipDenominations_DenominationValue_NonNegative] CHECK ([DenominationValue] >= 0),
        CONSTRAINT [CK_POSShiftClosingSlipDenominations_Quantity_NonNegative] CHECK ([Quantity] >= 0),
        CONSTRAINT [FK_POSShiftClosingSlipDenominations_POSShiftClosingSlips_POSShiftClosingSlipId] FOREIGN KEY ([POSShiftClosingSlipId]) REFERENCES [POSShiftClosingSlips] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_POSShiftClosingSlipDenominations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604175143_AddPOSShiftClosingSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftClosingSlipDenominations_POSShiftClosingSlipId] ON [POSShiftClosingSlipDenominations] ([POSShiftClosingSlipId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604175143_AddPOSShiftClosingSlips'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShiftClosingSlipDenominations_StoreId_POSShiftClosingSlipId_DenominationValue] ON [POSShiftClosingSlipDenominations] ([StoreId], [POSShiftClosingSlipId], [DenominationValue]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604175143_AddPOSShiftClosingSlips'
)
BEGIN
    CREATE INDEX [IX_POSShiftClosingSlips_POSShiftId] ON [POSShiftClosingSlips] ([POSShiftId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604175143_AddPOSShiftClosingSlips'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShiftClosingSlips_StoreId_BarcodeValue] ON [POSShiftClosingSlips] ([StoreId], [BarcodeValue]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604175143_AddPOSShiftClosingSlips'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShiftClosingSlips_StoreId_POSShiftId] ON [POSShiftClosingSlips] ([StoreId], [POSShiftId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604175143_AddPOSShiftClosingSlips'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_POSShiftClosingSlips_StoreId_SlipCode] ON [POSShiftClosingSlips] ([StoreId], [SlipCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260604175143_AddPOSShiftClosingSlips'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260604175143_AddPOSShiftClosingSlips', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260605141953_AddStockDocumentRevisionRequestFields'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [HasRevisionRequest] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260605141953_AddStockDocumentRevisionRequestFields'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [RevisionRequestNote] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260605141953_AddStockDocumentRevisionRequestFields'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [RevisionRequestedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260605141953_AddStockDocumentRevisionRequestFields'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [RevisionRequestedByUserId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260605141953_AddStockDocumentRevisionRequestFields'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [RevisionResolvedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260605141953_AddStockDocumentRevisionRequestFields'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [RevisionResolvedByUserId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260605141953_AddStockDocumentRevisionRequestFields'
)
BEGIN
    CREATE INDEX [IX_StockDocument_HasRevisionRequest] ON [StockDocument] ([HasRevisionRequest]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260605141953_AddStockDocumentRevisionRequestFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260605141953_AddStockDocumentRevisionRequestFields', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606041246_AddWholesalePriceAndCustomerPriceTier'
)
BEGIN
    ALTER TABLE [ProductVariant] ADD [WholesalePrice] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606041246_AddWholesalePriceAndCustomerPriceTier'
)
BEGIN
    ALTER TABLE [ProductUnitConversion] ADD [WholesalePrice] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606041246_AddWholesalePriceAndCustomerPriceTier'
)
BEGIN
    ALTER TABLE [Customers] ADD [PriceTier] nvarchar(30) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606041246_AddWholesalePriceAndCustomerPriceTier'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260606041246_AddWholesalePriceAndCustomerPriceTier', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606164300_AddProductBarcodeVerificationRequests'
)
BEGIN
    CREATE TABLE [ProductBarcodeVerificationRequests] (
        [Id] int NOT NULL IDENTITY,
        [StoreId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [ProductUnitConversionId] int NOT NULL,
        [StockDocumentId] int NULL,
        [ProductNameSnapshot] nvarchar(500) NOT NULL,
        [UnitNameSnapshot] nvarchar(100) NOT NULL,
        [FactorSnapshot] decimal(18,3) NOT NULL,
        [SuggestedBarcode] nvarchar(100) NULL,
        [RequestType] int NOT NULL,
        [Status] int NOT NULL,
        [EmployeeNote] nvarchar(1000) NULL,
        [ManagerNote] nvarchar(1000) NULL,
        [RequestedByUserId] int NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [ResolvedByUserId] int NULL,
        [ResolvedAtUtc] datetime2 NULL,
        [CreatedBarcodeId] int NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_ProductBarcodeVerificationRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductBarcodeVerificationRequests_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductBarcodeVerificationRequests_ProductVariantUnitBarcode_CreatedBarcodeId] FOREIGN KEY ([CreatedBarcodeId]) REFERENCES [ProductVariantUnitBarcode] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductBarcodeVerificationRequests_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductBarcodeVerificationRequests_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductBarcodeVerificationRequests_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606164300_AddProductBarcodeVerificationRequests'
)
BEGIN
    CREATE INDEX [IX_ProductBarcodeVerificationRequests_CreatedBarcodeId] ON [ProductBarcodeVerificationRequests] ([CreatedBarcodeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606164300_AddProductBarcodeVerificationRequests'
)
BEGIN
    CREATE INDEX [IX_ProductBarcodeVerificationRequests_ProductUnitConversionId] ON [ProductBarcodeVerificationRequests] ([ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606164300_AddProductBarcodeVerificationRequests'
)
BEGIN
    CREATE INDEX [IX_ProductBarcodeVerificationRequests_ProductVariantId] ON [ProductBarcodeVerificationRequests] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606164300_AddProductBarcodeVerificationRequests'
)
BEGIN
    CREATE INDEX [IX_ProductBarcodeVerificationRequests_StockDocumentId_IsDeleted] ON [ProductBarcodeVerificationRequests] ([StockDocumentId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606164300_AddProductBarcodeVerificationRequests'
)
BEGIN
    CREATE INDEX [IX_ProductBarcodeVerificationRequests_StoreId_ProductUnitConversionId_Status_IsDeleted] ON [ProductBarcodeVerificationRequests] ([StoreId], [ProductUnitConversionId], [Status], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606164300_AddProductBarcodeVerificationRequests'
)
BEGIN
    CREATE INDEX [IX_ProductBarcodeVerificationRequests_StoreId_SuggestedBarcode_Status_IsDeleted] ON [ProductBarcodeVerificationRequests] ([StoreId], [SuggestedBarcode], [Status], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260606164300_AddProductBarcodeVerificationRequests'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260606164300_AddProductBarcodeVerificationRequests', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607070207_AddProductUnitConversionIdToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [ProductUnitConversionId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607070207_AddProductUnitConversionIdToOrderLine'
)
BEGIN
    CREATE INDEX [IX_OrderLines_StoreId_ProductUnitConversionId] ON [OrderLines] ([StoreId], [ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607070207_AddProductUnitConversionIdToOrderLine'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260607070207_AddProductUnitConversionIdToOrderLine', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607084614_AddPromotionPhase1'
)
BEGIN
    ALTER TABLE [Orders] ADD [PromotionDiscountTotal] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607084614_AddPromotionPhase1'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [OriginalUnitPrice] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607084614_AddPromotionPhase1'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [PromotionDiscount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607084614_AddPromotionPhase1'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [PromotionId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607084614_AddPromotionPhase1'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [PromotionName] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607084614_AddPromotionPhase1'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260607084614_AddPromotionPhase1', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607085106_AddPromotionPhase2'
)
BEGIN
    CREATE TABLE [Promotions] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Type] tinyint NOT NULL,
        [DiscountType] tinyint NOT NULL,
        [DiscountValue] decimal(18,2) NOT NULL,
        [StartAtUtc] datetime2 NOT NULL,
        [EndAtUtc] datetime2 NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_Promotions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Promotions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607085106_AddPromotionPhase2'
)
BEGIN
    CREATE TABLE [PromotionItems] (
        [Id] int NOT NULL IDENTITY,
        [PromotionId] int NOT NULL,
        [ProductId] int NOT NULL,
        [VariantId] int NULL,
        [ProductUnitConversionId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_PromotionItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PromotionItems_Promotions_PromotionId] FOREIGN KEY ([PromotionId]) REFERENCES [Promotions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PromotionItems_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607085106_AddPromotionPhase2'
)
BEGIN
    CREATE INDEX [IX_PromotionItems_PromotionId] ON [PromotionItems] ([PromotionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607085106_AddPromotionPhase2'
)
BEGIN
    CREATE INDEX [IX_PromotionItems_StoreId_ProductId] ON [PromotionItems] ([StoreId], [ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607085106_AddPromotionPhase2'
)
BEGIN
    CREATE INDEX [IX_PromotionItems_StoreId_ProductUnitConversionId] ON [PromotionItems] ([StoreId], [ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607085106_AddPromotionPhase2'
)
BEGIN
    CREATE INDEX [IX_PromotionItems_StoreId_PromotionId] ON [PromotionItems] ([StoreId], [PromotionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607085106_AddPromotionPhase2'
)
BEGIN
    CREATE INDEX [IX_PromotionItems_StoreId_VariantId] ON [PromotionItems] ([StoreId], [VariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607085106_AddPromotionPhase2'
)
BEGIN
    CREATE INDEX [IX_Promotions_StoreId_IsActive_StartAtUtc_EndAtUtc] ON [Promotions] ([StoreId], [IsActive], [StartAtUtc], [EndAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607085106_AddPromotionPhase2'
)
BEGIN
    CREATE INDEX [IX_Promotions_StoreId_Type_IsDeleted] ON [Promotions] ([StoreId], [Type], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607085106_AddPromotionPhase2'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260607085106_AddPromotionPhase2', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607110148_AddPromotionPhase3'
)
BEGIN
    ALTER TABLE [Promotions] ADD [Priority] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607110148_AddPromotionPhase3'
)
BEGIN
    ALTER TABLE [PromotionItems] ADD [MinQuantity] decimal(18,4) NOT NULL DEFAULT 1.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607110148_AddPromotionPhase3'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260607110148_AddPromotionPhase3', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607130258_AddPromotionPhase4'
)
BEGIN
    ALTER TABLE [Promotions] ADD [CustomerPriceTier] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607130258_AddPromotionPhase4'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260607130258_AddPromotionPhase4', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607150920_AddComboPromotionPhase1'
)
BEGIN
    ALTER TABLE [Promotions] ADD [ComboFixedPrice] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607150920_AddComboPromotionPhase1'
)
BEGIN
    ALTER TABLE [Promotions] ADD [ComboNote] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607150920_AddComboPromotionPhase1'
)
BEGIN
    ALTER TABLE [Orders] ADD [ComboDiscountTotal] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607150920_AddComboPromotionPhase1'
)
BEGIN
    ALTER TABLE [Orders] ADD [ComboPromotionId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607150920_AddComboPromotionPhase1'
)
BEGIN
    ALTER TABLE [Orders] ADD [ComboPromotionName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607150920_AddComboPromotionPhase1'
)
BEGIN
    ALTER TABLE [Orders] ADD [ComboPromotionNote] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607150920_AddComboPromotionPhase1'
)
BEGIN
    CREATE TABLE [PromotionComboRule] (
        [Id] int NOT NULL IDENTITY,
        [PromotionId] int NOT NULL,
        [ProductId] int NOT NULL,
        [VariantId] int NULL,
        [ProductUnitConversionId] int NULL,
        [RequiredQuantity] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_PromotionComboRule] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PromotionComboRule_Promotions_PromotionId] FOREIGN KEY ([PromotionId]) REFERENCES [Promotions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PromotionComboRule_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607150920_AddComboPromotionPhase1'
)
BEGIN
    CREATE INDEX [IX_PromotionComboRule_PromotionId] ON [PromotionComboRule] ([PromotionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607150920_AddComboPromotionPhase1'
)
BEGIN
    CREATE INDEX [IX_PromotionComboRule_StoreId] ON [PromotionComboRule] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260607150920_AddComboPromotionPhase1'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260607150920_AddComboPromotionPhase1', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608033251_AddComboSnapshotToOrderLine'
)
BEGIN
    ALTER TABLE [PromotionComboRule] DROP CONSTRAINT [FK_PromotionComboRule_Promotions_PromotionId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608033251_AddComboSnapshotToOrderLine'
)
BEGIN
    DROP INDEX [IX_PromotionComboRule_StoreId] ON [PromotionComboRule];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608033251_AddComboSnapshotToOrderLine'
)
BEGIN
    DECLARE @var68 sysname;
    SELECT @var68 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PromotionComboRule]') AND [c].[name] = N'RequiredQuantity');
    IF @var68 IS NOT NULL EXEC(N'ALTER TABLE [PromotionComboRule] DROP CONSTRAINT [' + @var68 + '];');
    ALTER TABLE [PromotionComboRule] ALTER COLUMN [RequiredQuantity] decimal(18,4) NOT NULL;
    ALTER TABLE [PromotionComboRule] ADD DEFAULT 1.0 FOR [RequiredQuantity];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608033251_AddComboSnapshotToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [ComboAllocatedDiscount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608033251_AddComboSnapshotToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [ComboPromotionId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608033251_AddComboSnapshotToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [ComboPromotionName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608033251_AddComboSnapshotToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [ComboPromotionNote] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608033251_AddComboSnapshotToOrderLine'
)
BEGIN
    CREATE INDEX [IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId] ON [PromotionComboRule] ([StoreId], [PromotionId], [ProductId], [VariantId], [ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608033251_AddComboSnapshotToOrderLine'
)
BEGIN
    ALTER TABLE [PromotionComboRule] ADD CONSTRAINT [FK_PromotionComboRule_Promotions_PromotionId] FOREIGN KEY ([PromotionId]) REFERENCES [Promotions] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608033251_AddComboSnapshotToOrderLine'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260608033251_AddComboSnapshotToOrderLine', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608041549_AddBuyXGetYPromotion'
)
BEGIN
    ALTER TABLE [Promotions] ADD [BuyQuantity] decimal(18,4) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608041549_AddBuyXGetYPromotion'
)
BEGIN
    ALTER TABLE [Promotions] ADD [GetQuantity] decimal(18,4) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608041549_AddBuyXGetYPromotion'
)
BEGIN
    ALTER TABLE [Promotions] ADD [RequireGiftQuantityInCart] bit NOT NULL DEFAULT CAST(1 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608041549_AddBuyXGetYPromotion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260608041549_AddBuyXGetYPromotion', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608042551_AddPromotionTypeSnapshotToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [PromotionBuyQuantity] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608042551_AddPromotionTypeSnapshotToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [PromotionGiftQuantity] decimal(18,4) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608042551_AddPromotionTypeSnapshotToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [PromotionType] tinyint NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608042551_AddPromotionTypeSnapshotToOrderLine'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260608042551_AddPromotionTypeSnapshotToOrderLine', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608054047_ImprovePromotionManagementIndexes'
)
BEGIN
    DROP INDEX [IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId] ON [PromotionComboRule];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608054047_ImprovePromotionManagementIndexes'
)
BEGIN
    CREATE INDEX [IX_Promotions_StoreId_Type_IsActive_StartAtUtc_EndAtUtc_IsDeleted] ON [Promotions] ([StoreId], [Type], [IsActive], [StartAtUtc], [EndAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608054047_ImprovePromotionManagementIndexes'
)
BEGIN
    CREATE INDEX [IX_PromotionItems_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId_IsDeleted] ON [PromotionItems] ([StoreId], [PromotionId], [ProductId], [VariantId], [ProductUnitConversionId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608054047_ImprovePromotionManagementIndexes'
)
BEGIN
    CREATE INDEX [IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId_IsDeleted] ON [PromotionComboRule] ([StoreId], [PromotionId], [ProductId], [VariantId], [ProductUnitConversionId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608054047_ImprovePromotionManagementIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260608054047_ImprovePromotionManagementIndexes', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608161820_AddBuyXGetYGiftLineFieldsToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [GiftPromotionId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608161820_AddBuyXGetYGiftLineFieldsToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [GiftPromotionName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608161820_AddBuyXGetYGiftLineFieldsToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [GiftPromotionNote] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608161820_AddBuyXGetYGiftLineFieldsToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [GiftSourceLineId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608161820_AddBuyXGetYGiftLineFieldsToOrderLine'
)
BEGIN
    ALTER TABLE [OrderLines] ADD [IsPromotionGift] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608161820_AddBuyXGetYGiftLineFieldsToOrderLine'
)
BEGIN
    CREATE INDEX [IX_OrderLines_StoreId_GiftPromotionId] ON [OrderLines] ([StoreId], [GiftPromotionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608161820_AddBuyXGetYGiftLineFieldsToOrderLine'
)
BEGIN
    CREATE INDEX [IX_OrderLines_StoreId_GiftSourceLineId] ON [OrderLines] ([StoreId], [GiftSourceLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608161820_AddBuyXGetYGiftLineFieldsToOrderLine'
)
BEGIN
    CREATE INDEX [IX_OrderLines_StoreId_IsPromotionGift] ON [OrderLines] ([StoreId], [IsPromotionGift]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608161820_AddBuyXGetYGiftLineFieldsToOrderLine'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260608161820_AddBuyXGetYGiftLineFieldsToOrderLine', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceDetails] DROP CONSTRAINT [FK_InvoiceDetails_InvoiceHeads_InvoiceHeadId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceDetails] DROP CONSTRAINT [FK_InvoiceDetails_OrderLines_OrderLineId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceDetails] DROP CONSTRAINT [FK_InvoiceDetails_ProductVariant_ProductVariantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    DROP INDEX [IX_InvoiceHeads_StoreId_InvoiceDate] ON [InvoiceHeads];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    DROP INDEX [IX_InvoiceHeads_StoreId_InvoiceNumber] ON [InvoiceHeads];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    DROP INDEX [IX_InvoiceHeads_StoreId_IsLocked] ON [InvoiceHeads];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    DROP INDEX [IX_InvoiceHeads_StoreId_OrderId] ON [InvoiceHeads];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    DROP INDEX [IX_InvoiceDetails_StoreId_InvoiceHeadId] ON [InvoiceDetails];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    DROP INDEX [IX_InvoiceDetails_StoreId_OrderLineId] ON [InvoiceDetails];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    DROP INDEX [IX_InvoiceDetails_StoreId_ProductVariantId] ON [InvoiceDetails];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    DROP INDEX [IX_InvoiceDetails_StoreId_SourceType] ON [InvoiceDetails];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [CodeOfTax] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [InvoiceSeries] nvarchar(25) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [InvoiceType] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [IssuedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [LastErrorCode] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [LastErrorMessage] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [LastSyncedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [PdfFilePath] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [ProviderCode] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [ProviderInvoiceNo] nvarchar(35) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [ProviderStatus] tinyint NOT NULL DEFAULT CAST(0 AS tinyint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [ProviderTransactionId] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [ReservationCode] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [SupplierTaxCode] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [TemplateCode] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [TransactionUuid] nvarchar(36) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [ZipFilePath] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    DECLARE @var69 sysname;
    SELECT @var69 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceDetails]') AND [c].[name] = N'VatRate');
    IF @var69 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceDetails] DROP CONSTRAINT [' + @var69 + '];');
    ALTER TABLE [InvoiceDetails] ALTER COLUMN [VatRate] decimal(9,2) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    DECLARE @var70 sysname;
    SELECT @var70 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceDetails]') AND [c].[name] = N'SourceType');
    IF @var70 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceDetails] DROP CONSTRAINT [' + @var70 + '];');
    ALTER TABLE [InvoiceDetails] ALTER COLUMN [SourceType] tinyint NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE TABLE [InvoiceIntegrationLogs] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceHeadId] int NOT NULL,
        [ActionType] tinyint NOT NULL,
        [RequestUrl] nvarchar(500) NULL,
        [RequestBody] nvarchar(max) NULL,
        [ResponseBody] nvarchar(max) NULL,
        [IsSuccess] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ErrorCode] nvarchar(100) NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [FinishedAtUtc] datetime2 NULL,
        [DurationMs] bigint NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InvoiceIntegrationLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvoiceIntegrationLogs_InvoiceHeads_InvoiceHeadId] FOREIGN KEY ([InvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InvoiceIntegrationLogs_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE TABLE [InvoiceProviderSettings] (
        [Id] int NOT NULL IDENTITY,
        [ProviderCode] nvarchar(50) NOT NULL,
        [IsProduction] bit NOT NULL DEFAULT CAST(0 AS bit),
        [BaseUrl] nvarchar(500) NOT NULL,
        [Username] nvarchar(150) NOT NULL,
        [Password] nvarchar(500) NOT NULL,
        [SupplierTaxCode] nvarchar(20) NOT NULL,
        [InvoiceType] nvarchar(20) NOT NULL,
        [TemplateCode] nvarchar(20) NOT NULL,
        [InvoiceSeries] nvarchar(25) NOT NULL,
        [CurrencyCode] nvarchar(3) NOT NULL,
        [ExchangeRate] decimal(18,2) NOT NULL DEFAULT 1.0,
        [PaymentMethodName] nvarchar(50) NOT NULL,
        [CusGetInvoiceRight] bit NOT NULL DEFAULT CAST(1 AS bit),
        [DefaultPaymentStatus] bit NOT NULL DEFAULT CAST(1 AS bit),
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InvoiceProviderSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvoiceProviderSettings_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_InvoiceDate_IsDeleted] ON [InvoiceHeads] ([StoreId], [InvoiceDate], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_OrderId] ON [InvoiceHeads] ([StoreId], [OrderId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_ProviderInvoiceNo_IsDeleted] ON [InvoiceHeads] ([StoreId], [ProviderInvoiceNo], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_ProviderStatus_IsDeleted] ON [InvoiceHeads] ([StoreId], [ProviderStatus], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_TransactionUuid] ON [InvoiceHeads] ([StoreId], [TransactionUuid]) WHERE [TransactionUuid] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_StoreId_InvoiceHeadId_IsDeleted] ON [InvoiceDetails] ([StoreId], [InvoiceHeadId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLineId] ON [InvoiceDetails] ([StoreId], [InvoiceHeadId], [OrderLineId]) WHERE [OrderLineId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_StoreId_ProductVariantId_IsDeleted] ON [InvoiceDetails] ([StoreId], [ProductVariantId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_StoreId_SourceType_IsDeleted] ON [InvoiceDetails] ([StoreId], [SourceType], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceIntegrationLogs_InvoiceHeadId] ON [InvoiceIntegrationLogs] ([InvoiceHeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceIntegrationLogs_StoreId_InvoiceHeadId_ActionType] ON [InvoiceIntegrationLogs] ([StoreId], [InvoiceHeadId], [ActionType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceIntegrationLogs_StoreId_IsSuccess_ActionType] ON [InvoiceIntegrationLogs] ([StoreId], [IsSuccess], [ActionType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceIntegrationLogs_StoreId_StartedAtUtc] ON [InvoiceIntegrationLogs] ([StoreId], [StartedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    CREATE INDEX [IX_InvoiceProviderSettings_StoreId_IsActive_IsDeleted] ON [InvoiceProviderSettings] ([StoreId], [IsActive], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceProviderSettings_StoreId_ProviderCode_SupplierTaxCode_TemplateCode_InvoiceSeries] ON [InvoiceProviderSettings] ([StoreId], [ProviderCode], [SupplierTaxCode], [TemplateCode], [InvoiceSeries]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceDetails] ADD CONSTRAINT [FK_InvoiceDetails_InvoiceHeads_InvoiceHeadId] FOREIGN KEY ([InvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceDetails] ADD CONSTRAINT [FK_InvoiceDetails_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    ALTER TABLE [InvoiceDetails] ADD CONSTRAINT [FK_InvoiceDetails_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616151219_AddViettelInvoiceIntegration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260616151219_AddViettelInvoiceIntegration', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616155656_AddInvoiceProviderAuthMode'
)
BEGIN
    ALTER TABLE [InvoiceProviderSettings] ADD [AuthMode] tinyint NOT NULL DEFAULT CAST(2 AS tinyint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260616155656_AddInvoiceProviderAuthMode'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260616155656_AddInvoiceProviderAuthMode', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [AdditionalReferenceDateUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [AdditionalReferenceDesc] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [AdjustedNote] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [CorrectionType] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [OriginalInvoiceHeadId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [OriginalInvoiceIssuedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [OriginalInvoiceNo] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    CREATE TABLE [InvoiceCorrectionCases] (
        [Id] int NOT NULL IDENTITY,
        [OriginalInvoiceHeadId] int NOT NULL,
        [NewInvoiceHeadId] int NULL,
        [Type] int NOT NULL,
        [Status] int NOT NULL,
        [Reason] nvarchar(max) NOT NULL,
        [AgreementDocumentNo] nvarchar(max) NOT NULL,
        [AgreementDateUtc] datetime2 NOT NULL,
        [Note] nvarchar(max) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedByUserId] int NULL,
        [IssuedAtUtc] datetime2 NULL,
        [LastErrorCode] nvarchar(max) NULL,
        [LastErrorMessage] nvarchar(max) NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InvoiceCorrectionCases] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvoiceCorrectionCases_InvoiceHeads_NewInvoiceHeadId] FOREIGN KEY ([NewInvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]),
        CONSTRAINT [FK_InvoiceCorrectionCases_InvoiceHeads_OriginalInvoiceHeadId] FOREIGN KEY ([OriginalInvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_InvoiceCorrectionCases_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_OriginalInvoiceHeadId] ON [InvoiceHeads] ([OriginalInvoiceHeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    CREATE INDEX [IX_InvoiceCorrectionCases_NewInvoiceHeadId] ON [InvoiceCorrectionCases] ([NewInvoiceHeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    CREATE INDEX [IX_InvoiceCorrectionCases_OriginalInvoiceHeadId] ON [InvoiceCorrectionCases] ([OriginalInvoiceHeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    CREATE INDEX [IX_InvoiceCorrectionCases_StoreId] ON [InvoiceCorrectionCases] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD CONSTRAINT [FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId] FOREIGN KEY ([OriginalInvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181254_AddInvoiceCorrectionSupport'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260617181254_AddInvoiceCorrectionSupport', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    ALTER TABLE [InvoiceCorrectionCases] DROP CONSTRAINT [FK_InvoiceCorrectionCases_InvoiceHeads_NewInvoiceHeadId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    ALTER TABLE [InvoiceCorrectionCases] DROP CONSTRAINT [FK_InvoiceCorrectionCases_InvoiceHeads_OriginalInvoiceHeadId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    ALTER TABLE [InvoiceHeads] DROP CONSTRAINT [FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DROP INDEX [IX_InvoiceHeads_StoreId_OrderId] ON [InvoiceHeads];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DROP INDEX [IX_InvoiceCorrectionCases_StoreId] ON [InvoiceCorrectionCases];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var71 sysname;
    SELECT @var71 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceHeads]') AND [c].[name] = N'OriginalInvoiceNo');
    IF @var71 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceHeads] DROP CONSTRAINT [' + @var71 + '];');
    ALTER TABLE [InvoiceHeads] ALTER COLUMN [OriginalInvoiceNo] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var72 sysname;
    SELECT @var72 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceHeads]') AND [c].[name] = N'CorrectionType');
    IF @var72 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceHeads] DROP CONSTRAINT [' + @var72 + '];');
    ALTER TABLE [InvoiceHeads] ALTER COLUMN [CorrectionType] tinyint NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var73 sysname;
    SELECT @var73 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceHeads]') AND [c].[name] = N'AdjustedNote');
    IF @var73 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceHeads] DROP CONSTRAINT [' + @var73 + '];');
    ALTER TABLE [InvoiceHeads] ALTER COLUMN [AdjustedNote] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var74 sysname;
    SELECT @var74 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceHeads]') AND [c].[name] = N'AdditionalReferenceDesc');
    IF @var74 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceHeads] DROP CONSTRAINT [' + @var74 + '];');
    ALTER TABLE [InvoiceHeads] ALTER COLUMN [AdditionalReferenceDesc] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var75 sysname;
    SELECT @var75 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceCorrectionCases]') AND [c].[name] = N'Type');
    IF @var75 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceCorrectionCases] DROP CONSTRAINT [' + @var75 + '];');
    ALTER TABLE [InvoiceCorrectionCases] ALTER COLUMN [Type] tinyint NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var76 sysname;
    SELECT @var76 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceCorrectionCases]') AND [c].[name] = N'Status');
    IF @var76 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceCorrectionCases] DROP CONSTRAINT [' + @var76 + '];');
    ALTER TABLE [InvoiceCorrectionCases] ALTER COLUMN [Status] tinyint NOT NULL;
    ALTER TABLE [InvoiceCorrectionCases] ADD DEFAULT CAST(0 AS tinyint) FOR [Status];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var77 sysname;
    SELECT @var77 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceCorrectionCases]') AND [c].[name] = N'Reason');
    IF @var77 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceCorrectionCases] DROP CONSTRAINT [' + @var77 + '];');
    ALTER TABLE [InvoiceCorrectionCases] ALTER COLUMN [Reason] nvarchar(255) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var78 sysname;
    SELECT @var78 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceCorrectionCases]') AND [c].[name] = N'Note');
    IF @var78 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceCorrectionCases] DROP CONSTRAINT [' + @var78 + '];');
    ALTER TABLE [InvoiceCorrectionCases] ALTER COLUMN [Note] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var79 sysname;
    SELECT @var79 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceCorrectionCases]') AND [c].[name] = N'LastErrorMessage');
    IF @var79 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceCorrectionCases] DROP CONSTRAINT [' + @var79 + '];');
    ALTER TABLE [InvoiceCorrectionCases] ALTER COLUMN [LastErrorMessage] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var80 sysname;
    SELECT @var80 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceCorrectionCases]') AND [c].[name] = N'LastErrorCode');
    IF @var80 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceCorrectionCases] DROP CONSTRAINT [' + @var80 + '];');
    ALTER TABLE [InvoiceCorrectionCases] ALTER COLUMN [LastErrorCode] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    DECLARE @var81 sysname;
    SELECT @var81 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceCorrectionCases]') AND [c].[name] = N'AgreementDocumentNo');
    IF @var81 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceCorrectionCases] DROP CONSTRAINT [' + @var81 + '];');
    ALTER TABLE [InvoiceCorrectionCases] ALTER COLUMN [AgreementDocumentNo] nvarchar(255) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_CorrectionType_IsDeleted] ON [InvoiceHeads] ([StoreId], [CorrectionType], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_OrderId] ON [InvoiceHeads] ([StoreId], [OrderId]) WHERE [IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_OriginalInvoiceHeadId_IsDeleted] ON [InvoiceHeads] ([StoreId], [OriginalInvoiceHeadId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    CREATE INDEX [IX_InvoiceCorrectionCases_StoreId_CreatedAtUtc_IsDeleted] ON [InvoiceCorrectionCases] ([StoreId], [CreatedAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    CREATE INDEX [IX_InvoiceCorrectionCases_StoreId_NewInvoiceHeadId_IsDeleted] ON [InvoiceCorrectionCases] ([StoreId], [NewInvoiceHeadId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    CREATE INDEX [IX_InvoiceCorrectionCases_StoreId_OriginalInvoiceHeadId_IsDeleted] ON [InvoiceCorrectionCases] ([StoreId], [OriginalInvoiceHeadId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    CREATE INDEX [IX_InvoiceCorrectionCases_StoreId_Type_Status_IsDeleted] ON [InvoiceCorrectionCases] ([StoreId], [Type], [Status], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    ALTER TABLE [InvoiceCorrectionCases] ADD CONSTRAINT [FK_InvoiceCorrectionCases_InvoiceHeads_NewInvoiceHeadId] FOREIGN KEY ([NewInvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    ALTER TABLE [InvoiceCorrectionCases] ADD CONSTRAINT [FK_InvoiceCorrectionCases_InvoiceHeads_OriginalInvoiceHeadId] FOREIGN KEY ([OriginalInvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD CONSTRAINT [FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId] FOREIGN KEY ([OriginalInvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617181524_AddInvoiceCorrectionSupport1'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260617181524_AddInvoiceCorrectionSupport1', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    DECLARE @var82 sysname;
    SELECT @var82 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceHeads]') AND [c].[name] = N'BuyerName');
    IF @var82 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceHeads] DROP CONSTRAINT [' + @var82 + '];');
    ALTER TABLE [InvoiceHeads] ALTER COLUMN [BuyerName] nvarchar(300) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    DECLARE @var83 sysname;
    SELECT @var83 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceHeads]') AND [c].[name] = N'BuyerAddress');
    IF @var83 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceHeads] DROP CONSTRAINT [' + @var83 + '];');
    ALTER TABLE [InvoiceHeads] ALTER COLUMN [BuyerAddress] nvarchar(1200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [BuyerEmail] nvarchar(2000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [BuyerLegalName] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [BuyerPhone] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [BuyerType] nvarchar(30) NOT NULL DEFAULT N'NoInvoice';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    CREATE TABLE [InvoiceBuyerProfiles] (
        [Id] int NOT NULL IDENTITY,
        [CustomerId] int NULL,
        [BuyerType] nvarchar(30) NOT NULL DEFAULT N'Business',
        [TaxCode] nvarchar(50) NULL,
        [BuyerName] nvarchar(300) NULL,
        [BuyerLegalName] nvarchar(500) NULL,
        [BuyerAddress] nvarchar(1200) NULL,
        [BuyerEmail] nvarchar(2000) NULL,
        [BuyerPhone] nvarchar(30) NULL,
        [Source] nvarchar(50) NOT NULL DEFAULT N'manual',
        [IsVerifiedByUser] bit NOT NULL DEFAULT CAST(0 AS bit),
        [LastLookupAtUtc] datetime2 NULL,
        [LastUsedAtUtc] datetime2 NULL,
        [UseCount] int NOT NULL DEFAULT 0,
        [Note] nvarchar(500) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_InvoiceBuyerProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvoiceBuyerProfiles_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_InvoiceBuyerProfiles_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_BuyerTaxCode_IsDeleted] ON [InvoiceHeads] ([StoreId], [BuyerTaxCode], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    CREATE INDEX [IX_InvoiceBuyerProfiles_CustomerId] ON [InvoiceBuyerProfiles] ([CustomerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    CREATE INDEX [IX_InvoiceBuyerProfiles_StoreId_BuyerType_IsDeleted] ON [InvoiceBuyerProfiles] ([StoreId], [BuyerType], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    CREATE INDEX [IX_InvoiceBuyerProfiles_StoreId_CustomerId_IsDeleted] ON [InvoiceBuyerProfiles] ([StoreId], [CustomerId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    CREATE INDEX [IX_InvoiceBuyerProfiles_StoreId_IsActive_IsDeleted] ON [InvoiceBuyerProfiles] ([StoreId], [IsActive], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    CREATE INDEX [IX_InvoiceBuyerProfiles_StoreId_LastUsedAtUtc_IsDeleted] ON [InvoiceBuyerProfiles] ([StoreId], [LastUsedAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    CREATE INDEX [IX_InvoiceBuyerProfiles_StoreId_TaxCode_IsDeleted] ON [InvoiceBuyerProfiles] ([StoreId], [TaxCode], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620153053_AddInvoiceBuyerProfileAndBuyerType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260620153053_AddInvoiceBuyerProfileAndBuyerType', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [EmailSendCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [EmailSentAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [EmailStatus] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [LastEmailErrorMessage] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [LastEmailTo] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [OfficialPdfDownloadedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [OfficialPdfFileName] nvarchar(260) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [OfficialPdfStatus] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [OfficialZipXmlDownloadedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [OfficialZipXmlFileName] nvarchar(260) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [OfficialZipXmlStatus] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN

    UPDATE InvoiceHeads
    SET
        OfficialPdfStatus = 1,
        OfficialPdfDownloadedAtUtc = COALESCE(LastSyncedAtUtc, IssuedAtUtc),
        OfficialPdfFileName = RIGHT(PdfFilePath, CHARINDEX('\', REVERSE(PdfFilePath) + '\') - 1)
    WHERE PdfFilePath IS NOT NULL
      AND LTRIM(RTRIM(PdfFilePath)) <> '';

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN

    UPDATE InvoiceHeads
    SET
        OfficialZipXmlStatus = 1,
        OfficialZipXmlDownloadedAtUtc = COALESCE(LastSyncedAtUtc, IssuedAtUtc),
        OfficialZipXmlFileName = RIGHT(ZipFilePath, CHARINDEX('\', REVERSE(ZipFilePath) + '\') - 1)
    WHERE ZipFilePath IS NOT NULL
      AND LTRIM(RTRIM(ZipFilePath)) <> '';

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622154759_AddInvoiceViettelFileEmailStatus'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260622154759_AddInvoiceViettelFileEmailStatus', N'8.0.29');
END;
GO

COMMIT;
GO

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
    DECLARE @var84 sysname;
    SELECT @var84 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND [c].[name] = N'LegalEntityId');
    IF @var84 IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @var84 + '];');
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

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    ALTER TABLE [Orders] ADD [HasMultipleLegalEntities] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    ALTER TABLE [Orders] ADD [LegalEntityAllocatedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    ALTER TABLE [Orders] ADD [LegalEntityCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    CREATE TABLE [OrderLegalEntityAllocations] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [OrderLineId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [ProductUnitConversionId] int NULL,
        [LegalEntityId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [InventoryTransactionId] int NULL,
        [SalePriority] int NOT NULL,
        [Quantity] decimal(18,4) NOT NULL,
        [BaseQuantity] decimal(18,4) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        [DiscountAllocated] decimal(18,2) NOT NULL,
        [PromotionDiscountAllocated] decimal(18,2) NOT NULL,
        [ComboDiscountAllocated] decimal(18,2) NOT NULL,
        [OrderDiscountAllocated] decimal(18,2) NOT NULL,
        [VoucherDiscountAllocated] decimal(18,2) NOT NULL,
        [NetAmount] decimal(18,2) NOT NULL,
        [AllocationSource] tinyint NOT NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_OrderLegalEntityAllocations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OrderLegalEntityAllocations_Amounts_NonNegative] CHECK ([LineTotal] >= 0 AND [DiscountAllocated] >= 0 AND [PromotionDiscountAllocated] >= 0 AND [ComboDiscountAllocated] >= 0 AND [OrderDiscountAllocated] >= 0 AND [VoucherDiscountAllocated] >= 0 AND [NetAmount] >= 0),
        CONSTRAINT [CK_OrderLegalEntityAllocations_Quantity_Positive] CHECK ([Quantity] > 0 AND [BaseQuantity] > 0),
        CONSTRAINT [CK_OrderLegalEntityAllocations_SalePriority_Positive] CHECK ([SalePriority] > 0),
        CONSTRAINT [FK_OrderLegalEntityAllocations_InventoryTransactions_InventoryTransactionId] FOREIGN KEY ([InventoryTransactionId]) REFERENCES [InventoryTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocations_LegalEntities_StoreId_LegalEntityId] FOREIGN KEY ([StoreId], [LegalEntityId]) REFERENCES [LegalEntities] ([StoreId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocations_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocations_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocations_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocations_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_OrderLegalEntityAllocations_Warehouses_StoreId_WarehouseId] FOREIGN KEY ([StoreId], [WarehouseId]) REFERENCES [Warehouses] ([StoreId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    CREATE INDEX [IX_Orders_StoreId_HasMultipleLegalEntities_LegalEntityAllocatedAtUtc_IsDeleted] ON [Orders] ([StoreId], [HasMultipleLegalEntities], [LegalEntityAllocatedAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocations_InventoryTransactionId] ON [OrderLegalEntityAllocations] ([InventoryTransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocations_OrderId] ON [OrderLegalEntityAllocations] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocations_OrderLineId] ON [OrderLegalEntityAllocations] ([OrderLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocations_ProductUnitConversionId] ON [OrderLegalEntityAllocations] ([ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocations_ProductVariantId] ON [OrderLegalEntityAllocations] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrderLegalEntityAllocations_StoreId_InventoryTransactionId] ON [OrderLegalEntityAllocations] ([StoreId], [InventoryTransactionId]) WHERE [InventoryTransactionId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocations_StoreId_LegalEntityId_OrderId_IsDeleted] ON [OrderLegalEntityAllocations] ([StoreId], [LegalEntityId], [OrderId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocations_StoreId_OrderId_IsDeleted] ON [OrderLegalEntityAllocations] ([StoreId], [OrderId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrderLegalEntityAllocations_StoreId_OrderId_OrderLineId_LegalEntityId_WarehouseId] ON [OrderLegalEntityAllocations] ([StoreId], [OrderId], [OrderLineId], [LegalEntityId], [WarehouseId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocations_StoreId_WarehouseId_ProductVariantId_IsDeleted] ON [OrderLegalEntityAllocations] ([StoreId], [WarehouseId], [ProductVariantId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718102545_Phase225OrderLegalEntityAllocation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260718102545_Phase225OrderLegalEntityAllocation', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE TABLE [OrderLegalEntityAllocationReversals] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [OrderLineId] int NOT NULL,
        [OrderLegalEntityAllocationId] int NOT NULL,
        [SalesReturnId] int NULL,
        [SalesReturnLineId] int NULL,
        [LegalEntityId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [SourceValuationEntryId] int NOT NULL,
        [InventoryTransactionId] int NULL,
        [ReversalType] tinyint NOT NULL,
        [BaseQuantity] decimal(18,4) NOT NULL,
        [FinancialAmount] decimal(18,2) NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_OrderLegalEntityAllocationReversals] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OrderLegalEntityAllocationReversals_BaseQuantity_Positive] CHECK ([BaseQuantity] > 0),
        CONSTRAINT [CK_OrderLegalEntityAllocationReversals_FinancialAmount_NonNegative] CHECK ([FinancialAmount] >= 0),
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_InventoryTransactions_InventoryTransactionId] FOREIGN KEY ([InventoryTransactionId]) REFERENCES [InventoryTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_InventoryValuationEntries_SourceValuationEntryId] FOREIGN KEY ([SourceValuationEntryId]) REFERENCES [InventoryValuationEntries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_LegalEntities_StoreId_LegalEntityId] FOREIGN KEY ([StoreId], [LegalEntityId]) REFERENCES [LegalEntities] ([StoreId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_OrderLegalEntityAllocations_OrderLegalEntityAllocationId] FOREIGN KEY ([OrderLegalEntityAllocationId]) REFERENCES [OrderLegalEntityAllocations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_SalesReturnLines_SalesReturnLineId] FOREIGN KEY ([SalesReturnLineId]) REFERENCES [SalesReturnLines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_SalesReturns_SalesReturnId] FOREIGN KEY ([SalesReturnId]) REFERENCES [SalesReturns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_OrderLegalEntityAllocationReversals_Warehouses_StoreId_WarehouseId] FOREIGN KEY ([StoreId], [WarehouseId]) REFERENCES [Warehouses] ([StoreId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_InventoryTransactionId] ON [OrderLegalEntityAllocationReversals] ([InventoryTransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_OrderId] ON [OrderLegalEntityAllocationReversals] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_OrderLegalEntityAllocationId] ON [OrderLegalEntityAllocationReversals] ([OrderLegalEntityAllocationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_OrderLineId] ON [OrderLegalEntityAllocationReversals] ([OrderLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_ProductVariantId] ON [OrderLegalEntityAllocationReversals] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_SalesReturnId] ON [OrderLegalEntityAllocationReversals] ([SalesReturnId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_SalesReturnLineId] ON [OrderLegalEntityAllocationReversals] ([SalesReturnLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_SourceValuationEntryId] ON [OrderLegalEntityAllocationReversals] ([SourceValuationEntryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_LegalEntityId] ON [OrderLegalEntityAllocationReversals] ([StoreId], [LegalEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_OrderId_IsDeleted] ON [OrderLegalEntityAllocationReversals] ([StoreId], [OrderId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_OrderLegalEntityAllocationId_IsDeleted] ON [OrderLegalEntityAllocationReversals] ([StoreId], [OrderLegalEntityAllocationId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_ReversalType_SalesReturnLineId_SourceValuationEntryId] ON [OrderLegalEntityAllocationReversals] ([StoreId], [ReversalType], [SalesReturnLineId], [SourceValuationEntryId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_SalesReturnId_IsDeleted] ON [OrderLegalEntityAllocationReversals] ([StoreId], [SalesReturnId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_SourceValuationEntryId_IsDeleted] ON [OrderLegalEntityAllocationReversals] ([StoreId], [SourceValuationEntryId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_WarehouseId] ON [OrderLegalEntityAllocationReversals] ([StoreId], [WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718131425_Phase226HoldVoidRefund'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260718131425_Phase226HoldVoidRefund', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    DROP INDEX [IX_InvoiceHeads_StoreId_OrderId] ON [InvoiceHeads];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [InvoiceProviderSettingId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD [LegalEntityId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    ALTER TABLE [InvoiceDetails] ADD [OrderLegalEntityAllocationId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    ALTER TABLE [OrderLegalEntityAllocations] ADD CONSTRAINT [AK_OrderLegalEntityAllocations_StoreId_Id] UNIQUE ([StoreId], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_InvoiceProviderSettingId_IsDeleted] ON [InvoiceHeads] ([StoreId], [InvoiceProviderSettingId], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    CREATE INDEX [IX_InvoiceHeads_StoreId_LegalEntityId_InvoiceDate_IsDeleted] ON [InvoiceHeads] ([StoreId], [LegalEntityId], [InvoiceDate], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_OrderId] ON [InvoiceHeads] ([StoreId], [OrderId]) WHERE [IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_OrderId_LegalEntityId] ON [InvoiceHeads] ([StoreId], [OrderId], [LegalEntityId]) WHERE [IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLegalEntityAllocationId] ON [InvoiceDetails] ([StoreId], [InvoiceHeadId], [OrderLegalEntityAllocationId]) WHERE [OrderLegalEntityAllocationId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    CREATE INDEX [IX_InvoiceDetails_StoreId_OrderLegalEntityAllocationId] ON [InvoiceDetails] ([StoreId], [OrderLegalEntityAllocationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    ALTER TABLE [InvoiceDetails] ADD CONSTRAINT [FK_InvoiceDetails_OrderLegalEntityAllocations_StoreId_OrderLegalEntityAllocationId] FOREIGN KEY ([StoreId], [OrderLegalEntityAllocationId]) REFERENCES [OrderLegalEntityAllocations] ([StoreId], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD CONSTRAINT [FK_InvoiceHeads_InvoiceProviderSettings_StoreId_InvoiceProviderSettingId] FOREIGN KEY ([StoreId], [InvoiceProviderSettingId]) REFERENCES [InvoiceProviderSettings] ([StoreId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    ALTER TABLE [InvoiceHeads] ADD CONSTRAINT [FK_InvoiceHeads_LegalEntities_StoreId_LegalEntityId] FOREIGN KEY ([StoreId], [LegalEntityId]) REFERENCES [LegalEntities] ([StoreId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718135948_Phase227LegalEntityInvoices'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260718135948_Phase227LegalEntityInvoices', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718165310_Phase229CanaryActivation'
)
BEGIN
    ALTER TABLE [Orders] ADD [LegalEntityActivationAtUtcSnapshot] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718165310_Phase229CanaryActivation'
)
BEGIN
    ALTER TABLE [Orders] ADD [LegalEntityModeCapturedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718165310_Phase229CanaryActivation'
)
BEGIN
    ALTER TABLE [Orders] ADD [UseMultiLegalEntity] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718165310_Phase229CanaryActivation'
)
BEGIN
    CREATE TABLE [LegalEntityActivationEvents] (
        [Id] int NOT NULL IDENTITY,
        [Action] tinyint NOT NULL,
        [PreviousIsEnabled] bit NOT NULL,
        [NewIsEnabled] bit NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [ActivationAtUtc] datetime2 NULL,
        [ChangedByUserId] int NULL,
        [ChangedByUserName] nvarchar(200) NULL,
        [Reason] nvarchar(500) NOT NULL,
        [PreflightPassed] bit NOT NULL,
        [PreflightSnapshotJson] nvarchar(max) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_LegalEntityActivationEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LegalEntityActivationEvents_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718165310_Phase229CanaryActivation'
)
BEGIN
    CREATE INDEX [IX_Orders_StoreId_UseMultiLegalEntity_Status_LegalEntityModeCapturedAtUtc_IsDeleted] ON [Orders] ([StoreId], [UseMultiLegalEntity], [Status], [LegalEntityModeCapturedAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718165310_Phase229CanaryActivation'
)
BEGIN
    CREATE INDEX [IX_LegalEntityActivationEvents_StoreId_OccurredAtUtc_IsDeleted] ON [LegalEntityActivationEvents] ([StoreId], [OccurredAtUtc], [IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718165310_Phase229CanaryActivation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260718165310_Phase229CanaryActivation', N'8.0.29');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [FreightAllocation] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [ProductUnitConversionId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [PurchaseOrderLineId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [ShortageDisposition] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [ShortageReason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [TaxId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [TaxNameSnapshot] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [TaxRate] decimal(5,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [UnitPriceAfterVat] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [UnitPriceBeforeVat] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD [VatAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [DirectReceiptReason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [FreightNote] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [FreightPayeeName] nvarchar(250) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [FreightTotal] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [HasFreight] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [HasVat] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [IsFreightPaid] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [PurchaseOrderId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [ReceiptSource] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [SubtotalBeforeVat] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD [VatAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE TABLE [PurchaseOrders] (
        [Id] int NOT NULL IDENTITY,
        [OrderNumber] nvarchar(50) NOT NULL,
        [SupplierId] int NOT NULL,
        [ExpectedWarehouseId] int NOT NULL,
        [LegalEntityId] int NOT NULL,
        [OrderDate] datetime2 NOT NULL,
        [ExpectedDeliveryDate] datetime2 NULL,
        [Note] nvarchar(1000) NULL,
        [HasVat] bit NOT NULL,
        [Status] int NOT NULL,
        [SubtotalBeforeVat] decimal(18,2) NOT NULL,
        [VatTotal] decimal(18,2) NOT NULL,
        [TotalAfterVat] decimal(18,2) NOT NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [SubmittedByUserId] int NULL,
        [ApprovedAtUtc] datetime2 NULL,
        [ApprovedByUserId] int NULL,
        [RejectedAtUtc] datetime2 NULL,
        [RejectedByUserId] int NULL,
        [ReturnedAtUtc] datetime2 NULL,
        [ReturnedByUserId] int NULL,
        [SentToSupplierAtUtc] datetime2 NULL,
        [SentToSupplierByUserId] int NULL,
        [CancelledAtUtc] datetime2 NULL,
        [CancelledByUserId] int NULL,
        [WorkflowNote] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_PurchaseOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrders_LegalEntities_LegalEntityId] FOREIGN KEY ([LegalEntityId]) REFERENCES [LegalEntities] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrders_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_PurchaseOrders_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrders_Warehouses_ExpectedWarehouseId] FOREIGN KEY ([ExpectedWarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE TABLE [PurchaseOrderActions] (
        [Id] int NOT NULL IDENTITY,
        [PurchaseOrderId] int NOT NULL,
        [ActionType] int NOT NULL,
        [FromStatus] int NOT NULL,
        [ToStatus] int NOT NULL,
        [ActorUserId] int NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [Note] nvarchar(1000) NULL,
        [StockDocumentId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_PurchaseOrderActions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrderActions_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [PurchaseOrders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrderActions_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrderActions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE TABLE [PurchaseOrderLines] (
        [Id] int NOT NULL IDENTITY,
        [PurchaseOrderId] int NOT NULL,
        [LineNo] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [UnitId] int NOT NULL,
        [ProductUnitConversionId] int NOT NULL,
        [TaxId] int NULL,
        [ProductNameSnapshot] nvarchar(250) NOT NULL,
        [SkuSnapshot] nvarchar(100) NULL,
        [UnitNameSnapshot] nvarchar(100) NOT NULL,
        [TaxNameSnapshot] nvarchar(100) NULL,
        [ConversionFactor] decimal(18,4) NOT NULL,
        [OrderedQuantity] decimal(18,3) NOT NULL,
        [UnitPriceBeforeVat] decimal(18,2) NOT NULL,
        [TaxRate] decimal(5,2) NOT NULL,
        [VatAmount] decimal(18,2) NOT NULL,
        [UnitPriceAfterVat] decimal(18,2) NOT NULL,
        [LineTotalBeforeVat] decimal(18,2) NOT NULL,
        [LineTotalAfterVat] decimal(18,2) NOT NULL,
        [ReceivedQuantity] decimal(18,3) NOT NULL,
        [ShortClosedQuantity] decimal(18,3) NOT NULL,
        [ReceiptStatus] int NOT NULL,
        [ShortCloseReason] nvarchar(500) NULL,
        [ShortClosedAtUtc] datetime2 NULL,
        [ShortClosedByUserId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] int NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] int NULL,
        [RowVersion] rowversion NOT NULL,
        [StoreId] int NOT NULL,
        CONSTRAINT [PK_PurchaseOrderLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrderLines_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrderLines_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [PurchaseOrders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrderLines_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_PurchaseOrderLines_Taxes_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [Taxes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrderLines_Unit_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE TABLE [PurchasePayables] (
        [Id] int NOT NULL IDENTITY,
        [StockDocumentId] int NOT NULL,
        [PurchaseOrderId] int NULL,
        [Type] int NOT NULL,
        [SourceKey] nvarchar(150) NOT NULL,
        [SupplierId] int NULL,
        [PayeeName] nvarchar(250) NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [RecognizedAtUtc] datetime2 NOT NULL,
        [PaidAtUtc] datetime2 NULL,
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
        CONSTRAINT [PK_PurchasePayables] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchasePayables_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [PurchaseOrders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchasePayables_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchasePayables_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
        CONSTRAINT [FK_PurchasePayables_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_StockDocumentLine_ProductUnitConversionId] ON [StockDocumentLine] ([ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_StockDocumentLine_PurchaseOrderLineId] ON [StockDocumentLine] ([PurchaseOrderLineId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_StockDocumentLine_TaxId] ON [StockDocumentLine] ([TaxId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_StockDocument_PurchaseOrderId] ON [StockDocument] ([PurchaseOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderActions_PurchaseOrderId] ON [PurchaseOrderActions] ([PurchaseOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderActions_StockDocumentId] ON [PurchaseOrderActions] ([StockDocumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderActions_StoreId_PurchaseOrderId_OccurredAtUtc] ON [PurchaseOrderActions] ([StoreId], [PurchaseOrderId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_ProductUnitConversionId] ON [PurchaseOrderLines] ([ProductUnitConversionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_ProductVariantId] ON [PurchaseOrderLines] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchaseOrderLines_PurchaseOrderId_LineNo] ON [PurchaseOrderLines] ([PurchaseOrderId], [LineNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_StoreId] ON [PurchaseOrderLines] ([StoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_TaxId] ON [PurchaseOrderLines] ([TaxId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_UnitId] ON [PurchaseOrderLines] ([UnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_ExpectedWarehouseId] ON [PurchaseOrders] ([ExpectedWarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_LegalEntityId] ON [PurchaseOrders] ([LegalEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_StoreId_LegalEntityId_Status] ON [PurchaseOrders] ([StoreId], [LegalEntityId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchaseOrders_StoreId_OrderNumber] ON [PurchaseOrders] ([StoreId], [OrderNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_StoreId_Status_OrderDate] ON [PurchaseOrders] ([StoreId], [Status], [OrderDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_StoreId_SupplierId_OrderDate] ON [PurchaseOrders] ([StoreId], [SupplierId], [OrderDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_SupplierId] ON [PurchaseOrders] ([SupplierId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchasePayables_PurchaseOrderId] ON [PurchasePayables] ([PurchaseOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchasePayables_StockDocumentId] ON [PurchasePayables] ([StockDocumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchasePayables_StoreId_SourceKey] ON [PurchasePayables] ([StoreId], [SourceKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchasePayables_StoreId_Status_RecognizedAtUtc] ON [PurchasePayables] ([StoreId], [Status], [RecognizedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    CREATE INDEX [IX_PurchasePayables_SupplierId] ON [PurchasePayables] ([SupplierId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocument] ADD CONSTRAINT [FK_StockDocument_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [PurchaseOrders] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD CONSTRAINT [FK_StockDocumentLine_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD CONSTRAINT [FK_StockDocumentLine_PurchaseOrderLines_PurchaseOrderLineId] FOREIGN KEY ([PurchaseOrderLineId]) REFERENCES [PurchaseOrderLines] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    ALTER TABLE [StockDocumentLine] ADD CONSTRAINT [FK_StockDocumentLine_Taxes_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [Taxes] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719034458_AddPurchaseManagement'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719034458_AddPurchaseManagement', N'8.0.29');
END;
GO

COMMIT;
GO

