-- Reviewed initial import. Run only through Invoke-Migration.ps1.
IF @@TRANCOUNT<>1 OR ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'GSTORE_INITIAL_IMPORT')),0)<>1
    THROW 55100,'Run through the transactional package runner.',1;
SET NOCOUNT ON;
SET XACT_ABORT ON;



DECLARE @StoreId int = 1;
DECLARE @LegacyFallbackCustomerId bigint = 8;
DECLARE @LedgerTypeImportOldBalance int = 1;
DECLARE @VoucherStatusUsed int = 2;
DECLARE @LegacyVoucherValue decimal(18,2) = 30000.00;
DECLARE @LegacyVoucherRequiredAmount decimal(18,2) = 1500000.00;
DECLARE @ImportUtc datetime2(7) = SYSUTCDATETIME();


IF EXISTS(SELECT 1 FROM [__SOURCE__].dbo.[User] u WHERE GroupID IN('MEMBER','WHOLESALE') AND
 (LEN(COALESCE(NULLIF(LTRIM(RTRIM(Name)),N''),LTRIM(RTRIM(Code))))>200
 OR LEN(LTRIM(RTRIM(Address)))>300 OR LEN(LTRIM(RTRIM(Code)))>50
 OR LEN(LTRIM(RTRIM(Email)))>100 OR LEN(LTRIM(RTRIM(MST)))>50))
 THROW 55101,'Customer source text exceeds target lengths; review before import.',1;
IF EXISTS(SELECT 1 FROM [__SOURCE__].dbo.UserPoint WHERE LEN(Description)>1000)
 THROW 55102,'Voucher description exceeds target length; review before import.',1;

DROP TABLE IF EXISTS #CustomerStage;
DROP TABLE IF EXISTS #LedgerStage;
DROP TABLE IF EXISTS #VoucherStage;

CREATE TABLE #CustomerStage
(
    TargetCustomerId int NOT NULL PRIMARY KEY,
    OldCustomerId bigint NOT NULL UNIQUE,
    TargetName nvarchar(200) NOT NULL,
    TargetPhone nvarchar(30) NULL,
    TargetAddress nvarchar(300) NULL,
    TargetCode nvarchar(50) NOT NULL,
    TargetEmail nvarchar(100) NULL,
    TargetTaxCode nvarchar(50) NULL,
    CustomerGroup nvarchar(30) NOT NULL,
    PriceTier nvarchar(30) NOT NULL,
    IsActive bit NOT NULL,
    ImportedRewardAmount decimal(18,2) NOT NULL,
    CreatedAtUtc datetime2(7) NOT NULL,
    UpdatedAtUtc datetime2(7) NULL
);

;WITH L AS
(
    SELECT
        u.ID AS OldCustomerId,
        ROW_NUMBER() OVER (ORDER BY u.ID) AS TargetCustomerId,
        CASE
            WHEN NULLIF(LTRIM(RTRIM(u.Name)),'') IS NOT NULL THEN LTRIM(RTRIM(u.Name))
            ELSE LTRIM(RTRIM(u.Code))
        END AS TargetName,
        CASE
            WHEN NULLIF(LTRIM(RTRIM(u.Phone)),'') IS NULL THEN NULL
            WHEN LEN(LTRIM(RTRIM(u.Phone))) > 30 THEN NULL
            ELSE LTRIM(RTRIM(u.Phone))
        END AS TargetPhone,
        NULLIF(LTRIM(RTRIM(u.Address)),'') AS TargetAddress,
        NULLIF(LTRIM(RTRIM(u.Code)),'') AS TargetCode,
        NULLIF(LTRIM(RTRIM(u.Email)),'') AS TargetEmail,
        NULLIF(LTRIM(RTRIM(u.MST)),'') AS TargetTaxCode,
        u.GroupID AS CustomerGroup,
        CASE WHEN u.GroupID='WHOLESALE' THEN 'WHOLESALE' ELSE 'RETAIL' END AS PriceTier,
        CONVERT(bit, CASE WHEN u.Status=1 THEN 1 ELSE 0 END) AS IsActive,
        CONVERT(decimal(18,2),
            CASE WHEN ISNULL(u.AccumulativePoint,0) > 0 THEN u.AccumulativePoint ELSE 0 END
        ) AS ImportedRewardAmount,
        DATEADD(HOUR,-7,CONVERT(datetime2(7),u.CreatedDate)) AS CreatedAtUtc,
        CASE WHEN u.ModifiedDate IS NULL THEN NULL
             ELSE DATEADD(HOUR,-7,CONVERT(datetime2(7),u.ModifiedDate)) END AS UpdatedAtUtc
    FROM [__SOURCE__].dbo.[User] u
    WHERE u.GroupID IN ('MEMBER','WHOLESALE')
)
INSERT INTO #CustomerStage
SELECT
    CONVERT(int,TargetCustomerId),
    OldCustomerId,
    CONVERT(nvarchar(200),TargetName),
    CONVERT(nvarchar(30),TargetPhone),
    CONVERT(nvarchar(300),TargetAddress),
    CONVERT(nvarchar(50),TargetCode),
    CONVERT(nvarchar(100),TargetEmail),
    CONVERT(nvarchar(50),TargetTaxCode),
    CONVERT(nvarchar(30),CustomerGroup),
    CONVERT(nvarchar(30),PriceTier),
    IsActive,
    ImportedRewardAmount,
    CreatedAtUtc,
    UpdatedAtUtc
