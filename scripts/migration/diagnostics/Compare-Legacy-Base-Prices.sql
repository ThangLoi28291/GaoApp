/*
  Compare the reviewed 01-products BASE-UNIT price contract with current data.
  Source: DataGaoStore. Target: GaoAppDb. StoreId: 1.
  Run this entire file in SSMS against WIN-HU6RO2EMIJF\SQLEXPRESS.
  Business tables are SELECT-only. Intermediate results use session temp tables.
  No migration runner, explicit transaction, table lock hint or NOLOCK is used.
  This identifies CURRENT differences, not the time or author of a price change.
  Prices for child/alternate units require their separate Promotion contract.
*/
USE [GaoAppDb];
SET NOCOUNT ON;

DECLARE @StoreId int = 1;
IF @@TRANCOUNT <> 0
    THROW 55272, N'Hãy chạy trong cửa sổ query mới, không có transaction đang mở.', 1;
-- Run in a new query connection. Restore its default timeout with a literal value.
-- SQL Server SET LOCK_TIMEOUT does not accept a local variable.
SET LOCK_TIMEOUT 2000;

BEGIN TRY
    IF DB_ID(N'DataGaoStore') IS NULL
        THROW 55270, N'Không tìm thấy database nguồn DataGaoStore trên instance này.', 1;

    DROP TABLE IF EXISTS #PriceParsedChild;
    DROP TABLE IF EXISTS #PriceChildCodes;
    DROP TABLE IF EXISTS #FinalProduct;
    DROP TABLE IF EXISTS #BaseUnit;
    DROP TABLE IF EXISTS #PriceBaseCounts;
    DROP TABLE IF EXISTS #PriceAudit;

    -- Same can/banh parser and exclusion as the reviewed product import.
    ;WITH LegacyRaw AS
    (
        SELECT
            CONVERT(int, pd.Id) AS ChildProductDetailId,
            LTRIM(RTRIM(CONVERT(nvarchar(128), pd.Code))) AS ChildBarcode,
            LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Promotion))) AS LegacyPromotion,
            CASE
                WHEN LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Promotion)))
                     COLLATE Latin1_General_100_CI_AI LIKE N'%banh%' THEN N'banh'
                WHEN LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Promotion)))
                     COLLATE Latin1_General_100_CI_AI LIKE N'%can%' THEN N'can'
                ELSE NULL
            END AS Marker
        FROM [DataGaoStore].dbo.ProductDetail AS pd
        WHERE pd.Promotion IS NOT NULL
    ), Parsed AS
    (
        SELECT *, CHARINDEX(Marker,
            LegacyPromotion COLLATE Latin1_General_100_CI_AI) AS MarkerPos
        FROM LegacyRaw
        WHERE Marker IS NOT NULL
    )
    SELECT
        ChildProductDetailId, ChildBarcode,
        LEFT(LegacyPromotion, MarkerPos - 1) AS BaseCode,
        TRY_CONVERT(int, SUBSTRING(LegacyPromotion, MarkerPos + LEN(Marker), 50)) AS Factor
    INTO #PriceParsedChild
    FROM Parsed
    WHERE MarkerPos > 1;

    IF EXISTS (SELECT 1 FROM #PriceParsedChild WHERE Factor IS NULL OR Factor <= 0)
        THROW 55271, N'Nguồn có mã can/banh với hệ số không hợp lệ; cần kiểm tra trước khi phân loại giá đơn vị gốc.', 1;

    SELECT DISTINCT ChildBarcode
    INTO #PriceChildCodes
    FROM #PriceParsedChild
    WHERE ChildBarcode <> BaseCode;

    SELECT
        CONVERT(int, pd.Id) AS ProductId,
        LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Code))) AS Code,
        LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Name))) AS Name,
        CONVERT(nvarchar(400), pd.DVT) AS SourceDVT,
        CONVERT(decimal(18,2), pd.Price) AS RetailPrice,
        CONVERT(decimal(18,2), pd.WholesalePrice) AS WholesalePrice,
        pd.ModifiedDate AS SourceModifiedLocal
    INTO #FinalProduct
    FROM [DataGaoStore].dbo.ProductDetail AS pd
    WHERE NOT EXISTS
    (
        SELECT 1 FROM #PriceChildCodes AS tc
        WHERE tc.ChildBarcode = LTRIM(RTRIM(CONVERT(nvarchar(128), pd.Code)))
    )
    OPTION (MAXDOP 1);

    /* BASE_UNIT_RULE_START: copied from the reviewed product migration. */
    /* ---------- Base Unit ---------- */
CREATE TABLE #BaseUnit
(
    ProductId int NOT NULL PRIMARY KEY,
    UnitName nvarchar(400) NOT NULL
);

