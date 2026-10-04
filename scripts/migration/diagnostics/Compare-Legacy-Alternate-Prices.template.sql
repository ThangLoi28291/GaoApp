/*
  Read-only comparison of NONBASE unit prices, e.g. TH milk loc/thung.
  Required filter: GaoApp Price and WholesalePrice are BOTH non-NULL and DIFFERENT.
  Main detail report lists only the units whose copied prices differ from source.
  Source Promotion: Status=1, empty CodePromotion, MEMBER retail / WHOLESALE wholesale.
  Package prices are compared directly; never divide/multiply them by Factor.
  Exact mapping: preserved product/variant ID + SKU + Factor + migration unit name.
  Changed names, missing mappings and source price conflicts are reported separately.
  No real database table is modified; only session temp tables are written.
*/
USE [GaoAppDb];
SET NOCOUNT ON;

DECLARE @StoreId int=1;
DECLARE @NameFilter nvarchar(200)=NULL; -- NULL = all; optional example N'%TH%'

IF @@TRANCOUNT<>0
    THROW 55300,N'Hãy chạy toàn bộ file trong cửa sổ query mới, không có transaction đang mở.',1;
SET LOCK_TIMEOUT 2000;

BEGIN TRY
    IF DB_ID(N'DataGaoStore') IS NULL
        THROW 55301,N'Không tìm thấy nguồn DataGaoStore trên instance này.',1;
    DROP TABLE IF EXISTS #PriceParsedChild,#PriceChildCodes,#FinalProduct,#BaseUnit,
        #AltPromotionRaw,#AltSourceGroups,#AltSourceExpected,#AltSourceNameCounts,
        #AltTargetPool,#AltCompared;

    -- Exact base-product/child-code and normalized base-unit rules from the base audit.
