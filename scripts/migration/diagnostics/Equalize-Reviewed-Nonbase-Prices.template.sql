/*
  GaoApp nonbase unit price equalization, 2026-10-04.
  Scope: the 1,424 conversions in the user's reviewed target-only listing.
  Rule: both prices become the larger non-NULL price; one NULL uses the other price.
  ProductId 384155 / chietkhauthuongmai / chietkhauhoadon is excluded.

  Run the ENTIRE generated file in a new SSMS query on WIN-HU6RO2EMIJF\SQLEXPRESS.
  First @Apply=0 (preview). Then @Apply=1 (transaction, update, verify, commit).
  A current price pair must match the reviewed pair or the already-equalized pair.
  Other current prices, missing records, or changed unit mappings abort the repair.
  Only nonbase ProductUnitConversion prices and update metadata are written.
*/
USE [GaoAppDb];
SET NOCOUNT ON;

DECLARE @Apply bit = 0; -- 0 = PREVIEW; 1 = APPLY AND VERIFY
DECLARE @StoreId int = 1;
DECLARE @StartedTransaction bit = 0;
DECLARE @ChangedAtUtc datetime2(7) = SYSUTCDATETIME();
DECLARE @XactAbortWasOn bit = CASE WHEN (@@OPTIONS & 16384)=16384 THEN 1 ELSE 0 END;

IF @@TRANCOUNT <> 0
    THROW 55310, N'Hãy chạy toàn bộ file trong cửa sổ query mới, không có transaction đang mở.', 1;
IF CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) COLLATE Latin1_General_100_CI_AS
    <> N'WIN-HU6RO2EMIJF\SQLEXPRESS'
    THROW 55311, N'Sai SQL Server; file này chỉ dành cho WIN-HU6RO2EMIJF\SQLEXPRESS.', 1;

SET XACT_ABORT ON;
SET LOCK_TIMEOUT 2000;

BEGIN TRY
    DROP TABLE IF EXISTS #ReviewedUnits, #UnitNow, #UnitPlan, #UnitWrites;
    CREATE TABLE #ReviewedUnits
    (
        ConversionId int NOT NULL PRIMARY KEY,
        ProductId int NOT NULL,
        ProductVariantId int NOT NULL,
        Sku nvarchar(400) NOT NULL,
        UnitName nvarchar(400) NOT NULL,
        Factor decimal(18,4) NOT NULL,
        BeforeRetail decimal(18,2) NULL,
        BeforeWholesale decimal(18,2) NULL,
        NewPrice decimal(18,2) NOT NULL
    );