;WITH ProductDvtHistory AS
(
    SELECT
        LTRIM(RTRIM(CONVERT(nvarchar(400), p.Code))) AS Code,
        COUNT(DISTINCT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), p.DVT))), N'')) AS DistinctDvtCount,
        MAX(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), p.DVT))), N'')) AS OnlyDvt
    FROM [DataGaoStore].dbo.Product p
    GROUP BY LTRIM(RTRIM(CONVERT(nvarchar(400), p.Code)))
), BaseRaw AS
(
    SELECT
        fp.ProductId,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), pd.DVT))), N''),
            CASE WHEN h.DistinctDvtCount = 1 THEN h.OnlyDvt END,
            N'Cái'
        ) AS RawUnit
    FROM #FinalProduct fp
    JOIN [DataGaoStore].dbo.ProductDetail pd ON CONVERT(int, pd.Id) = fp.ProductId
    LEFT JOIN ProductDvtHistory h ON h.Code = fp.Code
)
INSERT #BaseUnit(ProductId, UnitName)
SELECT
    ProductId,
    CASE
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI IN
             (N'cái',N' cai',N'cai',N'câi',N'cấi',N'cá',N'casi',N'ca',N'xe') THEN N'Cái'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI IN (N'bịch',N' bịch',N'bích') THEN N'Bịch'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'chai' THEN N'Chai'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'hộp' THEN N'Hộp'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'gói' THEN N'Gói'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI IN (N'hũ',N'hủ') THEN N'Hũ'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'túi' THEN N'Túi'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'bộ' THEN N'Bộ'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'cây' THEN N'Cây'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI IN (N'vỉ',N'vĩ') THEN N'Vỉ'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'can' THEN N'Can'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI IN (N'tuýp',N'tuýp6') THEN N'Tuýp'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'lon' THEN N'Lon'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'lốc' THEN N'Lốc'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'dây' THEN N'Dây'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'cặp' THEN N'Cặp'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'cuộn' THEN N'Cuộn'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'thanh' THEN N'Thanh'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'quả' THEN N'Quả'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'ống' THEN N'Ống'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'kmai' THEN N'Phiếu'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI IN (N'g',N'gam',N'gr') THEN N'gr'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'ông' THEN N'ông'
        WHEN RawUnit COLLATE Latin1_General_100_CI_AI = N'setcombo' THEN N'setcombo'
        ELSE LTRIM(RTRIM(RawUnit))
    END
FROM BaseRaw;