-- BASE_STAGING_PLACEHOLDER

    IF EXISTS(SELECT Code FROM #FinalProduct GROUP BY Code HAVING COUNT_BIG(*)>1)
        THROW 55302,N'Nguồn có mã sản phẩm gốc bị trùng; chưa thể ghép đơn vị quy đổi.',1;

    SELECT pr.Id,
        LTRIM(RTRIM(CONVERT(nvarchar(400),pr.Code))) AS BaseCode,
        TRY_CONVERT(int,pr.Quantity) AS Factor,
        CASE WHEN TRY_CONVERT(decimal(38,10),pr.Quantity) IS NULL
            OR TRY_CONVERT(decimal(38,10),pr.Quantity)<>TRY_CONVERT(int,pr.Quantity)
            THEN 1 ELSE 0 END AS NonIntegerFactor,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(40),pr.GroupID)))) AS PriceGroup,
        CONVERT(decimal(18,2),pr.Price) AS Price,
        NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400),pr.Note))),N'') AS UnitNote
    INTO #AltPromotionRaw
    FROM [DataGaoStore].dbo.Promotion pr
    WHERE pr.Status=1
        AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400),pr.CodePromotion))),N'') IS NULL
    OPTION(MAXDOP 1);

    ;WITH G AS
    (
        SELECT BaseCode,Factor,MIN(Id) AS SourcePromotionId,
            SUM(CASE WHEN PriceGroup=N'MEMBER' THEN 1 ELSE 0 END) AS MemberRows,
            SUM(CASE WHEN PriceGroup=N'WHOLESALE' THEN 1 ELSE 0 END) AS WholesaleRows,
            SUM(CASE WHEN PriceGroup=N'MEMBER' AND Price IS NULL THEN 1 ELSE 0 END) AS MemberNullPriceRows,
            SUM(CASE WHEN PriceGroup=N'WHOLESALE' AND Price IS NULL THEN 1 ELSE 0 END) AS WholesaleNullPriceRows,
            COUNT(DISTINCT CASE WHEN PriceGroup=N'MEMBER' THEN Price END) AS MemberDistinctPrices,
            COUNT(DISTINCT CASE WHEN PriceGroup=N'WHOLESALE' THEN Price END) AS WholesaleDistinctPrices,
            MAX(CASE WHEN PriceGroup=N'MEMBER' THEN Price END) AS RawRetailPrice,
            MAX(CASE WHEN PriceGroup=N'WHOLESALE' THEN Price END) AS RawWholesalePrice,
            MAX(CASE WHEN PriceGroup=N'MEMBER' THEN UnitNote END) AS MemberNote,
            MAX(CASE WHEN PriceGroup=N'WHOLESALE' THEN UnitNote END) AS WholesaleNote,
            SUM(CASE WHEN PriceGroup IS NULL OR PriceGroup NOT IN(N'MEMBER',N'WHOLESALE') THEN 1 ELSE 0 END) AS UnsupportedGroupRows,
            SUM(NonIntegerFactor) AS NonIntegerFactorRows
        FROM #AltPromotionRaw
        GROUP BY BaseCode,Factor
    ), N AS
    (
        SELECT g.*,fp.ProductId,fp.Name AS SourceProductName,
            CASE WHEN g.BaseCode=N'20G136404' AND g.Factor=20 THEN N'Bịch'
                ELSE COALESCE(g.MemberNote,g.WholesaleNote,N'Set') END AS ChosenName0,
            CASE WHEN g.BaseCode=N'20G136404' AND g.Factor=20 THEN CONVERT(decimal(18,2),235000)
                ELSE g.RawWholesalePrice END AS ExpectedWholesalePrice,
            CASE WHEN g.BaseCode=N'20G136404' AND g.Factor=20 THEN 1 ELSE 0 END AS ReviewedWholesaleOverride
        FROM G g JOIN #FinalProduct fp ON fp.Code=g.BaseCode
    )
    SELECT n.*,
        CASE WHEN ChosenName0 COLLATE SQL_Latin1_General_CP1_CI_AS=N'lố' THEN N'Lốc'
            ELSE LTRIM(RTRIM(ChosenName0)) END AS ChosenName
    INTO #AltSourceGroups
    FROM N n;

    -- Preserve both suffix rules: shared note across factors, then base-unit collision.
    ;WITH NameFactors AS
    (
        SELECT BaseCode,ChosenName,COUNT(DISTINCT Factor) AS FactorCount
        FROM #AltSourceGroups GROUP BY BaseCode,ChosenName
    ), Preliminary AS
    (
        SELECT s.*,bu.UnitName AS SourceBaseUnit,
            CASE WHEN nf.FactorCount>1 THEN s.ChosenName+CONVERT(nvarchar(30),s.Factor)
                ELSE s.ChosenName END AS PreliminaryUnitName
        FROM #AltSourceGroups s
        JOIN NameFactors nf ON nf.BaseCode=s.BaseCode AND nf.ChosenName=s.ChosenName
        JOIN #BaseUnit bu ON bu.ProductId=s.ProductId
    )
    SELECT p.*,
        CASE WHEN PreliminaryUnitName COLLATE SQL_Latin1_General_CP1_CI_AS=
            SourceBaseUnit COLLATE SQL_Latin1_General_CP1_CI_AS AND Factor>1
            THEN PreliminaryUnitName+CONVERT(nvarchar(30),Factor)
            ELSE PreliminaryUnitName END AS ExpectedUnitName,
        CASE WHEN Factor IS NULL OR Factor<=0 OR NonIntegerFactorRows>0
            OR UnsupportedGroupRows>0 OR MemberDistinctPrices>1
            OR (WholesaleDistinctPrices>1 AND ReviewedWholesaleOverride=0)
            OR (PreliminaryUnitName COLLATE SQL_Latin1_General_CP1_CI_AS=
                SourceBaseUnit COLLATE SQL_Latin1_General_CP1_CI_AS AND Factor<=1)
            THEN 1 ELSE 0 END AS SourceRuleIssue
    INTO #AltSourceExpected
    FROM Preliminary p;

    SELECT ProductId,ExpectedUnitName COLLATE SQL_Latin1_General_CP1_CI_AS AS UnitKey,
        COUNT_BIG(*) AS NameCount
    INTO #AltSourceNameCounts
    FROM #AltSourceExpected
    GROUP BY ProductId,ExpectedUnitName COLLATE SQL_Latin1_General_CP1_CI_AS;

    SELECT c.Id AS ConversionId,v.Id AS ProductVariantId,p.Id AS TargetProductId,
        v.Sku,p.Name AS TargetProductName,v.ProductVariantName,
        u.Name AS TargetUnitName,c.Factor,c.IsActive AS ConversionIsActive,
        c.Price AS TargetRetailPrice,c.WholesalePrice AS TargetWholesalePrice,
        DATEADD(hour,7,c.UpdatedAtUtc) AS TargetUnitUpdatedLocal
    INTO #AltTargetPool
    FROM dbo.ProductUnitConversion c
    JOIN dbo.ProductVariant v ON v.Id=c.ProductVariantId AND v.StoreId=@StoreId AND v.IsDeleted=0
    JOIN dbo.Products p ON p.Id=v.ProductId AND p.StoreId=@StoreId AND p.IsDeleted=0
    JOIN dbo.Unit u ON u.Id=c.UnitId AND u.StoreId=@StoreId AND u.IsDeleted=0
    WHERE c.StoreId=@StoreId AND c.IsBaseUnit=0 AND c.IsDeleted=0
        AND c.Price IS NOT NULL AND c.WholesalePrice IS NOT NULL
        AND c.Price<>c.WholesalePrice
        AND (@NameFilter IS NULL OR p.Name LIKE @NameFilter OR v.ProductVariantName LIKE @NameFilter)
    OPTION(MAXDOP 1);

    SELECT t.*,s.BaseCode AS SourceCode,s.SourceProductName,s.SourcePromotionId,
        s.ExpectedUnitName,s.RawRetailPrice AS SourceRetailPrice,
        s.RawWholesalePrice AS SourceWholesaleRawPrice,
        s.ExpectedWholesalePrice AS SourceWholesalePrice,s.ReviewedWholesaleOverride,
        s.MemberRows,s.WholesaleRows,s.MemberNullPriceRows,s.WholesaleNullPriceRows,
        s.SourceRuleIssue,nc.NameCount,
        CASE WHEN s.ProductId IS NOT NULL AND s.SourceRuleIssue=0
            AND nc.NameCount=1 AND LEN(s.ExpectedUnitName)<=400
            AND s.RawRetailPrice IS NOT NULL AND s.ExpectedWholesalePrice IS NOT NULL
            THEN 1 ELSE 0 END AS CanCompare,
        CASE WHEN s.ProductId IS NOT NULL AND s.SourceRuleIssue=0 AND nc.NameCount=1
            AND LEN(s.ExpectedUnitName)<=400
            AND s.RawRetailPrice IS NOT NULL AND s.ExpectedWholesalePrice IS NOT NULL
            AND EXISTS(SELECT t.TargetRetailPrice EXCEPT SELECT s.RawRetailPrice)
            THEN 1 ELSE 0 END AS RetailMismatch,
        CASE WHEN s.ProductId IS NOT NULL AND s.SourceRuleIssue=0 AND nc.NameCount=1
            AND LEN(s.ExpectedUnitName)<=400
            AND s.RawRetailPrice IS NOT NULL AND s.ExpectedWholesalePrice IS NOT NULL
            AND EXISTS(SELECT t.TargetWholesalePrice EXCEPT SELECT s.ExpectedWholesalePrice)
            THEN 1 ELSE 0 END AS WholesaleMismatch
    INTO #AltCompared
    FROM #AltTargetPool t
    LEFT JOIN #AltSourceExpected s
        ON s.ProductId=t.ProductVariantId AND s.ProductId=t.TargetProductId
        AND s.BaseCode COLLATE DATABASE_DEFAULT=t.Sku COLLATE DATABASE_DEFAULT
        AND s.Factor=t.Factor
        AND s.ExpectedUnitName COLLATE SQL_Latin1_General_CP1_CI_AS=
            t.TargetUnitName COLLATE SQL_Latin1_General_CP1_CI_AS
    LEFT JOIN #AltSourceNameCounts nc ON nc.ProductId=s.ProductId
        AND nc.UnitKey=s.ExpectedUnitName COLLATE SQL_Latin1_General_CP1_CI_AS;

    -- 1. One summary for the exact requested TARGET price-pair filter.
    SELECT N'ALTERNATE_PRICE_AUDIT_SUMMARY' AS Report,SYSDATETIMEOFFSET() AS ComparedAt,
        N'GaoApp Price IS NOT NULL AND WholesalePrice IS NOT NULL AND Price <> WholesalePrice' AS FilterApplied,
        (SELECT COUNT_BIG(*) FROM #AltTargetPool) AS EligibleTargetUnits,
        (SELECT COUNT_BIG(DISTINCT ConversionId) FROM #AltCompared WHERE CanCompare=1) AS ComparedUnits,
        (SELECT COUNT_BIG(DISTINCT ConversionId) FROM #AltCompared WHERE CanCompare=1 AND RetailMismatch+WholesaleMismatch>0) AS UnitsWithPriceDifferences,
        (SELECT COUNT_BIG(DISTINCT ConversionId) FROM #AltCompared WHERE RetailMismatch=1) AS RetailDifferences,
        (SELECT COUNT_BIG(DISTINCT ConversionId) FROM #AltCompared WHERE WholesaleMismatch=1) AS WholesaleDifferences,
        (SELECT COUNT_BIG(DISTINCT ConversionId) FROM #AltCompared WHERE CanCompare=0) AS UnitsNotCompared;

    -- 2. Only nonbase units with unequal GaoApp retail/wholesale AND a copied-price mismatch.
    SELECT N'ALTERNATE_PRICE_MISMATCHES' AS Report,
        ConversionId,ProductVariantId,SourceCode,SourceProductName,
        TargetProductName,TargetUnitName,Factor,
        SourceRetailPrice,TargetRetailPrice,TargetRetailPrice-SourceRetailPrice AS RetailDelta,
        SourceWholesalePrice,TargetWholesalePrice,TargetWholesalePrice-SourceWholesalePrice AS WholesaleDelta,
        RetailMismatch,WholesaleMismatch,SourcePromotionId,
        SourceWholesaleRawPrice,ReviewedWholesaleOverride,
        MemberNullPriceRows,WholesaleNullPriceRows,ConversionIsActive,TargetUnitUpdatedLocal
    FROM #AltCompared WHERE CanCompare=1 AND RetailMismatch+WholesaleMismatch>0
    ORDER BY SourceCode,Factor,TargetUnitName,ConversionId;

    -- 3. Units in the same requested filter which cannot safely be price-compared.
    -- By-factor candidates are evidence only; their prices are not declared matches.
    SELECT N'ALTERNATE_MAPPING_OR_SOURCE_ISSUES' AS Report,
        t.ConversionId,t.ProductVariantId,t.Sku,t.TargetProductName,
        t.TargetUnitName,t.Factor,t.TargetRetailPrice,t.TargetWholesalePrice,
        CASE WHEN t.SourceCode IS NULL THEN N'Không có nguồn khớp ID/SKU/hệ số/tên đơn vị'
            WHEN t.SourceRetailPrice IS NULL OR t.SourceWholesalePrice IS NULL THEN N'Nguồn thiếu giá sỉ/lẻ để đối chiếu'
            ELSE N'Nguồn có xung đột giá hoặc tên đơn vị không duy nhất' END AS Reason,
        candidate.SourcePromotionId,candidate.BaseCode AS CandidateSourceCode,
        candidate.ExpectedUnitName AS CandidateUnitName,
        candidate.RawRetailPrice AS CandidateRetailRawPrice,
        candidate.RawWholesalePrice AS CandidateWholesaleRawPrice,
        candidate.ExpectedWholesalePrice AS CandidateWholesaleExpectedPrice,
        candidate.MemberRows,candidate.WholesaleRows,
        candidate.MemberNullPriceRows,candidate.WholesaleNullPriceRows,
        candidate.MemberDistinctPrices,candidate.WholesaleDistinctPrices,
        candidate.UnsupportedGroupRows,candidate.NonIntegerFactorRows,
        candidate.SourceRuleIssue,candidate.ReviewedWholesaleOverride
    FROM #AltCompared t
    LEFT JOIN #AltSourceExpected candidate ON candidate.ProductId=t.ProductVariantId
        AND candidate.Factor=t.Factor
    WHERE t.CanCompare=0
    ORDER BY t.Sku,t.Factor,t.TargetUnitName,t.ConversionId;

    SET LOCK_TIMEOUT -1;
    DROP TABLE #AltCompared,#AltTargetPool,#AltSourceNameCounts,#AltSourceExpected,
        #AltSourceGroups,#AltPromotionRaw,#BaseUnit,#FinalProduct,#PriceChildCodes,#PriceParsedChild;
END TRY
BEGIN CATCH
    SET LOCK_TIMEOUT -1;
    THROW;
END CATCH;
