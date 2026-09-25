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

CREATE TABLE [AuditLogs] (
    [Id] bigint NOT NULL IDENTITY,
    [StoreId] int NOT NULL,
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
GO

CREATE TABLE [Permissions] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(150) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [GroupName] nvarchar(100) NOT NULL,
    CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [Stores] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [SubDomain] nvarchar(60) NOT NULL,
    [SubDomainNormalized] nvarchar(60) NOT NULL,
    [IsActive] bit NOT NULL,
    [IsMultiLegalEntityEnabled] bit NOT NULL DEFAULT CAST(0 AS bit),
    [MultiLegalEntityActivatedAtUtc] datetime2 NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Stores] PRIMARY KEY ([Id])
);
GO

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
GO

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
GO

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
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_Attribute] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Attribute_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

CREATE TABLE [Category] (
    [Id] int NOT NULL IDENTITY,
    [ParentId] int NULL,
    [IsRewardEligible] bit NOT NULL,
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
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [SortOrder] int NOT NULL DEFAULT 0,
    CONSTRAINT [PK_Category] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Category_Category_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [Category] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Category_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [Customers] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Phone] nvarchar(30) NULL,
    [Address] nvarchar(300) NULL,
    [Note] nvarchar(500) NULL,
    [Code] nvarchar(50) NULL,
    [OldCustomerId] bigint NULL,
    [CustomerGroup] nvarchar(30) NOT NULL DEFAULT N'MEMBER',
    [Email] nvarchar(100) NULL,
    [TaxCode] nvarchar(50) NULL,
    [HaveDebt] bit NOT NULL DEFAULT CAST(0 AS bit),
    [IsImportedFromOldSystem] bit NOT NULL DEFAULT CAST(0 AS bit),
    [ImportedRewardAmount] decimal(18,2) NOT NULL DEFAULT 0.0,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [PriceTier] nvarchar(30) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_Customers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customers_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
    [Priority] int NOT NULL,
    [IsFlashSale] bit NOT NULL,
    [CountdownToUtc] datetime2 NULL,
    [IsFullscreen] bit NOT NULL,
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
GO

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
GO

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
GO

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
    [AuthMode] tinyint NOT NULL DEFAULT CAST(2 AS tinyint),
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
    CONSTRAINT [AK_InvoiceProviderSettings_StoreId_Id] UNIQUE ([StoreId], [Id]),
    CONSTRAINT [FK_InvoiceProviderSettings_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

CREATE TABLE [MediaAssets] (
    [Id] int NOT NULL IDENTITY,
    [StoragePath] nvarchar(260) NOT NULL,
    [OriginalFileName] nvarchar(260) NULL,
    [ContentType] nvarchar(100) NULL,
    [SizeBytes] bigint NOT NULL,
    [Sha256] nvarchar(64) NULL,
    [IsTemp] bit NOT NULL,
    [TempToken] nvarchar(80) NULL,
    [ExpireAtUtc] datetime2 NULL,
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
GO

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
GO

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
GO

CREATE TABLE [POSTerminals] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(30) NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [LocalIp] nvarchar(100) NULL,
    [DeviceName] nvarchar(150) NULL,
    [AutoResolveByIp] bit NOT NULL,
    [Status] int NOT NULL,
    [IsActive] bit NOT NULL,
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
    CONSTRAINT [PK_POSTerminals] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_POSTerminals_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
    [Priority] int NOT NULL DEFAULT 0,
    [CustomerPriceTier] nvarchar(20) NULL,
    [ComboFixedPrice] decimal(18,2) NULL,
    [ComboNote] nvarchar(500) NULL,
    [BuyQuantity] decimal(18,4) NULL,
    [GetQuantity] decimal(18,4) NULL,
    [RequireGiftQuantityInCart] bit NOT NULL DEFAULT CAST(1 AS bit),
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
GO

CREATE TABLE [PurchaseRequests] (
    [Id] int NOT NULL IDENTITY,
    [RequestNumber] nvarchar(50) NOT NULL,
    [Title] nvarchar(250) NOT NULL,
    [RequestDate] datetime2 NOT NULL,
    [NeedByDate] datetime2 NULL,
    [Note] nvarchar(1000) NULL,
    [Status] int NOT NULL,
    [RequestedByUserId] int NOT NULL,
    [SubmittedAtUtc] datetime2 NULL,
    [SubmittedByUserId] int NULL,
    [ReturnedAtUtc] datetime2 NULL,
    [ReturnedByUserId] int NULL,
    [RejectedAtUtc] datetime2 NULL,
    [RejectedByUserId] int NULL,
    [ApprovedAtUtc] datetime2 NULL,
    [ApprovedByUserId] int NULL,
    [ConvertedAtUtc] datetime2 NULL,
    [ConvertedByUserId] int NULL,
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
    CONSTRAINT [PK_PurchaseRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PurchaseRequests_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

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
GO

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
GO

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
GO

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
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    [Code] nvarchar(30) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [IsActive] bit NOT NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_Taxes] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Taxes_Rate_0_100] CHECK ([Rate] >= 0 AND [Rate] <= 100),
    CONSTRAINT [FK_Taxes_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

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
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_AttributeValue] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AttributeValue_Attribute_AttributeId] FOREIGN KEY ([AttributeId]) REFERENCES [Attribute] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AttributeValue_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

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
GO

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
GO

CREATE TABLE [PromotionComboRule] (
    [Id] int NOT NULL IDENTITY,
    [PromotionId] int NOT NULL,
    [ProductId] int NOT NULL,
    [VariantId] int NULL,
    [ProductUnitConversionId] int NULL,
    [RequiredQuantity] decimal(18,4) NOT NULL DEFAULT 1.0,
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
    CONSTRAINT [FK_PromotionComboRule_Promotions_PromotionId] FOREIGN KEY ([PromotionId]) REFERENCES [Promotions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionComboRule_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [PromotionItems] (
    [Id] int NOT NULL IDENTITY,
    [PromotionId] int NOT NULL,
    [ProductId] int NOT NULL,
    [VariantId] int NULL,
    [ProductUnitConversionId] int NULL,
    [MinQuantity] decimal(18,4) NOT NULL DEFAULT 1.0,
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
GO

CREATE TABLE [RolePermissions] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] int NOT NULL,
    [PermissionId] int NOT NULL,
    CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [UserInStores] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [RoleId] int NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [PhoneNumber] nvarchar(30) NULL,
    [PositionName] nvarchar(100) NULL,
    [JoinedDate] datetime2 NULL,
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
    CONSTRAINT [PK_UserInStores] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UserInStores_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_UserInStores_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_UserInStores_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [Products] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Alias] nvarchar(200) NOT NULL,
    [CategoryId] int NOT NULL,
    [SupplierId] int NOT NULL,
    [BrandId] int NULL,
    [TaxId] int NULL,
    [BaseUnitId] int NOT NULL,
    [BasePrice] decimal(18,2) NOT NULL,
    [Description] nvarchar(500) NULL,
    [Content] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [IsSellable] bit NOT NULL DEFAULT CAST(1 AS bit),
    [IsRewardEligibleOverride] bit NULL,
    [RewardBulkExcludeQuantity] decimal(18,4) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Products_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_Category_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Category] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_Products_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_Taxes_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [Taxes] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_Unit_BaseUnitId] FOREIGN KEY ([BaseUnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
);
GO

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
    CONSTRAINT [FK_ProductImages_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ProductImages_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [ProductVariant] (
    [Id] int NOT NULL IDENTITY,
    [ProductId] int NOT NULL,
    [Sku] nvarchar(60) NOT NULL,
    [ProductVariantName] nvarchar(255) NULL,
    [ProductVariantNameNormalized] nvarchar(255) NULL,
    [CostPrice] decimal(18,2) NOT NULL,
    [Price] decimal(18,2) NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [PrimaryProductImageId] int NULL,
    [HasInputInvoice] bit NOT NULL,
    [WholesalePrice] decimal(18,2) NULL,
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
    CONSTRAINT [FK_ProductVariant_ProductImages_PrimaryProductImageId] FOREIGN KEY ([PrimaryProductImageId]) REFERENCES [ProductImages] ([Id]),
    CONSTRAINT [FK_ProductVariant_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ProductVariant_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [ProductUnitConversion] (
    [Id] int NOT NULL IDENTITY,
    [ProductVariantId] int NOT NULL,
    [UnitId] int NOT NULL,
    [Factor] decimal(18,4) NOT NULL,
    [IsBaseUnit] bit NOT NULL DEFAULT CAST(0 AS bit),
    [IsDefaultForSale] bit NOT NULL DEFAULT CAST(0 AS bit),
    [Price] decimal(18,2) NULL,
    [WholesalePrice] decimal(18,2) NULL,
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
GO

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
    CONSTRAINT [FK_ProductVariantAttributeValue_AttributeValue_AttributeValueId] FOREIGN KEY ([AttributeValueId]) REFERENCES [AttributeValue] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductVariantAttributeValue_Attribute_AttributeId] FOREIGN KEY ([AttributeId]) REFERENCES [Attribute] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductVariantAttributeValue_ProductVariant_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductVariantAttributeValue_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

CREATE TABLE [PurchaseRequestLines] (
    [Id] int NOT NULL IDENTITY,
    [PurchaseRequestId] int NOT NULL,
    [LineNo] int NOT NULL,
    [ItemKind] int NOT NULL DEFAULT 1,
    [ProductVariantId] int NULL,
    [UnitId] int NULL,
    [ProductUnitConversionId] int NULL,
    [ProductNameSnapshot] nvarchar(250) NOT NULL,
    [SkuSnapshot] nvarchar(100) NULL,
    [UnitNameSnapshot] nvarchar(100) NOT NULL,
    [ConversionFactor] decimal(18,4) NOT NULL,
    [RequestedQuantity] decimal(18,3) NOT NULL,
    [ApprovedQuantity] decimal(18,3) NULL,
    [ConvertedQuantity] decimal(18,3) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_PurchaseRequestLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PurchaseRequestLines_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseRequestLines_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseRequestLines_PurchaseRequests_PurchaseRequestId] FOREIGN KEY ([PurchaseRequestId]) REFERENCES [PurchaseRequests] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseRequestLines_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_PurchaseRequestLines_Unit_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [ProductVariantBarcodeHistory] (
    [Id] int NOT NULL IDENTITY,
    [ProductVariantId] int NOT NULL,
    [ProductUnitConversionId] int NOT NULL,
    [OldBarcodeId] int NULL,
    [NewBarcodeId] int NULL,
    [OldBarcode] nvarchar(64) NULL,
    [NewBarcode] nvarchar(64) NULL,
    [ActionType] int NOT NULL,
    [Reason] nvarchar(500) NULL,
    [ChangedByUserId] int NULL,
    [ChangedByUserName] nvarchar(200) NULL,
    [ChangedAtUtc] datetime2 NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_ProductVariantBarcodeHistory] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProductVariantBarcodeHistory_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductVariantBarcodeHistory_ProductVariantUnitBarcode_NewBarcodeId] FOREIGN KEY ([NewBarcodeId]) REFERENCES [ProductVariantUnitBarcode] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductVariantBarcodeHistory_ProductVariantUnitBarcode_OldBarcodeId] FOREIGN KEY ([OldBarcodeId]) REFERENCES [ProductVariantUnitBarcode] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductVariantBarcodeHistory_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductVariantBarcodeHistory_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
    CONSTRAINT [FK_CustomerRewardLedgers_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [CustomerRewardVouchers] (
    [Id] int NOT NULL IDENTITY,
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
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_CustomerRewardVouchers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustomerRewardVouchers_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerRewardVouchers_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
    CONSTRAINT [FK_InventoryAdjustmentDocuments_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

CREATE TABLE [InventoryBalances] (
    [Id] int NOT NULL IDENTITY,
    [WarehouseId] int NOT NULL,
    [ProductVariantId] int NOT NULL,
    [OnHandQty] decimal(18,4) NOT NULL DEFAULT 0.0,
    [ReservedQty] decimal(18,4) NOT NULL DEFAULT 0.0,
    [InventoryValue] decimal(18,4) NOT NULL DEFAULT 0.0,
    [AverageUnitCost] decimal(18,6) NOT NULL DEFAULT 0.0,
    [LastInboundUnitCost] decimal(18,6) NULL,
    [LastInboundAtUtc] datetime2 NULL,
    [LastValuationAtUtc] datetime2 NULL,
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
    CONSTRAINT [FK_InventoryBalances_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
    [ResolvedQuantity] decimal(18,4) NOT NULL,
    [ResolvedAmount] decimal(18,4) NOT NULL,
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
    CONSTRAINT [FK_InventoryCostLayerAllocations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
    [ResolvedProvisionalQty] decimal(18,4) NOT NULL,
    [RemainingOpenProvisionalQty] decimal(18,4) NOT NULL,
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
    CONSTRAINT [FK_InventoryCostLayers_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryCostLayers_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
    CONSTRAINT [FK_InventoryReservations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [InventoryTransactions] (
    [Id] int NOT NULL IDENTITY,
    [WarehouseId] int NOT NULL,
    [ProductVariantId] int NOT NULL,
    [TransactionType] int NOT NULL,
    [ReferenceType] int NOT NULL,
    [ReferenceId] nvarchar(64) NOT NULL,
    [ReferenceLineId] int NULL,
    [QuantityChange] decimal(18,4) NOT NULL,
    [BeforeQty] decimal(18,4) NOT NULL,
    [AfterQty] decimal(18,4) NOT NULL,
    [UnitCostSnapshot] decimal(18,6) NOT NULL DEFAULT 0.0,
    [TotalCost] decimal(18,4) NOT NULL DEFAULT 0.0,
    [BeforeInventoryValue] decimal(18,4) NOT NULL DEFAULT 0.0,
    [AfterInventoryValue] decimal(18,4) NOT NULL DEFAULT 0.0,
    [RunningAverageUnitCostAfter] decimal(18,6) NOT NULL DEFAULT 0.0,
    [CostSourceType] int NOT NULL,
    [IsProvisionalCost] bit NOT NULL DEFAULT CAST(0 AS bit),
    [CostFinalizedAtUtc] datetime2 NULL,
    [OccurredAtUtc] datetime2 NOT NULL,
    [Note] nvarchar(1000) NULL,
    [ReferenceSubKey] nvarchar(100) NULL,
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
    CONSTRAINT [FK_InventoryTransactions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [InventoryValuationEntries] (
    [Id] int NOT NULL IDENTITY,
    [InventoryTransactionId] int NOT NULL,
    [WarehouseId] int NOT NULL,
    [ProductVariantId] int NOT NULL,
    [EntryType] int NOT NULL,
    [ReferenceType] int NOT NULL,
    [ReferenceId] nvarchar(64) NOT NULL,
    [ReferenceLineId] int NULL,
    [ReferenceSubKey] nvarchar(100) NULL,
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
    [SourceValuationEntryId] int NULL,
    [SourceReferenceSubKey] nvarchar(100) NULL,
    [InventoryCostLayerId] int NULL,
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
    CONSTRAINT [FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId] FOREIGN KEY ([InventoryCostLayerId]) REFERENCES [InventoryCostLayers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryValuationEntries_InventoryTransactions_InventoryTransactionId] FOREIGN KEY ([InventoryTransactionId]) REFERENCES [InventoryTransactions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryValuationEntries_InventoryValuationEntries_RevaluationOfEntryId] FOREIGN KEY ([RevaluationOfEntryId]) REFERENCES [InventoryValuationEntries] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryValuationEntries_InventoryValuationEntries_SourceValuationEntryId] FOREIGN KEY ([SourceValuationEntryId]) REFERENCES [InventoryValuationEntries] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryValuationEntries_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryValuationEntries_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [InvoiceCorrectionCases] (
    [Id] int NOT NULL IDENTITY,
    [OriginalInvoiceHeadId] int NOT NULL,
    [NewInvoiceHeadId] int NULL,
    [Type] tinyint NOT NULL,
    [Status] tinyint NOT NULL DEFAULT CAST(0 AS tinyint),
    [Reason] nvarchar(255) NOT NULL,
    [AgreementDocumentNo] nvarchar(255) NOT NULL,
    [AgreementDateUtc] datetime2 NOT NULL,
    [Note] nvarchar(1000) NULL,
    [CreatedByUserId] int NULL,
    [IssuedAtUtc] datetime2 NULL,
    [LastErrorCode] nvarchar(100) NULL,
    [LastErrorMessage] nvarchar(1000) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_InvoiceCorrectionCases] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_InvoiceCorrectionCases_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [InvoiceDetails] (
    [Id] int NOT NULL IDENTITY,
    [InvoiceHeadId] int NOT NULL,
    [OrderLineId] int NULL,
    [OrderLegalEntityAllocationId] int NULL,
    [ProductVariantId] int NULL,
    [SourceType] tinyint NOT NULL,
    [ItemName] nvarchar(250) NOT NULL,
    [UnitName] nvarchar(100) NULL,
    [Quantity] decimal(18,3) NOT NULL,
    [UnitPrice] decimal(18,2) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [VatRate] decimal(9,2) NOT NULL,
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
    CONSTRAINT [FK_InvoiceDetails_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]),
    CONSTRAINT [FK_InvoiceDetails_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [InvoiceHeads] (
    [Id] int NOT NULL IDENTITY,
    [LegalEntityId] int NULL,
    [InvoiceProviderSettingId] int NULL,
    [OrderId] int NOT NULL,
    [InvoiceNumber] nvarchar(50) NULL,
    [InvoiceDate] datetime2 NOT NULL,
    [BuyerType] nvarchar(30) NOT NULL DEFAULT N'NoInvoice',
    [BuyerName] nvarchar(300) NULL,
    [BuyerLegalName] nvarchar(500) NULL,
    [BuyerTaxCode] nvarchar(50) NULL,
    [BuyerAddress] nvarchar(1200) NULL,
    [BuyerEmail] nvarchar(2000) NULL,
    [BuyerPhone] nvarchar(30) NULL,
    [TotalQuantity] decimal(18,3) NOT NULL,
    [SubTotal] decimal(18,2) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [GrandTotal] decimal(18,2) NOT NULL,
    [Note] nvarchar(500) NULL,
    [IsLocked] bit NOT NULL DEFAULT CAST(0 AS bit),
    [LockedAtUtc] datetime2 NULL,
    [LockedByUserId] int NULL,
    [LockReason] nvarchar(500) NULL,
    [ProviderStatus] tinyint NOT NULL DEFAULT CAST(0 AS tinyint),
    [TransactionUuid] nvarchar(36) NULL,
    [ProviderCode] nvarchar(50) NULL,
    [SupplierTaxCode] nvarchar(20) NULL,
    [InvoiceType] nvarchar(20) NULL,
    [TemplateCode] nvarchar(20) NULL,
    [InvoiceSeries] nvarchar(25) NULL,
    [ProviderInvoiceNo] nvarchar(35) NULL,
    [ProviderTransactionId] nvarchar(100) NULL,
    [ReservationCode] nvarchar(100) NULL,
    [CodeOfTax] nvarchar(200) NULL,
    [IssuedAtUtc] datetime2 NULL,
    [LastSyncedAtUtc] datetime2 NULL,
    [LastErrorCode] nvarchar(100) NULL,
    [LastErrorMessage] nvarchar(1000) NULL,
    [PdfFilePath] nvarchar(500) NULL,
    [ZipFilePath] nvarchar(500) NULL,
    [OfficialPdfStatus] int NOT NULL,
    [OfficialPdfDownloadedAtUtc] datetime2 NULL,
    [OfficialPdfFileName] nvarchar(260) NULL,
    [OfficialZipXmlStatus] int NOT NULL,
    [OfficialZipXmlDownloadedAtUtc] datetime2 NULL,
    [OfficialZipXmlFileName] nvarchar(260) NULL,
    [EmailStatus] int NOT NULL,
    [EmailSentAtUtc] datetime2 NULL,
    [LastEmailTo] nvarchar(500) NULL,
    [EmailSendCount] int NOT NULL,
    [LastEmailErrorMessage] nvarchar(1000) NULL,
    [OriginalInvoiceHeadId] int NULL,
    [CorrectionType] tinyint NULL,
    [OriginalInvoiceNo] nvarchar(50) NULL,
    [OriginalInvoiceIssuedAtUtc] datetime2 NULL,
    [AdjustedNote] nvarchar(255) NULL,
    [AdditionalReferenceDesc] nvarchar(255) NULL,
    [AdditionalReferenceDateUtc] datetime2 NULL,
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
    CONSTRAINT [FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId] FOREIGN KEY ([OriginalInvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InvoiceHeads_InvoiceProviderSettings_StoreId_InvoiceProviderSettingId] FOREIGN KEY ([StoreId], [InvoiceProviderSettingId]) REFERENCES [InvoiceProviderSettings] ([StoreId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InvoiceHeads_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

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
    CONSTRAINT [FK_LegalEntities_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [Warehouses] (
    [Id] int NOT NULL IDENTITY,
    [LegalEntityId] int NOT NULL,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Location] nvarchar(255) NULL,
    [Note] nvarchar(500) NULL,
    [IsDefault] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [AllowNegativeInventory] bit NOT NULL,
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
    CONSTRAINT [AK_Warehouses_StoreId_Id] UNIQUE ([StoreId], [Id]),
    CONSTRAINT [FK_Warehouses_LegalEntities_StoreId_LegalEntityId] FOREIGN KEY ([StoreId], [LegalEntityId]) REFERENCES [LegalEntities] ([StoreId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Warehouses_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

CREATE TABLE [PurchaseOrders] (
    [Id] int NOT NULL IDENTITY,
    [OrderNumber] nvarchar(50) NOT NULL,
    [SourcePurchaseRequestId] int NULL,
    [SourceConversionKey] nvarchar(64) NULL,
    [Title] nvarchar(250) NULL,
    [SupplierId] int NOT NULL,
    [ExpectedWarehouseId] int NOT NULL,
    [LegalEntityId] int NOT NULL,
    [OrderDate] datetime2 NOT NULL,
    [ExpectedDeliveryDate] datetime2 NULL,
    [Note] nvarchar(1000) NULL,
    [OutsideRequestReason] nvarchar(500) NULL,
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
    CONSTRAINT [FK_PurchaseOrders_PurchaseRequests_SourcePurchaseRequestId] FOREIGN KEY ([SourcePurchaseRequestId]) REFERENCES [PurchaseRequests] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseOrders_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_PurchaseOrders_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseOrders_Warehouses_ExpectedWarehouseId] FOREIGN KEY ([ExpectedWarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [StockCountDocument] (
    [Id] int NOT NULL IDENTITY,
    [WarehouseId] int NOT NULL,
    [DocumentNo] nvarchar(50) NOT NULL,
    [DocumentName] nvarchar(250) NULL,
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
GO

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
GO

CREATE TABLE [PurchaseOrderLines] (
    [Id] int NOT NULL IDENTITY,
    [PurchaseOrderId] int NOT NULL,
    [SourcePurchaseRequestLineId] int NULL,
    [LineNo] int NOT NULL,
    [ItemKind] int NOT NULL DEFAULT 1,
    [ProductVariantId] int NULL,
    [UnitId] int NULL,
    [ProductUnitConversionId] int NULL,
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
    [ResolvedAtUtc] datetime2 NULL,
    [ResolvedByUserId] int NULL,
    [ResolutionNote] nvarchar(500) NULL,
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
    CONSTRAINT [FK_PurchaseOrderLines_PurchaseRequestLines_SourcePurchaseRequestLineId] FOREIGN KEY ([SourcePurchaseRequestLineId]) REFERENCES [PurchaseRequestLines] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseOrderLines_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_PurchaseOrderLines_Taxes_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [Taxes] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseOrderLines_Unit_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [PurchaseRequestActions] (
    [Id] int NOT NULL IDENTITY,
    [PurchaseRequestId] int NOT NULL,
    [ActionType] int NOT NULL,
    [FromStatus] int NOT NULL,
    [ToStatus] int NOT NULL,
    [ActorUserId] int NULL,
    [OccurredAtUtc] datetime2 NOT NULL,
    [Note] nvarchar(1000) NULL,
    [PurchaseOrderId] int NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_PurchaseRequestActions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PurchaseRequestActions_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [PurchaseOrders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseRequestActions_PurchaseRequests_PurchaseRequestId] FOREIGN KEY ([PurchaseRequestId]) REFERENCES [PurchaseRequests] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseRequestActions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [StockDocument] (
    [Id] int NOT NULL IDENTITY,
    [DocumentNo] nvarchar(50) NOT NULL,
    [DocumentTitle] nvarchar(255) NULL,
    [Type] int NOT NULL,
    [Status] int NOT NULL,
    [DocumentDate] datetime2 NOT NULL,
    [WarehouseId] int NOT NULL,
    [SupplierId] int NULL,
    [ReceiptSource] int NOT NULL,
    [PurchaseOrderId] int NULL,
    [DirectReceiptReason] nvarchar(500) NULL,
    [HasVat] bit NOT NULL,
    [SubtotalBeforeVat] decimal(18,2) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [HasFreight] bit NOT NULL,
    [FreightTotal] decimal(18,2) NOT NULL,
    [FreightPayeeName] nvarchar(250) NULL,
    [FreightNote] nvarchar(1000) NULL,
    [IsFreightPaid] bit NOT NULL,
    [IsMerchandisePaid] bit NOT NULL,
    [MerchandisePayeeName] nvarchar(250) NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [Note] nvarchar(1000) NULL,
    [SubmittedAtUtc] datetime2 NULL,
    [SubmittedByUserId] int NULL,
    [ApprovedAtUtc] datetime2 NULL,
    [ApprovedByUserId] int NULL,
    [ApprovalNote] nvarchar(1000) NULL,
    [HasRevisionRequest] bit NOT NULL,
    [RevisionRequestNote] nvarchar(1000) NULL,
    [RevisionRequestedAtUtc] datetime2 NULL,
    [RevisionRequestedByUserId] int NULL,
    [RevisionResolvedAtUtc] datetime2 NULL,
    [RevisionResolvedByUserId] int NULL,
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
    CONSTRAINT [FK_StockDocument_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [PurchaseOrders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocument_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_StockDocument_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocument_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
);
GO

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
    [UnitCostSnapshot] decimal(18,6) NOT NULL DEFAULT 0.0,
    [LineCostTotal] decimal(18,4) NOT NULL DEFAULT 0.0,
    [IsProvisionalCost] bit NOT NULL DEFAULT CAST(0 AS bit),
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
GO

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
    [UnitCostSnapshot] decimal(18,6) NOT NULL DEFAULT 0.0,
    [LineCostTotal] decimal(18,4) NOT NULL DEFAULT 0.0,
    [IsProvisionalCost] bit NOT NULL DEFAULT CAST(0 AS bit),
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
GO

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
GO

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
GO

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
GO

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
GO

CREATE TABLE [StockDocumentLine] (
    [Id] int NOT NULL IDENTITY,
    [StockDocumentId] int NOT NULL,
    [LineNo] int NOT NULL,
    [ProductVariantId] int NOT NULL,
    [PurchaseOrderLineId] int NULL,
    [ProductUnitConversionId] int NULL,
    [TaxId] int NULL,
    [TaxNameSnapshot] nvarchar(100) NULL,
    [UnitId] int NULL,
    [UnitNameSnapshot] nvarchar(100) NULL,
    [Factor] decimal(18,4) NOT NULL,
    [Quantity] decimal(18,3) NOT NULL,
    [BaseQuantity] decimal(18,3) NOT NULL,
    [UnitCost] decimal(18,2) NOT NULL,
    [LineTotal] decimal(18,2) NOT NULL,
    [UnitPriceBeforeVat] decimal(18,2) NOT NULL,
    [TaxRate] decimal(5,2) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [UnitPriceAfterVat] decimal(18,2) NOT NULL,
    [FreightAllocation] decimal(18,2) NOT NULL,
    [ShortageDisposition] int NOT NULL,
    [ShortageReason] nvarchar(500) NULL,
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
    CONSTRAINT [FK_StockDocumentLine_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentLine_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentLine_PurchaseOrderLines_PurchaseOrderLineId] FOREIGN KEY ([PurchaseOrderLineId]) REFERENCES [PurchaseOrderLines] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentLine_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_StockDocumentLine_Taxes_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [Taxes] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentLine_Unit_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
);
GO

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
GO

CREATE TABLE [OrderInventoryIssueActions] (
    [Id] int NOT NULL IDENTITY,
    [OrderInventoryIssueId] int NOT NULL,
    [OrderInventoryIssueLineId] int NULL,
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
    CONSTRAINT [FK_OrderInventoryIssueActions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_OrderInventoryIssueActions_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [OrderInventoryIssueLineAllocations] (
    [Id] int NOT NULL IDENTITY,
    [OrderInventoryIssueId] int NOT NULL,
    [OrderInventoryIssueLineId] int NOT NULL,
    [SourceReferenceType] int NOT NULL,
    [SourceReferenceId] int NOT NULL,
    [SourceReferenceLineId] int NULL,
    [InventoryCostLayerId] int NULL,
    [InventoryCostLayerAllocationId] int NULL,
    [InventoryTransactionId] int NULL,
    [AllocatedQuantity] decimal(18,4) NOT NULL,
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
    CONSTRAINT [FK_OrderInventoryIssueLineAllocations_InventoryCostLayerAllocations_InventoryCostLayerAllocationId] FOREIGN KEY ([InventoryCostLayerAllocationId]) REFERENCES [InventoryCostLayerAllocations] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderInventoryIssueLineAllocations_InventoryCostLayers_InventoryCostLayerId] FOREIGN KEY ([InventoryCostLayerId]) REFERENCES [InventoryCostLayers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderInventoryIssueLineAllocations_InventoryTransactions_InventoryTransactionId] FOREIGN KEY ([InventoryTransactionId]) REFERENCES [InventoryTransactions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderInventoryIssueLineAllocations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [OrderInventoryIssueLines] (
    [Id] int NOT NULL IDENTITY,
    [OrderInventoryIssueId] int NOT NULL,
    [OrderId] int NOT NULL,
    [OrderLineId] int NOT NULL,
    [ProductId] int NOT NULL,
    [ProductVariantId] int NOT NULL,
    [ProductUnitConversionId] int NULL,
    [BarcodeId] int NULL,
    [OrderedQty] decimal(18,4) NOT NULL,
    [StockBefore] decimal(18,4) NOT NULL,
    [StockAfter] decimal(18,4) NOT NULL,
    [NegativeQty] decimal(18,4) NOT NULL,
    [ProvisionalUnitCost] decimal(18,6) NULL,
    [ProvisionalCostAmount] decimal(18,2) NULL,
    [RevaluationAmount] decimal(18,2) NULL,
    [IsResolved] bit NOT NULL,
    [ResolvedAtUtc] datetime2 NULL,
    [AutoDetectedInboundQty] decimal(18,4) NOT NULL DEFAULT 0.0,
    [AutoDetectedDocumentResolved] bit NOT NULL,
    [AutoDetectedCostResolved] bit NOT NULL,
    [AutoDetectedRevaluationAmount] decimal(18,2) NULL,
    [AutoResolveNote] nvarchar(2000) NULL,
    [LastAutoResolvedAtUtc] datetime2 NULL,
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
    CONSTRAINT [FK_OrderInventoryIssueLines_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderInventoryIssueLines_ProductVariantUnitBarcode_BarcodeId] FOREIGN KEY ([BarcodeId]) REFERENCES [ProductVariantUnitBarcode] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderInventoryIssueLines_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderInventoryIssueLines_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderInventoryIssueLines_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
    [OverdueSinceUtc] datetime2 NULL,
    [LastOverdueNotifiedAtUtc] datetime2 NULL,
    [LastAutoResolvedAtUtc] datetime2 NULL,
    [AutoResolvedLineCount] int NOT NULL,
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
    CONSTRAINT [FK_OrderInventoryIssues_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_OrderInventoryIssues_Users_ApprovedByUserId] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderInventoryIssues_Users_RejectedByUserId] FOREIGN KEY ([RejectedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO

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
    CONSTRAINT [FK_OrderLegalEntityAllocationReversals_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderLegalEntityAllocationReversals_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_OrderLegalEntityAllocationReversals_Warehouses_StoreId_WarehouseId] FOREIGN KEY ([StoreId], [WarehouseId]) REFERENCES [Warehouses] ([StoreId], [Id]) ON DELETE NO ACTION
);
GO

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
    CONSTRAINT [AK_OrderLegalEntityAllocations_StoreId_Id] UNIQUE ([StoreId], [Id]),
    CONSTRAINT [CK_OrderLegalEntityAllocations_Amounts_NonNegative] CHECK ([LineTotal] >= 0 AND [DiscountAllocated] >= 0 AND [PromotionDiscountAllocated] >= 0 AND [ComboDiscountAllocated] >= 0 AND [OrderDiscountAllocated] >= 0 AND [VoucherDiscountAllocated] >= 0 AND [NetAmount] >= 0),
    CONSTRAINT [CK_OrderLegalEntityAllocations_Quantity_Positive] CHECK ([Quantity] > 0 AND [BaseQuantity] > 0),
    CONSTRAINT [CK_OrderLegalEntityAllocations_SalePriority_Positive] CHECK ([SalePriority] > 0),
    CONSTRAINT [FK_OrderLegalEntityAllocations_InventoryTransactions_InventoryTransactionId] FOREIGN KEY ([InventoryTransactionId]) REFERENCES [InventoryTransactions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderLegalEntityAllocations_LegalEntities_StoreId_LegalEntityId] FOREIGN KEY ([StoreId], [LegalEntityId]) REFERENCES [LegalEntities] ([StoreId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderLegalEntityAllocations_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderLegalEntityAllocations_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderLegalEntityAllocations_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_OrderLegalEntityAllocations_Warehouses_StoreId_WarehouseId] FOREIGN KEY ([StoreId], [WarehouseId]) REFERENCES [Warehouses] ([StoreId], [Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [OrderLines] (
    [Id] int NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [ProductId] int NOT NULL,
    [VariantId] int NOT NULL,
    [ItemName] nvarchar(200) NOT NULL,
    [UnitName] nvarchar(50) NULL,
    [Sku] nvarchar(50) NULL,
    [Barcode] nvarchar(50) NULL,
    [Quantity] decimal(18,4) NOT NULL,
    [UnitCostSnapshot] decimal(18,2) NULL,
    [LineCostTotal] decimal(18,2) NULL,
    [GrossProfit] decimal(18,2) NULL,
    [IsProvisionalCost] bit NOT NULL DEFAULT CAST(0 AS bit),
    [ProvisionalUnitCost] decimal(18,2) NULL,
    [CostSnapshotNote] nvarchar(1000) NULL,
    [SellingUnitId] int NULL,
    [ProductUnitConversionId] int NULL,
    [SellingUnitName] nvarchar(100) NULL,
    [BaseUnitId] int NULL,
    [BaseUnitName] nvarchar(100) NULL,
    [Multiplier] decimal(18,6) NOT NULL DEFAULT 1.0,
    [BaseQuantity] decimal(18,4) NOT NULL,
    [ScannedBarcode] nvarchar(100) NULL,
    [BarcodeSource] int NULL,
    [UnitPrice] decimal(18,2) NOT NULL,
    [LineDiscount] decimal(18,2) NOT NULL,
    [LineTotal] decimal(18,2) NOT NULL,
    [OriginalUnitPrice] decimal(18,2) NOT NULL,
    [PromotionDiscount] decimal(18,2) NOT NULL,
    [PromotionId] int NULL,
    [PromotionName] nvarchar(max) NULL,
    [ComboPromotionId] int NULL,
    [ComboPromotionName] nvarchar(200) NULL,
    [ComboPromotionNote] nvarchar(500) NULL,
    [ComboAllocatedDiscount] decimal(18,2) NOT NULL,
    [PromotionType] tinyint NULL,
    [PromotionBuyQuantity] decimal(18,4) NOT NULL DEFAULT 0.0,
    [PromotionGiftQuantity] decimal(18,4) NOT NULL DEFAULT 0.0,
    [IsPromotionGift] bit NOT NULL DEFAULT CAST(0 AS bit),
    [GiftPromotionId] int NULL,
    [GiftSourceLineId] int NULL,
    [GiftPromotionName] nvarchar(200) NULL,
    [GiftPromotionNote] nvarchar(500) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_OrderLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLines_ProductVariant_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrderLines_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [OrderPayments] (
    [Id] int NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [Method] int NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [ReferenceCode] nvarchar(100) NULL,
    [Provider] nvarchar(50) NULL,
    [PaidAtUtc] datetime2 NOT NULL,
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
    CONSTRAINT [PK_OrderPayments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderPayments_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
    CONSTRAINT [FK_OrderRewardVouchers_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [Orders] (
    [Id] int NOT NULL IDENTITY,
    [OrderNumber] nvarchar(30) NULL,
    [Status] tinyint NOT NULL,
    [PaymentStatus] tinyint NOT NULL,
    [CustomerId] int NULL,
    [Subtotal] decimal(18,2) NOT NULL,
    [DiscountTotal] decimal(18,2) NOT NULL,
    [OrderDiscount] decimal(18,2) NOT NULL,
    [GrandTotal] decimal(18,2) NOT NULL,
    [PaidTotal] decimal(18,2) NOT NULL,
    [BalanceDue] decimal(18,2) NOT NULL,
    [ChangeDue] decimal(18,2) NOT NULL,
    [Note] nvarchar(500) NULL,
    [POSShiftId] int NOT NULL,
    [CompletedAtUtc] datetime2 NULL,
    [HeldAtUtc] datetime2 NULL,
    [HoldNote] nvarchar(500) NULL,
    [HoldCode] nvarchar(50) NULL,
    [HasReservation] bit NOT NULL,
    [ReservedAtUtc] datetime2 NULL,
    [LegalEntityCount] int NOT NULL DEFAULT 0,
    [HasMultipleLegalEntities] bit NOT NULL DEFAULT CAST(0 AS bit),
    [LegalEntityAllocatedAtUtc] datetime2 NULL,
    [UseMultiLegalEntity] bit NOT NULL DEFAULT CAST(0 AS bit),
    [LegalEntityModeCapturedAtUtc] datetime2 NULL,
    [LegalEntityActivationAtUtcSnapshot] datetime2 NULL,
    [HasInventoryIssue] bit NOT NULL DEFAULT CAST(0 AS bit),
    [InventoryResolutionStatus] int NOT NULL DEFAULT 0,
    [InventoryIssueOpenedAtUtc] datetime2 NULL,
    [InventoryIssueApprovedAtUtc] datetime2 NULL,
    [VoucherDiscountTotal] decimal(18,2) NOT NULL,
    [PromotionDiscountTotal] decimal(18,2) NOT NULL,
    [ComboDiscountTotal] decimal(18,2) NOT NULL,
    [ComboPromotionId] int NULL,
    [ComboPromotionName] nvarchar(200) NULL,
    [ComboPromotionNote] nvarchar(500) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Orders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]),
    CONSTRAINT [FK_Orders_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

CREATE TABLE [POSShifts] (
    [Id] int NOT NULL IDENTITY,
    [TerminalId] int NOT NULL,
    [OpenedByUserId] int NOT NULL,
    [OpenedAtUtc] datetime2 NOT NULL,
    [Status] tinyint NOT NULL,
    [ShiftCode] nvarchar(30) NULL,
    [OpeningCash] decimal(18,2) NOT NULL DEFAULT 0.0,
    [OpenNote] nvarchar(300) NULL,
    [CashSalesTotal] decimal(18,2) NOT NULL DEFAULT 0.0,
    [NonCashSalesTotal] decimal(18,2) NOT NULL DEFAULT 0.0,
    [CashRefundTotal] decimal(18,2) NOT NULL DEFAULT 0.0,
    [NonCashRefundTotal] decimal(18,2) NOT NULL DEFAULT 0.0,
    [RefundCount] int NOT NULL,
    [VoidCount] int NOT NULL,
    [CashInTotal] decimal(18,2) NOT NULL DEFAULT 0.0,
    [CashOutTotal] decimal(18,2) NOT NULL DEFAULT 0.0,
    [ClosingCashExpected] decimal(18,2) NOT NULL DEFAULT 0.0,
    [ClosingCashActual] decimal(18,2) NULL,
    [ClosedByUserId] int NULL,
    [ClosedAtUtc] datetime2 NULL,
    [CloseNote] nvarchar(300) NULL,
    [CurrentOrderId] int NULL,
    [WarehouseId] int NOT NULL,
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
    CONSTRAINT [CK_POSShifts_CashInTotal_NonNegative] CHECK ([CashInTotal] >= 0),
    CONSTRAINT [CK_POSShifts_CashOutTotal_NonNegative] CHECK ([CashOutTotal] >= 0),
    CONSTRAINT [CK_POSShifts_CashSalesTotal_NonNegative] CHECK ([CashSalesTotal] >= 0),
    CONSTRAINT [CK_POSShifts_ClosingCashActual_NonNegative] CHECK ([ClosingCashActual] IS NULL OR [ClosingCashActual] >= 0),
    CONSTRAINT [CK_POSShifts_ClosingCashExpected_NonNegative] CHECK ([ClosingCashExpected] >= 0),
    CONSTRAINT [CK_POSShifts_NonCashSalesTotal_NonNegative] CHECK ([NonCashSalesTotal] >= 0),
    CONSTRAINT [CK_POSShifts_OpeningCash_NonNegative] CHECK ([OpeningCash] >= 0),
    CONSTRAINT [FK_POSShifts_Orders_CurrentOrderId] FOREIGN KEY ([CurrentOrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_POSShifts_POSTerminals_TerminalId] FOREIGN KEY ([TerminalId]) REFERENCES [POSTerminals] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_POSShifts_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_POSShifts_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
);
GO

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
GO

CREATE TABLE [POSShiftCashTransactions] (
    [Id] int NOT NULL IDENTITY,
    [POSShiftId] int NOT NULL,
    [Type] int NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Reason] nvarchar(300) NOT NULL,
    [Note] nvarchar(500) NULL,
    [CreatedByUserId] int NOT NULL,
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
GO

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
GO

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
GO

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
    [CompletedByUserId] int NULL,
    [CompletedAtUtc] datetime2 NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
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
GO

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
GO

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
GO

CREATE TABLE [SalesReturnLines] (
    [Id] int NOT NULL IDENTITY,
    [SalesReturnId] int NOT NULL,
    [OrderLineId] int NOT NULL,
    [ProductId] int NOT NULL,
    [VariantId] int NOT NULL,
    [ItemName] nvarchar(250) NOT NULL,
    [UnitName] nvarchar(100) NULL,
    [ReturnQuantity] decimal(18,3) NOT NULL,
    [ReturnBaseQuantity] decimal(18,3) NOT NULL,
    [RefundUnitAmount] decimal(18,2) NOT NULL DEFAULT 0.0,
    [RefundLineTotal] decimal(18,2) NOT NULL DEFAULT 0.0,
    [Action] int NOT NULL,
    [Reason] nvarchar(500) NULL,
    [UnitCostSnapshot] decimal(18,6) NOT NULL DEFAULT 0.0,
    [LineCostTotal] decimal(18,4) NOT NULL DEFAULT 0.0,
    [IsProvisionalCost] bit NOT NULL DEFAULT CAST(0 AS bit),
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
    CONSTRAINT [FK_SalesReturnLines_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SalesReturnLines_SalesReturns_SalesReturnId] FOREIGN KEY ([SalesReturnId]) REFERENCES [SalesReturns] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_SalesReturnLines_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

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
GO

CREATE INDEX [IX_AdminMenuItems_ParentId] ON [AdminMenuItems] ([ParentId]);
GO

CREATE INDEX [IX_AdminMenuItems_StoreId_ParentId_SortOrder] ON [AdminMenuItems] ([StoreId], [ParentId], [SortOrder]);
GO

CREATE UNIQUE INDEX [IX_Attribute_StoreId_Code] ON [Attribute] ([StoreId], [Code]);
GO

CREATE UNIQUE INDEX [IX_Attribute_StoreId_Name] ON [Attribute] ([StoreId], [Name]);
GO

CREATE INDEX [IX_AttributeValue_AttributeId] ON [AttributeValue] ([AttributeId]);
GO

CREATE UNIQUE INDEX [IX_AttributeValue_StoreId_AttributeId_Code] ON [AttributeValue] ([StoreId], [AttributeId], [Code]);
GO

CREATE UNIQUE INDEX [IX_AttributeValue_StoreId_AttributeId_Name] ON [AttributeValue] ([StoreId], [AttributeId], [Name]);
GO

CREATE INDEX [IX_AuditLogs_EntityName_EntityId_CreatedAtUtc] ON [AuditLogs] ([EntityName], [EntityId], [CreatedAtUtc]);
GO

CREATE INDEX [IX_AuditLogs_StoreId] ON [AuditLogs] ([StoreId]);
GO

CREATE INDEX [IX_AuditLogs_StoreId_ActorUserId_CreatedAtUtc] ON [AuditLogs] ([StoreId], [ActorUserId], [CreatedAtUtc]);
GO

CREATE INDEX [IX_AuditLogs_StoreId_CreatedAtUtc] ON [AuditLogs] ([StoreId], [CreatedAtUtc]);
GO

CREATE INDEX [IX_AuditLogs_StoreId_Module_ActionType_CreatedAtUtc] ON [AuditLogs] ([StoreId], [Module], [ActionType], [CreatedAtUtc]);
GO

CREATE INDEX [IX_AuditLogs_TraceId] ON [AuditLogs] ([TraceId]);
GO

CREATE UNIQUE INDEX [IX_Brands_StoreId_Code] ON [Brands] ([StoreId], [Code]);
GO

CREATE UNIQUE INDEX [IX_Brands_StoreId_Name] ON [Brands] ([StoreId], [Name]);
GO

CREATE INDEX [IX_Category_ParentId] ON [Category] ([ParentId]);
GO

CREATE UNIQUE INDEX [IX_Category_StoreId_Code] ON [Category] ([StoreId], [Code]);
GO

CREATE UNIQUE INDEX [IX_Category_StoreId_Name] ON [Category] ([StoreId], [Name]);
GO

CREATE INDEX [IX_CustomerRewardLedgers_CustomerId] ON [CustomerRewardLedgers] ([CustomerId]);
GO

CREATE INDEX [IX_CustomerRewardLedgers_OrderId] ON [CustomerRewardLedgers] ([OrderId]);
GO

CREATE INDEX [IX_CustomerRewardLedgers_SalesReturnId] ON [CustomerRewardLedgers] ([SalesReturnId]);
GO

CREATE INDEX [IX_CustomerRewardLedgers_StoreId_CustomerId_CreatedAtUtc] ON [CustomerRewardLedgers] ([StoreId], [CustomerId], [CreatedAtUtc]);
GO

CREATE INDEX [IX_CustomerRewardLedgers_StoreId_OrderId] ON [CustomerRewardLedgers] ([StoreId], [OrderId]);
GO

CREATE INDEX [IX_CustomerRewardLedgers_StoreId_ReferenceCode] ON [CustomerRewardLedgers] ([StoreId], [ReferenceCode]);
GO

CREATE INDEX [IX_CustomerRewardLedgers_StoreId_SalesReturnId] ON [CustomerRewardLedgers] ([StoreId], [SalesReturnId]);
GO

CREATE INDEX [IX_CustomerRewardLedgers_StoreId_VoucherId] ON [CustomerRewardLedgers] ([StoreId], [VoucherId]);
GO

CREATE INDEX [IX_CustomerRewardLedgers_VoucherId] ON [CustomerRewardLedgers] ([VoucherId]);
GO

CREATE INDEX [IX_CustomerRewardVouchers_CustomerId] ON [CustomerRewardVouchers] ([CustomerId]);
GO

CREATE INDEX [IX_CustomerRewardVouchers_StoreId_CustomerId_Status] ON [CustomerRewardVouchers] ([StoreId], [CustomerId], [Status]);
GO

CREATE INDEX [IX_CustomerRewardVouchers_StoreId_ReferenceCode] ON [CustomerRewardVouchers] ([StoreId], [ReferenceCode]);
GO

CREATE UNIQUE INDEX [IX_CustomerRewardVouchers_StoreId_VoucherCode] ON [CustomerRewardVouchers] ([StoreId], [VoucherCode]);
GO

CREATE INDEX [IX_CustomerRewardVouchers_UsedOrderId] ON [CustomerRewardVouchers] ([UsedOrderId]);
GO

CREATE INDEX [IX_Customers_StoreId_Code] ON [Customers] ([StoreId], [Code]);
GO

CREATE INDEX [IX_Customers_StoreId_OldCustomerId] ON [Customers] ([StoreId], [OldCustomerId]);
GO

CREATE INDEX [IX_Customers_StoreId_Phone] ON [Customers] ([StoreId], [Phone]);
GO

CREATE INDEX [IX_Customers_StoreId_TaxCode] ON [Customers] ([StoreId], [TaxCode]);
GO

CREATE INDEX [IX_DisplayPromotions_StoreId_IsActive_SortOrder] ON [DisplayPromotions] ([StoreId], [IsActive], [SortOrder]);
GO

CREATE UNIQUE INDEX [IX_DocumentNumberSequences_StoreId_SequenceType_SequenceDate] ON [DocumentNumberSequences] ([StoreId], [SequenceType], [SequenceDate]);
GO

CREATE INDEX [IX_InputInvoiceDetail_InputInvoiceHeadId_LineNo] ON [InputInvoiceDetail] ([InputInvoiceHeadId], [LineNo]);
GO

CREATE INDEX [IX_InputInvoiceHead_StoreId_SellerTaxCode_InvoiceTemplateCode_InvoiceSeries_InvoiceNumber] ON [InputInvoiceHead] ([StoreId], [SellerTaxCode], [InvoiceTemplateCode], [InvoiceSeries], [InvoiceNumber]);
GO

CREATE INDEX [IX_InputInvoiceHead_StoreId_XmlHash] ON [InputInvoiceHead] ([StoreId], [XmlHash]);
GO

CREATE INDEX [IX_InventoryAdjustmentDocument_Store_Status_Date] ON [InventoryAdjustmentDocuments] ([StoreId], [Status], [DocumentDate]);
GO

CREATE INDEX [IX_InventoryAdjustmentDocument_Store_Warehouse_Date] ON [InventoryAdjustmentDocuments] ([StoreId], [WarehouseId], [DocumentDate]);
GO

CREATE INDEX [IX_InventoryAdjustmentDocuments_WarehouseId] ON [InventoryAdjustmentDocuments] ([WarehouseId]);
GO

CREATE UNIQUE INDEX [UX_InventoryAdjustmentDocument_Store_DocumentNo] ON [InventoryAdjustmentDocuments] ([StoreId], [DocumentNo]);
GO

CREATE INDEX [IX_InventoryAdjustmentLine_Store_Document] ON [InventoryAdjustmentLines] ([StoreId], [InventoryAdjustmentDocumentId]);
GO

CREATE INDEX [IX_InventoryAdjustmentLine_Store_ProductVariant] ON [InventoryAdjustmentLines] ([StoreId], [ProductVariantId]);
GO

CREATE INDEX [IX_InventoryAdjustmentLines_InventoryAdjustmentDocumentId] ON [InventoryAdjustmentLines] ([InventoryAdjustmentDocumentId]);
GO

CREATE INDEX [IX_InventoryAdjustmentLines_ProductUnitConversionId] ON [InventoryAdjustmentLines] ([ProductUnitConversionId]);
GO

CREATE INDEX [IX_InventoryAdjustmentLines_ProductVariantId] ON [InventoryAdjustmentLines] ([ProductVariantId]);
GO

CREATE INDEX [IX_InventoryAdjustmentLines_UnitId] ON [InventoryAdjustmentLines] ([UnitId]);
GO

CREATE INDEX [IX_InventoryBalances_ProductVariantId] ON [InventoryBalances] ([ProductVariantId]);
GO

CREATE INDEX [IX_InventoryBalances_StoreId_ProductVariantId] ON [InventoryBalances] ([StoreId], [ProductVariantId]);
GO

CREATE INDEX [IX_InventoryBalances_StoreId_WarehouseId] ON [InventoryBalances] ([StoreId], [WarehouseId]);
GO

CREATE UNIQUE INDEX [IX_InventoryBalances_StoreId_WarehouseId_ProductVariantId] ON [InventoryBalances] ([StoreId], [WarehouseId], [ProductVariantId]);
GO

CREATE INDEX [IX_InventoryBalances_WarehouseId] ON [InventoryBalances] ([WarehouseId]);
GO

CREATE INDEX [IX_InventoryCostLayerAllocations_CostLayerId] ON [InventoryCostLayerAllocations] ([InventoryCostLayerId]);
GO

CREATE INDEX [IX_InventoryCostLayerAllocations_OpenProvisional] ON [InventoryCostLayerAllocations] ([StoreId], [IsProvisional], [IsResolved], [Id]);
GO

CREATE INDEX [IX_InventoryCostLayerAllocations_ResolvedByLayerId] ON [InventoryCostLayerAllocations] ([ResolvedByInventoryCostLayerId]);
GO

CREATE INDEX [IX_InventoryCostLayerAllocations_ReverseOfAllocationId] ON [InventoryCostLayerAllocations] ([ReverseOfAllocationId]);
GO

CREATE INDEX [IX_InventoryCostLayerAllocations_ValuationEntryId] ON [InventoryCostLayerAllocations] ([InventoryValuationEntryId]);
GO

CREATE INDEX [IX_InventoryCostLayers_FIFO] ON [InventoryCostLayers] ([StoreId], [WarehouseId], [ProductVariantId], [OccurredAtUtc], [Id]);
GO

CREATE INDEX [IX_InventoryCostLayers_InventoryTransactionId] ON [InventoryCostLayers] ([InventoryTransactionId]);
GO

CREATE UNIQUE INDEX [IX_InventoryCostLayers_InventoryValuationEntryId] ON [InventoryCostLayers] ([InventoryValuationEntryId]);
GO

CREATE INDEX [IX_InventoryCostLayers_Open] ON [InventoryCostLayers] ([StoreId], [WarehouseId], [ProductVariantId], [RemainingQuantity]);
GO

CREATE INDEX [IX_InventoryCostLayers_ProductVariantId] ON [InventoryCostLayers] ([ProductVariantId]);
GO

CREATE INDEX [IX_InventoryCostLayers_WarehouseId] ON [InventoryCostLayers] ([WarehouseId]);
GO

CREATE INDEX [IX_InventoryReservations_ProductVariantId] ON [InventoryReservations] ([ProductVariantId]);
GO

CREATE INDEX [IX_InventoryReservations_StoreId_ReferenceType_ReferenceId] ON [InventoryReservations] ([StoreId], [ReferenceType], [ReferenceId]);
GO

CREATE INDEX [IX_InventoryReservations_StoreId_ReferenceType_ReferenceId_ReferenceLineId_WarehouseId_ProductVariantId_Status] ON [InventoryReservations] ([StoreId], [ReferenceType], [ReferenceId], [ReferenceLineId], [WarehouseId], [ProductVariantId], [Status]);
GO

CREATE INDEX [IX_InventoryReservations_StoreId_WarehouseId_ProductVariantId_Status] ON [InventoryReservations] ([StoreId], [WarehouseId], [ProductVariantId], [Status]);
GO

CREATE INDEX [IX_InventoryReservations_WarehouseId] ON [InventoryReservations] ([WarehouseId]);
GO

CREATE INDEX [IX_InventoryTransactions_ProductVariantId] ON [InventoryTransactions] ([ProductVariantId]);
GO

CREATE INDEX [IX_InventoryTransactions_StoreId_ReferenceType_ReferenceId_ReferenceLineId_TransactionType] ON [InventoryTransactions] ([StoreId], [ReferenceType], [ReferenceId], [ReferenceLineId], [TransactionType]);
GO

CREATE INDEX [IX_InventoryTransactions_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc_Id] ON [InventoryTransactions] ([StoreId], [WarehouseId], [ProductVariantId], [OccurredAtUtc], [Id]);
GO

CREATE INDEX [IX_InventoryTransactions_WarehouseId] ON [InventoryTransactions] ([WarehouseId]);
GO

CREATE INDEX [IX_InventoryValuationEntries_InventoryCostLayerId] ON [InventoryValuationEntries] ([InventoryCostLayerId]);
GO

CREATE INDEX [IX_InventoryValuationEntries_InventoryTransactionId] ON [InventoryValuationEntries] ([InventoryTransactionId]);
GO

CREATE INDEX [IX_InventoryValuationEntries_ProductVariantId] ON [InventoryValuationEntries] ([ProductVariantId]);
GO

CREATE INDEX [IX_InventoryValuationEntries_RevaluationOfEntryId] ON [InventoryValuationEntries] ([RevaluationOfEntryId]);
GO

CREATE INDEX [IX_InventoryValuationEntries_SourceValuationEntryId] ON [InventoryValuationEntries] ([SourceValuationEntryId]);
GO

CREATE INDEX [IX_InventoryValuationEntries_StoreId_InventoryCostLayerId] ON [InventoryValuationEntries] ([StoreId], [InventoryCostLayerId]);
GO

CREATE INDEX [IX_InventoryValuationEntries_StoreId_InventoryTransactionId] ON [InventoryValuationEntries] ([StoreId], [InventoryTransactionId]);
GO

CREATE INDEX [IX_InventoryValuationEntries_StoreId_ReferenceType_ReferenceId_ReferenceLineId_ReferenceSubKey_EntryType] ON [InventoryValuationEntries] ([StoreId], [ReferenceType], [ReferenceId], [ReferenceLineId], [ReferenceSubKey], [EntryType]);
GO

CREATE INDEX [IX_InventoryValuationEntries_StoreId_RevaluationOfEntryId] ON [InventoryValuationEntries] ([StoreId], [RevaluationOfEntryId]);
GO

CREATE INDEX [IX_InventoryValuationEntries_StoreId_SourceValuationEntryId] ON [InventoryValuationEntries] ([StoreId], [SourceValuationEntryId]);
GO

CREATE INDEX [IX_InventoryValuationEntries_StoreId_WarehouseId_ProductVariantId_EntryType_IsProvisional_CostFinalizedAtUtc_OccurredAtUtc_Id] ON [InventoryValuationEntries] ([StoreId], [WarehouseId], [ProductVariantId], [EntryType], [IsProvisional], [CostFinalizedAtUtc], [OccurredAtUtc], [Id]);
GO

CREATE INDEX [IX_InventoryValuationEntries_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc_Id] ON [InventoryValuationEntries] ([StoreId], [WarehouseId], [ProductVariantId], [OccurredAtUtc], [Id]);
GO

CREATE INDEX [IX_InventoryValuationEntries_WarehouseId] ON [InventoryValuationEntries] ([WarehouseId]);
GO

CREATE INDEX [IX_InvoiceBuyerProfiles_CustomerId] ON [InvoiceBuyerProfiles] ([CustomerId]);
GO

CREATE INDEX [IX_InvoiceBuyerProfiles_StoreId_BuyerType_IsDeleted] ON [InvoiceBuyerProfiles] ([StoreId], [BuyerType], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceBuyerProfiles_StoreId_CustomerId_IsDeleted] ON [InvoiceBuyerProfiles] ([StoreId], [CustomerId], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceBuyerProfiles_StoreId_IsActive_IsDeleted] ON [InvoiceBuyerProfiles] ([StoreId], [IsActive], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceBuyerProfiles_StoreId_LastUsedAtUtc_IsDeleted] ON [InvoiceBuyerProfiles] ([StoreId], [LastUsedAtUtc], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceBuyerProfiles_StoreId_TaxCode_IsDeleted] ON [InvoiceBuyerProfiles] ([StoreId], [TaxCode], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceCorrectionCases_NewInvoiceHeadId] ON [InvoiceCorrectionCases] ([NewInvoiceHeadId]);
GO

CREATE INDEX [IX_InvoiceCorrectionCases_OriginalInvoiceHeadId] ON [InvoiceCorrectionCases] ([OriginalInvoiceHeadId]);
GO

CREATE INDEX [IX_InvoiceCorrectionCases_StoreId_CreatedAtUtc_IsDeleted] ON [InvoiceCorrectionCases] ([StoreId], [CreatedAtUtc], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceCorrectionCases_StoreId_NewInvoiceHeadId_IsDeleted] ON [InvoiceCorrectionCases] ([StoreId], [NewInvoiceHeadId], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceCorrectionCases_StoreId_OriginalInvoiceHeadId_IsDeleted] ON [InvoiceCorrectionCases] ([StoreId], [OriginalInvoiceHeadId], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceCorrectionCases_StoreId_Type_Status_IsDeleted] ON [InvoiceCorrectionCases] ([StoreId], [Type], [Status], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceDetails_InvoiceHeadId] ON [InvoiceDetails] ([InvoiceHeadId]);
GO

CREATE INDEX [IX_InvoiceDetails_OrderLineId] ON [InvoiceDetails] ([OrderLineId]);
GO

CREATE INDEX [IX_InvoiceDetails_ProductVariantId] ON [InvoiceDetails] ([ProductVariantId]);
GO

CREATE INDEX [IX_InvoiceDetails_StoreId_InvoiceHeadId_IsDeleted] ON [InvoiceDetails] ([StoreId], [InvoiceHeadId], [IsDeleted]);
GO

CREATE UNIQUE INDEX [IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLegalEntityAllocationId] ON [InvoiceDetails] ([StoreId], [InvoiceHeadId], [OrderLegalEntityAllocationId]) WHERE [OrderLegalEntityAllocationId] IS NOT NULL AND [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLineId] ON [InvoiceDetails] ([StoreId], [InvoiceHeadId], [OrderLineId]) WHERE [OrderLineId] IS NOT NULL AND [IsDeleted] = 0;
GO

CREATE INDEX [IX_InvoiceDetails_StoreId_OrderLegalEntityAllocationId] ON [InvoiceDetails] ([StoreId], [OrderLegalEntityAllocationId]);
GO

CREATE INDEX [IX_InvoiceDetails_StoreId_ProductVariantId_IsDeleted] ON [InvoiceDetails] ([StoreId], [ProductVariantId], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceDetails_StoreId_SourceType_IsDeleted] ON [InvoiceDetails] ([StoreId], [SourceType], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceHeads_OrderId] ON [InvoiceHeads] ([OrderId]);
GO

CREATE INDEX [IX_InvoiceHeads_OriginalInvoiceHeadId] ON [InvoiceHeads] ([OriginalInvoiceHeadId]);
GO

CREATE INDEX [IX_InvoiceHeads_StoreId_BuyerTaxCode_IsDeleted] ON [InvoiceHeads] ([StoreId], [BuyerTaxCode], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceHeads_StoreId_CorrectionType_IsDeleted] ON [InvoiceHeads] ([StoreId], [CorrectionType], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceHeads_StoreId_InvoiceDate_IsDeleted] ON [InvoiceHeads] ([StoreId], [InvoiceDate], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceHeads_StoreId_InvoiceProviderSettingId_IsDeleted] ON [InvoiceHeads] ([StoreId], [InvoiceProviderSettingId], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceHeads_StoreId_LegalEntityId_InvoiceDate_IsDeleted] ON [InvoiceHeads] ([StoreId], [LegalEntityId], [InvoiceDate], [IsDeleted]);
GO

CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_OrderId] ON [InvoiceHeads] ([StoreId], [OrderId]) WHERE [IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NULL;
GO

CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_OrderId_LegalEntityId] ON [InvoiceHeads] ([StoreId], [OrderId], [LegalEntityId]) WHERE [IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NOT NULL;
GO

CREATE INDEX [IX_InvoiceHeads_StoreId_OriginalInvoiceHeadId_IsDeleted] ON [InvoiceHeads] ([StoreId], [OriginalInvoiceHeadId], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceHeads_StoreId_ProviderInvoiceNo_IsDeleted] ON [InvoiceHeads] ([StoreId], [ProviderInvoiceNo], [IsDeleted]);
GO

CREATE INDEX [IX_InvoiceHeads_StoreId_ProviderStatus_IsDeleted] ON [InvoiceHeads] ([StoreId], [ProviderStatus], [IsDeleted]);
GO

CREATE UNIQUE INDEX [IX_InvoiceHeads_StoreId_TransactionUuid] ON [InvoiceHeads] ([StoreId], [TransactionUuid]) WHERE [TransactionUuid] IS NOT NULL AND [IsDeleted] = 0;
GO

CREATE INDEX [IX_InvoiceIntegrationLogs_InvoiceHeadId] ON [InvoiceIntegrationLogs] ([InvoiceHeadId]);
GO

CREATE INDEX [IX_InvoiceIntegrationLogs_StoreId_InvoiceHeadId_ActionType] ON [InvoiceIntegrationLogs] ([StoreId], [InvoiceHeadId], [ActionType]);
GO

CREATE INDEX [IX_InvoiceIntegrationLogs_StoreId_IsSuccess_ActionType] ON [InvoiceIntegrationLogs] ([StoreId], [IsSuccess], [ActionType]);
GO

CREATE INDEX [IX_InvoiceIntegrationLogs_StoreId_StartedAtUtc] ON [InvoiceIntegrationLogs] ([StoreId], [StartedAtUtc]);
GO

CREATE INDEX [IX_InvoiceProviderSettings_StoreId_IsActive_IsDeleted] ON [InvoiceProviderSettings] ([StoreId], [IsActive], [IsDeleted]);
GO

CREATE UNIQUE INDEX [IX_InvoiceProviderSettings_StoreId_ProviderCode_SupplierTaxCode_TemplateCode_InvoiceSeries] ON [InvoiceProviderSettings] ([StoreId], [ProviderCode], [SupplierTaxCode], [TemplateCode], [InvoiceSeries]) WHERE [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [IX_LegalEntities_StoreId] ON [LegalEntities] ([StoreId]) WHERE [IsDefaultForPurchase] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [IX_LegalEntities_StoreId_Code] ON [LegalEntities] ([StoreId], [Code]) WHERE [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [IX_LegalEntities_StoreId_DefaultWarehouseId] ON [LegalEntities] ([StoreId], [DefaultWarehouseId]) WHERE [DefaultWarehouseId] IS NOT NULL AND [IsDeleted] = 0;
GO

CREATE INDEX [IX_LegalEntities_StoreId_InvoiceProviderSettingId] ON [LegalEntities] ([StoreId], [InvoiceProviderSettingId]);
GO

CREATE INDEX [IX_LegalEntities_StoreId_IsActive_IsDeleted] ON [LegalEntities] ([StoreId], [IsActive], [IsDeleted]);
GO

CREATE UNIQUE INDEX [IX_LegalEntities_StoreId_SalePriority] ON [LegalEntities] ([StoreId], [SalePriority]) WHERE [IsActive] = 1 AND [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [IX_LegalEntities_StoreId_TaxCode] ON [LegalEntities] ([StoreId], [TaxCode]) WHERE [TaxCode] IS NOT NULL AND [TaxCode] <> '' AND [IsDeleted] = 0;
GO

CREATE INDEX [IX_LegalEntityActivationEvents_StoreId_OccurredAtUtc_IsDeleted] ON [LegalEntityActivationEvents] ([StoreId], [OccurredAtUtc], [IsDeleted]);
GO

CREATE INDEX [IX_MediaAssets_StoreId_Id] ON [MediaAssets] ([StoreId], [Id]);
GO

CREATE INDEX [IX_MediaAssets_StoreId_IsDeleted] ON [MediaAssets] ([StoreId], [IsDeleted]);
GO

CREATE INDEX [IX_NegativeInventoryLog_ProductVariantId] ON [NegativeInventoryLog] ([ProductVariantId]);
GO

CREATE INDEX [IX_NegativeInventoryLog_Store_Warehouse_Variant_OccurredAt] ON [NegativeInventoryLog] ([StoreId], [WarehouseId], [ProductVariantId], [OccurredAtUtc]);
GO

CREATE INDEX [IX_NegativeInventoryLog_WarehouseId] ON [NegativeInventoryLog] ([WarehouseId]);
GO

CREATE INDEX [IX_OrderInventoryIssueActions_ActorUserId] ON [OrderInventoryIssueActions] ([ActorUserId]);
GO

CREATE INDEX [IX_OrderInventoryIssueActions_OrderInventoryIssueId_ActionAtUtc_IsDeleted] ON [OrderInventoryIssueActions] ([OrderInventoryIssueId], [ActionAtUtc], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssueActions_OrderInventoryIssueLineId] ON [OrderInventoryIssueActions] ([OrderInventoryIssueLineId]);
GO

CREATE INDEX [IX_OrderInventoryIssueActions_StoreId_ActionType_ActionAtUtc_IsDeleted] ON [OrderInventoryIssueActions] ([StoreId], [ActionType], [ActionAtUtc], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssueActions_StoreId_ReferenceType_ReferenceId_IsDeleted] ON [OrderInventoryIssueActions] ([StoreId], [ReferenceType], [ReferenceId], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssueLineAllocations_InventoryCostLayerAllocationId] ON [OrderInventoryIssueLineAllocations] ([InventoryCostLayerAllocationId]);
GO

CREATE INDEX [IX_OrderInventoryIssueLineAllocations_InventoryCostLayerId] ON [OrderInventoryIssueLineAllocations] ([InventoryCostLayerId]);
GO

CREATE INDEX [IX_OrderInventoryIssueLineAllocations_InventoryTransactionId] ON [OrderInventoryIssueLineAllocations] ([InventoryTransactionId]);
GO

CREATE INDEX [IX_OrderInventoryIssueLineAllocations_OrderInventoryIssueId_OrderInventoryIssueLineId_IsDeleted] ON [OrderInventoryIssueLineAllocations] ([OrderInventoryIssueId], [OrderInventoryIssueLineId], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssueLineAllocations_OrderInventoryIssueLineId] ON [OrderInventoryIssueLineAllocations] ([OrderInventoryIssueLineId]);
GO

CREATE INDEX [IX_OrderInventoryIssueLineAllocations_StoreId] ON [OrderInventoryIssueLineAllocations] ([StoreId]);
GO

CREATE INDEX [IX_OrderInventoryIssueLines_BarcodeId] ON [OrderInventoryIssueLines] ([BarcodeId]);
GO

CREATE INDEX [IX_OrderInventoryIssueLines_OrderId_IsDeleted] ON [OrderInventoryIssueLines] ([OrderId], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssueLines_OrderInventoryIssueId_IsDeleted] ON [OrderInventoryIssueLines] ([OrderInventoryIssueId], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssueLines_OrderLineId_IsDeleted] ON [OrderInventoryIssueLines] ([OrderLineId], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssueLines_ProductId] ON [OrderInventoryIssueLines] ([ProductId]);
GO

CREATE INDEX [IX_OrderInventoryIssueLines_ProductUnitConversionId] ON [OrderInventoryIssueLines] ([ProductUnitConversionId]);
GO

CREATE INDEX [IX_OrderInventoryIssueLines_ProductVariantId] ON [OrderInventoryIssueLines] ([ProductVariantId]);
GO

CREATE INDEX [IX_OrderInventoryIssueLines_StoreId_ProductUnitConversionId_IsResolved_IsDeleted] ON [OrderInventoryIssueLines] ([StoreId], [ProductUnitConversionId], [IsResolved], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssueLines_StoreId_ProductVariantId_IsResolved_IsDeleted] ON [OrderInventoryIssueLines] ([StoreId], [ProductVariantId], [IsResolved], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssues_ApprovedByUserId] ON [OrderInventoryIssues] ([ApprovedByUserId]);
GO

CREATE UNIQUE INDEX [IX_OrderInventoryIssues_OrderId] ON [OrderInventoryIssues] ([OrderId]) WHERE [IsDeleted] = 0;
GO

CREATE INDEX [IX_OrderInventoryIssues_RejectedByUserId] ON [OrderInventoryIssues] ([RejectedByUserId]);
GO

CREATE UNIQUE INDEX [IX_OrderInventoryIssues_StoreId_Code] ON [OrderInventoryIssues] ([StoreId], [Code]) WHERE [IsDeleted] = 0;
GO

CREATE INDEX [IX_OrderInventoryIssues_StoreId_DueAtUtc_Status_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [DueAtUtc], [Status], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssues_StoreId_IsOverdue_LastOverdueNotifiedAtUtc_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [IsOverdue], [LastOverdueNotifiedAtUtc], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssues_StoreId_IsOverdue_OverdueSinceUtc_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [IsOverdue], [OverdueSinceUtc], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssues_StoreId_IsOverdue_Status_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [IsOverdue], [Status], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssues_StoreId_OpenedAtUtc_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [OpenedAtUtc], [IsDeleted]);
GO

CREATE INDEX [IX_OrderInventoryIssues_StoreId_Status_IsDeleted] ON [OrderInventoryIssues] ([StoreId], [Status], [IsDeleted]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_InventoryTransactionId] ON [OrderLegalEntityAllocationReversals] ([InventoryTransactionId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_OrderId] ON [OrderLegalEntityAllocationReversals] ([OrderId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_OrderLegalEntityAllocationId] ON [OrderLegalEntityAllocationReversals] ([OrderLegalEntityAllocationId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_OrderLineId] ON [OrderLegalEntityAllocationReversals] ([OrderLineId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_ProductVariantId] ON [OrderLegalEntityAllocationReversals] ([ProductVariantId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_SalesReturnId] ON [OrderLegalEntityAllocationReversals] ([SalesReturnId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_SalesReturnLineId] ON [OrderLegalEntityAllocationReversals] ([SalesReturnLineId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_SourceValuationEntryId] ON [OrderLegalEntityAllocationReversals] ([SourceValuationEntryId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_LegalEntityId] ON [OrderLegalEntityAllocationReversals] ([StoreId], [LegalEntityId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_OrderId_IsDeleted] ON [OrderLegalEntityAllocationReversals] ([StoreId], [OrderId], [IsDeleted]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_OrderLegalEntityAllocationId_IsDeleted] ON [OrderLegalEntityAllocationReversals] ([StoreId], [OrderLegalEntityAllocationId], [IsDeleted]);
GO

CREATE UNIQUE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_ReversalType_SalesReturnLineId_SourceValuationEntryId] ON [OrderLegalEntityAllocationReversals] ([StoreId], [ReversalType], [SalesReturnLineId], [SourceValuationEntryId]) WHERE [IsDeleted] = 0;
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_SalesReturnId_IsDeleted] ON [OrderLegalEntityAllocationReversals] ([StoreId], [SalesReturnId], [IsDeleted]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_SourceValuationEntryId_IsDeleted] ON [OrderLegalEntityAllocationReversals] ([StoreId], [SourceValuationEntryId], [IsDeleted]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocationReversals_StoreId_WarehouseId] ON [OrderLegalEntityAllocationReversals] ([StoreId], [WarehouseId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocations_InventoryTransactionId] ON [OrderLegalEntityAllocations] ([InventoryTransactionId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocations_OrderId] ON [OrderLegalEntityAllocations] ([OrderId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocations_OrderLineId] ON [OrderLegalEntityAllocations] ([OrderLineId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocations_ProductUnitConversionId] ON [OrderLegalEntityAllocations] ([ProductUnitConversionId]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocations_ProductVariantId] ON [OrderLegalEntityAllocations] ([ProductVariantId]);
GO

CREATE UNIQUE INDEX [IX_OrderLegalEntityAllocations_StoreId_InventoryTransactionId] ON [OrderLegalEntityAllocations] ([StoreId], [InventoryTransactionId]) WHERE [InventoryTransactionId] IS NOT NULL AND [IsDeleted] = 0;
GO

CREATE INDEX [IX_OrderLegalEntityAllocations_StoreId_LegalEntityId_OrderId_IsDeleted] ON [OrderLegalEntityAllocations] ([StoreId], [LegalEntityId], [OrderId], [IsDeleted]);
GO

CREATE INDEX [IX_OrderLegalEntityAllocations_StoreId_OrderId_IsDeleted] ON [OrderLegalEntityAllocations] ([StoreId], [OrderId], [IsDeleted]);
GO

CREATE UNIQUE INDEX [IX_OrderLegalEntityAllocations_StoreId_OrderId_OrderLineId_LegalEntityId_WarehouseId] ON [OrderLegalEntityAllocations] ([StoreId], [OrderId], [OrderLineId], [LegalEntityId], [WarehouseId]) WHERE [IsDeleted] = 0;
GO

CREATE INDEX [IX_OrderLegalEntityAllocations_StoreId_WarehouseId_ProductVariantId_IsDeleted] ON [OrderLegalEntityAllocations] ([StoreId], [WarehouseId], [ProductVariantId], [IsDeleted]);
GO

CREATE INDEX [IX_OrderLines_OrderId] ON [OrderLines] ([OrderId]);
GO

CREATE INDEX [IX_OrderLines_StoreId_GiftPromotionId] ON [OrderLines] ([StoreId], [GiftPromotionId]);
GO

CREATE INDEX [IX_OrderLines_StoreId_GiftSourceLineId] ON [OrderLines] ([StoreId], [GiftSourceLineId]);
GO

CREATE INDEX [IX_OrderLines_StoreId_IsPromotionGift] ON [OrderLines] ([StoreId], [IsPromotionGift]);
GO

CREATE INDEX [IX_OrderLines_StoreId_OrderId] ON [OrderLines] ([StoreId], [OrderId]);
GO

CREATE INDEX [IX_OrderLines_StoreId_ProductId] ON [OrderLines] ([StoreId], [ProductId]);
GO

CREATE INDEX [IX_OrderLines_StoreId_ProductUnitConversionId] ON [OrderLines] ([StoreId], [ProductUnitConversionId]);
GO

CREATE INDEX [IX_OrderLines_StoreId_VariantId] ON [OrderLines] ([StoreId], [VariantId]);
GO

CREATE INDEX [IX_OrderLines_VariantId] ON [OrderLines] ([VariantId]);
GO

CREATE UNIQUE INDEX [IX_OrderNumberSequences_StoreId_DateKey] ON [OrderNumberSequences] ([StoreId], [DateKey]);
GO

CREATE INDEX [IX_OrderPayments_OrderId] ON [OrderPayments] ([OrderId]);
GO

CREATE INDEX [IX_OrderPayments_StoreId_OrderId] ON [OrderPayments] ([StoreId], [OrderId]);
GO

CREATE INDEX [IX_OrderRewardVouchers_OrderId] ON [OrderRewardVouchers] ([OrderId]);
GO

CREATE INDEX [IX_OrderRewardVouchers_StoreId_OrderId] ON [OrderRewardVouchers] ([StoreId], [OrderId]);
GO

CREATE INDEX [IX_OrderRewardVouchers_StoreId_VoucherId] ON [OrderRewardVouchers] ([StoreId], [VoucherId]);
GO

CREATE INDEX [IX_OrderRewardVouchers_VoucherId] ON [OrderRewardVouchers] ([VoucherId]);
GO

CREATE INDEX [IX_Orders_CustomerId] ON [Orders] ([CustomerId]);
GO

CREATE INDEX [IX_Orders_POSShiftId] ON [Orders] ([POSShiftId]);
GO

CREATE INDEX [IX_Orders_StoreId_CompletedAtUtc] ON [Orders] ([StoreId], [CompletedAtUtc]);
GO

CREATE INDEX [IX_Orders_StoreId_HasInventoryIssue_InventoryResolutionStatus_IsDeleted] ON [Orders] ([StoreId], [HasInventoryIssue], [InventoryResolutionStatus], [IsDeleted]);
GO

CREATE INDEX [IX_Orders_StoreId_HasMultipleLegalEntities_LegalEntityAllocatedAtUtc_IsDeleted] ON [Orders] ([StoreId], [HasMultipleLegalEntities], [LegalEntityAllocatedAtUtc], [IsDeleted]);
GO

CREATE INDEX [IX_Orders_StoreId_InventoryIssueOpenedAtUtc_IsDeleted] ON [Orders] ([StoreId], [InventoryIssueOpenedAtUtc], [IsDeleted]);
GO

CREATE UNIQUE INDEX [IX_Orders_StoreId_OrderNumber] ON [Orders] ([StoreId], [OrderNumber]) WHERE [OrderNumber] IS NOT NULL;
GO

CREATE INDEX [IX_Orders_StoreId_POSShiftId_Status] ON [Orders] ([StoreId], [POSShiftId], [Status]);
GO

CREATE INDEX [IX_Orders_StoreId_UseMultiLegalEntity_Status_LegalEntityModeCapturedAtUtc_IsDeleted] ON [Orders] ([StoreId], [UseMultiLegalEntity], [Status], [LegalEntityModeCapturedAtUtc], [IsDeleted]);
GO

CREATE UNIQUE INDEX [IX_Permissions_Code] ON [Permissions] ([Code]);
GO

CREATE INDEX [IX_POSAuditLogs_StoreId] ON [POSAuditLogs] ([StoreId]);
GO

CREATE INDEX [IX_PosPaymentQrRequests_BankAccountId] ON [PosPaymentQrRequests] ([BankAccountId]);
GO

CREATE INDEX [IX_PosPaymentQrRequests_OrderId] ON [PosPaymentQrRequests] ([OrderId]);
GO

CREATE INDEX [IX_PosPaymentQrRequests_StoreId_OrderId_Status] ON [PosPaymentQrRequests] ([StoreId], [OrderId], [Status]);
GO

CREATE UNIQUE INDEX [IX_PosPaymentQrRequests_StoreId_RequestCode] ON [PosPaymentQrRequests] ([StoreId], [RequestCode]);
GO

CREATE INDEX [IX_POSShiftCashDenominations_POSShiftId] ON [POSShiftCashDenominations] ([POSShiftId]);
GO

CREATE INDEX [IX_POSShiftCashDenominations_StoreId_POSShiftId_EntryType] ON [POSShiftCashDenominations] ([StoreId], [POSShiftId], [EntryType]);
GO

CREATE UNIQUE INDEX [IX_POSShiftCashDenominations_StoreId_POSShiftId_EntryType_DenominationValue] ON [POSShiftCashDenominations] ([StoreId], [POSShiftId], [EntryType], [DenominationValue]) WHERE [IsDeleted] = 0;
GO

CREATE INDEX [IX_POSShiftCashTransactions_CreatedAtUtc] ON [POSShiftCashTransactions] ([CreatedAtUtc]);
GO

CREATE INDEX [IX_POSShiftCashTransactions_POSShiftId] ON [POSShiftCashTransactions] ([POSShiftId]);
GO

CREATE INDEX [IX_POSShiftCashTransactions_StoreId_POSShiftId] ON [POSShiftCashTransactions] ([StoreId], [POSShiftId]);
GO

CREATE INDEX [IX_POSShiftCashTransactions_StoreId_Type] ON [POSShiftCashTransactions] ([StoreId], [Type]);
GO

CREATE INDEX [IX_POSShiftClosingSlipDenominations_POSShiftClosingSlipId] ON [POSShiftClosingSlipDenominations] ([POSShiftClosingSlipId]);
GO

CREATE UNIQUE INDEX [IX_POSShiftClosingSlipDenominations_StoreId_POSShiftClosingSlipId_DenominationValue] ON [POSShiftClosingSlipDenominations] ([StoreId], [POSShiftClosingSlipId], [DenominationValue]) WHERE [IsDeleted] = 0;
GO

CREATE INDEX [IX_POSShiftClosingSlips_POSShiftId] ON [POSShiftClosingSlips] ([POSShiftId]);
GO

CREATE UNIQUE INDEX [IX_POSShiftClosingSlips_StoreId_BarcodeValue] ON [POSShiftClosingSlips] ([StoreId], [BarcodeValue]) WHERE [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [IX_POSShiftClosingSlips_StoreId_POSShiftId] ON [POSShiftClosingSlips] ([StoreId], [POSShiftId]) WHERE [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [IX_POSShiftClosingSlips_StoreId_SlipCode] ON [POSShiftClosingSlips] ([StoreId], [SlipCode]) WHERE [IsDeleted] = 0;
GO

CREATE INDEX [IX_POSShiftHandoverSlipDenominations_POSShiftHandoverSlipId] ON [POSShiftHandoverSlipDenominations] ([POSShiftHandoverSlipId]);
GO

CREATE INDEX [IX_POSShiftHandoverSlipDenominations_StoreId_POSShiftHandoverSlipId] ON [POSShiftHandoverSlipDenominations] ([StoreId], [POSShiftHandoverSlipId]);
GO

CREATE UNIQUE INDEX [IX_POSShiftHandoverSlipDenominations_StoreId_POSShiftHandoverSlipId_DenominationValue] ON [POSShiftHandoverSlipDenominations] ([StoreId], [POSShiftHandoverSlipId], [DenominationValue]) WHERE [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [IX_POSShiftHandoverSlips_StoreId_BarcodeValue] ON [POSShiftHandoverSlips] ([StoreId], [BarcodeValue]) WHERE [IsDeleted] = 0;
GO

CREATE INDEX [IX_POSShiftHandoverSlips_StoreId_CreatedAtUtc] ON [POSShiftHandoverSlips] ([StoreId], [CreatedAtUtc]);
GO

CREATE UNIQUE INDEX [IX_POSShiftHandoverSlips_StoreId_SlipCode] ON [POSShiftHandoverSlips] ([StoreId], [SlipCode]) WHERE [IsDeleted] = 0;
GO

CREATE INDEX [IX_POSShiftHandoverSlips_StoreId_Status] ON [POSShiftHandoverSlips] ([StoreId], [Status]);
GO

CREATE INDEX [IX_POSShiftHandoverSlips_StoreId_TerminalId] ON [POSShiftHandoverSlips] ([StoreId], [TerminalId]);
GO

CREATE INDEX [IX_POSShiftHandoverSlips_StoreId_WarehouseId] ON [POSShiftHandoverSlips] ([StoreId], [WarehouseId]);
GO

CREATE INDEX [IX_POSShiftHandoverSlips_TerminalId] ON [POSShiftHandoverSlips] ([TerminalId]);
GO

CREATE INDEX [IX_POSShiftHandoverSlips_UsedPOSShiftId] ON [POSShiftHandoverSlips] ([UsedPOSShiftId]);
GO

CREATE INDEX [IX_POSShiftHandoverSlips_WarehouseId] ON [POSShiftHandoverSlips] ([WarehouseId]);
GO

CREATE INDEX [IX_POSShifts_CurrentOrderId] ON [POSShifts] ([CurrentOrderId]);
GO

CREATE INDEX [IX_POSShifts_StoreId_ClosedAtUtc] ON [POSShifts] ([StoreId], [ClosedAtUtc]);
GO

CREATE INDEX [IX_POSShifts_StoreId_OpenedAtUtc] ON [POSShifts] ([StoreId], [OpenedAtUtc]);
GO

CREATE UNIQUE INDEX [IX_POSShifts_StoreId_ShiftCode] ON [POSShifts] ([StoreId], [ShiftCode]) WHERE [ShiftCode] IS NOT NULL;
GO

CREATE INDEX [IX_POSShifts_StoreId_Status] ON [POSShifts] ([StoreId], [Status]);
GO

CREATE UNIQUE INDEX [IX_POSShifts_StoreId_TerminalId_Status] ON [POSShifts] ([StoreId], [TerminalId], [Status]) WHERE [Status] = 1 AND [IsDeleted] = 0;
GO

CREATE INDEX [IX_POSShifts_TerminalId] ON [POSShifts] ([TerminalId]);
GO

CREATE INDEX [IX_POSShifts_WarehouseId] ON [POSShifts] ([WarehouseId]);
GO

CREATE UNIQUE INDEX [IX_POSTerminalDevices_StoreId_DeviceKey] ON [POSTerminalDevices] ([StoreId], [DeviceKey]);
GO

CREATE INDEX [IX_POSTerminalDevices_StoreId_TerminalId_IsActive] ON [POSTerminalDevices] ([StoreId], [TerminalId], [IsActive]);
GO

CREATE INDEX [IX_POSTerminalDevices_TerminalId] ON [POSTerminalDevices] ([TerminalId]);
GO

CREATE UNIQUE INDEX [IX_POSTerminals_StoreId_Code] ON [POSTerminals] ([StoreId], [Code]);
GO

CREATE UNIQUE INDEX [IX_POSTerminals_StoreId_LocalIp] ON [POSTerminals] ([StoreId], [LocalIp]) WHERE [LocalIp] IS NOT NULL;
GO

CREATE INDEX [IX_ProductBarcodeVerificationRequests_CreatedBarcodeId] ON [ProductBarcodeVerificationRequests] ([CreatedBarcodeId]);
GO

CREATE INDEX [IX_ProductBarcodeVerificationRequests_ProductUnitConversionId] ON [ProductBarcodeVerificationRequests] ([ProductUnitConversionId]);
GO

CREATE INDEX [IX_ProductBarcodeVerificationRequests_ProductVariantId] ON [ProductBarcodeVerificationRequests] ([ProductVariantId]);
GO

CREATE INDEX [IX_ProductBarcodeVerificationRequests_StockDocumentId_IsDeleted] ON [ProductBarcodeVerificationRequests] ([StockDocumentId], [IsDeleted]);
GO

CREATE INDEX [IX_ProductBarcodeVerificationRequests_StoreId_ProductUnitConversionId_Status_IsDeleted] ON [ProductBarcodeVerificationRequests] ([StoreId], [ProductUnitConversionId], [Status], [IsDeleted]);
GO

CREATE INDEX [IX_ProductBarcodeVerificationRequests_StoreId_SuggestedBarcode_Status_IsDeleted] ON [ProductBarcodeVerificationRequests] ([StoreId], [SuggestedBarcode], [Status], [IsDeleted]);
GO

CREATE INDEX [IX_ProductImages_MediaAssetId] ON [ProductImages] ([MediaAssetId]);
GO

CREATE INDEX [IX_ProductImages_ProductId] ON [ProductImages] ([ProductId]);
GO

CREATE UNIQUE INDEX [IX_ProductImages_StoreId_ProductId] ON [ProductImages] ([StoreId], [ProductId]) WHERE [IsPrimary] = 1 AND [IsDeleted] = 0;
GO

CREATE INDEX [IX_ProductImages_StoreId_ProductId_IsPrimary] ON [ProductImages] ([StoreId], [ProductId], [IsPrimary]);
GO

CREATE INDEX [IX_ProductImages_StoreId_ProductId_SortOrder] ON [ProductImages] ([StoreId], [ProductId], [SortOrder]);
GO

CREATE INDEX [IX_Products_BaseUnitId] ON [Products] ([BaseUnitId]);
GO

CREATE INDEX [IX_Products_BrandId] ON [Products] ([BrandId]);
GO

CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);
GO

CREATE UNIQUE INDEX [IX_Products_StoreId_Alias] ON [Products] ([StoreId], [Alias]);
GO

CREATE INDEX [IX_Products_SupplierId] ON [Products] ([SupplierId]);
GO

CREATE INDEX [IX_Products_TaxId] ON [Products] ([TaxId]);
GO

CREATE INDEX [IX_ProductUnitConversion_ProductVariantId] ON [ProductUnitConversion] ([ProductVariantId]);
GO

CREATE INDEX [IX_ProductUnitConversion_StoreId_ProductVariantId_IsActive] ON [ProductUnitConversion] ([StoreId], [ProductVariantId], [IsActive]);
GO

CREATE INDEX [IX_ProductUnitConversion_StoreId_ProductVariantId_IsBaseUnit] ON [ProductUnitConversion] ([StoreId], [ProductVariantId], [IsBaseUnit]);
GO

CREATE INDEX [IX_ProductUnitConversion_StoreId_ProductVariantId_IsDefaultForSale] ON [ProductUnitConversion] ([StoreId], [ProductVariantId], [IsDefaultForSale]);
GO

CREATE UNIQUE INDEX [IX_ProductUnitConversion_StoreId_ProductVariantId_UnitId] ON [ProductUnitConversion] ([StoreId], [ProductVariantId], [UnitId]);
GO

CREATE INDEX [IX_ProductUnitConversion_UnitId] ON [ProductUnitConversion] ([UnitId]);
GO

CREATE INDEX [IX_ProductVariant_PrimaryProductImageId] ON [ProductVariant] ([PrimaryProductImageId]);
GO

CREATE INDEX [IX_ProductVariant_ProductId] ON [ProductVariant] ([ProductId]);
GO

CREATE INDEX [IX_ProductVariant_Store_ProductVariantName] ON [ProductVariant] ([StoreId], [IsDeleted], [IsActive], [ProductVariantName]);
GO

CREATE INDEX [IX_ProductVariant_Store_ProductVariantNameNormalized] ON [ProductVariant] ([StoreId], [IsDeleted], [IsActive], [ProductVariantNameNormalized]);
GO

CREATE INDEX [IX_ProductVariant_Store_Sku] ON [ProductVariant] ([StoreId], [IsDeleted], [IsActive], [Sku]);
GO

CREATE UNIQUE INDEX [UX_ProductVariant_Store_Product_Sku] ON [ProductVariant] ([StoreId], [ProductId], [Sku]);
GO

CREATE INDEX [IX_ProductVariantAttributeValue_AttributeId] ON [ProductVariantAttributeValue] ([AttributeId]);
GO

CREATE INDEX [IX_ProductVariantAttributeValue_AttributeValueId] ON [ProductVariantAttributeValue] ([AttributeValueId]);
GO

CREATE UNIQUE INDEX [IX_ProductVariantAttributeValue_StoreId_VariantId_AttributeId] ON [ProductVariantAttributeValue] ([StoreId], [VariantId], [AttributeId]);
GO

CREATE INDEX [IX_ProductVariantAttributeValue_VariantId] ON [ProductVariantAttributeValue] ([VariantId]);
GO

CREATE INDEX [IX_ProductVariantBarcodeHistory_Conversion_ChangedAtUtc] ON [ProductVariantBarcodeHistory] ([ProductUnitConversionId], [ChangedAtUtc]);
GO

CREATE INDEX [IX_ProductVariantBarcodeHistory_NewBarcodeId] ON [ProductVariantBarcodeHistory] ([NewBarcodeId]);
GO

CREATE INDEX [IX_ProductVariantBarcodeHistory_OldBarcodeId] ON [ProductVariantBarcodeHistory] ([OldBarcodeId]);
GO

CREATE INDEX [IX_ProductVariantBarcodeHistory_Store_NewBarcode] ON [ProductVariantBarcodeHistory] ([StoreId], [NewBarcode]);
GO

CREATE INDEX [IX_ProductVariantBarcodeHistory_Store_OldBarcode] ON [ProductVariantBarcodeHistory] ([StoreId], [OldBarcode]);
GO

CREATE INDEX [IX_ProductVariantBarcodeHistory_Variant_ChangedAtUtc] ON [ProductVariantBarcodeHistory] ([ProductVariantId], [ChangedAtUtc]);
GO

CREATE INDEX [IX_ProductVariantUnitBarcode_Conversion_Active_Primary] ON [ProductVariantUnitBarcode] ([ProductUnitConversionId], [IsActive], [IsPrimary]);
GO

CREATE INDEX [IX_ProductVariantUnitBarcode_Store_Conversion] ON [ProductVariantUnitBarcode] ([StoreId], [ProductUnitConversionId]);
GO

CREATE UNIQUE INDEX [UX_ProductVariantUnitBarcode_Conversion_Primary_Active] ON [ProductVariantUnitBarcode] ([ProductUnitConversionId]) WHERE [IsDeleted] = 0 AND [IsActive] = 1 AND [IsPrimary] = 1;
GO

CREATE UNIQUE INDEX [UX_ProductVariantUnitBarcode_Store_Barcode_Active] ON [ProductVariantUnitBarcode] ([StoreId], [Barcode]) WHERE [IsDeleted] = 0 AND [IsActive] = 1;
GO

CREATE INDEX [IX_PromotionComboRule_PromotionId] ON [PromotionComboRule] ([PromotionId]);
GO

CREATE INDEX [IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId_IsDeleted] ON [PromotionComboRule] ([StoreId], [PromotionId], [ProductId], [VariantId], [ProductUnitConversionId], [IsDeleted]);
GO

CREATE INDEX [IX_PromotionItems_PromotionId] ON [PromotionItems] ([PromotionId]);
GO

CREATE INDEX [IX_PromotionItems_StoreId_ProductId] ON [PromotionItems] ([StoreId], [ProductId]);
GO

CREATE INDEX [IX_PromotionItems_StoreId_ProductUnitConversionId] ON [PromotionItems] ([StoreId], [ProductUnitConversionId]);
GO

CREATE INDEX [IX_PromotionItems_StoreId_PromotionId] ON [PromotionItems] ([StoreId], [PromotionId]);
GO

CREATE INDEX [IX_PromotionItems_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId_IsDeleted] ON [PromotionItems] ([StoreId], [PromotionId], [ProductId], [VariantId], [ProductUnitConversionId], [IsDeleted]);
GO

CREATE INDEX [IX_PromotionItems_StoreId_VariantId] ON [PromotionItems] ([StoreId], [VariantId]);
GO

CREATE INDEX [IX_Promotions_StoreId_IsActive_StartAtUtc_EndAtUtc] ON [Promotions] ([StoreId], [IsActive], [StartAtUtc], [EndAtUtc]);
GO

CREATE INDEX [IX_Promotions_StoreId_Type_IsActive_StartAtUtc_EndAtUtc_IsDeleted] ON [Promotions] ([StoreId], [Type], [IsActive], [StartAtUtc], [EndAtUtc], [IsDeleted]);
GO

CREATE INDEX [IX_Promotions_StoreId_Type_IsDeleted] ON [Promotions] ([StoreId], [Type], [IsDeleted]);
GO

CREATE INDEX [IX_PurchaseOrderActions_PurchaseOrderId] ON [PurchaseOrderActions] ([PurchaseOrderId]);
GO

CREATE INDEX [IX_PurchaseOrderActions_StockDocumentId] ON [PurchaseOrderActions] ([StockDocumentId]);
GO

CREATE INDEX [IX_PurchaseOrderActions_StoreId_PurchaseOrderId_OccurredAtUtc] ON [PurchaseOrderActions] ([StoreId], [PurchaseOrderId], [OccurredAtUtc]);
GO

CREATE INDEX [IX_PurchaseOrderLines_ProductUnitConversionId] ON [PurchaseOrderLines] ([ProductUnitConversionId]);
GO

CREATE INDEX [IX_PurchaseOrderLines_ProductVariantId] ON [PurchaseOrderLines] ([ProductVariantId]);
GO

CREATE UNIQUE INDEX [IX_PurchaseOrderLines_PurchaseOrderId_LineNo] ON [PurchaseOrderLines] ([PurchaseOrderId], [LineNo]);
GO

CREATE INDEX [IX_PurchaseOrderLines_SourcePurchaseRequestLineId] ON [PurchaseOrderLines] ([SourcePurchaseRequestLineId]);
GO

CREATE INDEX [IX_PurchaseOrderLines_StoreId] ON [PurchaseOrderLines] ([StoreId]);
GO

CREATE INDEX [IX_PurchaseOrderLines_TaxId] ON [PurchaseOrderLines] ([TaxId]);
GO

CREATE INDEX [IX_PurchaseOrderLines_UnitId] ON [PurchaseOrderLines] ([UnitId]);
GO

CREATE INDEX [IX_PurchaseOrders_ExpectedWarehouseId] ON [PurchaseOrders] ([ExpectedWarehouseId]);
GO

CREATE INDEX [IX_PurchaseOrders_LegalEntityId] ON [PurchaseOrders] ([LegalEntityId]);
GO

CREATE INDEX [IX_PurchaseOrders_SourcePurchaseRequestId] ON [PurchaseOrders] ([SourcePurchaseRequestId]);
GO

CREATE INDEX [IX_PurchaseOrders_StoreId_LegalEntityId_Status] ON [PurchaseOrders] ([StoreId], [LegalEntityId], [Status]);
GO

CREATE UNIQUE INDEX [IX_PurchaseOrders_StoreId_OrderNumber] ON [PurchaseOrders] ([StoreId], [OrderNumber]);
GO

CREATE UNIQUE INDEX [IX_PurchaseOrders_StoreId_SourcePurchaseRequestId_SourceConversionKey] ON [PurchaseOrders] ([StoreId], [SourcePurchaseRequestId], [SourceConversionKey]) WHERE [SourcePurchaseRequestId] IS NOT NULL AND [SourceConversionKey] IS NOT NULL;
GO

CREATE INDEX [IX_PurchaseOrders_StoreId_Status_OrderDate] ON [PurchaseOrders] ([StoreId], [Status], [OrderDate]);
GO

CREATE INDEX [IX_PurchaseOrders_StoreId_SupplierId_OrderDate] ON [PurchaseOrders] ([StoreId], [SupplierId], [OrderDate]);
GO

CREATE INDEX [IX_PurchaseOrders_SupplierId] ON [PurchaseOrders] ([SupplierId]);
GO

CREATE INDEX [IX_PurchasePayables_PurchaseOrderId] ON [PurchasePayables] ([PurchaseOrderId]);
GO

CREATE INDEX [IX_PurchasePayables_StockDocumentId] ON [PurchasePayables] ([StockDocumentId]);
GO

CREATE UNIQUE INDEX [IX_PurchasePayables_StoreId_SourceKey] ON [PurchasePayables] ([StoreId], [SourceKey]);
GO

CREATE INDEX [IX_PurchasePayables_StoreId_Status_RecognizedAtUtc] ON [PurchasePayables] ([StoreId], [Status], [RecognizedAtUtc]);
GO

CREATE INDEX [IX_PurchasePayables_SupplierId] ON [PurchasePayables] ([SupplierId]);
GO

CREATE INDEX [IX_PurchaseRequestActions_PurchaseOrderId] ON [PurchaseRequestActions] ([PurchaseOrderId]);
GO

CREATE INDEX [IX_PurchaseRequestActions_PurchaseRequestId] ON [PurchaseRequestActions] ([PurchaseRequestId]);
GO

CREATE INDEX [IX_PurchaseRequestActions_StoreId_PurchaseRequestId_OccurredAtUtc] ON [PurchaseRequestActions] ([StoreId], [PurchaseRequestId], [OccurredAtUtc]);
GO

CREATE INDEX [IX_PurchaseRequestLines_ProductUnitConversionId] ON [PurchaseRequestLines] ([ProductUnitConversionId]);
GO

CREATE INDEX [IX_PurchaseRequestLines_ProductVariantId] ON [PurchaseRequestLines] ([ProductVariantId]);
GO

CREATE UNIQUE INDEX [IX_PurchaseRequestLines_PurchaseRequestId_LineNo] ON [PurchaseRequestLines] ([PurchaseRequestId], [LineNo]);
GO

CREATE INDEX [IX_PurchaseRequestLines_StoreId] ON [PurchaseRequestLines] ([StoreId]);
GO

CREATE INDEX [IX_PurchaseRequestLines_UnitId] ON [PurchaseRequestLines] ([UnitId]);
GO

CREATE INDEX [IX_PurchaseRequests_StoreId_RequestedByUserId_Status] ON [PurchaseRequests] ([StoreId], [RequestedByUserId], [Status]);
GO

CREATE UNIQUE INDEX [IX_PurchaseRequests_StoreId_RequestNumber] ON [PurchaseRequests] ([StoreId], [RequestNumber]);
GO

CREATE INDEX [IX_PurchaseRequests_StoreId_Status_RequestDate] ON [PurchaseRequests] ([StoreId], [Status], [RequestDate]);
GO

CREATE UNIQUE INDEX [IX_RewardSettings_StoreId] ON [RewardSettings] ([StoreId]);
GO

CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
GO

CREATE UNIQUE INDEX [IX_RolePermissions_RoleId_PermissionId] ON [RolePermissions] ([RoleId], [PermissionId]);
GO

CREATE UNIQUE INDEX [IX_Roles_StoreId_Code] ON [Roles] ([StoreId], [Code]);
GO

CREATE INDEX [IX_Roles_StoreId_Name] ON [Roles] ([StoreId], [Name]);
GO

CREATE INDEX [IX_SalesReturnLines_OrderLineId] ON [SalesReturnLines] ([OrderLineId]);
GO

CREATE INDEX [IX_SalesReturnLines_SalesReturnId] ON [SalesReturnLines] ([SalesReturnId]);
GO

CREATE INDEX [IX_SalesReturnLines_StoreId_OrderLineId] ON [SalesReturnLines] ([StoreId], [OrderLineId]);
GO

CREATE INDEX [IX_SalesReturnLines_StoreId_SalesReturnId] ON [SalesReturnLines] ([StoreId], [SalesReturnId]);
GO

CREATE INDEX [IX_SalesReturnLines_StoreId_VariantId] ON [SalesReturnLines] ([StoreId], [VariantId]);
GO

CREATE INDEX [IX_SalesReturnPayments_SalesReturnId] ON [SalesReturnPayments] ([SalesReturnId]);
GO

CREATE INDEX [IX_SalesReturnPayments_StoreId_PaidAtUtc] ON [SalesReturnPayments] ([StoreId], [PaidAtUtc]);
GO

CREATE INDEX [IX_SalesReturnPayments_StoreId_SalesReturnId] ON [SalesReturnPayments] ([StoreId], [SalesReturnId]);
GO

CREATE INDEX [IX_SalesReturns_OrderId] ON [SalesReturns] ([OrderId]);
GO

CREATE INDEX [IX_SalesReturns_POSShiftId] ON [SalesReturns] ([POSShiftId]);
GO

CREATE INDEX [IX_SalesReturns_StoreId_OrderId] ON [SalesReturns] ([StoreId], [OrderId]);
GO

CREATE INDEX [IX_SalesReturns_StoreId_POSShiftId] ON [SalesReturns] ([StoreId], [POSShiftId]);
GO

CREATE UNIQUE INDEX [IX_SalesReturns_StoreId_ReturnNumber] ON [SalesReturns] ([StoreId], [ReturnNumber]);
GO

CREATE UNIQUE INDEX [IX_StockCountDocument_StoreId_DocumentNo] ON [StockCountDocument] ([StoreId], [DocumentNo]);
GO

CREATE INDEX [IX_StockCountDocument_StoreId_WarehouseId_Status_DocumentDate] ON [StockCountDocument] ([StoreId], [WarehouseId], [Status], [DocumentDate]);
GO

CREATE INDEX [IX_StockCountDocument_WarehouseId] ON [StockCountDocument] ([WarehouseId]);
GO

CREATE INDEX [IX_StockCountLine_ProductVariantId] ON [StockCountLine] ([ProductVariantId]);
GO

CREATE UNIQUE INDEX [IX_StockCountLine_StockCountDocumentId_LineNo] ON [StockCountLine] ([StockCountDocumentId], [LineNo]);
GO

CREATE INDEX [IX_StockCountLine_StockCountDocumentId_ProductVariantId] ON [StockCountLine] ([StockCountDocumentId], [ProductVariantId]);
GO

CREATE INDEX [IX_StockCountLine_StoreId] ON [StockCountLine] ([StoreId]);
GO

CREATE INDEX [IX_StockCountLine_UnitId] ON [StockCountLine] ([UnitId]);
GO

CREATE INDEX [IX_StockDocument_HasRevisionRequest] ON [StockDocument] ([HasRevisionRequest]);
GO

CREATE INDEX [IX_StockDocument_PurchaseOrderId] ON [StockDocument] ([PurchaseOrderId]);
GO

CREATE INDEX [IX_StockDocument_Status] ON [StockDocument] ([Status]);
GO

CREATE UNIQUE INDEX [IX_StockDocument_StoreId_DocumentNo] ON [StockDocument] ([StoreId], [DocumentNo]);
GO

CREATE INDEX [IX_StockDocument_StoreId_Type_Status_DocumentDate] ON [StockDocument] ([StoreId], [Type], [Status], [DocumentDate]);
GO

CREATE INDEX [IX_StockDocument_StoreId_WarehouseId_DocumentDate] ON [StockDocument] ([StoreId], [WarehouseId], [DocumentDate]);
GO

CREATE INDEX [IX_StockDocument_SupplierId] ON [StockDocument] ([SupplierId]);
GO

CREATE INDEX [IX_StockDocument_WarehouseId] ON [StockDocument] ([WarehouseId]);
GO

CREATE INDEX [IX_StockDocumentInputInvoiceMap_InputInvoiceHeadId] ON [StockDocumentInputInvoiceMap] ([InputInvoiceHeadId]);
GO

CREATE INDEX [IX_StockDocumentInputInvoiceMap_StockDocumentId] ON [StockDocumentInputInvoiceMap] ([StockDocumentId]);
GO

CREATE UNIQUE INDEX [IX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_InputInvoiceHeadId] ON [StockDocumentInputInvoiceMap] ([StoreId], [StockDocumentId], [InputInvoiceHeadId]);
GO

CREATE INDEX [IX_StockDocumentLine_ProductUnitConversionId] ON [StockDocumentLine] ([ProductUnitConversionId]);
GO

CREATE INDEX [IX_StockDocumentLine_ProductVariantId] ON [StockDocumentLine] ([ProductVariantId]);
GO

CREATE INDEX [IX_StockDocumentLine_PurchaseOrderLineId] ON [StockDocumentLine] ([PurchaseOrderLineId]);
GO

CREATE INDEX [IX_StockDocumentLine_StockDocumentId] ON [StockDocumentLine] ([StockDocumentId]);
GO

CREATE UNIQUE INDEX [IX_StockDocumentLine_StockDocumentId_LineNo] ON [StockDocumentLine] ([StockDocumentId], [LineNo]);
GO

CREATE INDEX [IX_StockDocumentLine_TaxId] ON [StockDocumentLine] ([TaxId]);
GO

CREATE INDEX [IX_StockDocumentLine_UnitId] ON [StockDocumentLine] ([UnitId]);
GO

CREATE INDEX [IX_StockDocumentLineInputInvoiceMap_InputInvoiceDetailId] ON [StockDocumentLineInputInvoiceMap] ([InputInvoiceDetailId]);
GO

CREATE INDEX [IX_StockDocumentLineInputInvoiceMap_StockDocumentId] ON [StockDocumentLineInputInvoiceMap] ([StockDocumentId]);
GO

CREATE INDEX [IX_StockDocumentLineInputInvoiceMap_StockDocumentLineId] ON [StockDocumentLineInputInvoiceMap] ([StockDocumentLineId]);
GO

CREATE INDEX [IX_StockDocumentLineInputInvoiceMap_StoreId_StockDocumentId_UseInputInvoice] ON [StockDocumentLineInputInvoiceMap] ([StoreId], [StockDocumentId], [UseInputInvoice]);
GO

CREATE UNIQUE INDEX [IX_StockDocumentLineInputInvoiceMap_StoreId_StockDocumentLineId] ON [StockDocumentLineInputInvoiceMap] ([StoreId], [StockDocumentLineId]);
GO

CREATE INDEX [IX_StockTransferDocument_FromWarehouseId] ON [StockTransferDocument] ([FromWarehouseId]);
GO

CREATE INDEX [IX_StockTransferDocument_StoreId_DocumentDate_Status] ON [StockTransferDocument] ([StoreId], [DocumentDate], [Status]);
GO

CREATE UNIQUE INDEX [IX_StockTransferDocument_StoreId_DocumentNo] ON [StockTransferDocument] ([StoreId], [DocumentNo]);
GO

CREATE INDEX [IX_StockTransferDocument_StoreId_FromWarehouseId_DocumentDate] ON [StockTransferDocument] ([StoreId], [FromWarehouseId], [DocumentDate]);
GO

CREATE INDEX [IX_StockTransferDocument_StoreId_ToWarehouseId_DocumentDate] ON [StockTransferDocument] ([StoreId], [ToWarehouseId], [DocumentDate]);
GO

CREATE INDEX [IX_StockTransferDocument_ToWarehouseId] ON [StockTransferDocument] ([ToWarehouseId]);
GO

CREATE INDEX [IX_StockTransferLine_ProductVariantId] ON [StockTransferLine] ([ProductVariantId]);
GO

CREATE UNIQUE INDEX [IX_StockTransferLine_StockTransferDocumentId_LineNo] ON [StockTransferLine] ([StockTransferDocumentId], [LineNo]);
GO

CREATE INDEX [IX_StockTransferLine_StoreId_ProductVariantId] ON [StockTransferLine] ([StoreId], [ProductVariantId]);
GO

CREATE UNIQUE INDEX [IX_StoreBankAccounts_StoreId_AccountNumber] ON [StoreBankAccounts] ([StoreId], [AccountNumber]);
GO

CREATE INDEX [IX_StoreBankAccounts_StoreId_IsDefault] ON [StoreBankAccounts] ([StoreId], [IsDefault]);
GO

CREATE UNIQUE INDEX [IX_Stores_SubDomainNormalized] ON [Stores] ([SubDomainNormalized]);
GO

CREATE UNIQUE INDEX [IX_Suppliers_StoreId_Code] ON [Suppliers] ([StoreId], [Code]);
GO

CREATE UNIQUE INDEX [IX_Suppliers_StoreId_Name] ON [Suppliers] ([StoreId], [Name]);
GO

CREATE UNIQUE INDEX [IX_Taxes_StoreId_Code] ON [Taxes] ([StoreId], [Code]);
GO

CREATE UNIQUE INDEX [IX_Taxes_StoreId_Name] ON [Taxes] ([StoreId], [Name]);
GO

CREATE UNIQUE INDEX [IX_Unit_StoreId_Code] ON [Unit] ([StoreId], [Code]);
GO

CREATE UNIQUE INDEX [IX_Unit_StoreId_Name] ON [Unit] ([StoreId], [Name]);
GO

CREATE INDEX [IX_UserInStores_RoleId] ON [UserInStores] ([RoleId]);
GO

CREATE INDEX [IX_UserInStores_StoreId_IsActive] ON [UserInStores] ([StoreId], [IsActive]);
GO

CREATE INDEX [IX_UserInStores_StoreId_RoleId] ON [UserInStores] ([StoreId], [RoleId]);
GO

CREATE UNIQUE INDEX [IX_UserInStores_StoreId_UserId] ON [UserInStores] ([StoreId], [UserId]);
GO

CREATE INDEX [IX_UserInStores_UserId] ON [UserInStores] ([UserId]);
GO

CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]) WHERE [Email] IS NOT NULL;
GO

CREATE UNIQUE INDEX [IX_Users_UserName] ON [Users] ([UserName]);
GO

CREATE UNIQUE INDEX [IX_Warehouses_StoreId_Code] ON [Warehouses] ([StoreId], [Code]);
GO

CREATE INDEX [IX_Warehouses_StoreId_LegalEntityId] ON [Warehouses] ([StoreId], [LegalEntityId]);
GO

CREATE UNIQUE INDEX [IX_Warehouses_StoreId_Name] ON [Warehouses] ([StoreId], [Name]);
GO

ALTER TABLE [CustomerRewardLedgers] ADD CONSTRAINT [FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId] FOREIGN KEY ([VoucherId]) REFERENCES [CustomerRewardVouchers] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [CustomerRewardLedgers] ADD CONSTRAINT [FK_CustomerRewardLedgers_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [CustomerRewardLedgers] ADD CONSTRAINT [FK_CustomerRewardLedgers_SalesReturns_SalesReturnId] FOREIGN KEY ([SalesReturnId]) REFERENCES [SalesReturns] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [CustomerRewardVouchers] ADD CONSTRAINT [FK_CustomerRewardVouchers_Orders_UsedOrderId] FOREIGN KEY ([UsedOrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryAdjustmentDocuments] ADD CONSTRAINT [FK_InventoryAdjustmentDocuments_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryBalances] ADD CONSTRAINT [FK_InventoryBalances_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryCostLayerAllocations] ADD CONSTRAINT [FK_InventoryCostLayerAllocations_InventoryCostLayers_InventoryCostLayerId] FOREIGN KEY ([InventoryCostLayerId]) REFERENCES [InventoryCostLayers] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryCostLayerAllocations] ADD CONSTRAINT [FK_InventoryCostLayerAllocations_InventoryCostLayers_ResolvedByInventoryCostLayerId] FOREIGN KEY ([ResolvedByInventoryCostLayerId]) REFERENCES [InventoryCostLayers] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryCostLayerAllocations] ADD CONSTRAINT [FK_InventoryCostLayerAllocations_InventoryValuationEntries_InventoryValuationEntryId] FOREIGN KEY ([InventoryValuationEntryId]) REFERENCES [InventoryValuationEntries] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryCostLayers] ADD CONSTRAINT [FK_InventoryCostLayers_InventoryTransactions_InventoryTransactionId] FOREIGN KEY ([InventoryTransactionId]) REFERENCES [InventoryTransactions] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryCostLayers] ADD CONSTRAINT [FK_InventoryCostLayers_InventoryValuationEntries_InventoryValuationEntryId] FOREIGN KEY ([InventoryValuationEntryId]) REFERENCES [InventoryValuationEntries] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryCostLayers] ADD CONSTRAINT [FK_InventoryCostLayers_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryReservations] ADD CONSTRAINT [FK_InventoryReservations_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryTransactions] ADD CONSTRAINT [FK_InventoryTransactions_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InventoryValuationEntries] ADD CONSTRAINT [FK_InventoryValuationEntries_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InvoiceCorrectionCases] ADD CONSTRAINT [FK_InvoiceCorrectionCases_InvoiceHeads_NewInvoiceHeadId] FOREIGN KEY ([NewInvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InvoiceCorrectionCases] ADD CONSTRAINT [FK_InvoiceCorrectionCases_InvoiceHeads_OriginalInvoiceHeadId] FOREIGN KEY ([OriginalInvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InvoiceDetails] ADD CONSTRAINT [FK_InvoiceDetails_InvoiceHeads_InvoiceHeadId] FOREIGN KEY ([InvoiceHeadId]) REFERENCES [InvoiceHeads] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InvoiceDetails] ADD CONSTRAINT [FK_InvoiceDetails_OrderLegalEntityAllocations_StoreId_OrderLegalEntityAllocationId] FOREIGN KEY ([StoreId], [OrderLegalEntityAllocationId]) REFERENCES [OrderLegalEntityAllocations] ([StoreId], [Id]);
GO

ALTER TABLE [InvoiceDetails] ADD CONSTRAINT [FK_InvoiceDetails_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]);
GO

ALTER TABLE [InvoiceHeads] ADD CONSTRAINT [FK_InvoiceHeads_LegalEntities_StoreId_LegalEntityId] FOREIGN KEY ([StoreId], [LegalEntityId]) REFERENCES [LegalEntities] ([StoreId], [Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [InvoiceHeads] ADD CONSTRAINT [FK_InvoiceHeads_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [LegalEntities] ADD CONSTRAINT [FK_LegalEntities_Warehouses_StoreId_DefaultWarehouseId] FOREIGN KEY ([StoreId], [DefaultWarehouseId]) REFERENCES [Warehouses] ([StoreId], [Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderInventoryIssueActions] ADD CONSTRAINT [FK_OrderInventoryIssueActions_OrderInventoryIssueLines_OrderInventoryIssueLineId] FOREIGN KEY ([OrderInventoryIssueLineId]) REFERENCES [OrderInventoryIssueLines] ([Id]);
GO

ALTER TABLE [OrderInventoryIssueActions] ADD CONSTRAINT [FK_OrderInventoryIssueActions_OrderInventoryIssues_OrderInventoryIssueId] FOREIGN KEY ([OrderInventoryIssueId]) REFERENCES [OrderInventoryIssues] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderInventoryIssueLineAllocations] ADD CONSTRAINT [FK_OrderInventoryIssueLineAllocations_OrderInventoryIssueLines_OrderInventoryIssueLineId] FOREIGN KEY ([OrderInventoryIssueLineId]) REFERENCES [OrderInventoryIssueLines] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderInventoryIssueLineAllocations] ADD CONSTRAINT [FK_OrderInventoryIssueLineAllocations_OrderInventoryIssues_OrderInventoryIssueId] FOREIGN KEY ([OrderInventoryIssueId]) REFERENCES [OrderInventoryIssues] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderInventoryIssueLines] ADD CONSTRAINT [FK_OrderInventoryIssueLines_OrderInventoryIssues_OrderInventoryIssueId] FOREIGN KEY ([OrderInventoryIssueId]) REFERENCES [OrderInventoryIssues] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderInventoryIssueLines] ADD CONSTRAINT [FK_OrderInventoryIssueLines_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderInventoryIssueLines] ADD CONSTRAINT [FK_OrderInventoryIssueLines_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderInventoryIssues] ADD CONSTRAINT [FK_OrderInventoryIssues_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderLegalEntityAllocationReversals] ADD CONSTRAINT [FK_OrderLegalEntityAllocationReversals_OrderLegalEntityAllocations_OrderLegalEntityAllocationId] FOREIGN KEY ([OrderLegalEntityAllocationId]) REFERENCES [OrderLegalEntityAllocations] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderLegalEntityAllocationReversals] ADD CONSTRAINT [FK_OrderLegalEntityAllocationReversals_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderLegalEntityAllocationReversals] ADD CONSTRAINT [FK_OrderLegalEntityAllocationReversals_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderLegalEntityAllocationReversals] ADD CONSTRAINT [FK_OrderLegalEntityAllocationReversals_SalesReturnLines_SalesReturnLineId] FOREIGN KEY ([SalesReturnLineId]) REFERENCES [SalesReturnLines] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderLegalEntityAllocationReversals] ADD CONSTRAINT [FK_OrderLegalEntityAllocationReversals_SalesReturns_SalesReturnId] FOREIGN KEY ([SalesReturnId]) REFERENCES [SalesReturns] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderLegalEntityAllocations] ADD CONSTRAINT [FK_OrderLegalEntityAllocations_OrderLines_OrderLineId] FOREIGN KEY ([OrderLineId]) REFERENCES [OrderLines] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderLegalEntityAllocations] ADD CONSTRAINT [FK_OrderLegalEntityAllocations_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [OrderLines] ADD CONSTRAINT [FK_OrderLines_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE;
GO

ALTER TABLE [OrderPayments] ADD CONSTRAINT [FK_OrderPayments_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE;
GO

ALTER TABLE [OrderRewardVouchers] ADD CONSTRAINT [FK_OrderRewardVouchers_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]);
GO

ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_POSShifts_POSShiftId] FOREIGN KEY ([POSShiftId]) REFERENCES [POSShifts] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260726073029_InitialProductionBaseline', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [InventoryTransactions] ADD [IdempotencyKey] varbinary(32) NULL;
GO

CREATE UNIQUE INDEX [UX_InventoryTransactions_StoreId_IdempotencyKey_Active] ON [InventoryTransactions] ([StoreId], [IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL AND [IsDeleted] = 0;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260801110856_AddInventoryPostingIdempotency', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [PurchaseReceiptAuditEvents] (
    [Id] bigint NOT NULL IDENTITY,
    [StoreId] int NOT NULL,
    [StockDocumentId] int NOT NULL,
    [StockDocumentLineId] int NULL,
    [EventType] int NOT NULL,
    [ActorUserId] int NOT NULL,
    [ActorUserName] nvarchar(200) NULL,
    [OccurredAtUtc] datetime2 NOT NULL,
    [Reason] nvarchar(1000) NULL,
    [Note] nvarchar(1000) NULL,
    [ChangedFieldsJson] nvarchar(max) NOT NULL,
    [OldValuesJson] nvarchar(max) NOT NULL,
    [NewValuesJson] nvarchar(max) NOT NULL,
    [TraceId] nvarchar(100) NULL,
    [IsSuccess] bit NOT NULL,
    CONSTRAINT [PK_PurchaseReceiptAuditEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PurchaseReceiptAuditEvents_StockDocumentLine_StockDocumentLineId] FOREIGN KEY ([StockDocumentLineId]) REFERENCES [StockDocumentLine] ([Id]),
    CONSTRAINT [FK_PurchaseReceiptAuditEvents_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]),
    CONSTRAINT [FK_PurchaseReceiptAuditEvents_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE INDEX [IX_PurchaseReceiptAuditEvents_StockDocumentLineId] ON [PurchaseReceiptAuditEvents] ([StockDocumentLineId]);
GO

CREATE INDEX [IX_PurchaseReceiptAuditEvents_StockDocumentId] ON [PurchaseReceiptAuditEvents] ([StockDocumentId]);
GO

CREATE INDEX [IX_PurchaseReceiptAuditEvents_Store_Document_Occurred_Id] ON [PurchaseReceiptAuditEvents] ([StoreId], [StockDocumentId], [OccurredAtUtc], [Id]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260814090000_AddPurchaseReceiptAuditEvents', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [StockDocument] ADD [CapitalizeFreightInInventoryCost] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

ALTER TABLE [StockDocument] ADD [IncludeVatInInventoryCost] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

UPDATE [dbo].[StockDocument]
SET [IncludeVatInInventoryCost] = CASE WHEN [HasVat] = 1 THEN 1 ELSE 0 END,
    [CapitalizeFreightInInventoryCost] = CASE WHEN [HasFreight] = 1 THEN 1 ELSE 0 END
WHERE [Status] = 3;
GO

UPDATE line
SET line.[FreightAllocation] = 0
FROM [dbo].[StockDocumentLine] AS line
INNER JOIN [dbo].[StockDocument] AS document
    ON document.[Id] = line.[StockDocumentId]
WHERE document.[Status] <> 3
  AND line.[FreightAllocation] <> 0;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260817090000_AddPurchaseReceiptCostCapitalizationPolicy', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [InputInvoiceHead] ADD [InvoiceIdentityDate] date NULL;
GO

ALTER TABLE [InputInvoiceHead] ADD [NormalizedInvoiceNumber] nvarchar(50) NULL;
GO

ALTER TABLE [InputInvoiceHead] ADD [NormalizedInvoiceSeries] nvarchar(50) NULL;
GO

ALTER TABLE [InputInvoiceHead] ADD [NormalizedSellerTaxCode] nvarchar(50) NULL;
GO

UPDATE [dbo].[InputInvoiceHead]
SET [NormalizedSellerTaxCode] = NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([SellerTaxCode])), N' ', N''), N'.', N''), N'-', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''),
    [NormalizedInvoiceSeries] = NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([InvoiceSeries])), N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''),
    [NormalizedInvoiceNumber] = NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([InvoiceNumber])), N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''),
    [InvoiceIdentityDate] = CONVERT(date, [InvoiceDate])
WHERE [IsDeleted] = 0;
GO

IF EXISTS
(
    SELECT 1
    FROM [dbo].[InputInvoiceHead]
    WHERE [IsDeleted] = 0
      AND [NormalizedSellerTaxCode] IS NOT NULL
      AND [NormalizedInvoiceSeries] IS NOT NULL
      AND [NormalizedInvoiceNumber] IS NOT NULL
      AND [InvoiceIdentityDate] IS NOT NULL
    GROUP BY [StoreId], [NormalizedSellerTaxCode], [NormalizedInvoiceSeries], [NormalizedInvoiceNumber], [InvoiceIdentityDate]
    HAVING COUNT_BIG(*) > 1
)
    THROW 51001, 'Duplicate active input-invoice business identities must be resolved before migration.', 1;

IF EXISTS
(
    SELECT 1
    FROM [dbo].[InputInvoiceHead]
    WHERE [IsDeleted] = 0
      AND [XmlHash] IS NOT NULL
    GROUP BY [StoreId], [XmlHash]
    HAVING COUNT_BIG(*) > 1
)
    THROW 51002, 'Duplicate active input-invoice XML hashes must be resolved before migration.', 1;
GO

CREATE UNIQUE INDEX [UX_InputInvoiceHead_StoreId_BusinessIdentity_Active] ON [InputInvoiceHead] ([StoreId], [NormalizedSellerTaxCode], [NormalizedInvoiceSeries], [NormalizedInvoiceNumber], [InvoiceIdentityDate]) WHERE [NormalizedSellerTaxCode] IS NOT NULL AND [NormalizedInvoiceSeries] IS NOT NULL AND [NormalizedInvoiceNumber] IS NOT NULL AND [InvoiceIdentityDate] IS NOT NULL AND [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [UX_InputInvoiceHead_StoreId_XmlHash_Active] ON [InputInvoiceHead] ([StoreId], [XmlHash]) WHERE [XmlHash] IS NOT NULL AND [IsDeleted] = 0;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260817150000_AddInputInvoiceIdentityUniqueness', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [Suppliers] ADD [NormalizedTaxCode] AS CONVERT(nvarchar(50), NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([TaxCode])), N' ', N''), N'.', N''), N'-', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N'')) PERSISTED;
GO

ALTER TABLE [InputInvoiceHead] ADD [ResolvedSupplierId] int NULL;
GO

ALTER TABLE [InputInvoiceHead] ADD [SupplierResolutionStatus] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [InputInvoiceHead] ADD [SupplierResolutionUpdatedAtUtc] datetime2 NULL;
GO

CREATE TABLE [InputInvoiceSupplierResolutionEvent] (
    [Id] bigint NOT NULL IDENTITY,
    [StoreId] int NOT NULL,
    [InputInvoiceHeadId] int NOT NULL,
    [StockDocumentId] int NULL,
    [EventType] int NOT NULL,
    [PreviousStatus] int NOT NULL,
    [NewStatus] int NOT NULL,
    [OldSupplierId] int NULL,
    [NewSupplierId] int NULL,
    [CandidateCount] int NULL,
    [Reason] nvarchar(1000) NULL,
    [ActorUserId] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_InputInvoiceSupplierResolutionEvent] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_InputInvoiceSupplierResolutionEvent_InputInvoiceHead_InputInvoiceHeadId] FOREIGN KEY ([InputInvoiceHeadId]) REFERENCES [InputInvoiceHead] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InputInvoiceSupplierResolutionEvent_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]),
    CONSTRAINT [FK_InputInvoiceSupplierResolutionEvent_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_InputInvoiceSupplierResolutionEvent_Suppliers_NewSupplierId] FOREIGN KEY ([NewSupplierId]) REFERENCES [Suppliers] ([Id]),
    CONSTRAINT [FK_InputInvoiceSupplierResolutionEvent_Suppliers_OldSupplierId] FOREIGN KEY ([OldSupplierId]) REFERENCES [Suppliers] ([Id])
);
GO

CREATE INDEX [IX_Suppliers_StoreId_NormalizedTaxCode_State] ON [Suppliers] ([StoreId], [NormalizedTaxCode], [IsDeleted], [IsActive]);
GO

CREATE INDEX [IX_InputInvoiceHead_StoreId_ResolvedSupplierId] ON [InputInvoiceHead] ([StoreId], [ResolvedSupplierId]);
GO

CREATE INDEX [IX_InputInvoiceSupplierResolutionEvent_InputInvoiceHeadId] ON [InputInvoiceSupplierResolutionEvent] ([InputInvoiceHeadId]);
GO

CREATE INDEX [IX_InputInvoiceSupplierResolutionEvent_NewSupplierId] ON [InputInvoiceSupplierResolutionEvent] ([NewSupplierId]);
GO

CREATE INDEX [IX_InputInvoiceSupplierResolutionEvent_OldSupplierId] ON [InputInvoiceSupplierResolutionEvent] ([OldSupplierId]);
GO

CREATE INDEX [IX_InputInvoiceSupplierResolutionEvent_StockDocumentId] ON [InputInvoiceSupplierResolutionEvent] ([StockDocumentId]);
GO

CREATE INDEX [IX_InputInvoiceSupplierResolutionEvent_Store_Invoice_Time] ON [InputInvoiceSupplierResolutionEvent] ([StoreId], [InputInvoiceHeadId], [CreatedAtUtc]);
GO

CREATE INDEX [IX_InputInvoiceSupplierResolutionEvent_Store_Receipt_Time] ON [InputInvoiceSupplierResolutionEvent] ([StoreId], [StockDocumentId], [CreatedAtUtc]);
GO

ALTER TABLE [InputInvoiceHead] ADD CONSTRAINT [FK_InputInvoiceHead_Suppliers_ResolvedSupplierId] FOREIGN KEY ([ResolvedSupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260822090000_AddInputInvoiceSupplierResolution', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

DROP INDEX [IX_LegalEntities_StoreId_TaxCode] ON [LegalEntities];
GO

ALTER TABLE [LegalEntities] ADD [NormalizedTaxCode] AS CONVERT(nvarchar(50), NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([TaxCode])), N' ', N''), N'.', N''), N'-', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N'')) PERSISTED;
GO

ALTER TABLE [InputInvoiceHead] ADD [NormalizedBuyerTaxCode] AS CONVERT(nvarchar(50), NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([BuyerTaxCode])), N' ', N''), N'.', N''), N'-', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N'')) PERSISTED;
GO

ALTER TABLE [InputInvoiceHead] ADD [BuyerOwnerResolutionStatus] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [InputInvoiceHead] ADD [ResolvedBuyerLegalEntityId] int NULL;
GO

ALTER TABLE [InputInvoiceHead] ADD [BuyerOwnerResolutionUpdatedAtUtc] datetime2 NULL;
GO

ALTER TABLE [StockDocument] ADD [ConfirmedLegalEntityId] int NULL;
GO

UPDATE i
   SET [BuyerOwnerResolutionStatus] =
         CASE WHEN i.[NormalizedBuyerTaxCode] IS NULL THEN 1
              WHEN c.[CandidateCount] = 0 THEN 2
              WHEN c.[CandidateCount] > 1 THEN 3 ELSE 4 END,
       [ResolvedBuyerLegalEntityId] =
         CASE WHEN c.[CandidateCount] = 1 THEN c.[LegalEntityId] ELSE NULL END,
       [BuyerOwnerResolutionUpdatedAtUtc] = SYSUTCDATETIME()
  FROM [InputInvoiceHead] i
  OUTER APPLY (
        SELECT COUNT_BIG(*) [CandidateCount], MIN(le.[Id]) [LegalEntityId]
          FROM [LegalEntities] le
         WHERE le.[StoreId] = i.[StoreId]
           AND le.[NormalizedTaxCode] = i.[NormalizedBuyerTaxCode]
           AND le.[IsActive] = 1 AND le.[IsDeleted] = 0
  ) c;

UPDATE d
   SET [ConfirmedLegalEntityId] = w.[LegalEntityId]
  FROM [StockDocument] d
  JOIN [Warehouses] w ON w.[Id] = d.[WarehouseId]
                      AND w.[StoreId] = d.[StoreId]
 WHERE d.[Type] = 1 AND d.[Status] = 3;

IF EXISTS (
    SELECT 1 FROM [StockDocument]
     WHERE [Type] = 1 AND [Status] = 3
       AND [ConfirmedLegalEntityId] IS NULL)
    THROW 51001, 'C2 backfill blocked: confirmed receipt owner is not deterministic.', 1;

IF EXISTS (
    SELECT 1
      FROM [StockDocumentInputInvoiceMap] m
      JOIN [StockDocument] d ON d.[Id] = m.[StockDocumentId]
      JOIN [InputInvoiceHead] i ON i.[Id] = m.[InputInvoiceHeadId]
      JOIN [Warehouses] w ON w.[Id] = d.[WarehouseId]
     WHERE m.[IsDeleted] = 0 AND d.[IsDeleted] = 0 AND i.[IsDeleted] = 0
       AND d.[Type] = 1
       AND (i.[BuyerOwnerResolutionStatus] <> 4
         OR i.[ResolvedBuyerLegalEntityId] <>
            CASE WHEN d.[Status] = 3 THEN d.[ConfirmedLegalEntityId]
                 ELSE w.[LegalEntityId] END))
    THROW 51002, 'C2 backfill blocked: active receipt-invoice owner link is unsafe.', 1;
GO

CREATE INDEX [IX_LegalEntities_StoreId_TaxCode] ON [LegalEntities] ([StoreId], [TaxCode]) WHERE [TaxCode] IS NOT NULL AND [TaxCode] <> '' AND [IsDeleted] = 0;
GO

CREATE INDEX [IX_LegalEntities_StoreId_NormalizedTaxCode_State] ON [LegalEntities] ([StoreId], [NormalizedTaxCode], [IsDeleted], [IsActive]);
GO

CREATE INDEX [IX_InputInvoiceHead_StoreId_NormalizedBuyerTaxCode] ON [InputInvoiceHead] ([StoreId], [NormalizedBuyerTaxCode]);
GO

CREATE INDEX [IX_InputInvoiceHead_StoreId_ResolvedBuyerLegalEntityId] ON [InputInvoiceHead] ([StoreId], [ResolvedBuyerLegalEntityId]);
GO

CREATE INDEX [IX_StockDocument_StoreId_ConfirmedLegalEntityId] ON [StockDocument] ([StoreId], [ConfirmedLegalEntityId]);
GO

ALTER TABLE [InputInvoiceHead] ADD CONSTRAINT [FK_InputInvoiceHead_LegalEntities_StoreId_ResolvedBuyerLegalEntityId] FOREIGN KEY ([StoreId], [ResolvedBuyerLegalEntityId]) REFERENCES [LegalEntities] ([StoreId], [Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [StockDocument] ADD CONSTRAINT [FK_StockDocument_LegalEntities_StoreId_ConfirmedLegalEntityId] FOREIGN KEY ([StoreId], [ConfirmedLegalEntityId]) REFERENCES [LegalEntities] ([StoreId], [Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [StockDocument] ADD CONSTRAINT [CK_StockDocument_ConfirmedReceiptOwner] CHECK ([Type] <> 1 OR [Status] <> 3 OR [ConfirmedLegalEntityId] IS NOT NULL);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260824150000_AddInputInvoiceBuyerOwnerGuard', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [InputInvoiceDetail] ADD [NormalizedItemName] nvarchar(500) NULL;
GO

ALTER TABLE [InputInvoiceDetail] ADD [NormalizedSupplierItemCode] nvarchar(200) NULL;
GO

ALTER TABLE [InputInvoiceDetail] ADD [NormalizedUnitName] nvarchar(100) NULL;
GO

ALTER TABLE [InputInvoiceDetail] ADD [SupplierItemCode] nvarchar(200) NULL;
GO

UPDATE [InputInvoiceDetail]
   SET [NormalizedItemName] = NULLIF(UPPER(LTRIM(RTRIM(
         REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
           TRANSLATE([ItemName], NCHAR(9)+NCHAR(10)+NCHAR(13)+NCHAR(160), N'    '),
           N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' '),
           N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' ')
       ))), N''),
       [NormalizedUnitName] = NULLIF(UPPER(LTRIM(RTRIM(
         REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
           TRANSLATE([UnitName], NCHAR(9)+NCHAR(10)+NCHAR(13)+NCHAR(160), N'    '),
           N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' '),
           N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' ')
       ))), N'')
 WHERE [IsDeleted] = 0;
GO

CREATE TABLE [InputInvoiceItemCatalogMap] (
    [Id] int NOT NULL IDENTITY,
    [StoreId] int NOT NULL,
    [SupplierId] int NOT NULL,
    [SupplierItemCode] nvarchar(200) NULL,
    [NormalizedSupplierItemCode] nvarchar(200) NULL,
    [SupplierItemName] nvarchar(500) NOT NULL,
    [NormalizedSupplierItemName] nvarchar(500) NOT NULL,
    [SupplierUnitName] nvarchar(100) NOT NULL,
    [NormalizedSupplierUnitName] nvarchar(100) NOT NULL,
    [ProductVariantId] int NOT NULL,
    [ProductUnitConversionId] int NOT NULL,
    [ConfirmedUnitId] int NOT NULL,
    [ConfirmedFactor] decimal(18,4) NOT NULL,
    [ConfirmedBaseUnitId] int NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_InputInvoiceItemCatalogMap] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_InputInvoiceItemCatalogMap_ProductUnitConversion_ProductUnitConversionId] FOREIGN KEY ([ProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InputInvoiceItemCatalogMap_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InputInvoiceItemCatalogMap_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_InputInvoiceItemCatalogMap_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InputInvoiceItemCatalogMap_Unit_ConfirmedBaseUnitId] FOREIGN KEY ([ConfirmedBaseUnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InputInvoiceItemCatalogMap_Unit_ConfirmedUnitId] FOREIGN KEY ([ConfirmedUnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_InputInvoiceDetail_InputInvoiceHeadId_NormalizedSupplierItemCode_NormalizedUnitName] ON [InputInvoiceDetail] ([InputInvoiceHeadId], [NormalizedSupplierItemCode], [NormalizedUnitName]);
GO

CREATE INDEX [IX_InputInvoiceItemCatalogMap_ConfirmedBaseUnitId] ON [InputInvoiceItemCatalogMap] ([ConfirmedBaseUnitId]);
GO

CREATE INDEX [IX_InputInvoiceItemCatalogMap_ConfirmedUnitId] ON [InputInvoiceItemCatalogMap] ([ConfirmedUnitId]);
GO

CREATE INDEX [IX_InputInvoiceItemCatalogMap_ProductUnitConversionId] ON [InputInvoiceItemCatalogMap] ([ProductUnitConversionId]);
GO

CREATE INDEX [IX_InputInvoiceItemCatalogMap_ProductVariantId] ON [InputInvoiceItemCatalogMap] ([ProductVariantId]);
GO

CREATE INDEX [IX_InputInvoiceItemCatalogMap_SupplierId] ON [InputInvoiceItemCatalogMap] ([SupplierId]);
GO

CREATE INDEX [IX_InputInvoiceItemCatalogMap_StoreId_ProductVariantId_ProductUnitConversionId_IsActive] ON [InputInvoiceItemCatalogMap] ([StoreId], [ProductVariantId], [ProductUnitConversionId], [IsActive]);
GO

CREATE UNIQUE INDEX [UX_InputInvoiceItemCatalogMap_CodeUnit_Active] ON [InputInvoiceItemCatalogMap] ([StoreId], [SupplierId], [NormalizedSupplierItemCode], [NormalizedSupplierUnitName]) WHERE [NormalizedSupplierItemCode] IS NOT NULL AND [IsActive] = 1 AND [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [UX_InputInvoiceItemCatalogMap_NameUnit_Active] ON [InputInvoiceItemCatalogMap] ([StoreId], [SupplierId], [NormalizedSupplierItemName], [NormalizedSupplierUnitName]) WHERE [NormalizedSupplierItemCode] IS NULL AND [IsActive] = 1 AND [IsDeleted] = 0;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260826150000_AddInputInvoiceItemCatalogMapping', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [StockDocumentLineInputInvoiceMap] ADD [ExclusionReason] nvarchar(1000) NULL;
GO

CREATE TABLE [StockDocumentInputInvoiceReconciliation] (
    [Id] int NOT NULL IDENTITY,
    [StockDocumentInputInvoiceMapId] int NOT NULL,
    [StockDocumentId] int NOT NULL,
    [InputInvoiceHeadId] int NOT NULL,
    [OverallState] int NOT NULL,
    [EvidenceFingerprint] nvarchar(64) NOT NULL,
    [LastCalculatedAtUtc] datetime2 NOT NULL,
    [TotalDetailCount] int NOT NULL,
    [MatchedDetailCount] int NOT NULL,
    [MismatchDetailCount] int NOT NULL,
    [UnmatchedDetailCount] int NOT NULL,
    [IgnoredDetailCount] int NOT NULL,
    [ExcludedLineCount] int NOT NULL,
    [IncompleteCount] int NOT NULL,
    [ReceiptSubtotalBeforeVat] decimal(18,2) NOT NULL,
    [XmlTotalBeforeTax] decimal(18,2) NOT NULL,
    [SubtotalDifference] decimal(18,2) NOT NULL,
    [ReceiptVatAmount] decimal(18,2) NOT NULL,
    [XmlTaxAmount] decimal(18,2) NOT NULL,
    [VatDifference] decimal(18,2) NOT NULL,
    [ReceiptGoodsTotal] decimal(18,2) NOT NULL,
    [XmlPaymentAmount] decimal(18,2) NOT NULL,
    [PaymentDifference] decimal(18,2) NOT NULL,
    [HasUnsupportedHeader] bit NOT NULL,
    [UnsupportedHeaderReason] nvarchar(500) NULL,
    [AcceptedEvidenceFingerprint] nvarchar(64) NULL,
    [AcceptanceReason] nvarchar(1000) NULL,
    [AcceptedByUserId] int NULL,
    [AcceptedAtUtc] datetime2 NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_StockDocumentInputInvoiceReconciliation] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_StockDocumentInputInvoiceReconciliation_InputInvoiceHead_InputInvoiceHeadId] FOREIGN KEY ([InputInvoiceHeadId]) REFERENCES [InputInvoiceHead] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentInputInvoiceReconciliation_StockDocumentInputInvoiceMap_StockDocumentInputInvoiceMapId] FOREIGN KEY ([StockDocumentInputInvoiceMapId]) REFERENCES [StockDocumentInputInvoiceMap] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentInputInvoiceReconciliation_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentInputInvoiceReconciliation_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [StockDocumentInputInvoiceDetailReconciliation] (
    [Id] int NOT NULL IDENTITY,
    [StockDocumentInputInvoiceReconciliationId] int NOT NULL,
    [StockDocumentId] int NOT NULL,
    [InputInvoiceHeadId] int NOT NULL,
    [InputInvoiceDetailId] int NOT NULL,
    [DetailState] int NOT NULL,
    [ProductVariantId] int NULL,
    [ProductUnitConversionId] int NULL,
    [ConfirmedUnitId] int NULL,
    [ConfirmedBaseUnitId] int NULL,
    [ConfirmedFactor] decimal(18,4) NULL,
    [XmlQuantity] decimal(18,4) NOT NULL,
    [DerivedBaseQuantity] decimal(18,4) NOT NULL,
    [ReceiptBaseQuantity] decimal(18,4) NOT NULL,
    [QuantityDifference] decimal(18,4) NOT NULL,
    [ReceiptBeforeVatAmount] decimal(18,2) NOT NULL,
    [XmlBeforeVatAmount] decimal(18,2) NOT NULL,
    [AmountDifference] decimal(18,2) NOT NULL,
    [ReceiptVatRate] decimal(9,4) NULL,
    [XmlVatRate] decimal(9,4) NULL,
    [VatComparable] bit NOT NULL,
    [VatComparisonReason] nvarchar(500) NULL,
    [ReceiptVatAmount] decimal(18,2) NOT NULL,
    [XmlVatAmount] decimal(18,2) NOT NULL,
    [VatAmountDifference] decimal(18,2) NOT NULL,
    [IsIgnored] bit NOT NULL,
    [IgnoreReason] nvarchar(1000) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_StockDocumentInputInvoiceDetailReconciliation] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_StockDocumentInputInvoiceDetailReconciliation_InputInvoiceDetail_InputInvoiceDetailId] FOREIGN KEY ([InputInvoiceDetailId]) REFERENCES [InputInvoiceDetail] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentInputInvoiceDetailReconciliation_StockDocumentInputInvoiceReconciliation_StockDocumentInputInvoiceReconciliatio~] FOREIGN KEY ([StockDocumentInputInvoiceReconciliationId]) REFERENCES [StockDocumentInputInvoiceReconciliation] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentInputInvoiceDetailReconciliation_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE INDEX [IX_InputInvoiceDetailReconciliation_Receipt_State] ON [StockDocumentInputInvoiceDetailReconciliation] ([StoreId], [StockDocumentId], [InputInvoiceHeadId], [DetailState]);
GO

CREATE INDEX [IX_StockDocumentInputInvoiceDetailReconciliation_InputInvoiceDetailId] ON [StockDocumentInputInvoiceDetailReconciliation] ([InputInvoiceDetailId]);
GO

CREATE INDEX [IX_StockDocumentInputInvoiceDetailReconciliation_StockDocumentInputInvoiceReconciliationId] ON [StockDocumentInputInvoiceDetailReconciliation] ([StockDocumentInputInvoiceReconciliationId]);
GO

CREATE UNIQUE INDEX [UX_InputInvoiceDetailReconciliation_Detail_Active] ON [StockDocumentInputInvoiceDetailReconciliation] ([StoreId], [StockDocumentInputInvoiceReconciliationId], [InputInvoiceDetailId]) WHERE [IsDeleted] = 0;
GO

CREATE INDEX [IX_InputInvoiceReconciliation_Receipt_State] ON [StockDocumentInputInvoiceReconciliation] ([StoreId], [StockDocumentId], [OverallState]);
GO

CREATE INDEX [IX_StockDocumentInputInvoiceReconciliation_InputInvoiceHeadId] ON [StockDocumentInputInvoiceReconciliation] ([InputInvoiceHeadId]);
GO

CREATE INDEX [IX_StockDocumentInputInvoiceReconciliation_StockDocumentId] ON [StockDocumentInputInvoiceReconciliation] ([StockDocumentId]);
GO

CREATE INDEX [IX_StockDocumentInputInvoiceReconciliation_StockDocumentInputInvoiceMapId] ON [StockDocumentInputInvoiceReconciliation] ([StockDocumentInputInvoiceMapId]);
GO

CREATE UNIQUE INDEX [UX_InputInvoiceReconciliation_Link_Active] ON [StockDocumentInputInvoiceReconciliation] ([StoreId], [StockDocumentInputInvoiceMapId]) WHERE [IsDeleted] = 0;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260827150000_AddInputInvoiceReconciliation', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE UNIQUE INDEX [UX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_Active] ON [StockDocumentInputInvoiceMap] ([StoreId], [StockDocumentId]) WHERE [IsDeleted] = 0;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260828150000_EnforceSingleActiveInputInvoicePerReceipt', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT 1
    FROM [PurchaseOrderLines]
    WHERE [IsDeleted] = 0 AND [ProductVariantId] IS NOT NULL
    GROUP BY [StoreId], [PurchaseOrderId], [ProductVariantId]
    HAVING COUNT_BIG(*) > 1)
    THROW 51001, 'RW migration preflight: duplicate active ProductVariant rows exist in a Purchase Order.', 1;

IF EXISTS (
    SELECT 1
    FROM [StockDocument]
    WHERE [IsDeleted] = 0 AND [Type] = 1 AND [ReceiptSource] = 2
      AND [PurchaseOrderId] IS NOT NULL AND [Status] IN (1, 4)
    GROUP BY [StoreId], [PurchaseOrderId]
    HAVING COUNT_BIG(*) > 1)
    THROW 51002, 'RW migration preflight: multiple editable Purchase Receipts exist for a Purchase Order.', 1;

IF EXISTS (
    SELECT 1
    FROM [StockDocumentLine] l
    INNER JOIN [StockDocument] d ON d.[Id] = l.[StockDocumentId]
    LEFT JOIN [PurchaseOrderLines] p ON p.[Id] = l.[PurchaseOrderLineId]
    WHERE l.[IsDeleted] = 0 AND d.[IsDeleted] = 0
      AND d.[Type] = 1 AND d.[ReceiptSource] = 2
      AND (l.[PurchaseOrderLineId] IS NULL OR p.[Id] IS NULL
           OR p.[PurchaseOrderId] <> d.[PurchaseOrderId]
           OR p.[StoreId] <> d.[StoreId]
           OR p.[ProductVariantId] <> l.[ProductVariantId]))
    THROW 51003, 'RW migration preflight: a PO receipt line has an inconsistent Purchase Order allocation.', 1;

IF EXISTS (
    SELECT 1
    FROM [StockDocumentLine] l
    INNER JOIN [StockDocument] d ON d.[Id] = l.[StockDocumentId]
    WHERE l.[IsDeleted] = 0 AND d.[IsDeleted] = 0
      AND d.[Type] = 1 AND d.[ReceiptSource] = 2
      AND l.[PurchaseOrderLineId] IS NOT NULL
      AND l.[ProductUnitConversionId] IS NOT NULL
    GROUP BY l.[StockDocumentId], l.[PurchaseOrderLineId], l.[ProductUnitConversionId]
    HAVING COUNT_BIG(*) > 1)
    THROW 51004, 'RW migration preflight: duplicate active PO receipt components exist.', 1;
GO

ALTER TABLE [StockDocumentLine] ADD [OutsidePoDecisionAtUtc] datetime2 NULL;
GO

ALTER TABLE [StockDocumentLine] ADD [OutsidePoDecisionByUserId] int NULL;
GO

ALTER TABLE [StockDocumentLine] ADD [OutsidePoDecisionNote] nvarchar(1000) NULL;
GO

ALTER TABLE [StockDocumentLine] ADD [OutsidePoDecisionStatus] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [StockDocumentLine] ADD [ReceiptAllocationKind] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [StockDocument] ADD [ReceivingLastSavedAtUtc] datetime2 NULL;
GO

ALTER TABLE [StockDocument] ADD [ReceivingLeaseExpiresAtUtc] datetime2 NULL;
GO

ALTER TABLE [StockDocument] ADD [ReceivingLeaseToken] uniqueidentifier NULL;
GO

ALTER TABLE [StockDocument] ADD [ReceivingOwnerUserId] int NULL;
GO

ALTER TABLE [StockDocument] ADD [ReceivingRevision] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [StockDocument] ADD [ReceivingSessionState] int NOT NULL DEFAULT 0;
GO

UPDATE l
SET [ReceiptAllocationKind] = 1,
    [OutsidePoDecisionStatus] = 0
FROM [StockDocumentLine] l
INNER JOIN [StockDocument] d ON d.[Id] = l.[StockDocumentId]
WHERE l.[IsDeleted] = 0 AND d.[IsDeleted] = 0
  AND d.[Type] = 1 AND d.[ReceiptSource] = 2
  AND l.[PurchaseOrderLineId] IS NOT NULL;

UPDATE [StockDocument]
SET [ReceivingSessionState] = CASE
        WHEN [Status] IN (1, 4) THEN 1
        WHEN [Status] = 2 THEN 2
        WHEN [Status] IN (3, 5) THEN 3
        ELSE 0
    END,
    [ReceivingRevision] = CASE WHEN [Status] = 4 THEN 1 ELSE 0 END
WHERE [IsDeleted] = 0 AND [Type] = 1 AND [ReceiptSource] = 2
  AND [PurchaseOrderId] IS NOT NULL;
GO

CREATE TABLE [PurchaseReceivingActions] (
    [Id] bigint NOT NULL IDENTITY,
    [StoreId] int NOT NULL,
    [StockDocumentId] int NOT NULL,
    [ReceivingRevision] int NOT NULL,
    [CommandId] uniqueidentifier NOT NULL,
    [ActionType] int NOT NULL,
    [StockDocumentLineId] int NOT NULL,
    [ProductVariantId] int NOT NULL,
    [ProductUnitConversionId] int NOT NULL,
    [PurchaseOrderLineId] int NULL,
    [ReceiptAllocationKind] int NOT NULL,
    [BeforeQuantity] decimal(18,3) NOT NULL,
    [AfterQuantity] decimal(18,3) NOT NULL,
    [BeforeIsDeleted] bit NOT NULL,
    [AfterIsDeleted] bit NOT NULL,
    [ActorUserId] int NOT NULL,
    [OccurredAtUtc] datetime2 NOT NULL,
    [UndoOfActionId] bigint NULL,
    CONSTRAINT [PK_PurchaseReceivingActions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PurchaseReceivingActions_PurchaseReceivingActions_UndoOfActionId] FOREIGN KEY ([UndoOfActionId]) REFERENCES [PurchaseReceivingActions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseReceivingActions_StockDocumentLine_StockDocumentLineId] FOREIGN KEY ([StockDocumentLineId]) REFERENCES [StockDocumentLine] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseReceivingActions_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE NO ACTION
);
GO

CREATE UNIQUE INDEX [UX_StockDocumentLine_OutsideReceivingComponent] ON [StockDocumentLine] ([StockDocumentId], [ProductVariantId], [ProductUnitConversionId]) WHERE [IsDeleted] = 0 AND [ReceiptAllocationKind] = 2 AND [ProductUnitConversionId] IS NOT NULL;
GO

CREATE UNIQUE INDEX [UX_StockDocumentLine_PoReceivingComponent] ON [StockDocumentLine] ([StockDocumentId], [PurchaseOrderLineId], [ProductUnitConversionId]) WHERE [IsDeleted] = 0 AND [ReceiptAllocationKind] = 1 AND [PurchaseOrderLineId] IS NOT NULL AND [ProductUnitConversionId] IS NOT NULL;
GO

ALTER TABLE [StockDocumentLine] ADD CONSTRAINT [CK_StockDocumentLine_ReceivingAllocation] CHECK ([ReceiptAllocationKind] = 0 AND [OutsidePoDecisionStatus] = 0 OR [ReceiptAllocationKind] = 1 AND [PurchaseOrderLineId] IS NOT NULL AND [OutsidePoDecisionStatus] = 0 OR [ReceiptAllocationKind] = 2 AND [PurchaseOrderLineId] IS NULL AND ([OutsidePoDecisionStatus] = 1 OR [OutsidePoDecisionStatus] = 2 OR [OutsidePoDecisionStatus] = 3));
GO

CREATE INDEX [IX_StockDocument_ReceivingLease] ON [StockDocument] ([StoreId], [ReceivingOwnerUserId], [ReceivingLeaseExpiresAtUtc]);
GO

CREATE UNIQUE INDEX [UX_StockDocument_ActiveReceivingDraft] ON [StockDocument] ([StoreId], [PurchaseOrderId]) WHERE [IsDeleted] = 0 AND [Type] = 1 AND [ReceiptSource] = 2 AND [ReceivingSessionState] = 1 AND [PurchaseOrderId] IS NOT NULL;
GO

CREATE UNIQUE INDEX [UX_PurchaseOrderLines_Store_Order_ProductVariant] ON [PurchaseOrderLines] ([StoreId], [PurchaseOrderId], [ProductVariantId]) WHERE [IsDeleted] = 0 AND [ProductVariantId] IS NOT NULL;
GO

CREATE INDEX [IX_PurchaseReceivingActions_Latest] ON [PurchaseReceivingActions] ([StoreId], [StockDocumentId], [ReceivingRevision], [Id]);
GO

CREATE INDEX [IX_PurchaseReceivingActions_StockDocumentId] ON [PurchaseReceivingActions] ([StockDocumentId]);
GO

CREATE INDEX [IX_PurchaseReceivingActions_StockDocumentLineId] ON [PurchaseReceivingActions] ([StockDocumentLineId]);
GO

CREATE UNIQUE INDEX [UX_PurchaseReceivingActions_Document_Command] ON [PurchaseReceivingActions] ([StoreId], [StockDocumentId], [CommandId]);
GO

CREATE UNIQUE INDEX [UX_PurchaseReceivingActions_UndoOf] ON [PurchaseReceivingActions] ([UndoOfActionId]) WHERE [UndoOfActionId] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260830112901_AddReceivingWorkbench', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

DROP INDEX [UX_StockDocument_ActiveReceivingDraft] ON [StockDocument];
GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PurchaseReceivingActions]') AND [c].[name] = N'StockDocumentLineId');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [PurchaseReceivingActions] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [PurchaseReceivingActions] ALTER COLUMN [StockDocumentLineId] int NULL;
GO

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PurchaseReceivingActions]') AND [c].[name] = N'ProductVariantId');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [PurchaseReceivingActions] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [PurchaseReceivingActions] ALTER COLUMN [ProductVariantId] int NULL;
GO

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PurchaseReceivingActions]') AND [c].[name] = N'ProductUnitConversionId');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [PurchaseReceivingActions] DROP CONSTRAINT [' + @var2 + '];');
ALTER TABLE [PurchaseReceivingActions] ALTER COLUMN [ProductUnitConversionId] int NULL;
GO

ALTER TABLE [PurchaseReceivingActions] ADD [AfterProvisionalStateJson] nvarchar(2000) NULL;
GO

ALTER TABLE [PurchaseReceivingActions] ADD [BeforeProvisionalStateJson] nvarchar(2000) NULL;
GO

ALTER TABLE [PurchaseReceivingActions] ADD [CommandPayloadHash] nvarchar(64) NULL;
GO

ALTER TABLE [PurchaseReceivingActions] ADD [StockDocumentProvisionalItemId] int NULL;
GO

ALTER TABLE [StockDocument] ADD [EditablePurchaseReceiptKey] AS CASE WHEN [IsDeleted] = 0 AND [Type] = 1 AND [ReceiptSource] = 2 AND ([Status] = 4 OR [Status] = 1) AND [PurchaseOrderId] IS NOT NULL THEN [PurchaseOrderId] ELSE -[Id] END PERSISTED;
GO

ALTER TABLE [PurchaseReceiptAuditEvents] ADD [StockDocumentProvisionalItemId] int NULL;
GO

CREATE TABLE [StockDocumentProvisionalItems] (
    [Id] int NOT NULL IDENTITY,
    [StockDocumentId] int NOT NULL,
    [NameSnapshot] nvarchar(200) NOT NULL,
    [RawBarcodeSnapshot] nvarchar(256) NULL,
    [NormalizedBarcode] nvarchar(64) NULL,
    [UnitId] int NULL,
    [UnitNameSnapshot] nvarchar(100) NULL,
    [NormalizedUnitNameSnapshot] nvarchar(100) NULL,
    [Quantity] decimal(18,3) NOT NULL,
    [Note] nvarchar(500) NULL,
    [Status] int NOT NULL,
    [ResolutionMethod] int NULL,
    [ResolvedStockDocumentLineId] int NULL,
    [ResolvedProductVariantId] int NULL,
    [ResolvedProductUnitConversionId] int NULL,
    [ResolvedByUserId] int NULL,
    [ResolvedAtUtc] datetime2 NULL,
    [RawBarcodeRemembered] bit NOT NULL,
    [CreatedBarcodeId] int NULL,
    [RemovedByUserId] int NULL,
    [RemovedAtUtc] datetime2 NULL,
    [SupersededByProvisionalItemId] int NULL,
    [ReceivingRevision] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_StockDocumentProvisionalItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_StockDocumentProvisionalItem_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_StockDocumentProvisionalItem_State] CHECK ([Status] = 0 AND [ResolvedStockDocumentLineId] IS NULL AND [ResolutionMethod] IS NULL AND [RemovedAtUtc] IS NULL OR [Status] = 1 AND [ResolvedStockDocumentLineId] IS NOT NULL AND [ResolvedProductVariantId] IS NOT NULL AND [ResolvedProductUnitConversionId] IS NOT NULL AND [ResolutionMethod] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL OR [Status] = 2 AND [ResolvedStockDocumentLineId] IS NULL AND [RemovedAtUtc] IS NOT NULL),
    CONSTRAINT [FK_StockDocumentProvisionalItems_ProductUnitConversion_ResolvedProductUnitConversionId] FOREIGN KEY ([ResolvedProductUnitConversionId]) REFERENCES [ProductUnitConversion] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentProvisionalItems_ProductVariantUnitBarcode_CreatedBarcodeId] FOREIGN KEY ([CreatedBarcodeId]) REFERENCES [ProductVariantUnitBarcode] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentProvisionalItems_ProductVariant_ResolvedProductVariantId] FOREIGN KEY ([ResolvedProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentProvisionalItems_StockDocumentLine_ResolvedStockDocumentLineId] FOREIGN KEY ([ResolvedStockDocumentLineId]) REFERENCES [StockDocumentLine] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentProvisionalItems_StockDocumentProvisionalItems_SupersededByProvisionalItemId] FOREIGN KEY ([SupersededByProvisionalItemId]) REFERENCES [StockDocumentProvisionalItems] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentProvisionalItems_StockDocument_StockDocumentId] FOREIGN KEY ([StockDocumentId]) REFERENCES [StockDocument] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StockDocumentProvisionalItems_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_StockDocumentProvisionalItems_Unit_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION
);
GO

CREATE UNIQUE INDEX [UX_StockDocument_EditablePurchaseReceipt] ON [StockDocument] ([StoreId], [EditablePurchaseReceiptKey]);
GO

CREATE INDEX [IX_PurchaseReceivingActions_StockDocumentProvisionalItemId] ON [PurchaseReceivingActions] ([StockDocumentProvisionalItemId]);
GO

ALTER TABLE [PurchaseReceivingActions] ADD CONSTRAINT [CK_PurchaseReceivingActions_Target] CHECK ([StockDocumentLineId] IS NOT NULL AND [StockDocumentProvisionalItemId] IS NULL AND [ProductVariantId] IS NOT NULL AND [ProductUnitConversionId] IS NOT NULL OR [StockDocumentLineId] IS NULL AND [StockDocumentProvisionalItemId] IS NOT NULL AND [ProductVariantId] IS NULL AND [ProductUnitConversionId] IS NULL);
GO

CREATE INDEX [IX_PurchaseReceiptAuditEvents_StockDocumentProvisionalItemId] ON [PurchaseReceiptAuditEvents] ([StockDocumentProvisionalItemId]);
GO

CREATE INDEX [IX_StockDocumentProvisionalItems_CreatedBarcodeId] ON [StockDocumentProvisionalItems] ([CreatedBarcodeId]);
GO

CREATE INDEX [IX_StockDocumentProvisionalItems_Document_Status] ON [StockDocumentProvisionalItems] ([StoreId], [StockDocumentId], [Status]);
GO

CREATE INDEX [IX_StockDocumentProvisionalItems_ResolvedProductUnitConversionId] ON [StockDocumentProvisionalItems] ([ResolvedProductUnitConversionId]);
GO

CREATE INDEX [IX_StockDocumentProvisionalItems_ResolvedProductVariantId] ON [StockDocumentProvisionalItems] ([ResolvedProductVariantId]);
GO

CREATE INDEX [IX_StockDocumentProvisionalItems_ResolvedStockDocumentLineId] ON [StockDocumentProvisionalItems] ([ResolvedStockDocumentLineId]);
GO

CREATE INDEX [IX_StockDocumentProvisionalItems_SupersededByProvisionalItemId] ON [StockDocumentProvisionalItems] ([SupersededByProvisionalItemId]);
GO

CREATE INDEX [IX_StockDocumentProvisionalItems_UnitId] ON [StockDocumentProvisionalItems] ([UnitId]);
GO

CREATE UNIQUE INDEX [UX_StockDocumentProvisionalItems_Barcode_Unit] ON [StockDocumentProvisionalItems] ([StockDocumentId], [NormalizedBarcode], [UnitId]) WHERE [IsDeleted] = 0 AND [Status] = 0 AND [NormalizedBarcode] IS NOT NULL AND [UnitId] IS NOT NULL;
GO

CREATE UNIQUE INDEX [UX_StockDocumentProvisionalItems_Barcode_UnitSnapshot] ON [StockDocumentProvisionalItems] ([StockDocumentId], [NormalizedBarcode], [NormalizedUnitNameSnapshot]) WHERE [IsDeleted] = 0 AND [Status] = 0 AND [NormalizedBarcode] IS NOT NULL AND [UnitId] IS NULL AND [NormalizedUnitNameSnapshot] IS NOT NULL;
GO

ALTER TABLE [PurchaseReceiptAuditEvents] ADD CONSTRAINT [FK_PurchaseReceiptAuditEvents_StockDocumentProvisionalItems_StockDocumentProvisionalItemId] FOREIGN KEY ([StockDocumentProvisionalItemId]) REFERENCES [StockDocumentProvisionalItems] ([Id]);
GO

ALTER TABLE [PurchaseReceivingActions] ADD CONSTRAINT [FK_PurchaseReceivingActions_StockDocumentProvisionalItems_StockDocumentProvisionalItemId] FOREIGN KEY ([StockDocumentProvisionalItemId]) REFERENCES [StockDocumentProvisionalItems] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260831135031_AddProvisionalReceivingItems', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [AcbQrSessions] (
    [Id] int NOT NULL IDENTITY,
    [QrRequestId] int NOT NULL,
    [OrderId] int NOT NULL,
    [ShiftId] int NOT NULL,
    [TerminalId] int NOT NULL,
    [CashierId] int NOT NULL,
    [ProviderOrderId] nvarchar(80) NOT NULL,
    [TraceNumber] nvarchar(100) NOT NULL,
    [VirtualAccount] nvarchar(100) NOT NULL,
    [CartFingerprint] nvarchar(64) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Status] int NOT NULL,
    [ReviewReason] nvarchar(500) NULL,
    [PaymentId] int NULL,
    [PrintClaimedAtUtc] datetime2 NULL,
    [LastRetrievedAtUtc] datetime2 NULL,
    [LastRetrieveJson] nvarchar(max) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_AcbQrSessions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AcbQrSessions_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AcbQrSessions_PosPaymentQrRequests_QrRequestId] FOREIGN KEY ([QrRequestId]) REFERENCES [PosPaymentQrRequests] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AcbQrSessions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [StoreAcbSettings] (
    [Id] int NOT NULL IDENTITY,
    [Enabled] bit NOT NULL,
    [BankAccountId] int NOT NULL,
    [TokenEndpoint] nvarchar(500) NOT NULL,
    [ApiBaseUrl] nvarchar(500) NOT NULL,
    [QrEndpoint] nvarchar(500) NOT NULL,
    [ClientId] nvarchar(200) NOT NULL,
    [ClientSecretProtected] nvarchar(max) NOT NULL,
    [CallbackApiKeyProtected] nvarchar(max) NOT NULL,
    [TokenScope] nvarchar(100) NOT NULL,
    [XService] nvarchar(100) NOT NULL,
    [XProviderId] nvarchar(100) NOT NULL,
    [XOwnerNumber] nvarchar(100) NOT NULL,
    [XOwnerType] nvarchar(100) NOT NULL,
    [VirtualAccountPrefix] nvarchar(100) NOT NULL,
    [MerchantId] nvarchar(100) NOT NULL,
    [BeneficiaryName] nvarchar(200) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_StoreAcbSettings] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_StoreAcbSettings_StoreBankAccounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [StoreBankAccounts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_StoreAcbSettings_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [AcbPaymentTransactions] (
    [Id] int NOT NULL IDENTITY,
    [SessionId] int NOT NULL,
    [TransactionNumber] nvarchar(100) NOT NULL,
    [Status] nvarchar(50) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Content] nvarchar(max) NOT NULL,
    [PostedAt] nvarchar(100) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_AcbPaymentTransactions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AcbPaymentTransactions_AcbQrSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [AcbQrSessions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AcbPaymentTransactions_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE UNIQUE INDEX [IX_AcbPaymentTransactions_SessionId_TransactionNumber] ON [AcbPaymentTransactions] ([SessionId], [TransactionNumber]);
GO

CREATE INDEX [IX_AcbPaymentTransactions_StoreId] ON [AcbPaymentTransactions] ([StoreId]);
GO

CREATE INDEX [IX_AcbQrSessions_OrderId] ON [AcbQrSessions] ([OrderId]);
GO

CREATE UNIQUE INDEX [IX_AcbQrSessions_QrRequestId] ON [AcbQrSessions] ([QrRequestId]);
GO

CREATE INDEX [IX_AcbQrSessions_StoreId_OrderId_Status] ON [AcbQrSessions] ([StoreId], [OrderId], [Status]);
GO

CREATE UNIQUE INDEX [IX_AcbQrSessions_StoreId_ProviderOrderId] ON [AcbQrSessions] ([StoreId], [ProviderOrderId]);
GO

CREATE INDEX [IX_StoreAcbSettings_BankAccountId] ON [StoreAcbSettings] ([BankAccountId]);
GO

CREATE UNIQUE INDEX [IX_StoreAcbSettings_StoreId] ON [StoreAcbSettings] ([StoreId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260908151919_AddStoreAcbPayments', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [AcbCallbackReceipts] (
    [Id] int NOT NULL IDENTITY,
    [ClientRequestId] nvarchar(36) NOT NULL,
    [Page] int NOT NULL,
    [PayloadHash] nvarchar(64) NOT NULL,
    [PayloadJson] nvarchar(max) NOT NULL,
    [Attempts] int NOT NULL,
    [NextAttemptAtUtc] datetime2 NOT NULL,
    [ProcessedAtUtc] datetime2 NULL,
    [NeedsReview] bit NOT NULL,
    [LastErrorCode] nvarchar(100) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_AcbCallbackReceipts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AcbCallbackReceipts_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE INDEX [IX_AcbCallbackReceipts_ProcessedAtUtc_NextAttemptAtUtc] ON [AcbCallbackReceipts] ([ProcessedAtUtc], [NextAttemptAtUtc]);
GO

CREATE UNIQUE INDEX [IX_AcbCallbackReceipts_StoreId_ClientRequestId_Page] ON [AcbCallbackReceipts] ([StoreId], [ClientRequestId], [Page]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260908155844_AddAcbCallbackInbox', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

DROP INDEX [IX_AcbCallbackReceipts_StoreId_ClientRequestId_Page] ON [AcbCallbackReceipts];
GO

ALTER TABLE [AcbCallbackReceipts] ADD [RequestCode] nvarchar(30) NOT NULL DEFAULT N'TRANSACTION_UPDATE';
GO

ALTER TABLE [AcbCallbackReceipts] ADD [TotalPages] int NOT NULL DEFAULT 1;
GO

CREATE TABLE [AcbQrNotificationItems] (
    [Id] int NOT NULL IDENTITY,
    [ReceiptId] int NOT NULL,
    [Position] int NOT NULL,
    [ProviderOrderId] nvarchar(300) NOT NULL,
    [RequestCode] nvarchar(30) NOT NULL,
    [BusinessDate] date NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [TransactionStatus] nvarchar(30) NOT NULL,
    [DebitOrCredit] nvarchar(10) NOT NULL,
    [Content] nvarchar(max) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_AcbQrNotificationItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AcbQrNotificationItems_AcbCallbackReceipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [AcbCallbackReceipts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AcbQrNotificationItems_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO


UPDATE r SET TotalPages = COALESCE(TRY_CONVERT(int, JSON_VALUE(j.Payload, '$.requestParameters.request.requestParams.pagination.totalPage')), 1)
FROM AcbCallbackReceipts r
CROSS APPLY (SELECT CASE WHEN ISJSON(r.PayloadJson) = 1 THEN r.PayloadJson ELSE '{}' END AS Payload) j;

INSERT INTO AcbQrNotificationItems
(ReceiptId, Position, ProviderOrderId, RequestCode, BusinessDate, Amount, TransactionStatus, DebitOrCredit, Content,
 CreatedAtUtc, CreatedBy, IsDeleted, StoreId)
SELECT r.Id, CONVERT(int, t.[key]), COALESCE(v.ProviderOrderId, ''), r.RequestCode, TRY_CONVERT(date, v.BusinessDate, 23),
 TRY_CONVERT(decimal(18,2), v.Amount), v.TransactionStatus, COALESCE(v.DebitOrCredit, ''), COALESCE(v.Content, ''),
 r.CreatedAtUtc, r.CreatedBy, r.IsDeleted, r.StoreId
FROM AcbCallbackReceipts r
CROSS APPLY OPENJSON(CASE WHEN ISJSON(r.PayloadJson) = 1 THEN r.PayloadJson ELSE '{}' END,
 '$.requestParameters.request.requestParams.transactions') t
CROSS APPLY OPENJSON(t.value) WITH (
 ProviderOrderId nvarchar(max) '$.transactionEntityAttribute.custom4',
 BusinessDate nvarchar(100) '$.effectiveDate', Amount nvarchar(100) '$.amount',
 TransactionStatus nvarchar(100) '$.transactionStatus', DebitOrCredit nvarchar(100) '$.debitOrCredit',
 Content nvarchar(max) '$.transactionContent') v
WHERE TRY_CONVERT(date, v.BusinessDate, 23) IS NOT NULL AND TRY_CONVERT(decimal(18,2), v.Amount) IS NOT NULL
 AND LEN(COALESCE(v.ProviderOrderId, '')) <= 300 AND LEN(COALESCE(v.DebitOrCredit, '')) <= 10
 AND v.TransactionStatus IN ('COMPLETED', 'ERRORCORRECTED');
GO

CREATE UNIQUE INDEX [IX_AcbCallbackReceipts_StoreId_RequestCode_ClientRequestId_Page] ON [AcbCallbackReceipts] ([StoreId], [RequestCode], [ClientRequestId], [Page]);
GO

CREATE UNIQUE INDEX [IX_AcbQrNotificationItems_ReceiptId_Position] ON [AcbQrNotificationItems] ([ReceiptId], [Position]);
GO

CREATE INDEX [IX_AcbQrNotificationItems_StoreId_BusinessDate_RequestCode_ProviderOrderId] ON [AcbQrNotificationItems] ([StoreId], [BusinessDate], [RequestCode], [ProviderOrderId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260908192844_AddAcbQrNotificationReconciliation', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [PosPaymentQrRequests] ADD [ClientRequestId] uniqueidentifier NULL;
GO

ALTER TABLE [PosPaymentQrRequests] ADD [PaymentId] int NULL;
GO

ALTER TABLE [PosPaymentQrRequests] ADD [PrintClaimedAtUtc] datetime2 NULL;
GO

CREATE INDEX [IX_PosPaymentQrRequests_PaymentId] ON [PosPaymentQrRequests] ([PaymentId]);
GO

CREATE UNIQUE INDEX [IX_PosPaymentQrRequests_StoreId_OrderId_ClientRequestId] ON [PosPaymentQrRequests] ([StoreId], [OrderId], [ClientRequestId]) WHERE [ClientRequestId] IS NOT NULL;
GO

ALTER TABLE [PosPaymentQrRequests] ADD CONSTRAINT [FK_PosPaymentQrRequests_OrderPayments_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [OrderPayments] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909015242_AddPosQrInstallmentLinks', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [AcbQrSessions] ADD [ConfirmationCallbackReceiptId] int NULL;
GO

ALTER TABLE [AcbQrSessions] ADD [ConfirmationSource] int NULL;
GO

ALTER TABLE [AcbQrSessions] ADD [ConfirmedAtUtc] datetime2 NULL;
GO

ALTER TABLE [AcbQrSessions] ADD [ConfirmedByUserId] int NULL;
GO

CREATE INDEX [IX_AcbQrSessions_ConfirmationCallbackReceiptId] ON [AcbQrSessions] ([ConfirmationCallbackReceiptId]);
GO

ALTER TABLE [AcbQrSessions] ADD CONSTRAINT [FK_AcbQrSessions_AcbCallbackReceipts_ConfirmationCallbackReceiptId] FOREIGN KEY ([ConfirmationCallbackReceiptId]) REFERENCES [AcbCallbackReceipts] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909024821_AddAcbConfirmationAudit', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF EXISTS (SELECT StoreId FROM StoreBankAccounts
           WHERE IsDefault = 1 AND IsDeleted = 0
           GROUP BY StoreId HAVING COUNT(*) > 1)
    THROW 51001, 'Multiple default bank accounts exist in a store. Select one default per store before retrying migration.', 1;
GO

CREATE UNIQUE INDEX [UX_StoreBankAccounts_OneDefaultPerStore] ON [StoreBankAccounts] ([StoreId]) WHERE [IsDefault] = 1 AND [IsDeleted] = 0;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909055844_EnforceSingleDefaultBankAccount', N'8.0.29');
GO

COMMIT;
GO

ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;
GO

BEGIN TRANSACTION;
GO

CREATE INDEX [IX_SalesReturns_StoreId_CompletedAtUtc] ON [SalesReturns] ([StoreId], [CompletedAtUtc]);
GO

CREATE INDEX [IX_InventoryValuationEntries_StoreId_EntryType_OccurredAtUtc] ON [InventoryValuationEntries] ([StoreId], [EntryType], [OccurredAtUtc]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909061611_EnableSnapshotProfitReads', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

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
GO

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
GO

CREATE INDEX [IX_AcbCallbackRouteChanges_RouteId_ChangedAtUtc] ON [AcbCallbackRouteChanges] ([RouteId], [ChangedAtUtc]);
GO

CREATE UNIQUE INDEX [IX_AcbCallbackRoutes_Host] ON [AcbCallbackRoutes] ([Host]);
GO

CREATE INDEX [IX_AcbCallbackRoutes_TargetStoreId] ON [AcbCallbackRoutes] ([TargetStoreId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909062834_AddAcbCallbackStoreRouting', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [OrderPayments] ADD [ClientRequestId] uniqueidentifier NULL;
GO

CREATE UNIQUE INDEX [UX_OrderPayments_StoreId_ClientRequestId] ON [OrderPayments] ([StoreId], [ClientRequestId]) WHERE [ClientRequestId] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909080000_AddPosCollectionIdempotency', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [Suppliers] ADD [BankAccountNumber] nvarchar(50) NULL;
GO

ALTER TABLE [Suppliers] ADD [BankAccountName] nvarchar(250) NULL;
GO

ALTER TABLE [Suppliers] ADD [BankName] nvarchar(250) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909100000_AddSupplierBankFields', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [PosOperationReceipts] (
    [Id] int NOT NULL IDENTITY,
    [OperationId] uniqueidentifier NOT NULL,
    [StoreId] int NOT NULL,
    [TerminalId] int NOT NULL,
    [UserId] int NOT NULL,
    [RequestHash] nvarchar(64) NOT NULL,
    [ResponseJson] nvarchar(max) NOT NULL,
    [WasOffline] bit NOT NULL,
    [OccurredAtUtc] datetime2 NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_PosOperationReceipts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PosOperationReceipts_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE UNIQUE INDEX [IX_PosOperationReceipts_StoreId_OperationId] ON [PosOperationReceipts] ([StoreId], [OperationId]);
GO

CREATE INDEX [IX_PosOperationReceipts_StoreId_TerminalId_CreatedAtUtc] ON [PosOperationReceipts] ([StoreId], [TerminalId], [CreatedAtUtc]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909210000_AddPosOfflineJournal', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

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
GO

CREATE INDEX [IX_PosReceiptTemplates_StoreId_IsDeleted] ON [PosReceiptTemplates] ([StoreId], [IsDeleted]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260910002000_AddPosReceiptTemplates', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [Stores] ADD [ReceiptName] nvarchar(200) NULL;
GO

ALTER TABLE [Stores] ADD [ReceiptAddress] nvarchar(300) NULL;
GO

ALTER TABLE [Stores] ADD [ReceiptPhone] nvarchar(50) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260910012000_AddStoreReceiptIdentity', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

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
GO

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
GO

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
GO

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
GO

CREATE INDEX [IX_ProductLabelJobs_PrinterId] ON [ProductLabelJobs] ([PrinterId]);
GO

CREATE INDEX [IX_ProductLabelJobs_StoreId_PrinterId_Status_Id] ON [ProductLabelJobs] ([StoreId], [PrinterId], [Status], [Id]);
GO

CREATE UNIQUE INDEX [IX_ProductLabelJobs_StoreId_RequestId] ON [ProductLabelJobs] ([StoreId], [RequestId]);
GO

CREATE UNIQUE INDEX [IX_ProductLabelJobs_StoreId_TaskId] ON [ProductLabelJobs] ([StoreId], [TaskId]) WHERE [TaskId] IS NOT NULL AND ([Status] IN (0, 1, 2, 3));
GO

CREATE INDEX [IX_ProductLabelJobs_TaskId] ON [ProductLabelJobs] ([TaskId]);
GO

CREATE UNIQUE INDEX [IX_ProductLabelPrinters_StoreId_WindowsPrinterName] ON [ProductLabelPrinters] ([StoreId], [WindowsPrinterName]);
GO

CREATE INDEX [IX_ProductLabelTasks_StockDocumentId] ON [ProductLabelTasks] ([StockDocumentId]);
GO

CREATE UNIQUE INDEX [IX_ProductLabelTasks_StoreId_StockDocumentId] ON [ProductLabelTasks] ([StoreId], [StockDocumentId]);
GO

CREATE INDEX [IX_ProductLabelTemplates_StoreId_IsDeleted] ON [ProductLabelTemplates] ([StoreId], [IsDeleted]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260910040620_AddProductLabelPrinting', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [StockDocumentProvisionalItems] ADD [ProposedBaseUnitId] int NULL;
GO

ALTER TABLE [StockDocumentProvisionalItems] ADD [ProposedBaseUnitName] nvarchar(100) NULL;
GO

ALTER TABLE [StockDocumentProvisionalItems] ADD [ProposedCategoryId] int NULL;
GO

ALTER TABLE [StockDocumentProvisionalItems] ADD [ProposedFactor] decimal(18,3) NULL;
GO

ALTER TABLE [StockDocumentProvisionalItems] ADD [ProposedProductVariantId] int NULL;
GO

CREATE INDEX [IX_StockDocumentProvisionalItems_ProposedBaseUnitId] ON [StockDocumentProvisionalItems] ([ProposedBaseUnitId]);
GO

CREATE INDEX [IX_StockDocumentProvisionalItems_ProposedProductVariantId] ON [StockDocumentProvisionalItems] ([ProposedProductVariantId]);
GO

ALTER TABLE [StockDocumentProvisionalItems] ADD CONSTRAINT [FK_StockDocumentProvisionalItems_ProductVariant_ProposedProductVariantId] FOREIGN KEY ([ProposedProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [StockDocumentProvisionalItems] ADD CONSTRAINT [FK_StockDocumentProvisionalItems_Unit_ProposedBaseUnitId] FOREIGN KEY ([ProposedBaseUnitId]) REFERENCES [Unit] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260911053655_AddReceiptIntakePacking', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [Stores] ADD [GuestWifiName] nvarchar(128) NULL;
GO

ALTER TABLE [Stores] ADD [GuestWifiPassword] nvarchar(128) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260912120000_AddCustomerDisplayWifi', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [StockDocumentProvisionalItems] ADD [PackagingPhoto] varbinary(max) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260912150000_AddReceivingPackagingPhoto', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [OrderLines] ADD [RewardBaseUnitPrice] decimal(18,2) NULL;
GO

ALTER TABLE [OrderLines] ADD [RewardableAmountSnapshot] decimal(18,2) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260914073514_AddOrderRewardEligibilitySnapshots', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [POSShifts] ADD [CashReceiptNote] nvarchar(500) NULL;
GO

ALTER TABLE [POSShifts] ADD [CashReceivedAmount] decimal(18,2) NULL;
GO

ALTER TABLE [POSShifts] ADD [CashReceivedAtUtc] datetime2 NULL;
GO

ALTER TABLE [POSShifts] ADD [CashReceivedByUserId] int NULL;
GO

CREATE INDEX [IX_POSShifts_StoreId_CashReceivedAtUtc] ON [POSShifts] ([StoreId], [CashReceivedAtUtc]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260914154923_AddPOSShiftCashReceipt', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [Orders] ADD [CreditDueDate] datetime2 NULL;
GO

ALTER TABLE [Orders] ADD [CreditInitialBalance] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [Orders] ADD [CreditNote] nvarchar(500) NULL;
GO

ALTER TABLE [Orders] ADD [CreditRequestId] uniqueidentifier NULL;
GO

ALTER TABLE [Orders] ADD [IsCreditSale] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

ALTER TABLE [OrderPayments] ADD [IsDebtCollection] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

CREATE TABLE [CustomerDebtReceipts] (
    [Id] int NOT NULL IDENTITY,
    [ClientRequestId] uniqueidentifier NOT NULL,
    [CustomerId] int NOT NULL,
    [POSShiftId] int NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Method] int NOT NULL,
    [StoreBankAccountId] int NULL,
    [Reference] nvarchar(100) NULL,
    [Note] nvarchar(500) NULL,
    [RequestJson] nvarchar(max) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_CustomerDebtReceipts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustomerDebtReceipts_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerDebtReceipts_POSShifts_POSShiftId] FOREIGN KEY ([POSShiftId]) REFERENCES [POSShifts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerDebtReceipts_StoreBankAccounts_StoreBankAccountId] FOREIGN KEY ([StoreBankAccountId]) REFERENCES [StoreBankAccounts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerDebtReceipts_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [CustomerReceivableEntries] (
    [Id] int NOT NULL IDENTITY,
    [CustomerId] int NOT NULL,
    [OrderId] int NOT NULL,
    [ReceiptId] int NULL,
    [SalesReturnId] int NULL,
    [Kind] nvarchar(20) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
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
    CONSTRAINT [PK_CustomerReceivableEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustomerReceivableEntries_CustomerDebtReceipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [CustomerDebtReceipts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerReceivableEntries_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerReceivableEntries_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerReceivableEntries_SalesReturns_SalesReturnId] FOREIGN KEY ([SalesReturnId]) REFERENCES [SalesReturns] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerReceivableEntries_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE INDEX [IX_CustomerDebtReceipts_CustomerId] ON [CustomerDebtReceipts] ([CustomerId]);
GO

CREATE INDEX [IX_CustomerDebtReceipts_POSShiftId] ON [CustomerDebtReceipts] ([POSShiftId]);
GO

CREATE INDEX [IX_CustomerDebtReceipts_StoreBankAccountId] ON [CustomerDebtReceipts] ([StoreBankAccountId]);
GO

CREATE UNIQUE INDEX [IX_CustomerDebtReceipts_StoreId_ClientRequestId] ON [CustomerDebtReceipts] ([StoreId], [ClientRequestId]);
GO

CREATE INDEX [IX_CustomerDebtReceipts_StoreId_CustomerId_Id] ON [CustomerDebtReceipts] ([StoreId], [CustomerId], [Id]);
GO

CREATE INDEX [IX_CustomerReceivableEntries_CustomerId] ON [CustomerReceivableEntries] ([CustomerId]);
GO

CREATE INDEX [IX_CustomerReceivableEntries_OrderId] ON [CustomerReceivableEntries] ([OrderId]);
GO

CREATE INDEX [IX_CustomerReceivableEntries_ReceiptId] ON [CustomerReceivableEntries] ([ReceiptId]);
GO

CREATE INDEX [IX_CustomerReceivableEntries_SalesReturnId] ON [CustomerReceivableEntries] ([SalesReturnId]);
GO

CREATE INDEX [IX_CustomerReceivableEntries_StoreId_CustomerId_Id] ON [CustomerReceivableEntries] ([StoreId], [CustomerId], [Id]);
GO

CREATE UNIQUE INDEX [IX_CustomerReceivableEntries_StoreId_OrderId_Kind] ON [CustomerReceivableEntries] ([StoreId], [OrderId], [Kind]) WHERE [Kind] IN ('Sale','Void');
GO

CREATE UNIQUE INDEX [IX_CustomerReceivableEntries_StoreId_OrderId_ReceiptId] ON [CustomerReceivableEntries] ([StoreId], [OrderId], [ReceiptId]) WHERE [ReceiptId] IS NOT NULL;
GO

CREATE UNIQUE INDEX [IX_CustomerReceivableEntries_StoreId_SalesReturnId] ON [CustomerReceivableEntries] ([StoreId], [SalesReturnId]) WHERE [SalesReturnId] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260919095814_AddCustomerReceivables', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [Orders] ADD [CustomerDepositId] int NULL;
GO

ALTER TABLE [Orders] ADD [DepositAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

CREATE TABLE [CustomerDeposits] (
    [Id] int NOT NULL IDENTITY,
    [CustomerId] int NOT NULL,
    [Purpose] nvarchar(500) NOT NULL,
    [ExpectedDeliveryDate] datetime2 NULL,
    [Balance] decimal(18,2) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_CustomerDeposits] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustomerDeposits_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerDeposits_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE TABLE [CustomerDepositEntries] (
    [Id] int NOT NULL IDENTITY,
    [CustomerDepositId] int NOT NULL,
    [OrderId] int NULL,
    [POSShiftId] int NOT NULL,
    [Kind] nvarchar(20) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Method] int NULL,
    [StoreBankAccountId] int NULL,
    [Reference] nvarchar(100) NULL,
    [Note] nvarchar(500) NULL,
    [ClientRequestId] uniqueidentifier NULL,
    [RequestJson] nvarchar(max) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] int NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [UpdatedBy] int NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedBy] int NULL,
    [RowVersion] rowversion NOT NULL,
    [StoreId] int NOT NULL,
    CONSTRAINT [PK_CustomerDepositEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustomerDepositEntries_CustomerDeposits_CustomerDepositId] FOREIGN KEY ([CustomerDepositId]) REFERENCES [CustomerDeposits] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerDepositEntries_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerDepositEntries_POSShifts_POSShiftId] FOREIGN KEY ([POSShiftId]) REFERENCES [POSShifts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerDepositEntries_StoreBankAccounts_StoreBankAccountId] FOREIGN KEY ([StoreBankAccountId]) REFERENCES [StoreBankAccounts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerDepositEntries_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id])
);
GO

CREATE INDEX [IX_Orders_CustomerDepositId] ON [Orders] ([CustomerDepositId]);
GO

CREATE INDEX [IX_CustomerDepositEntries_CustomerDepositId] ON [CustomerDepositEntries] ([CustomerDepositId]);
GO

CREATE INDEX [IX_CustomerDepositEntries_OrderId] ON [CustomerDepositEntries] ([OrderId]);
GO

CREATE INDEX [IX_CustomerDepositEntries_POSShiftId] ON [CustomerDepositEntries] ([POSShiftId]);
GO

CREATE INDEX [IX_CustomerDepositEntries_StoreBankAccountId] ON [CustomerDepositEntries] ([StoreBankAccountId]);
GO

CREATE UNIQUE INDEX [IX_CustomerDepositEntries_StoreId_ClientRequestId] ON [CustomerDepositEntries] ([StoreId], [ClientRequestId]) WHERE [ClientRequestId] IS NOT NULL;
GO

CREATE INDEX [IX_CustomerDepositEntries_StoreId_CustomerDepositId_Id] ON [CustomerDepositEntries] ([StoreId], [CustomerDepositId], [Id]);
GO

CREATE UNIQUE INDEX [IX_CustomerDepositEntries_StoreId_OrderId_Kind] ON [CustomerDepositEntries] ([StoreId], [OrderId], [Kind]) WHERE [OrderId] IS NOT NULL;
GO

CREATE INDEX [IX_CustomerDeposits_CustomerId] ON [CustomerDeposits] ([CustomerId]);
GO

CREATE INDEX [IX_CustomerDeposits_StoreId_CustomerId] ON [CustomerDeposits] ([StoreId], [CustomerId]);
GO

ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_CustomerDeposits_CustomerDepositId] FOREIGN KEY ([CustomerDepositId]) REFERENCES [CustomerDeposits] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260919111155_AddCustomerDeposits', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

DROP INDEX [IX_CustomerDepositEntries_StoreId_OrderId_Kind] ON [CustomerDepositEntries];
GO

ALTER TABLE [SalesReturns] ADD [DepositRestoredTotal] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [CustomerDepositEntries] ADD [SalesReturnId] int NULL;
GO

CREATE INDEX [IX_CustomerDepositEntries_SalesReturnId] ON [CustomerDepositEntries] ([SalesReturnId]);
GO

CREATE UNIQUE INDEX [IX_CustomerDepositEntries_StoreId_OrderId_Kind] ON [CustomerDepositEntries] ([StoreId], [OrderId], [Kind]) WHERE [OrderId] IS NOT NULL AND [Kind] IN ('Apply','Void');
GO

CREATE UNIQUE INDEX [IX_CustomerDepositEntries_StoreId_SalesReturnId] ON [CustomerDepositEntries] ([StoreId], [SalesReturnId]) WHERE [SalesReturnId] IS NOT NULL;
GO

ALTER TABLE [CustomerDepositEntries] ADD CONSTRAINT [FK_CustomerDepositEntries_SalesReturns_SalesReturnId] FOREIGN KEY ([SalesReturnId]) REFERENCES [SalesReturns] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260919111529_AddDepositReturnRestoration', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [PurchaseOrders] DROP CONSTRAINT [FK_PurchaseOrders_Suppliers_SupplierId];
GO

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PurchaseOrders]') AND [c].[name] = N'SupplierId');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [PurchaseOrders] DROP CONSTRAINT [' + @var3 + '];');
ALTER TABLE [PurchaseOrders] ALTER COLUMN [SupplierId] int NULL;
GO

ALTER TABLE [PurchaseOrders] ADD CONSTRAINT [FK_PurchaseOrders_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260919173000_MakePurchaseOrderSupplierOptional', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

DROP INDEX [UX_PurchaseOrderLines_Store_Order_ProductVariant] ON [PurchaseOrderLines];
GO

CREATE UNIQUE INDEX [UX_PurchaseOrderLines_Store_Order_ProductUnitConversion] ON [PurchaseOrderLines] ([StoreId], [PurchaseOrderId], [ProductUnitConversionId]) WHERE [IsDeleted] = 0 AND [ProductUnitConversionId] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260919174500_AllowPurchaseOrderVariantMultipleUnits', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE INDEX [IX_InventoryTransactions_LedgerTimeline]
ON [dbo].[InventoryTransactions]
    ([StoreId], [OccurredAtUtc] DESC, [Id] DESC)
INCLUDE ([QuantityChange], [AfterQty])
WHERE [IsDeleted] = 0;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260920093000_OptimizeInventoryLedgerTimeline', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [InvoiceInputStockSupplementalMovements] (
    [Id] int NOT NULL IDENTITY,
    [WarehouseId] int NOT NULL,
    [ProductVariantId] int NOT NULL,
    [EffectiveAtUtc] datetime2 NOT NULL,
    [QuantityChange] decimal(18,4) NOT NULL,
    [MovementType] int NOT NULL,
    [LegacySourceKey] nvarchar(200) NOT NULL,
    [SourcePeriod] nvarchar(50) NULL,
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
    CONSTRAINT [PK_InvoiceInputStockSupplementalMovements] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_InvoiceInputStockSupplementalMovements_ProductVariant_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariant] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InvoiceInputStockSupplementalMovements_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]),
    CONSTRAINT [FK_InvoiceInputStockSupplementalMovements_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_InvoiceInputStockSupplementalMovements_ProductVariantId] ON [InvoiceInputStockSupplementalMovements] ([ProductVariantId]);
GO

CREATE INDEX [IX_InvoiceInputStockSupplementalMovements_WarehouseId] ON [InvoiceInputStockSupplementalMovements] ([WarehouseId]);
GO

CREATE INDEX [IX_InvoiceInputStockSupplementalMovements_Store_Warehouse_Variant_EffectiveAt] ON [InvoiceInputStockSupplementalMovements] ([StoreId], [WarehouseId], [ProductVariantId], [EffectiveAtUtc]);
GO

CREATE UNIQUE INDEX [UX_InvoiceInputStockSupplementalMovements_Store_LegacySourceKey] ON [InvoiceInputStockSupplementalMovements] ([StoreId], [LegacySourceKey]) WHERE [StoreId] IS NOT NULL AND [LegacySourceKey] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260921100000_AddInvoiceInputStockSupplementalMovements', N'8.0.29');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [InvoiceInputStockSupplementalMovements] ADD [LegacyOrderId] bigint NULL;
GO

ALTER TABLE [InvoiceInputStockSupplementalMovements] ADD [LegacyInvoiceNumber] nvarchar(200) NULL;
GO

ALTER TABLE [InvoiceInputStockSupplementalMovements] ADD [LegacyInvoiceSymbol] nvarchar(200) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260923140000_AddInvoiceStockLegacyDocumentReferences', N'8.0.29');
GO

COMMIT;
GO

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

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoiceHeads]') AND [c].[name] = N'OrderId');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [InvoiceHeads] DROP CONSTRAINT [' + @var4 + '];');
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

BEGIN TRANSACTION;
GO

CREATE TABLE dbo.LegacyReturnArchives (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LegacyReturnArchives PRIMARY KEY,
    StoreId int NOT NULL,
    LegacyOrderId bigint NOT NULL,
    OccurredAtUtc datetime2(7) NULL,
    LegacyCustomerId bigint NULL,
    CustomerName nvarchar(400) NULL,
    LegacyUserId bigint NULL,
    EmployeeName nvarchar(400) NULL,
    SourceTotal decimal(18,2) NULL,
    SourcePaymentFlag bit NULL,
    HeaderJson nvarchar(max) NOT NULL,
    DetailsJson nvarchar(max) NOT NULL,
    ImportedAtUtc datetime2(7) NOT NULL,
    CONSTRAINT FK_LegacyReturnArchives_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id),
    CONSTRAINT CK_LegacyReturnArchives_Json CHECK(ISJSON(HeaderJson)=1 AND ISJSON(DetailsJson)=1),
    CONSTRAINT UQ_LegacyReturnArchives_Source UNIQUE(StoreId,LegacyOrderId)
);
CREATE INDEX IX_LegacyReturnArchives_Date ON dbo.LegacyReturnArchives(StoreId,OccurredAtUtc DESC,LegacyOrderId DESC);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260923180000_AddLegacyReturnArchive', N'8.0.29');
GO

COMMIT;
GO
