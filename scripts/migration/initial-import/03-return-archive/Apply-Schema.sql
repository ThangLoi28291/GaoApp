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