FROM L;

CREATE TABLE #LedgerStage
(
    TargetLedgerId int NOT NULL PRIMARY KEY,
    OldCustomerId bigint NOT NULL UNIQUE,
    Amount decimal(18,2) NOT NULL,
    ReferenceCode nvarchar(200) NOT NULL UNIQUE,
    Description nvarchar(1000) NULL
);

;WITH P AS
(
    SELECT
        c.OldCustomerId,
        c.ImportedRewardAmount,
        ROW_NUMBER() OVER (ORDER BY c.OldCustomerId) AS TargetLedgerId
    FROM #CustomerStage c
    WHERE c.ImportedRewardAmount > 0
)
INSERT INTO #LedgerStage
SELECT
    CONVERT(int,TargetLedgerId),
    OldCustomerId,
    ImportedRewardAmount,
    CONVERT(nvarchar(200),'LEGACY-BALANCE-'+CONVERT(varchar(30),OldCustomerId)),
    N'Số dư tích lũy chuyển từ DataGaoStore'
FROM P;

CREATE TABLE #VoucherStage
(
    TargetVoucherId int NOT NULL PRIMARY KEY,
    OldUserPointId bigint NOT NULL UNIQUE,
    OldCustomerId bigint NOT NULL,
    VoucherCode nvarchar(100) NOT NULL UNIQUE,
    VoucherValue decimal(18,2) NOT NULL,
    RequiredAmount decimal(18,2) NOT NULL,
    VoucherStatus int NOT NULL,
    IssuedAtUtc datetime2(7) NOT NULL,
    UsedAtUtc datetime2(7) NOT NULL,
    UsedDateWasFilled bit NOT NULL,
    Description nvarchar(1000) NULL,
    ReferenceCode nvarchar(200) NOT NULL UNIQUE
);

;WITH V AS
(
    SELECT
        up.ID AS OldUserPointId,
        up.CustomerId AS OldCustomerId,
        ROW_NUMBER() OVER (ORDER BY up.ID) AS TargetVoucherId,
        up.CreatedDate,
        up.CreatedDateUsed,
        up.Description
    FROM [__SOURCE__].dbo.UserPoint up
)
INSERT INTO #VoucherStage
SELECT
    CONVERT(int,TargetVoucherId),
    OldUserPointId,
    OldCustomerId,
    CONVERT(nvarchar(100),'LEGACY-UP-'+CONVERT(varchar(30),OldUserPointId)),
    @LegacyVoucherValue,
    @LegacyVoucherRequiredAmount,
    @VoucherStatusUsed,
    DATEADD(HOUR,-7,CONVERT(datetime2(7),CreatedDate)),
    DATEADD(HOUR,-7,CONVERT(datetime2(7),COALESCE(CreatedDateUsed,CreatedDate))),
    CONVERT(bit,CASE WHEN CreatedDateUsed IS NULL THEN 1 ELSE 0 END),
    CONVERT(nvarchar(1000),Description),
    CONVERT(nvarchar(200),'LEGACY-UP-'+CONVERT(varchar(30),OldUserPointId))
FROM V;

