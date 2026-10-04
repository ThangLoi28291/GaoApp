/*
  Reviewed catalogue price repair, 2026-10-04.
  Source DataGaoStore -> GaoAppDb, StoreId=1.
  Exactly 164 candidates from the server's 165-row price audit.
  Invoice-discount 384155 / chietkhauthuongmai is excluded.

  Run the COMPLETE file in a new SSMS query on WIN-HU6RO2EMIJF\SQLEXPRESS.
  First @Apply=0: preview only. Then @Apply=1: atomic update + verify + commit.
  Source prices and target mapping must still match the reviewed audit.
  Target price fields may be their reviewed before value or already the desired value.
  Any other price change stops the repair. No target migration hash is rewritten.
  Only the reviewed catalogue price fields and update metadata are written.
*/
USE [GaoAppDb];
SET NOCOUNT ON;

DECLARE @Apply bit = 0; -- 0 = PREVIEW; 1 = APPLY AND VERIFY
DECLARE @StoreId int = 1;
DECLARE @StartedTransaction bit = 0;
DECLARE @ChangedAtUtc datetime2(7) = SYSUTCDATETIME();
DECLARE @XactAbortWasOn bit = CASE WHEN (@@OPTIONS & 16384)=16384 THEN 1 ELSE 0 END;

IF @@TRANCOUNT <> 0
    THROW 55280, N'Hãy chạy toàn bộ file trong cửa sổ query mới, không có transaction đang mở.', 1;
IF CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) COLLATE Latin1_General_100_CI_AS
    <> N'WIN-HU6RO2EMIJF\SQLEXPRESS'
    THROW 55281, N'Sai SQL Server; file này chỉ dành cho WIN-HU6RO2EMIJF\SQLEXPRESS.', 1;
IF DB_ID(N'DataGaoStore') IS NULL
    THROW 55282, N'Không tìm thấy nguồn DataGaoStore.', 1;

SET XACT_ABORT ON;
SET LOCK_TIMEOUT 2000;

BEGIN TRY
    DROP TABLE IF EXISTS #ReviewedPrices, #SourceNow, #BaseCounts, #TargetNow,
        #PriceChanges, #WrittenRows, #AppliedChanges;
    CREATE TABLE #ReviewedPrices
    (
        ProductId int NOT NULL PRIMARY KEY,
        SourceCode nvarchar(400) NOT NULL,
        ExpectedBaseUnit nvarchar(400) NOT NULL,
        BaseConversionId int NOT NULL UNIQUE,
        OldProductRetail decimal(18,2) NOT NULL,
        OldVariantRetail decimal(18,2) NULL,
        OldVariantWholesale decimal(18,2) NULL,
        OldBaseRetail decimal(18,2) NULL,
        OldBaseWholesale decimal(18,2) NULL,
        NewRetail decimal(18,2) NOT NULL,
        NewWholesale decimal(18,2) NULL
    );
    INSERT #ReviewedPrices VALUES