-- REVIEWED_UNIT_VALUES_PLACEHOLDER

    IF (SELECT COUNT_BIG(*) FROM #ReviewedUnits) <> 1424
        OR EXISTS (SELECT 1 FROM #ReviewedUnits
            WHERE ProductId=384155
                OR LTRIM(RTRIM(Sku)) COLLATE Latin1_General_100_CI_AI
                    IN (N'chietkhauhoadon',N'chietkhauthuongmai')
                OR (BeforeRetail IS NULL AND BeforeWholesale IS NULL)
                OR EXISTS (
                    SELECT NewPrice EXCEPT SELECT
                    CASE WHEN BeforeRetail IS NULL THEN BeforeWholesale
                         WHEN BeforeWholesale IS NULL THEN BeforeRetail
                         WHEN BeforeRetail >= BeforeWholesale THEN BeforeRetail
                         ELSE BeforeWholesale END))
        THROW 55312, N'Danh sách đã duyệt hoặc quy tắc lấy giá lớn hơn không hợp lệ.', 1;

    IF @Apply=1
    BEGIN
        BEGIN TRANSACTION;
        SET @StartedTransaction=1;
        DECLARE @LockResult int;
        EXEC @LockResult=sys.sp_getapplock
            @Resource=N'GAOAPP-NONBASE-PRICE-EQUALIZE-20261004',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=2000;
        IF @LockResult < 0
            THROW 55313, N'Đang có lần đồng bộ giá khác chạy; chưa thay đổi dữ liệu.', 1;
    END;

    CREATE TABLE #UnitNow
    (
        ConversionId int NOT NULL PRIMARY KEY,
        ProductId int NOT NULL,
        ProductVariantId int NOT NULL,
        UnitId int NOT NULL,
        Sku nvarchar(400) NOT NULL,
        ProductName nvarchar(max) NULL,
        UnitName nvarchar(400) NOT NULL,
        Factor decimal(18,4) NOT NULL,
        RetailPrice decimal(18,2) NULL,
        WholesalePrice decimal(18,2) NULL,
        UnitIsActive bit NOT NULL
    );

    IF @Apply=1
    BEGIN
        INSERT #UnitNow
-- LOCKED_TARGET_SELECT_PLACEHOLDER
    END
    ELSE
    BEGIN
        INSERT #UnitNow
-- PREVIEW_TARGET_SELECT_PLACEHOLDER
    END;

    IF (SELECT COUNT_BIG(*) FROM #UnitNow) <> (SELECT COUNT_BIG(*) FROM #ReviewedUnits)
    BEGIN
        SELECT N'UNIT_MAPPING_CHANGED' AS Report,r.*
        FROM #ReviewedUnits r LEFT JOIN #UnitNow n ON n.ConversionId=r.ConversionId
        WHERE n.ConversionId IS NULL;
        THROW 55314, N'Có đơn vị thiếu hoặc đã đổi ánh xạ/trạng thái xóa. Chưa cập nhật.', 1;
    END;

    IF EXISTS (
        SELECT 1 FROM #ReviewedUnits r JOIN #UnitNow n ON n.ConversionId=r.ConversionId
        WHERE EXISTS (SELECT n.RetailPrice,n.WholesalePrice EXCEPT SELECT r.BeforeRetail,r.BeforeWholesale)
          AND EXISTS (SELECT n.RetailPrice,n.WholesalePrice EXCEPT SELECT r.NewPrice,r.NewPrice))
    BEGIN
        SELECT N'UNIT_PRICE_CHANGED_AFTER_LISTING' AS Report,
            n.ConversionId,n.ProductId,n.Sku,n.ProductName,n.UnitName,n.Factor,
            r.BeforeRetail AS ReviewedRetail,r.BeforeWholesale AS ReviewedWholesale,
            n.RetailPrice AS CurrentRetail,n.WholesalePrice AS CurrentWholesale,
            r.NewPrice AS ReviewedNewPrice
        FROM #ReviewedUnits r JOIN #UnitNow n ON n.ConversionId=r.ConversionId
        WHERE EXISTS (SELECT n.RetailPrice,n.WholesalePrice EXCEPT SELECT r.BeforeRetail,r.BeforeWholesale)
          AND EXISTS (SELECT n.RetailPrice,n.WholesalePrice EXCEPT SELECT r.NewPrice,r.NewPrice);
        THROW 55315, N'Giá đã thay đổi sau bản liệt kê. Dừng toàn bộ để tránh ghi đè giá mới.', 1;
    END;

    SELECT n.*,r.NewPrice,
        CONVERT(bit,CASE WHEN EXISTS (SELECT n.RetailPrice EXCEPT SELECT r.NewPrice) THEN 1 ELSE 0 END) AS RetailWillChange,
        CONVERT(bit,CASE WHEN EXISTS (SELECT n.WholesalePrice EXCEPT SELECT r.NewPrice) THEN 1 ELSE 0 END) AS WholesaleWillChange
    INTO #UnitPlan
    FROM #ReviewedUnits r JOIN #UnitNow n ON n.ConversionId=r.ConversionId
    WHERE EXISTS (SELECT n.RetailPrice,n.WholesalePrice EXCEPT SELECT r.NewPrice,r.NewPrice);

    CREATE TABLE #UnitWrites
    (
        ConversionId int NOT NULL PRIMARY KEY,
        BeforeRetail decimal(18,2) NULL,AfterRetail decimal(18,2) NULL,
        BeforeWholesale decimal(18,2) NULL,AfterWholesale decimal(18,2) NULL,
        BeforeUpdatedAtUtc datetime2(7) NULL,AfterUpdatedAtUtc datetime2(7) NULL,
        BeforeUpdatedBy int NULL,AfterUpdatedBy int NULL
    );

    IF @Apply=1
    BEGIN
        UPDATE c
        SET Price=t.NewPrice,WholesalePrice=t.NewPrice,
            UpdatedAtUtc=@ChangedAtUtc,UpdatedBy=NULL
        OUTPUT inserted.Id,deleted.Price,inserted.Price,
            deleted.WholesalePrice,inserted.WholesalePrice,
            deleted.UpdatedAtUtc,inserted.UpdatedAtUtc,deleted.UpdatedBy,inserted.UpdatedBy
        INTO #UnitWrites
        FROM dbo.ProductUnitConversion c
        JOIN #UnitPlan t ON t.ConversionId=c.Id
        WHERE c.StoreId=@StoreId AND c.IsBaseUnit=0 AND c.IsDeleted=0
            AND c.ProductVariantId=t.ProductVariantId AND c.UnitId=t.UnitId AND c.Factor=t.Factor
            AND t.ProductId<>384155
            AND LTRIM(RTRIM(t.Sku)) COLLATE Latin1_General_100_CI_AI
                NOT IN (N'chietkhauhoadon',N'chietkhauthuongmai')
            AND NOT EXISTS (SELECT c.Price,c.WholesalePrice EXCEPT SELECT t.RetailPrice,t.WholesalePrice);

        IF (SELECT COUNT_BIG(*) FROM #UnitWrites) <> (SELECT COUNT_BIG(*) FROM #UnitPlan)
            THROW 55316, N'Số dòng cập nhật không đúng kế hoạch; toàn bộ sẽ rollback.', 1;
        IF EXISTS (
            SELECT 1 FROM #UnitWrites w JOIN #UnitPlan t ON t.ConversionId=w.ConversionId
            WHERE EXISTS (SELECT w.BeforeRetail,w.BeforeWholesale EXCEPT SELECT t.RetailPrice,t.WholesalePrice)
               OR EXISTS (SELECT w.AfterRetail,w.AfterWholesale EXCEPT SELECT t.NewPrice,t.NewPrice))
            THROW 55317, N'Giá trước/sau cập nhật không đúng kế hoạch; toàn bộ sẽ rollback.', 1;

        IF EXISTS (
            SELECT 1 FROM #ReviewedUnits r
            JOIN #UnitNow n ON n.ConversionId=r.ConversionId
            LEFT JOIN dbo.ProductUnitConversion c ON c.Id=r.ConversionId
            LEFT JOIN dbo.ProductVariant v ON v.Id=c.ProductVariantId
            LEFT JOIN dbo.Products p ON p.Id=v.ProductId
            LEFT JOIN dbo.Unit u ON u.Id=c.UnitId
            WHERE c.Id IS NULL OR v.Id IS NULL OR p.Id IS NULL OR u.Id IS NULL
               OR c.StoreId<>@StoreId OR v.StoreId<>@StoreId OR p.StoreId<>@StoreId OR u.StoreId<>@StoreId
               OR c.IsBaseUnit<>0 OR c.IsDeleted<>0 OR v.IsDeleted<>0 OR p.IsDeleted<>0 OR u.IsDeleted<>0
               OR p.Id<>r.ProductId OR v.Id<>r.ProductVariantId
               OR c.UnitId<>n.UnitId OR c.IsActive<>n.UnitIsActive OR c.Factor<>r.Factor
               OR EXISTS (SELECT v.Sku COLLATE DATABASE_DEFAULT EXCEPT SELECT r.Sku COLLATE DATABASE_DEFAULT)
               OR EXISTS (SELECT u.Name COLLATE DATABASE_DEFAULT EXCEPT SELECT r.UnitName COLLATE DATABASE_DEFAULT)
               OR EXISTS (SELECT c.Price,c.WholesalePrice EXCEPT SELECT r.NewPrice,r.NewPrice))
            THROW 55318, N'Hậu kiểm không đạt; toàn bộ sẽ rollback.', 1;

        COMMIT TRANSACTION;
        SET @StartedTransaction=0;
    END;

    SELECT CASE WHEN @Apply=1 THEN N'UNIT_PRICE_EQUALIZE_COMMIT_PASS'
                ELSE N'UNIT_PRICE_EQUALIZE_PREVIEW_ONLY' END AS Report,
        (SELECT COUNT_BIG(*) FROM #ReviewedUnits) AS ReviewedUnits,
        (SELECT COUNT_BIG(*) FROM #UnitPlan) AS UnitsToChange,
        (SELECT COUNT_BIG(*) FROM #UnitPlan WHERE RetailWillChange=1) AS RetailPricesToChange,
        (SELECT COUNT_BIG(*) FROM #UnitPlan WHERE WholesaleWillChange=1) AS WholesalePricesToChange,
        (SELECT COUNT_BIG(*) FROM #ReviewedUnits)-(SELECT COUNT_BIG(*) FROM #UnitPlan) AS AlreadyEqualizedUnits,
        (SELECT COUNT_BIG(*) FROM #UnitWrites) AS UpdatedUnits;

    SELECT N'UNIT_PRICE_EQUALIZE_DETAILS' AS Report,
        t.ConversionId,t.ProductId,t.ProductVariantId,t.Sku,t.ProductName,t.UnitName,t.Factor,t.UnitIsActive,
        t.RetailPrice AS BeforeRetail,t.WholesalePrice AS BeforeWholesale,
        t.NewPrice AS AfterRetail,t.NewPrice AS AfterWholesale,t.RetailWillChange,t.WholesaleWillChange
    FROM #UnitPlan t ORDER BY t.ProductName,t.Sku,t.Factor,t.ConversionId;

    IF @Apply=1
        SELECT N'UNIT_PRICE_EQUALIZE_WRITE_EVIDENCE' AS Report,w.*
        FROM #UnitWrites w ORDER BY w.ConversionId;

    SET LOCK_TIMEOUT -1;
    IF @XactAbortWasOn=0 SET XACT_ABORT OFF;
    DROP TABLE #ReviewedUnits,#UnitNow,#UnitPlan,#UnitWrites;
END TRY
BEGIN CATCH
    IF @StartedTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    SET LOCK_TIMEOUT -1;
    IF @XactAbortWasOn=0 SET XACT_ABORT OFF;
    THROW;
END CATCH;