IF EXISTS (SELECT 1 FROM #BaseUnit WHERE UnitName = N'')
    THROW 51008, N'Có base unit rỗng sau normalization.', 1;
    /* BASE_UNIT_RULE_END */

    -- Count live base conversions without hiding duplicate base-unit definitions.
    SELECT c.ProductVariantId, COUNT_BIG(*) AS BaseConversionCount,
        CASE WHEN COUNT_BIG(*) = 1 THEN MAX(c.Id) END AS SingleBaseConversionId
    INTO #PriceBaseCounts
    FROM dbo.ProductUnitConversion AS c
    WHERE c.StoreId = @StoreId AND c.IsDeleted = 0 AND c.IsBaseUnit = 1
    GROUP BY c.ProductVariantId
    OPTION (MAXDOP 1);

    SELECT
        fp.ProductId AS LegacyProductDetailId,
        fp.Code AS SourceCode, fp.Name AS SourceName, fp.SourceDVT,
        bu.UnitName AS ExpectedBaseUnit,
        fp.RetailPrice AS SourceRetailPrice, fp.WholesalePrice AS SourceWholesalePrice,
        p.Id AS TargetProductId, p.Alias AS TargetAlias,
        p.BasePrice AS ProductBasePrice, pu.Name AS ProductBaseUnit,
        v.Id AS TargetVariantId, v.ProductId AS VariantProductId, v.Sku AS VariantSku,
        v.Price AS VariantRetailPrice, v.WholesalePrice AS VariantWholesalePrice,
        ISNULL(bc.BaseConversionCount, 0) AS BaseConversionCount,
        c.Id AS BaseConversionId, cu.Name AS ConversionBaseUnit, c.Factor AS BaseFactor,
        c.Price AS BaseUnitRetailPrice, c.WholesalePrice AS BaseUnitWholesalePrice,
        c.IsActive AS BaseUnitIsActive, c.IsDefaultForSale AS BaseUnitIsDefaultForSale,
        p.IsDeleted AS ProductIsDeleted, v.IsDeleted AS VariantIsDeleted,
        fp.SourceModifiedLocal,
        DATEADD(hour, 7, p.UpdatedAtUtc) AS ProductUpdatedLocal,
        DATEADD(hour, 7, v.UpdatedAtUtc) AS VariantUpdatedLocal,
        DATEADD(hour, 7, c.UpdatedAtUtc) AS BaseUnitUpdatedLocal,
        CASE WHEN p.Id IS NOT NULL AND EXISTS
            (SELECT p.BasePrice EXCEPT SELECT fp.RetailPrice) THEN 1 ELSE 0 END AS ProductRetailMismatch,
        CASE WHEN v.Id IS NOT NULL AND EXISTS
            (SELECT v.Price EXCEPT SELECT fp.RetailPrice) THEN 1 ELSE 0 END AS VariantRetailMismatch,
        CASE WHEN v.Id IS NOT NULL AND EXISTS
            (SELECT v.WholesalePrice EXCEPT SELECT fp.WholesalePrice) THEN 1 ELSE 0 END AS VariantWholesaleMismatch,
        CASE WHEN c.Id IS NOT NULL AND EXISTS
            (SELECT c.Price EXCEPT SELECT fp.RetailPrice) THEN 1 ELSE 0 END AS BaseRetailMismatch,
        CASE WHEN c.Id IS NOT NULL AND EXISTS
            (SELECT c.WholesalePrice EXCEPT SELECT fp.WholesalePrice) THEN 1 ELSE 0 END AS BaseWholesaleMismatch,
        CASE WHEN p.Id IS NULL OR v.Id IS NULL OR p.IsDeleted = 1 OR v.IsDeleted = 1
            OR v.ProductId <> fp.ProductId
            OR ISNULL(bc.BaseConversionCount, 0) <> 1
            OR EXISTS (SELECT p.Alias COLLATE DATABASE_DEFAULT
                       EXCEPT SELECT fp.Code COLLATE DATABASE_DEFAULT)
            OR EXISTS (SELECT v.Sku COLLATE DATABASE_DEFAULT
                       EXCEPT SELECT fp.Code COLLATE DATABASE_DEFAULT)
            THEN 1 ELSE 0 END AS MappingIssue,
        CASE WHEN p.Id IS NOT NULL AND
            (pu.Id IS NULL OR pu.IsDeleted = 1 OR EXISTS
                (SELECT pu.Name COLLATE DATABASE_DEFAULT
                 EXCEPT SELECT bu.UnitName COLLATE DATABASE_DEFAULT))
            OR c.Id IS NOT NULL AND
            (c.Factor <> 1 OR c.UnitId <> p.BaseUnitId OR c.IsActive = 0
                OR cu.Id IS NULL OR cu.IsDeleted = 1)
            THEN 1 ELSE 0 END AS UnitIssue,
        CASE WHEN fp.RetailPrice IS NULL THEN 1 ELSE 0 END AS SourceRetailMissing
    INTO #PriceAudit
    FROM #FinalProduct AS fp
    JOIN #BaseUnit AS bu ON bu.ProductId = fp.ProductId
    LEFT JOIN dbo.Products AS p ON p.Id = fp.ProductId AND p.StoreId = @StoreId
    LEFT JOIN dbo.ProductVariant AS v ON v.Id = fp.ProductId AND v.StoreId = @StoreId
    LEFT JOIN #PriceBaseCounts AS bc ON bc.ProductVariantId = v.Id
    LEFT JOIN dbo.ProductUnitConversion AS c ON c.Id = bc.SingleBaseConversionId
        AND c.StoreId = @StoreId AND c.IsDeleted = 0 AND c.IsBaseUnit = 1
    LEFT JOIN dbo.Unit AS pu ON pu.Id = p.BaseUnitId AND pu.StoreId = @StoreId
    LEFT JOIN dbo.Unit AS cu ON cu.Id = c.UnitId AND cu.StoreId = @StoreId
    OPTION (MAXDOP 1);

    -- 1. Totals. A zero price mismatch does not excuse missing/duplicate base units.
    SELECT N'BASE_PRICE_AUDIT_SUMMARY' AS Report, SYSDATETIMEOFFSET() AS ComparedAt,
        COUNT_BIG(*) AS ComparedBaseProducts,
        (SELECT COUNT_BIG(*) FROM [DataGaoStore].dbo.ProductDetail) - COUNT_BIG(*) AS ExcludedChildProductRows,
        COUNT_BIG(CASE WHEN ProductRetailMismatch + VariantRetailMismatch + VariantWholesaleMismatch
            + BaseRetailMismatch + BaseWholesaleMismatch > 0 THEN 1 END) AS ProductsWithPriceDifferences,
        SUM(CONVERT(bigint, ProductRetailMismatch)) AS ProductRetailDifferences,
        SUM(CONVERT(bigint, VariantRetailMismatch)) AS VariantRetailDifferences,
        SUM(CONVERT(bigint, VariantWholesaleMismatch)) AS VariantWholesaleDifferences,
        SUM(CONVERT(bigint, BaseRetailMismatch)) AS BaseUnitRetailDifferences,
        SUM(CONVERT(bigint, BaseWholesaleMismatch)) AS BaseUnitWholesaleDifferences,
        SUM(CONVERT(bigint, MappingIssue)) AS ProductsWithMappingIssues,
        SUM(CONVERT(bigint, UnitIssue)) AS ProductsWithUnitIssues,
        SUM(CONVERT(bigint, SourceRetailMissing)) AS SourceRetailMissing
    FROM #PriceAudit;

    -- 2. The requested list: one row per source base product with a different price.
    -- NULL and 0 remain different. A missing/duplicate conversion is report 3.
    SELECT N'PRICE_MISMATCHES' AS Report,
        LegacyProductDetailId, SourceCode, SourceName, ExpectedBaseUnit,
        SourceRetailPrice, ProductBasePrice, VariantRetailPrice, BaseUnitRetailPrice,
        SourceWholesalePrice, VariantWholesalePrice, BaseUnitWholesalePrice,
        ProductBasePrice - SourceRetailPrice AS ProductRetailDelta,
        VariantRetailPrice - SourceRetailPrice AS VariantRetailDelta,
        BaseUnitRetailPrice - SourceRetailPrice AS BaseRetailDelta,
        VariantWholesalePrice - SourceWholesalePrice AS VariantWholesaleDelta,
        BaseUnitWholesalePrice - SourceWholesalePrice AS BaseWholesaleDelta,
        ProductRetailMismatch, VariantRetailMismatch, BaseRetailMismatch,
        VariantWholesaleMismatch, BaseWholesaleMismatch,
        TargetProductId, TargetAlias, TargetVariantId, VariantSku,
        BaseConversionId, BaseConversionCount, ProductBaseUnit, ConversionBaseUnit, BaseFactor,
        MappingIssue, UnitIssue, SourceRetailMissing,
        SourceModifiedLocal, ProductUpdatedLocal, VariantUpdatedLocal, BaseUnitUpdatedLocal
    FROM #PriceAudit
    WHERE ProductRetailMismatch + VariantRetailMismatch + VariantWholesaleMismatch
        + BaseRetailMismatch + BaseWholesaleMismatch > 0
    ORDER BY SourceCode, LegacyProductDetailId;

    -- 3. Missing records, changed mapping, wrong units/factor, deleted/inactive base unit.
    SELECT N'UNIT_OR_MAPPING_ISSUES' AS Report, *
    FROM #PriceAudit
    WHERE MappingIssue = 1 OR UnitIssue = 1 OR SourceRetailMissing = 1
    ORDER BY SourceCode, LegacyProductDetailId;

    -- 4. Source quantity-1 Promotion prices different from ProductDetail.
    -- These were ALTERNATE conversions under the old contract, not base prices.
    -- Their presence alone is NOT proof that base prices must be changed.
    SELECT N'SOURCE_FACTOR1_PROMOTION_DIFFERENCES' AS Report,
        fp.ProductId AS LegacyProductDetailId, fp.Code AS SourceCode, fp.Name AS SourceName,
        pr.Id AS SourcePromotionId,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(40), pr.GroupID)))) AS SourcePriceGroup,
        pr.Note AS PromotionUnitNote,
        CONVERT(decimal(18,2), pr.Price) AS PromotionPrice,
        fp.RetailPrice AS ProductDetailRetailPrice,
        fp.WholesalePrice AS ProductDetailWholesalePrice
    FROM [DataGaoStore].dbo.Promotion AS pr
    JOIN #FinalProduct AS fp ON fp.Code COLLATE DATABASE_DEFAULT =
        LTRIM(RTRIM(CONVERT(nvarchar(400), pr.Code))) COLLATE DATABASE_DEFAULT
    WHERE pr.Status = 1 AND CONVERT(int, pr.Quantity) = 1
        AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), pr.CodePromotion))), N'') IS NULL
        AND (
            UPPER(LTRIM(RTRIM(CONVERT(nvarchar(40), pr.GroupID)))) = N'MEMBER'
            AND EXISTS (SELECT CONVERT(decimal(18,2), pr.Price) EXCEPT SELECT fp.RetailPrice)
            OR UPPER(LTRIM(RTRIM(CONVERT(nvarchar(40), pr.GroupID)))) = N'WHOLESALE'
            AND EXISTS (SELECT CONVERT(decimal(18,2), pr.Price) EXCEPT SELECT fp.WholesalePrice)
        )
    ORDER BY fp.Code, pr.Id
    OPTION (MAXDOP 1);

    DROP TABLE #PriceAudit, #PriceBaseCounts, #BaseUnit, #FinalProduct,
        #PriceChildCodes, #PriceParsedChild;
    SET LOCK_TIMEOUT -1;
END TRY
BEGIN CATCH
    SET LOCK_TIMEOUT -1;
    THROW;
END CATCH;