-- REVIEWED_VALUES_PLACEHOLDER

    IF (SELECT COUNT_BIG(*) FROM #ReviewedPrices) <> 164
        OR EXISTS (SELECT 1 FROM #ReviewedPrices WHERE ProductId=384155
            OR SourceCode COLLATE Latin1_General_100_CI_AI IN
                (N'chietkhauthuongmai',N'chietkhauhoadon')
            OR NewRetail < 0 OR NewWholesale < 0
            OR EXISTS (SELECT OldVariantRetail EXCEPT SELECT NewRetail))
        THROW 55283, N'Danh sách đã duyệt không hợp lệ hoặc có mã chiết khấu.', 1;

    IF @Apply=1
    BEGIN
        BEGIN TRANSACTION;
        SET @StartedTransaction=1;
        DECLARE @LockResult int;
        EXEC @LockResult=sys.sp_getapplock
            @Resource=N'GSTORE-BASE-PRICE-REPAIR-20261004',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=2000;
        IF @LockResult < 0
            THROW 55284, N'Đang có lần sửa giá khác chạy; chưa thay đổi dữ liệu.', 1;
    END;

    -- Source rows are read only; shared locks remain until commit in APPLY mode.
    SELECT r.ProductId, s.Id AS SourceId,
        LTRIM(RTRIM(CONVERT(nvarchar(400),s.Code))) AS SourceCode,
        CONVERT(nvarchar(400),s.Name) AS SourceName,
        CONVERT(decimal(18,2),s.Price) AS RetailPrice,
        CONVERT(decimal(18,2),s.WholesalePrice) AS WholesalePrice
    INTO #SourceNow
    FROM #ReviewedPrices r
    LEFT JOIN [DataGaoStore].dbo.ProductDetail s WITH(HOLDLOCK,ROWLOCK) ON s.Id=r.ProductId
    OPTION(MAXDOP 1);

    IF EXISTS (SELECT 1 FROM #ReviewedPrices r JOIN #SourceNow s ON s.ProductId=r.ProductId
        WHERE s.SourceId IS NULL
        OR EXISTS (SELECT s.SourceCode COLLATE DATABASE_DEFAULT
                   EXCEPT SELECT r.SourceCode COLLATE DATABASE_DEFAULT)
        OR EXISTS (SELECT s.RetailPrice,s.WholesalePrice EXCEPT SELECT r.NewRetail,r.NewWholesale))
        THROW 55285, N'Giá hoặc mã nguồn đã đổi từ bản đối chiếu; dừng và đối chiếu lại.', 1;

    -- Lock only the reviewed base-conversion ranges, not all catalogue tables.
    SELECT r.ProductId, COUNT_BIG(c.Id) AS BaseCount, MAX(c.Id) AS BaseId
    INTO #BaseCounts
    FROM #ReviewedPrices r
    LEFT JOIN dbo.ProductUnitConversion c WITH(UPDLOCK,HOLDLOCK,ROWLOCK)
        ON c.StoreId=@StoreId AND c.ProductVariantId=r.ProductId
        AND c.IsBaseUnit=1 AND c.IsDeleted=0
    GROUP BY r.ProductId
    OPTION(MAXDOP 1);

    SELECT r.ProductId, p.Id AS TargetProductId, v.Id AS TargetVariantId,
        v.ProductId AS VariantProductId, v.Sku,
        p.IsDeleted AS ProductIsDeleted, v.IsDeleted AS VariantIsDeleted,
        c.Id AS BaseId, c.ProductVariantId AS BaseVariantId,
        c.Factor, c.IsBaseUnit, c.IsDeleted AS BaseIsDeleted, c.IsActive AS BaseIsActive,
        c.UnitId AS ConversionUnitId, p.BaseUnitId AS ProductUnitId,
        u.Id AS UnitId, u.Name AS UnitName, u.IsDeleted AS UnitIsDeleted,
        p.BasePrice AS ProductRetail, v.Price AS VariantRetail,
        v.WholesalePrice AS VariantWholesale,
        c.Price AS BaseRetail, c.WholesalePrice AS BaseWholesale
    INTO #TargetNow
    FROM #ReviewedPrices r
    LEFT JOIN dbo.Products p WITH(UPDLOCK,HOLDLOCK,ROWLOCK)
        ON p.Id=r.ProductId AND p.StoreId=@StoreId
    LEFT JOIN dbo.ProductVariant v WITH(UPDLOCK,HOLDLOCK,ROWLOCK)
        ON v.Id=r.ProductId AND v.StoreId=@StoreId
    LEFT JOIN dbo.ProductUnitConversion c WITH(UPDLOCK,HOLDLOCK,ROWLOCK)
        ON c.Id=r.BaseConversionId AND c.StoreId=@StoreId
    LEFT JOIN dbo.Unit u WITH(HOLDLOCK,ROWLOCK) ON u.Id=c.UnitId AND u.StoreId=@StoreId
    OPTION(MAXDOP 1);

    -- Alias is deliberately not a mapping gate: the reviewed dot/hyphen aliases are valid.
    IF EXISTS (SELECT 1 FROM #ReviewedPrices r
        JOIN #TargetNow t ON t.ProductId=r.ProductId
        JOIN #BaseCounts b ON b.ProductId=r.ProductId
        WHERE t.TargetProductId IS NULL OR t.TargetVariantId IS NULL
            OR t.ProductIsDeleted<>0 OR t.VariantIsDeleted<>0
            OR t.VariantProductId<>r.ProductId
            OR EXISTS (SELECT t.Sku COLLATE DATABASE_DEFAULT
                       EXCEPT SELECT r.SourceCode COLLATE DATABASE_DEFAULT)
            OR b.BaseCount<>1 OR b.BaseId<>r.BaseConversionId
            OR t.BaseId IS NULL OR t.BaseVariantId<>r.ProductId
            OR t.Factor<>1 OR t.IsBaseUnit<>1 OR t.BaseIsDeleted<>0 OR t.BaseIsActive<>1
            OR t.ConversionUnitId<>t.ProductUnitId OR t.UnitId IS NULL OR t.UnitIsDeleted<>0
            OR EXISTS (SELECT t.UnitName COLLATE DATABASE_DEFAULT
                       EXCEPT SELECT r.ExpectedBaseUnit COLLATE DATABASE_DEFAULT))
        THROW 55286, N'Liên kết sản phẩm hoặc đơn vị gốc đã đổi; dừng để kiểm tra.', 1;

    -- Accept the reviewed BEFORE value or the desired value, supporting an identical rerun.
    IF EXISTS (SELECT 1 FROM #ReviewedPrices r JOIN #TargetNow t ON t.ProductId=r.ProductId
        WHERE (EXISTS(SELECT t.ProductRetail EXCEPT SELECT r.OldProductRetail)
           AND EXISTS(SELECT t.ProductRetail EXCEPT SELECT r.NewRetail))
        OR (EXISTS(SELECT t.VariantRetail EXCEPT SELECT r.OldVariantRetail)
           AND EXISTS(SELECT t.VariantRetail EXCEPT SELECT r.NewRetail))
        OR (EXISTS(SELECT t.VariantWholesale EXCEPT SELECT r.OldVariantWholesale)
           AND EXISTS(SELECT t.VariantWholesale EXCEPT SELECT r.NewWholesale))
        OR (EXISTS(SELECT t.BaseRetail EXCEPT SELECT r.OldBaseRetail)
           AND EXISTS(SELECT t.BaseRetail EXCEPT SELECT r.NewRetail))
        OR (EXISTS(SELECT t.BaseWholesale EXCEPT SELECT r.OldBaseWholesale)
           AND EXISTS(SELECT t.BaseWholesale EXCEPT SELECT r.NewWholesale)))
        THROW 55287, N'Có giá đích mới ngoài bản đã duyệt; chưa ghi đè, hãy đối chiếu lại.', 1;

    SELECT r.ProductId, r.SourceCode, s.SourceName,
        x.TableName, x.EntityId, x.ColumnName, x.BeforePrice, x.AfterPrice
    INTO #PriceChanges
    FROM #ReviewedPrices r JOIN #TargetNow t ON t.ProductId=r.ProductId
    JOIN #SourceNow s ON s.ProductId=r.ProductId
    CROSS APPLY(VALUES
        (N'Products',t.TargetProductId,N'BasePrice',t.ProductRetail,r.NewRetail),
        (N'ProductVariant',t.TargetVariantId,N'Price',t.VariantRetail,r.NewRetail),
        (N'ProductVariant',t.TargetVariantId,N'WholesalePrice',t.VariantWholesale,r.NewWholesale),
        (N'ProductUnitConversion',t.BaseId,N'Price',t.BaseRetail,r.NewRetail),
        (N'ProductUnitConversion',t.BaseId,N'WholesalePrice',t.BaseWholesale,r.NewWholesale)
    )x(TableName,EntityId,ColumnName,BeforePrice,AfterPrice)
    WHERE EXISTS(SELECT x.BeforePrice EXCEPT SELECT x.AfterPrice);

    CREATE TABLE #WrittenRows
    (
        TableName nvarchar(50) NOT NULL, ProductId int NOT NULL, EntityId int NOT NULL,
        BeforeRetail decimal(18,2) NULL, AfterRetail decimal(18,2) NULL,
        BeforeWholesale decimal(18,2) NULL, AfterWholesale decimal(18,2) NULL,
        BeforeUpdatedAtUtc datetime2(7) NULL, AfterUpdatedAtUtc datetime2(7) NULL,
        BeforeUpdatedBy int NULL, AfterUpdatedBy int NULL
    );

    IF @Apply=1
    BEGIN
        SET @ChangedAtUtc=SYSUTCDATETIME();
        UPDATE p SET BasePrice=r.NewRetail, UpdatedAtUtc=@ChangedAtUtc, UpdatedBy=NULL
        OUTPUT N'Products',r.ProductId,inserted.Id,
            deleted.BasePrice,inserted.BasePrice,NULL,NULL,
            deleted.UpdatedAtUtc,inserted.UpdatedAtUtc,deleted.UpdatedBy,inserted.UpdatedBy
            INTO #WrittenRows
        FROM dbo.Products p JOIN #ReviewedPrices r ON r.ProductId=p.Id
        WHERE p.StoreId=@StoreId AND p.IsDeleted=0 AND r.ProductId<>384155
            AND EXISTS(SELECT p.BasePrice EXCEPT SELECT r.NewRetail);

        -- Variant retail was correct in all reviewed rows and is not assigned here.
        UPDATE v SET WholesalePrice=r.NewWholesale, UpdatedAtUtc=@ChangedAtUtc, UpdatedBy=NULL
        OUTPUT N'ProductVariant',r.ProductId,inserted.Id,
            deleted.Price,inserted.Price,deleted.WholesalePrice,inserted.WholesalePrice,
            deleted.UpdatedAtUtc,inserted.UpdatedAtUtc,deleted.UpdatedBy,inserted.UpdatedBy
            INTO #WrittenRows
        FROM dbo.ProductVariant v JOIN #ReviewedPrices r ON r.ProductId=v.Id
        WHERE v.StoreId=@StoreId AND v.IsDeleted=0 AND r.ProductId<>384155
            AND EXISTS(SELECT v.WholesalePrice EXCEPT SELECT r.NewWholesale);

        UPDATE c SET Price=r.NewRetail, WholesalePrice=r.NewWholesale,
            UpdatedAtUtc=@ChangedAtUtc, UpdatedBy=NULL
        OUTPUT N'ProductUnitConversion',r.ProductId,inserted.Id,
            deleted.Price,inserted.Price,deleted.WholesalePrice,inserted.WholesalePrice,
            deleted.UpdatedAtUtc,inserted.UpdatedAtUtc,deleted.UpdatedBy,inserted.UpdatedBy
            INTO #WrittenRows
        FROM dbo.ProductUnitConversion c JOIN #ReviewedPrices r ON r.BaseConversionId=c.Id
        WHERE c.StoreId=@StoreId AND c.IsBaseUnit=1 AND c.IsDeleted=0
            AND c.ProductVariantId=r.ProductId AND r.ProductId<>384155
            AND EXISTS(SELECT c.Price,c.WholesalePrice EXCEPT SELECT r.NewRetail,r.NewWholesale);

        -- Verify every actual changed price field equals the reviewed plan exactly.
        SELECT w.TableName,w.EntityId,x.ColumnName,x.BeforePrice,x.AfterPrice
        INTO #AppliedChanges
        FROM #WrittenRows w CROSS APPLY(VALUES
            (CASE WHEN w.TableName=N'Products' THEN N'BasePrice' ELSE N'Price' END,
                w.BeforeRetail,w.AfterRetail),
            (N'WholesalePrice',w.BeforeWholesale,w.AfterWholesale)
        )x(ColumnName,BeforePrice,AfterPrice)
        WHERE EXISTS(SELECT x.BeforePrice EXCEPT SELECT x.AfterPrice);
        IF EXISTS(SELECT TableName,EntityId,ColumnName,BeforePrice,AfterPrice FROM #AppliedChanges
                  EXCEPT SELECT TableName,EntityId,ColumnName,BeforePrice,AfterPrice FROM #PriceChanges)
            OR EXISTS(SELECT TableName,EntityId,ColumnName,BeforePrice,AfterPrice FROM #PriceChanges
                  EXCEPT SELECT TableName,EntityId,ColumnName,BeforePrice,AfterPrice FROM #AppliedChanges)
            THROW 55288, N'Số hoặc giá trị cột sửa khác kế hoạch; rollback toàn bộ.', 1;

        -- Re-read the real target after UPDATE, including any trigger effects.
        IF EXISTS(SELECT 1 FROM #ReviewedPrices r
            LEFT JOIN dbo.Products p ON p.Id=r.ProductId AND p.StoreId=@StoreId AND p.IsDeleted=0
            LEFT JOIN dbo.ProductVariant v ON v.Id=r.ProductId AND v.StoreId=@StoreId AND v.IsDeleted=0
            LEFT JOIN dbo.ProductUnitConversion c ON c.Id=r.BaseConversionId
                AND c.StoreId=@StoreId AND c.IsDeleted=0 AND c.IsBaseUnit=1
            LEFT JOIN dbo.Unit u ON u.Id=c.UnitId AND u.StoreId=@StoreId
            WHERE p.Id IS NULL OR v.Id IS NULL OR c.Id IS NULL
                OR v.ProductId<>r.ProductId OR c.ProductVariantId<>r.ProductId
                OR EXISTS(SELECT v.Sku COLLATE DATABASE_DEFAULT
                          EXCEPT SELECT r.SourceCode COLLATE DATABASE_DEFAULT)
                OR c.Factor<>1 OR c.IsActive<>1 OR c.UnitId<>p.BaseUnitId
                OR u.Id IS NULL OR u.IsDeleted<>0
                OR EXISTS(SELECT u.Name COLLATE DATABASE_DEFAULT
                          EXCEPT SELECT r.ExpectedBaseUnit COLLATE DATABASE_DEFAULT)
                OR (SELECT COUNT_BIG(*) FROM dbo.ProductUnitConversion b
                    WHERE b.StoreId=@StoreId AND b.ProductVariantId=r.ProductId
                        AND b.IsDeleted=0 AND b.IsBaseUnit=1)<>1
                OR EXISTS(SELECT p.BasePrice,v.Price,v.WholesalePrice,c.Price,c.WholesalePrice
                          EXCEPT SELECT r.NewRetail,r.NewRetail,r.NewWholesale,r.NewRetail,r.NewWholesale))
            THROW 55289, N'Giá hoặc liên kết sau khi sửa không khớp; rollback toàn bộ.', 1;

        COMMIT TRANSACTION;
        SET @StartedTransaction=0;
    END;

    -- Emit evidence only after COMMIT, or clearly label the preview.
    SELECT CASE WHEN @Apply=1 THEN N'PRICE_REPAIR_COMMIT_PASS' ELSE N'PRICE_REPAIR_PREVIEW_ONLY' END AS Report,
        CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) AS ServerName,
        ORIGINAL_LOGIN() AS ExecutedBy,
        (SELECT COUNT_BIG(*) FROM #ReviewedPrices) AS ReviewedProducts,
        (SELECT COUNT_BIG(DISTINCT ProductId) FROM #PriceChanges) AS ProductsWithChanges,
        (SELECT COUNT_BIG(*) FROM #PriceChanges WHERE TableName=N'Products') AS ProductRetailFields,
        (SELECT COUNT_BIG(*) FROM #PriceChanges WHERE TableName=N'ProductVariant' AND ColumnName=N'Price') AS VariantRetailFields,
        (SELECT COUNT_BIG(*) FROM #PriceChanges WHERE TableName=N'ProductVariant' AND ColumnName=N'WholesalePrice') AS VariantWholesaleFields,
        (SELECT COUNT_BIG(*) FROM #PriceChanges WHERE TableName=N'ProductUnitConversion' AND ColumnName=N'Price') AS BaseRetailFields,
        (SELECT COUNT_BIG(*) FROM #PriceChanges WHERE TableName=N'ProductUnitConversion' AND ColumnName=N'WholesalePrice') AS BaseWholesaleFields,
        CASE WHEN @Apply=1 THEN (SELECT COUNT_BIG(*) FROM #WrittenRows) ELSE 0 END AS UpdatedRows,
        CONVERT(bit,1) AS InvoiceDiscountExcluded;
    SELECT N'REVIEWED_PRICE_CHANGES' AS Report,* FROM #PriceChanges ORDER BY SourceCode,TableName,ColumnName;
    IF @Apply=1
        SELECT N'COMMITTED_BEFORE_AFTER' AS Report,* FROM #WrittenRows ORDER BY ProductId,TableName;

    SET LOCK_TIMEOUT -1;
    IF @XactAbortWasOn=0 SET XACT_ABORT OFF;
    DROP TABLE IF EXISTS #ReviewedPrices, #SourceNow, #BaseCounts, #TargetNow,
        #PriceChanges, #WrittenRows, #AppliedChanges;
END TRY
BEGIN CATCH
    IF @StartedTransaction=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
    SET LOCK_TIMEOUT -1;
    IF @XactAbortWasOn=0 SET XACT_ABORT OFF;
    THROW;
END CATCH;