DECLARE @ExpectedCustomers bigint = (SELECT COUNT_BIG(*) FROM #CustomerStage);
DECLARE @ExpectedLedgers bigint = (SELECT COUNT_BIG(*) FROM #LedgerStage);
DECLARE @ExpectedVouchers bigint = (SELECT COUNT_BIG(*) FROM #VoucherStage);
DECLARE @ExpectedReward decimal(38,2) =
    (SELECT ISNULL(SUM(CONVERT(decimal(38,2),Amount)),0) FROM #LedgerStage);

-- SOURCE / STAGING BLOCKERS
IF @ExpectedCustomers <= 0
    THROW 55001, 'STOP: no Customer source rows.', 1;

IF (SELECT COUNT_BIG(*) FROM #CustomerStage WHERE OldCustomerId=@LegacyFallbackCustomerId) <> 1
    THROW 55002, 'STOP: fallback legacy customer ID 8 missing/duplicated.', 1;

IF EXISTS (
    SELECT 1 FROM #CustomerStage
    WHERE TargetName IS NULL OR LTRIM(RTRIM(TargetName))=''
       OR TargetCode IS NULL OR LTRIM(RTRIM(TargetCode))=''
       OR CreatedAtUtc IS NULL
       OR ImportedRewardAmount < 0
)
    THROW 55003, 'STOP: invalid transformed Customer staging.', 1;

IF EXISTS (
    SELECT 1
    FROM #VoucherStage v
    LEFT JOIN #CustomerStage c ON c.OldCustomerId=v.OldCustomerId
    WHERE c.OldCustomerId IS NULL
)
    THROW 55004, 'STOP: legacy voucher cannot map to Customer.', 1;

IF EXISTS (
    SELECT 1 FROM [__SOURCE__].dbo.UserPoint
    WHERE Point IS NULL OR Point<>30 OR CreatedDate IS NULL
)
    THROW 55005, 'STOP: unexpected UserPoint source data.', 1;

IF EXISTS (
    SELECT 1 FROM #VoucherStage
    WHERE IssuedAtUtc IS NULL OR UsedAtUtc IS NULL OR UsedAtUtc<IssuedAtUtc
)
    THROW 55006, 'STOP: invalid historical voucher timestamps.', 1;

-- TARGET SAFETY BLOCKERS
IF NOT EXISTS (SELECT 1 FROM dbo.Stores WHERE Id=@StoreId)
    THROW 55010, 'STOP: StoreId 1 not found.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.RewardSettings WHERE StoreId=@StoreId AND IsDeleted=0)
    THROW 55011, 'STOP: active RewardSettings not found.', 1;

IF EXISTS (SELECT 1 FROM dbo.Customers WHERE StoreId<>@StoreId)
    THROW 55012, 'STOP: Customers contains another Store.', 1;

IF EXISTS (SELECT 1 FROM dbo.CustomerRewardLedgers WHERE StoreId<>@StoreId)
    THROW 55013, 'STOP: RewardLedgers contains another Store.', 1;

IF EXISTS (SELECT 1 FROM dbo.CustomerRewardVouchers WHERE StoreId<>@StoreId)
    THROW 55014, 'STOP: RewardVouchers contains another Store.', 1;

IF EXISTS (
    SELECT 1 FROM dbo.Orders o
    JOIN dbo.Customers c ON c.Id=o.CustomerId
    WHERE c.StoreId=@StoreId
)
    THROW 55015, 'STOP: Orders currently reference Customers.', 1;

IF EXISTS (
    SELECT 1 FROM dbo.InvoiceBuyerProfiles p
    JOIN dbo.Customers c ON c.Id=p.CustomerId
    WHERE c.StoreId=@StoreId
)
    THROW 55016, 'STOP: InvoiceBuyerProfiles currently reference Customers.', 1;

IF EXISTS (
    SELECT 1 FROM dbo.OrderRewardVouchers ov
    JOIN dbo.CustomerRewardVouchers v ON v.Id=ov.VoucherId
    WHERE v.StoreId=@StoreId
)
    THROW 55017, 'STOP: OrderRewardVouchers references vouchers.', 1;

SELECT
    'IMPORT_EXPECTED' AS Section,
    @ExpectedCustomers AS Customers,
    @ExpectedLedgers AS RewardLedgers,
    @ExpectedReward AS RewardBalance,
    @ExpectedVouchers AS HistoricalVouchers;

IF @Mode='PREVIEW' RETURN;
    SET IDENTITY_INSERT dbo.Customers ON;

    INSERT INTO dbo.Customers
    (
        Id,Name,Phone,Address,Note,Code,OldCustomerId,CustomerGroup,Email,TaxCode,
        HaveDebt,IsImportedFromOldSystem,ImportedRewardAmount,IsActive,PriceTier,
        CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy,StoreId
    )
    SELECT
        TargetCustomerId,TargetName,TargetPhone,TargetAddress,NULL,TargetCode,OldCustomerId,
        CustomerGroup,TargetEmail,TargetTaxCode,
        CONVERT(bit,0),CONVERT(bit,1),ImportedRewardAmount,IsActive,PriceTier,
        CreatedAtUtc,NULL,UpdatedAtUtc,NULL,CONVERT(bit,0),NULL,NULL,@StoreId
    FROM #CustomerStage;

    SET IDENTITY_INSERT dbo.Customers OFF;

    SET IDENTITY_INSERT dbo.CustomerRewardLedgers ON;

    INSERT INTO dbo.CustomerRewardLedgers
    (
        Id,CustomerId,Type,Amount,OrderId,SalesReturnId,VoucherId,ReferenceCode,Description,
        CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy,StoreId
    )
    SELECT
        l.TargetLedgerId,c.TargetCustomerId,@LedgerTypeImportOldBalance,l.Amount,
        NULL,NULL,NULL,l.ReferenceCode,l.Description,@ImportUtc,NULL,NULL,NULL,
        CONVERT(bit,0),NULL,NULL,@StoreId
    FROM #LedgerStage l
    JOIN #CustomerStage c ON c.OldCustomerId=l.OldCustomerId;

    SET IDENTITY_INSERT dbo.CustomerRewardLedgers OFF;

    SET IDENTITY_INSERT dbo.CustomerRewardVouchers ON;

    INSERT INTO dbo.CustomerRewardVouchers
    (
        Id,CustomerId,VoucherCode,Value,RequiredAmount,Status,IssuedAtUtc,UsedAtUtc,
        UsedOrderId,Description,ReferenceCode,CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,
        IsDeleted,DeletedAtUtc,DeletedBy,StoreId
    )
    SELECT
        v.TargetVoucherId,c.TargetCustomerId,v.VoucherCode,v.VoucherValue,v.RequiredAmount,
        v.VoucherStatus,v.IssuedAtUtc,v.UsedAtUtc,NULL,v.Description,v.ReferenceCode,
        v.IssuedAtUtc,NULL,NULL,NULL,CONVERT(bit,0),NULL,NULL,@StoreId
    FROM #VoucherStage v
    JOIN #CustomerStage c ON c.OldCustomerId=v.OldCustomerId;

    SET IDENTITY_INSERT dbo.CustomerRewardVouchers OFF;

    -- IN-TRANSACTION ASSERTIONS
    IF (SELECT COUNT_BIG(*) FROM dbo.Customers WHERE StoreId=@StoreId AND IsDeleted=0) <> @ExpectedCustomers
        THROW 55030, 'FAIL: Customer count mismatch.', 1;

    IF (SELECT COUNT_BIG(DISTINCT OldCustomerId) FROM dbo.Customers WHERE StoreId=@StoreId AND IsDeleted=0) <> @ExpectedCustomers
        THROW 55031, 'FAIL: OldCustomerId coverage mismatch.', 1;

    IF (SELECT COUNT_BIG(*) FROM dbo.CustomerRewardLedgers WHERE StoreId=@StoreId AND IsDeleted=0) <> @ExpectedLedgers
        THROW 55032, 'FAIL: Reward ledger count mismatch.', 1;

    IF (
        SELECT ISNULL(SUM(CONVERT(decimal(38,2),Amount)),0)
        FROM dbo.CustomerRewardLedgers
        WHERE StoreId=@StoreId AND IsDeleted=0 AND Type=@LedgerTypeImportOldBalance
    ) <> @ExpectedReward
        THROW 55033, 'FAIL: Reward ledger balance mismatch.', 1;

    IF (SELECT COUNT_BIG(*) FROM dbo.CustomerRewardVouchers WHERE StoreId=@StoreId AND IsDeleted=0) <> @ExpectedVouchers
        THROW 55034, 'FAIL: Voucher count mismatch.', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.CustomerRewardVouchers
        WHERE StoreId=@StoreId AND IsDeleted=0
          AND (Status<>@VoucherStatusUsed OR Value<>@LegacyVoucherValue
               OR RequiredAmount<>@LegacyVoucherRequiredAmount OR UsedAtUtc IS NULL)
    )
        THROW 55035, 'FAIL: historical voucher state mismatch.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.Customers c
        OUTER APPLY (
            SELECT SUM(l.Amount) LedgerBalance
            FROM dbo.CustomerRewardLedgers l
            WHERE l.StoreId=@StoreId
              AND l.CustomerId=c.Id
              AND l.IsDeleted=0
              AND l.Type=@LedgerTypeImportOldBalance
        ) b
        WHERE c.StoreId=@StoreId
          AND c.IsDeleted=0
          AND c.ImportedRewardAmount<>ISNULL(b.LedgerBalance,0)
    )
        THROW 55036, 'FAIL: Customer reward differs from ledger.', 1;

    SELECT
        'IMPORT_BEFORE_COMMIT' AS Section,
        (SELECT COUNT_BIG(*) FROM dbo.Customers WHERE StoreId=@StoreId AND IsDeleted=0) AS Customers,
        (SELECT COUNT_BIG(*) FROM dbo.CustomerRewardLedgers WHERE StoreId=@StoreId AND IsDeleted=0) AS RewardLedgers,
        (SELECT COUNT_BIG(*) FROM dbo.CustomerRewardVouchers WHERE StoreId=@StoreId AND IsDeleted=0) AS HistoricalVouchers,
        (SELECT Id FROM dbo.Customers WHERE StoreId=@StoreId AND OldCustomerId=@LegacyFallbackCustomerId AND IsDeleted=0) AS GaoAppIdForLegacyFallback8;
