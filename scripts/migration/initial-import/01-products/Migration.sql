-- Reviewed initial import. Run only through Invoke-Migration.ps1.
IF @@TRANCOUNT<>1 OR ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'GSTORE_INITIAL_IMPORT')),0)<>1
    THROW 55100,'Run through the transactional package runner.',1;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @StoreId int=1;
/* ============================================================
   1. SOURCE STAGING — build everything before touching target
   ============================================================ */

DROP TABLE IF EXISTS #LegacyParsed;
DROP TABLE IF EXISTS #TrueChildCodes;
DROP TABLE IF EXISTS #FinalProduct;
DROP TABLE IF EXISTS #BaseUnit;
DROP TABLE IF EXISTS #AltConv;
DROP TABLE IF EXISTS #AltUnitRename;
DROP TABLE IF EXISTS #LegacyAltBarcode;
DROP TABLE IF EXISTS #UnitManifest;
DROP TABLE IF EXISTS #GeneratedAltBarcode;

CREATE TABLE #LegacyParsed
(
    ChildProductDetailId int NOT NULL,
    ChildBarcode nvarchar(128) NOT NULL,
    BaseCode nvarchar(400) NOT NULL,
    Factor int NULL,
    ChildCreatedDate datetime2 NULL,
    ChildImage nvarchar(max) NULL,
    PRIMARY KEY (ChildProductDetailId)
);

;WITH LegacyRaw AS
(
    SELECT
        CONVERT(int, pd.Id) AS ChildProductDetailId,
        LTRIM(RTRIM(CONVERT(nvarchar(128), pd.Code))) AS ChildBarcode,
        LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Promotion))) AS LegacyPromotion,
        pd.CreatedDate AS ChildCreatedDate,
        CONVERT(nvarchar(max), pd.Image) AS ChildImage,
        CASE
            WHEN LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Promotion)))
                 COLLATE Latin1_General_100_CI_AI LIKE N'%banh%'
                THEN N'banh'
            WHEN LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Promotion)))
                 COLLATE Latin1_General_100_CI_AI LIKE N'%can%'
                THEN N'can'
            ELSE NULL
        END AS Marker
    FROM [__SOURCE__].dbo.ProductDetail pd
    WHERE pd.Promotion IS NOT NULL
      AND
      (
           LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Promotion)))
               COLLATE Latin1_General_100_CI_AI LIKE N'%banh%'
        OR LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Promotion)))
               COLLATE Latin1_General_100_CI_AI LIKE N'%can%'
      )
), P AS
(
    SELECT *,
           CHARINDEX(Marker, LegacyPromotion COLLATE Latin1_General_100_CI_AI) AS MarkerPos
    FROM LegacyRaw
)
INSERT #LegacyParsed
(
    ChildProductDetailId, ChildBarcode, BaseCode, Factor,
    ChildCreatedDate, ChildImage
)
SELECT
    ChildProductDetailId,
    ChildBarcode,
    LEFT(LegacyPromotion, MarkerPos - 1),
    TRY_CONVERT(int, SUBSTRING(LegacyPromotion, MarkerPos + LEN(Marker), 50)),
    ChildCreatedDate,
    ChildImage
FROM P
WHERE MarkerPos > 1;

IF EXISTS (SELECT 1 FROM #LegacyParsed WHERE Factor IS NULL OR Factor <= 0)
    THROW 51002, N'Có ProductDetail can/banh parse Factor không hợp lệ. STOP.', 1;

CREATE TABLE #TrueChildCodes
(
    ChildBarcode nvarchar(128) NOT NULL PRIMARY KEY
);
INSERT #TrueChildCodes(ChildBarcode)
SELECT DISTINCT ChildBarcode
FROM #LegacyParsed
WHERE ChildBarcode <> BaseCode;

CREATE TABLE #FinalProduct
(
    ProductId int NOT NULL PRIMARY KEY,
    Code nvarchar(400) NOT NULL,
    Name nvarchar(400) NOT NULL,
    CategoryId int NOT NULL,
    SupplierId int NOT NULL,
    PurchasePrice decimal(18,2) NOT NULL,
    RetailPrice decimal(18,2) NOT NULL,
    WholesalePrice decimal(18,2) NULL,
    IsActive bit NOT NULL,
    HasInputInvoice bit NOT NULL,
    CreatedDate datetime2 NULL,
    ModifiedDate datetime2 NULL,
    Image nvarchar(max) NULL
);

INSERT #FinalProduct
(
    ProductId, Code, Name, CategoryId, SupplierId,
    PurchasePrice, RetailPrice, WholesalePrice,
    IsActive, HasInputInvoice, CreatedDate, ModifiedDate, Image
)
SELECT
    CONVERT(int, pd.Id),
    LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Code))),
    LTRIM(RTRIM(CONVERT(nvarchar(400), pd.Name))),
    CONVERT(int, pd.CategoryID),
    CASE
        WHEN pd.Id = 143415 AND NOT EXISTS(SELECT 1 FROM [__SOURCE__].dbo.Supplier s WHERE s.ID=pd.SupplierID) THEN 40042
        WHEN pd.Id = 291459 AND NOT EXISTS(SELECT 1 FROM [__SOURCE__].dbo.Supplier s WHERE s.ID=pd.SupplierID) THEN 11
        ELSE CONVERT(int, pd.SupplierID)
    END,
    CONVERT(decimal(18,2), ISNULL(pd.PurchasePrice, 0)),
    CONVERT(decimal(18,2), pd.Price),
    CONVERT(decimal(18,2), pd.WholesalePrice),
    CONVERT(bit, pd.Status),
    CONVERT(bit, ISNULL(pd.HDDV, 0)),
    pd.CreatedDate,
    pd.ModifiedDate,
    CONVERT(nvarchar(max), pd.Image)
FROM [__SOURCE__].dbo.ProductDetail pd
WHERE NOT EXISTS
(
    SELECT 1
    FROM #TrueChildCodes tc
    WHERE tc.ChildBarcode = LTRIM(RTRIM(CONVERT(nvarchar(128), pd.Code)))
);

IF EXISTS (SELECT 1 FROM #FinalProduct WHERE Code = N'' OR Name = N'')
    THROW 51003, N'Có Product cuối thiếu Code/Name.', 1;

IF EXISTS (SELECT 1 FROM #FinalProduct WHERE RetailPrice IS NULL)
    THROW 51004, N'Có Product cuối Price NULL nhưng Products.BasePrice bắt buộc.', 1;

IF EXISTS
(
    SELECT Code FROM #FinalProduct GROUP BY Code HAVING COUNT(*) > 1
)
    THROW 51005, N'Duplicate Product Code/Alias trong final Product.', 1;

IF EXISTS
(
    SELECT 1
    FROM #FinalProduct
    WHERE CategoryId NOT IN (1,19,20,21,22)
)
    THROW 51006, N'Có CategoryId ngoài 1,19,20,21,22 trong final Product.', 1;

IF EXISTS
(
    SELECT 1
    FROM #FinalProduct fp
    LEFT JOIN [__SOURCE__].dbo.Supplier s ON s.ID = fp.SupplierId
    WHERE s.ID IS NULL
)
    THROW 51007, N'Có final Product không resolve được Supplier sau exception mapping.', 1;

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
    FROM [__SOURCE__].dbo.Product p
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
    JOIN [__SOURCE__].dbo.ProductDetail pd ON CONVERT(int, pd.Id) = fp.ProductId
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

/* ---------- Promotion logical conversions ---------- */
CREATE TABLE #AltConv
(
    BaseCode nvarchar(400) NOT NULL,
    ProductId int NOT NULL,
    Factor int NOT NULL,
    SourcePromotionId int NOT NULL,
    UnitName nvarchar(400) NOT NULL,
    MemberPrice decimal(18,2) NULL,
    WholesalePrice decimal(18,2) NULL,
    CreatedAtUtc datetime2 NOT NULL,
    PRIMARY KEY (BaseCode, Factor)
);

;WITH P AS
(
    SELECT
        p.Id,
        LTRIM(RTRIM(CONVERT(nvarchar(400), p.Code))) AS BaseCode,
        CONVERT(int, p.Quantity) AS Quantity,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(40), p.GroupID)))) AS GroupID,
        CONVERT(decimal(18,2), p.Price) AS Price,
        NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), p.Note))), N'') AS Note,
        p.CreatedDate
    FROM [__SOURCE__].dbo.Promotion p
    WHERE p.Status = 1
      AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), p.CodePromotion))), N'') IS NULL
), G AS
(
    SELECT
        BaseCode,
        Quantity,
        MIN(Id) AS SourcePromotionId,
        COUNT(DISTINCT CASE WHEN GroupID = 'MEMBER' THEN Price END) AS MemberPriceDistinct,
        COUNT(DISTINCT CASE WHEN GroupID = 'WHOLESALE' THEN Price END) AS WholesalePriceDistinct,
        MAX(CASE WHEN GroupID = 'MEMBER' THEN Price END) AS MemberPriceRaw,
        MAX(CASE WHEN GroupID = 'WHOLESALE' THEN Price END) AS WholesalePriceRaw,
        MAX(CASE WHEN GroupID = 'MEMBER' THEN Note END) AS MemberNote,
        MAX(CASE WHEN GroupID = 'WHOLESALE' THEN Note END) AS WholesaleNote,
        MIN(CreatedDate) AS FirstCreatedDate
    FROM P
    GROUP BY BaseCode, Quantity
), Valid AS
(
    SELECT
        g.*,
        fp.ProductId,
        CASE
            WHEN g.BaseCode = N'20G136404' AND g.Quantity = 20 THEN N'Bịch'
            WHEN NULLIF(g.MemberNote,N'') IS NOT NULL THEN g.MemberNote
            WHEN NULLIF(g.WholesaleNote,N'') IS NOT NULL THEN g.WholesaleNote
            ELSE N'Set'
        END AS ChosenNote0,
        CASE
            WHEN g.BaseCode = N'20G136404' AND g.Quantity = 20 THEN CONVERT(decimal(18,2),235000)
            ELSE g.WholesalePriceRaw
        END AS WholesalePriceFinal
    FROM G g
    JOIN #FinalProduct fp ON fp.Code = g.BaseCode
    WHERE NOT EXISTS
    (
        SELECT 1 FROM #TrueChildCodes tc WHERE tc.ChildBarcode = g.BaseCode
    )
), N AS
(
    SELECT
        *,
        CASE
            WHEN ChosenNote0 COLLATE SQL_Latin1_General_CP1_CI_AS = N'lố' THEN N'Lốc'
            ELSE LTRIM(RTRIM(ChosenNote0))
        END AS ChosenNote
    FROM Valid
), M AS
(
    SELECT BaseCode, ChosenNote, COUNT(DISTINCT Quantity) AS QuantityCount
    FROM N
    GROUP BY BaseCode, ChosenNote
)
INSERT #AltConv
(
    BaseCode, ProductId, Factor, SourcePromotionId, UnitName,
    MemberPrice, WholesalePrice, CreatedAtUtc
)
SELECT
    n.BaseCode,
    n.ProductId,
    n.Quantity,
    n.SourcePromotionId,
    CASE
        WHEN m.QuantityCount > 1 THEN n.ChosenNote + CONVERT(nvarchar(30), n.Quantity)
        ELSE n.ChosenNote
    END,
    n.MemberPriceRaw,
    n.WholesalePriceFinal,
    COALESCE(DATEADD(hour,-7,n.FirstCreatedDate),SYSUTCDATETIME())
FROM N n
JOIN M m ON m.BaseCode = n.BaseCode AND m.ChosenNote = n.ChosenNote;

IF EXISTS
(
    SELECT 1
    FROM [__SOURCE__].dbo.Promotion p
    WHERE p.Status = 1
      AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), p.CodePromotion))), N'') IS NULL
      AND UPPER(LTRIM(RTRIM(CONVERT(nvarchar(40), p.GroupID)))) NOT IN ('MEMBER','WHOLESALE')
)
    THROW 51009, N'Xuất hiện Promotion GroupID mới ngoài MEMBER/WHOLESALE.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        SELECT
            LTRIM(RTRIM(CONVERT(nvarchar(400), p.Code))) AS BaseCode,
            CONVERT(int,p.Quantity) AS Quantity,
            UPPER(LTRIM(RTRIM(CONVERT(nvarchar(40), p.GroupID)))) AS GroupID,
            COUNT(DISTINCT CONVERT(decimal(18,2),p.Price)) AS DifferentPrices
        FROM [__SOURCE__].dbo.Promotion p
        WHERE p.Status = 1
          AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400), p.CodePromotion))), N'') IS NULL
        GROUP BY
            LTRIM(RTRIM(CONVERT(nvarchar(400), p.Code))),
            CONVERT(int,p.Quantity),
            UPPER(LTRIM(RTRIM(CONVERT(nvarchar(40), p.GroupID))))
        HAVING COUNT(DISTINCT CONVERT(decimal(18,2),p.Price)) > 1
    ) x
    WHERE NOT (x.BaseCode = N'20G136404' AND x.Quantity = 20 AND x.GroupID = 'WHOLESALE')
)
    THROW 51010, N'Xuất hiện price conflict mới trong Promotion ngoài exception đã chốt.', 1;

IF EXISTS (SELECT 1 FROM #AltConv WHERE Factor <= 0 OR UnitName = N'')
    THROW 51011, N'Alternate conversion có Factor/UnitName không hợp lệ.', 1;

/*
   ProductVariant+Unit is unique in GaoApp.
   Locked migration rule:
   - if an alternate conversion resolves to the same UnitName as the base unit
     and Factor > 1, suffix the alternate UnitName with Factor.
     Example: base Bịch + alternate Bịch x10 => Bịch10.
   - Factor <= 1 collision is a new/unsupported pattern and must stop.
*/
CREATE TABLE #AltUnitRename
(
    ProductId int NOT NULL,
    BaseCode nvarchar(400) NOT NULL,
    Factor int NOT NULL,
    OldUnitName nvarchar(400) NOT NULL,
    NewUnitName nvarchar(400) NOT NULL,
    PRIMARY KEY(ProductId, Factor)
);

IF EXISTS
(
    SELECT 1
    FROM #BaseUnit b
    JOIN #AltConv a
      ON a.ProductId = b.ProductId
     AND a.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
         = b.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
    WHERE a.Factor <= 1
)
BEGIN
    SELECT
        N'BASE_ALT_FACTOR1_COLLISION' AS CollisionType,
        fp.ProductId,
        fp.Code,
        fp.Name,
        b.UnitName AS BaseUnit,
        a.Factor AS AlternateFactor,
        a.UnitName AS AlternateUnit,
        a.MemberPrice,
        a.WholesalePrice,
        a.SourcePromotionId
    FROM #BaseUnit b
    JOIN #AltConv a
      ON a.ProductId = b.ProductId
     AND a.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
         = b.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
    JOIN #FinalProduct fp ON fp.ProductId = b.ProductId
    WHERE a.Factor <= 1
    ORDER BY fp.ProductId, a.Factor;

    THROW 51012, N'Base/alternate trùng UnitName với Factor <= 1; đây là pattern mới, cần Human decision.', 1;
END;

INSERT #AltUnitRename(ProductId, BaseCode, Factor, OldUnitName, NewUnitName)
SELECT
    a.ProductId,
    a.BaseCode,
    a.Factor,
    a.UnitName,
    a.UnitName + CONVERT(nvarchar(30), a.Factor)
FROM #BaseUnit b
JOIN #AltConv a
  ON a.ProductId = b.ProductId
 AND a.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
     = b.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
WHERE a.Factor > 1;

IF EXISTS (SELECT 1 FROM #AltUnitRename WHERE LEN(NewUnitName) > 400)
    THROW 51013, N'UnitName sau suffix Factor vượt giới hạn 400 ký tự.', 1;

UPDATE a
SET a.UnitName = r.NewUnitName
FROM #AltConv a
JOIN #AltUnitRename r
  ON r.ProductId = a.ProductId
 AND r.Factor = a.Factor;

/* Evidence result: expected 11 rows on the 2026-09-16 TEST snapshot. */
SELECT
    N'BASE_ALT_RENAMED' AS AdjustmentType,
    fp.ProductId,
    fp.Code,
    fp.Name,
    r.Factor,
    r.OldUnitName,
    r.NewUnitName
FROM #AltUnitRename r
JOIN #FinalProduct fp ON fp.ProductId = r.ProductId
ORDER BY fp.ProductId, r.Factor;

/* After deterministic rename, any remaining duplicate UnitName is unexpected. */
IF EXISTS
(
    SELECT ProductId, UnitKey
    FROM
    (
        SELECT b.ProductId,
               b.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS AS UnitKey
        FROM #BaseUnit b
        UNION ALL
        SELECT a.ProductId,
               a.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
        FROM #AltConv a
    ) q
    GROUP BY ProductId, UnitKey
    HAVING COUNT(*) > 1
)
BEGIN
    SELECT
        N'REMAINING_UNIT_COLLISION' AS CollisionType,
        fp.ProductId,
        fp.Code,
        fp.Name,
        a.UnitName,
        a.Factor,
        b.UnitName AS BaseUnit
    FROM #AltConv a
    JOIN #FinalProduct fp ON fp.ProductId = a.ProductId
    LEFT JOIN #BaseUnit b ON b.ProductId = a.ProductId
    WHERE EXISTS
    (
        SELECT 1
        FROM
        (
            SELECT bx.UnitName AS UnitName FROM #BaseUnit bx WHERE bx.ProductId = a.ProductId
            UNION ALL
            SELECT ax.UnitName FROM #AltConv ax WHERE ax.ProductId = a.ProductId
        ) u
        WHERE u.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
              = a.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
        GROUP BY u.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
        HAVING COUNT(*) > 1
    )
    ORDER BY fp.ProductId, a.UnitName, a.Factor;

    THROW 51014, N'Sau suffix Factor vẫn còn UnitName collision; dừng để review.', 1;
END;

/* ---------- Legacy alternate barcodes ---------- */
CREATE TABLE #LegacyAltBarcode
(
    ProductId int NOT NULL,
    Factor int NOT NULL,
    ChildProductDetailId int NOT NULL,
    Barcode nvarchar(128) NOT NULL,
    CreatedAtUtc datetime2 NOT NULL,
    PRIMARY KEY (ChildProductDetailId)
);

INSERT #LegacyAltBarcode(ProductId, Factor, ChildProductDetailId, Barcode, CreatedAtUtc)
SELECT
    a.ProductId,
    a.Factor,
    lp.ChildProductDetailId,
    lp.ChildBarcode,
    DATEADD(hour,-7,COALESCE(lp.ChildCreatedDate,SYSUTCDATETIME()))
FROM #LegacyParsed lp
JOIN #AltConv a
    ON a.BaseCode = lp.BaseCode
   AND a.Factor = lp.Factor
WHERE lp.ChildBarcode <> lp.BaseCode;

/* ---------- Generated EAN13 ---------- */
CREATE TABLE #GeneratedAltBarcode
(
    ProductId int NOT NULL,
    Factor int NOT NULL,
    SourcePromotionId int NOT NULL,
    Barcode nvarchar(13) NOT NULL,
    CreatedAtUtc datetime2 NOT NULL,
    PRIMARY KEY(ProductId, Factor),
    UNIQUE(Barcode)
);

;WITH Need AS
(
    SELECT
        a.ProductId,
        a.Factor,
        a.SourcePromotionId,
        a.CreatedAtUtc,
        N'29' + RIGHT(N'0000000000' + CONVERT(nvarchar(10), a.SourcePromotionId), 10) AS Body12
    FROM #AltConv a
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM #LegacyAltBarcode b
        WHERE b.ProductId = a.ProductId AND b.Factor = a.Factor
    )
), C AS
(
    SELECT *,
        (
            10 -
            (
                  CONVERT(int,SUBSTRING(Body12,1,1))
                + CONVERT(int,SUBSTRING(Body12,2,1))*3
                + CONVERT(int,SUBSTRING(Body12,3,1))
                + CONVERT(int,SUBSTRING(Body12,4,1))*3
                + CONVERT(int,SUBSTRING(Body12,5,1))
                + CONVERT(int,SUBSTRING(Body12,6,1))*3
                + CONVERT(int,SUBSTRING(Body12,7,1))
                + CONVERT(int,SUBSTRING(Body12,8,1))*3
                + CONVERT(int,SUBSTRING(Body12,9,1))
                + CONVERT(int,SUBSTRING(Body12,10,1))*3
                + CONVERT(int,SUBSTRING(Body12,11,1))
                + CONVERT(int,SUBSTRING(Body12,12,1))*3
            ) % 10
        ) % 10 AS CheckDigit
    FROM Need
)
INSERT #GeneratedAltBarcode(ProductId, Factor, SourcePromotionId, Barcode, CreatedAtUtc)
SELECT ProductId, Factor, SourcePromotionId,
       Body12 + CONVERT(nvarchar(1),CheckDigit), CreatedAtUtc
FROM C;

IF EXISTS
(
    SELECT Barcode
    FROM
    (
        SELECT fp.Code AS Barcode FROM #FinalProduct fp
        UNION ALL
        SELECT b.Barcode FROM #LegacyAltBarcode b
        UNION ALL
        SELECT g.Barcode FROM #GeneratedAltBarcode g
    ) x
    GROUP BY Barcode
    HAVING COUNT(*) > 1
)
    THROW 51013, N'Barcode collision trong expected final barcode dataset.', 1;

/* ---------- Unit manifest ---------- */
CREATE TABLE #UnitManifest
(
    UnitId int NOT NULL PRIMARY KEY,
    Code nvarchar(60) NOT NULL UNIQUE,
    Name nvarchar(400) NOT NULL UNIQUE,
    SortOrder int NOT NULL
);

;WITH Names AS
(
    SELECT UnitName FROM #BaseUnit
    UNION ALL
    SELECT UnitName FROM #AltConv
), D AS
(
    SELECT
        UnitName COLLATE SQL_Latin1_General_CP1_CI_AS AS UnitKey,
        MIN(UnitName COLLATE Latin1_General_100_BIN2) AS DisplayName
    FROM Names
    GROUP BY UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
), R AS
(
    SELECT
        ROW_NUMBER() OVER (ORDER BY UnitKey COLLATE Latin1_General_100_BIN2) AS Seq,
        DisplayName
    FROM D
)
INSERT #UnitManifest(UnitId, Code, Name, SortOrder)
SELECT
    CONVERT(int,Seq),
    N'UOM-MIG-' + RIGHT(N'0000' + CONVERT(nvarchar(10),Seq),4),
    DisplayName,
    CONVERT(int,Seq)
FROM R;

IF EXISTS (SELECT 1 FROM #UnitManifest WHERE LEN(Code) > 60 OR LEN(Name) > 400)
    THROW 51014, N'Unit Code/Name vượt target length.', 1;

/* ============================================================
   2. TARGET PREFLIGHT
   ============================================================ */

IF (SELECT COUNT(*) FROM dbo.Stores) <> 1 OR NOT EXISTS (SELECT 1 FROM dbo.Stores WHERE Id = @StoreId)
    THROW 51015, N'Rehearsal package yêu cầu đúng một Store và StoreId=1.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.check_constraints cc
    WHERE OBJECT_NAME(cc.parent_object_id) IN
          ('Products','ProductVariant','ProductUnitConversion','ProductVariantUnitBarcode')
      AND cc.is_disabled = 0
)
BEGIN
    /* The closure preflight found none. A later schema change must be reviewed. */
    THROW 51016, N'Có CHECK constraint mới trên Product pricing/catalog tables. Re-preflight required.', 1;
END;

-- Approved source exception: keep supplier IDs and all references unchanged.
-- Inherit the target Name collation so PREVIEW checks the same equality as its unique index.
SELECT TOP(0) CONVERT(int,Id) Id,Name,Note INTO #SupplierNames FROM dbo.Suppliers;
INSERT #SupplierNames(Id,Name,Note)
SELECT CONVERT(int,s.ID),LTRIM(RTRIM(CONVERT(nvarchar(400),s.Name))),NULL
FROM [__SOURCE__].dbo.Supplier s;
CREATE UNIQUE CLUSTERED INDEX IX_SupplierNames ON #SupplierNames(Id);
DECLARE @SupplierSuffix nvarchar(40)=N' [GaoStore ID 110276]';
IF EXISTS(SELECT 1 FROM #SupplierNames a JOIN #SupplierNames b ON b.Id=90173 AND b.Name=a.Name
          JOIN [__SOURCE__].dbo.Supplier sa ON sa.ID=a.Id
          JOIN [__SOURCE__].dbo.Supplier sb ON sb.ID=b.Id
          WHERE a.Id=110276 AND LTRIM(RTRIM(sa.TaxCode))=N'3801130408' AND LTRIM(RTRIM(sb.TaxCode))=N'3801130408')
BEGIN
 IF EXISTS(SELECT 1 FROM #SupplierNames WHERE Id=110276 AND
   (DATALENGTH(Name+@SupplierSuffix)>COL_LENGTH(N'dbo.Suppliers',N'Name') OR
    DATALENGTH(N'GaoStore SupplierID=110276; Tên gốc: '+Name)>COL_LENGTH(N'dbo.Suppliers',N'Note')))
  THROW 51030,N'Tên/ghi chú nhà cung cấp sau thêm hậu tố vượt độ dài; không tự cắt tên.',1;
 UPDATE #SupplierNames SET Note=N'GaoStore SupplierID=110276; Tên gốc: '+Name,
                          Name=Name+@SupplierSuffix WHERE Id=110276;
END;
IF EXISTS(SELECT Name FROM #SupplierNames GROUP BY Name HAVING COUNT_BIG(*)>1)
 THROW 51031,N'Nhà cung cấp còn trùng tên ngoài cặp đã duyệt 90173/110276. Dừng đối chiếu, không gộp hoặc tự đổi ID.',1;
SELECT N'SUPPLIER_NAME_ADJUSTMENTS' Report,Id SupplierId,
       LEFT(Name,LEN(Name)-LEN(@SupplierSuffix)) OriginalName,Name TargetName,Note
FROM #SupplierNames WHERE Id=110276 AND Note IS NOT NULL;

IF @Mode='PREVIEW'
BEGIN
 SELECT N'PRODUCT_PLAN' Report,(SELECT COUNT_BIG(*) FROM #FinalProduct) Products,(SELECT COUNT_BIG(*) FROM #AltConv) AlternateConversions,(SELECT COUNT_BIG(*) FROM #UnitManifest) Units;
 RETURN;
END;
    /* ---------- Category ---------- */
    SET IDENTITY_INSERT dbo.Category ON;
    INSERT dbo.Category
    (
        Id, ParentId, IsRewardEligible, CreatedAtUtc, CreatedBy,
        UpdatedAtUtc, UpdatedBy, IsDeleted, DeletedAtUtc, DeletedBy,
        StoreId, Code, Name, IsActive, SortOrder
    )
    VALUES
        (1,  NULL, 0, SYSUTCDATETIME(), NULL, NULL, NULL, 0, NULL, NULL, @StoreId, N'AO',          N'Áo',          1, 1),
        (19, NULL, 0, SYSUTCDATETIME(), NULL, NULL, NULL, 0, NULL, NULL, @StoreId, N'DO_GIA_DUNG', N'Đồ Gia Dụng', 1, 2),
        (20, NULL, 0, SYSUTCDATETIME(), NULL, NULL, NULL, 0, NULL, NULL, @StoreId, N'DO_AN_VAT',   N'Đồ Ăn Vặt',   1, 3),
        (21, NULL, 0, SYSUTCDATETIME(), NULL, NULL, NULL, 0, NULL, NULL, @StoreId, N'SUA',          N'Sữa',          1, 4),
        (22, NULL, 0, SYSUTCDATETIME(), NULL, NULL, NULL, 0, NULL, NULL, @StoreId, N'TRAI_CAY',     N'Trái Cây',     1, 5);
    SET IDENTITY_INSERT dbo.Category OFF;

    /* ---------- Suppliers ---------- */
    SET IDENTITY_INSERT dbo.Suppliers ON;
    INSERT dbo.Suppliers
    (
        Id, Phone, Email, Address, ContactName, TaxCode, Note,
        CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
        IsDeleted, DeletedAtUtc, DeletedBy,
        StoreId, Code, Name, IsActive, SortOrder,
        BankAccountNumber, BankAccountName, BankName
    )
    SELECT
        CONVERT(int,s.ID),
        NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(60),s.Phone))),N''),
        NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(400),s.Email))),N''),
        NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(600),s.Address))),N''),
        NULL,
        NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.TaxCode))),N''),
        sn.Note,
        COALESCE(DATEADD(hour,-7,s.CreatedDate),DATEADD(hour,-7,s.ModifiedDate),SYSUTCDATETIME()),
        NULL,
        CASE WHEN s.ModifiedDate IS NULL OR s.ModifiedDate < CONVERT(datetime2,'1900-01-01')
             THEN NULL ELSE DATEADD(hour,-7,s.ModifiedDate) END,
        NULL,
        0,NULL,NULL,
        @StoreId,
        N'SUP' + RIGHT(N'000000' + CONVERT(nvarchar(20),s.ID),6),
        sn.Name,
        CONVERT(bit,s.Status),
        0,
        /* NormalizedTaxCode là persisted computed column của GaoAppDb; SQL Server tự sinh từ TaxCode. */
        NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.BankAccountNumber))),N''),
        NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(500),s.BankAccountName))),N''),
        NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(500),s.BankName))),N'')
    FROM [__SOURCE__].dbo.Supplier s JOIN #SupplierNames sn ON sn.Id=s.ID;
    SET IDENTITY_INSERT dbo.Suppliers OFF;
    IF EXISTS(SELECT Id,Name,Note FROM #SupplierNames EXCEPT SELECT Id,Name,Note FROM dbo.Suppliers WHERE StoreId=@StoreId)
       OR EXISTS(SELECT Id,Name,Note FROM dbo.Suppliers WHERE StoreId=@StoreId EXCEPT SELECT Id,Name,Note FROM #SupplierNames)
        THROW 51032,N'Supplier ID/name/note differs from the reviewed staging plan.',1;

    /* ---------- Unit ---------- */
    SET IDENTITY_INSERT dbo.Unit ON;
    INSERT dbo.Unit
    (
        Id, Code, Name, IsActive, IsBase, SortOrder,
        CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
        IsDeleted, DeletedAtUtc, DeletedBy, StoreId
    )
    SELECT
        UnitId, Code, Name, 1, 1, SortOrder,
        SYSUTCDATETIME(), NULL, NULL, NULL,
        0,NULL,NULL,@StoreId
    FROM #UnitManifest;
    SET IDENTITY_INSERT dbo.Unit OFF;

    /* ---------- Products ---------- */
    SET IDENTITY_INSERT dbo.Products ON;
    INSERT dbo.Products
    (
        Id, Name, Alias, CategoryId, SupplierId, BrandId, TaxId, BaseUnitId,
        BasePrice, Description, Content, IsActive, IsSellable,
        IsRewardEligibleOverride, RewardBulkExcludeQuantity,
        CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
        IsDeleted, DeletedAtUtc, DeletedBy, StoreId
    )
    SELECT
        fp.ProductId,
        fp.Name,
        fp.Code,
        fp.CategoryId,
        fp.SupplierId,
        NULL,
        NULL,
        um.UnitId,
        fp.RetailPrice,
        NULL,
        NULL,
        fp.IsActive,
        fp.IsActive,
        NULL,
        NULL,
        COALESCE(DATEADD(hour,-7,fp.CreatedDate),SYSUTCDATETIME()),
        NULL,
        CASE WHEN fp.ModifiedDate IS NULL OR fp.ModifiedDate < CONVERT(datetime2,'1900-01-01')
             THEN NULL ELSE DATEADD(hour,-7,fp.ModifiedDate) END,
        NULL,
        0,NULL,NULL,@StoreId
    FROM #FinalProduct fp
    JOIN #BaseUnit bu ON bu.ProductId = fp.ProductId
    JOIN #UnitManifest um
      ON um.Name COLLATE SQL_Latin1_General_CP1_CI_AS = bu.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS;
    SET IDENTITY_INSERT dbo.Products OFF;

    /* ---------- ProductVariant ---------- */
    SET IDENTITY_INSERT dbo.ProductVariant ON;
    INSERT dbo.ProductVariant
    (
        Id, ProductId, Sku, ProductVariantName, ProductVariantNameNormalized,
        CostPrice, Price, IsActive, PrimaryProductImageId, HasInputInvoice,
        WholesalePrice,
        CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
        IsDeleted, DeletedAtUtc, DeletedBy, StoreId
    )
    SELECT
        fp.ProductId,
        fp.ProductId,
        fp.Code,
        fp.Name,
        TRANSLATE
        (
            LOWER(LTRIM(RTRIM(fp.Name))),
            N'àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ',
            N'aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd'
        ),
        fp.PurchasePrice,
        fp.RetailPrice,
        fp.IsActive,
        NULL,
        fp.HasInputInvoice,
        fp.WholesalePrice,
        COALESCE(DATEADD(hour,-7,fp.CreatedDate),SYSUTCDATETIME()),
        NULL,
        CASE WHEN fp.ModifiedDate IS NULL OR fp.ModifiedDate < CONVERT(datetime2,'1900-01-01')
             THEN NULL ELSE DATEADD(hour,-7,fp.ModifiedDate) END,
        NULL,
        0,NULL,NULL,@StoreId
    FROM #FinalProduct fp;
    SET IDENTITY_INSERT dbo.ProductVariant OFF;

    /* ---------- ProductUnitConversion: base ---------- */
    INSERT dbo.ProductUnitConversion
    (
        ProductVariantId, UnitId, Factor, IsBaseUnit, IsDefaultForSale,
        Price, WholesalePrice, IsActive, SortOrder,
        CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
        IsDeleted, DeletedAtUtc, DeletedBy, StoreId
    )
    SELECT
        fp.ProductId,
        um.UnitId,
        CONVERT(decimal(18,4),1),
        1,1,
        fp.RetailPrice,
        fp.WholesalePrice,
        1,0,
        COALESCE(DATEADD(hour,-7,fp.CreatedDate),SYSUTCDATETIME()),
        NULL,NULL,NULL,0,NULL,NULL,@StoreId
    FROM #FinalProduct fp
    JOIN #BaseUnit bu ON bu.ProductId = fp.ProductId
    JOIN #UnitManifest um
      ON um.Name COLLATE SQL_Latin1_General_CP1_CI_AS = bu.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS;

    /* ---------- ProductUnitConversion: alternate ---------- */
    ;WITH R AS
    (
        SELECT
            a.*,
            ROW_NUMBER() OVER (PARTITION BY a.ProductId ORDER BY a.Factor, a.UnitName) AS Seq
        FROM #AltConv a
    )
    INSERT dbo.ProductUnitConversion
    (
        ProductVariantId, UnitId, Factor, IsBaseUnit, IsDefaultForSale,
        Price, WholesalePrice, IsActive, SortOrder,
        CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
        IsDeleted, DeletedAtUtc, DeletedBy, StoreId
    )
    SELECT
        r.ProductId,
        um.UnitId,
        CONVERT(decimal(18,4),r.Factor),
        0,0,
        r.MemberPrice,
        r.WholesalePrice,
        1,
        CONVERT(int,r.Seq),
        r.CreatedAtUtc,
        NULL,NULL,NULL,0,NULL,NULL,@StoreId
    FROM R r
    JOIN #UnitManifest um
      ON um.Name COLLATE SQL_Latin1_General_CP1_CI_AS = r.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS;

    /* Conversion maps after identity insert. */
    DROP TABLE IF EXISTS #BaseConvMap;
    CREATE TABLE #BaseConvMap(ProductId int PRIMARY KEY, ConversionId int NOT NULL);
    INSERT #BaseConvMap(ProductId,ConversionId)
    SELECT p.Id, c.Id
    FROM dbo.Products p
    JOIN dbo.ProductUnitConversion c
      ON c.ProductVariantId = p.Id
     AND c.IsBaseUnit = 1
     AND c.IsDeleted = 0
    WHERE p.StoreId = @StoreId;

    DROP TABLE IF EXISTS #AltConvMap;
    CREATE TABLE #AltConvMap(ProductId int NOT NULL, Factor int NOT NULL, ConversionId int NOT NULL,
                             PRIMARY KEY(ProductId,Factor));
    INSERT #AltConvMap(ProductId,Factor,ConversionId)
    SELECT a.ProductId, a.Factor, c.Id
    FROM #AltConv a
    JOIN #UnitManifest um
      ON um.Name COLLATE SQL_Latin1_General_CP1_CI_AS = a.UnitName COLLATE SQL_Latin1_General_CP1_CI_AS
    JOIN dbo.ProductUnitConversion c
      ON c.ProductVariantId = a.ProductId
     AND c.UnitId = um.UnitId
     AND c.IsBaseUnit = 0
     AND c.IsDeleted = 0;

    /* ---------- Base barcodes: BarcodeType Legacy = 4 ---------- */
    INSERT dbo.ProductVariantUnitBarcode
    (
        ProductUnitConversionId, Barcode, BarcodeType, IsPrimary, IsActive, Note,
        CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
        IsDeleted, DeletedAtUtc, DeletedBy, StoreId
    )
    SELECT
        m.ConversionId,
        fp.Code,
        4,
        1,1,
        N'Legacy ProductDetail.Id=' + CONVERT(nvarchar(20),fp.ProductId) + N'; base',
        COALESCE(DATEADD(hour,-7,fp.CreatedDate),SYSUTCDATETIME()),
        NULL,NULL,NULL,0,NULL,NULL,@StoreId
    FROM #FinalProduct fp
    JOIN #BaseConvMap m ON m.ProductId = fp.ProductId;

    /* ---------- Alternate legacy barcodes ---------- */
    ;WITH R AS
    (
        SELECT
            b.*,
            ROW_NUMBER() OVER
            (
                PARTITION BY b.ProductId,b.Factor
                ORDER BY b.ChildProductDetailId
            ) AS PrimaryRank
        FROM #LegacyAltBarcode b
    )
    INSERT dbo.ProductVariantUnitBarcode
    (
        ProductUnitConversionId, Barcode, BarcodeType, IsPrimary, IsActive, Note,
        CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
        IsDeleted, DeletedAtUtc, DeletedBy, StoreId
    )
    SELECT
        m.ConversionId,
        r.Barcode,
        4,
        CASE WHEN r.PrimaryRank = 1 THEN 1 ELSE 0 END,
        1,
        N'Legacy child ProductDetail.Id=' + CONVERT(nvarchar(20),r.ChildProductDetailId)
            + N'; factor=' + CONVERT(nvarchar(20),r.Factor),
        r.CreatedAtUtc,
        NULL,NULL,NULL,0,NULL,NULL,@StoreId
    FROM R r
    JOIN #AltConvMap m ON m.ProductId = r.ProductId AND m.Factor = r.Factor;

    /* ---------- Generated internal barcodes: BarcodeType Internal = 0 ---------- */
    INSERT dbo.ProductVariantUnitBarcode
    (
        ProductUnitConversionId, Barcode, BarcodeType, IsPrimary, IsActive, Note,
        CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
        IsDeleted, DeletedAtUtc, DeletedBy, StoreId
    )
    SELECT
        m.ConversionId,
        g.Barcode,
        0,
        1,1,
        N'Generated migration EAN13; Promotion.Id=' + CONVERT(nvarchar(20),g.SourcePromotionId),
        g.CreatedAtUtc,
        NULL,NULL,NULL,0,NULL,NULL,@StoreId
    FROM #GeneratedAltBarcode g
    JOIN #AltConvMap m ON m.ProductId = g.ProductId AND m.Factor = g.Factor;

    /* Core in-transaction invariants before commit/rollback decision. */
    IF (SELECT COUNT(*) FROM dbo.Products WHERE StoreId=@StoreId) <> (SELECT COUNT(*) FROM #FinalProduct)
        THROW 51020, N'Product count mismatch trong transaction.', 1;

    IF (SELECT COUNT(*) FROM dbo.ProductVariant WHERE StoreId=@StoreId) <> (SELECT COUNT(*) FROM #FinalProduct)
        THROW 51021, N'Variant count mismatch trong transaction.', 1;

    IF (SELECT COUNT(*) FROM dbo.ProductUnitConversion WHERE StoreId=@StoreId AND IsBaseUnit=1 AND IsDeleted=0)
       <> (SELECT COUNT(*) FROM #FinalProduct)
        THROW 51022, N'Base conversion count mismatch trong transaction.', 1;

    IF (SELECT COUNT(*) FROM dbo.ProductUnitConversion WHERE StoreId=@StoreId AND IsBaseUnit=0 AND IsDeleted=0)
       <> (SELECT COUNT(*) FROM #AltConv)
        THROW 51023, N'Alternate conversion count mismatch trong transaction.', 1;

    IF EXISTS
    (
        SELECT ProductUnitConversionId
        FROM dbo.ProductVariantUnitBarcode
        WHERE StoreId=@StoreId AND IsDeleted=0 AND IsActive=1 AND IsPrimary=1
        GROUP BY ProductUnitConversionId
        HAVING COUNT(*) <> 1
    )
        THROW 51024, N'Một conversion có khác 1 active primary barcode.', 1;

    IF EXISTS
    (
        SELECT Barcode
        FROM dbo.ProductVariantUnitBarcode
        WHERE StoreId=@StoreId AND IsDeleted=0 AND IsActive=1
        GROUP BY Barcode
        HAVING COUNT(*) > 1
    )
        THROW 51025, N'Duplicate active barcode sau import.', 1;


SELECT N'PRODUCT_STAGED_AND_VERIFIED' Report,COUNT_BIG(*) Products FROM dbo.Products;

-- Inactive financial adjustment catalog used by the historical sales package.
-- No inventory movement is created for this SKU. IDs derive from the freshly imported catalog.
IF EXISTS(SELECT 1 FROM dbo.ProductVariant WHERE Sku=N'LEGACY-ADJUSTMENT')
 OR EXISTS(SELECT 1 FROM dbo.Products WHERE Alias=N'LEGACY-ADJUSTMENT-TECH')
 OR EXISTS(SELECT 1 FROM dbo.Unit WHERE Code=N'LEGACY')
 OR EXISTS(SELECT 1 FROM dbo.Suppliers WHERE Code=N'LEGACY-UNKNOWN')
 OR EXISTS(SELECT 1 FROM dbo.Category WHERE Code=N'LEGACY-HISTORY')
 THROW 55420,'Reserved historical adjustment catalog code already exists in source/import.',1;
DECLARE @TechCategory int=(SELECT MAX(Id)+1 FROM dbo.Category),
 @TechSupplier int=(SELECT MAX(Id)+1 FROM dbo.Suppliers),
 @TechUnit int=(SELECT MAX(Id)+1 FROM dbo.Unit),
 @TechProduct int=(SELECT MAX(Id)+1 FROM dbo.Products),@TechTime datetime2(7)=SYSUTCDATETIME();
SET IDENTITY_INSERT dbo.Category ON;
INSERT dbo.Category(Id,Code,Name,IsActive,IsRewardEligible,SortOrder,CreatedAtUtc,IsDeleted,StoreId)
VALUES(@TechCategory,N'LEGACY-HISTORY',N'Sản phẩm lịch sử',0,0,0,@TechTime,0,1);
SET IDENTITY_INSERT dbo.Category OFF;
SET IDENTITY_INSERT dbo.Suppliers ON;
INSERT dbo.Suppliers(Id,Code,Name,IsActive,SortOrder,CreatedAtUtc,IsDeleted,StoreId)
VALUES(@TechSupplier,N'LEGACY-UNKNOWN',N'Nhà cung cấp legacy không xác định',0,0,@TechTime,0,1);
SET IDENTITY_INSERT dbo.Suppliers OFF;
SET IDENTITY_INSERT dbo.Unit ON;
INSERT dbo.Unit(Id,Code,Name,IsActive,IsBase,SortOrder,CreatedAtUtc,IsDeleted,StoreId)
VALUES(@TechUnit,N'LEGACY',N'Đơn vị legacy',0,1,@TechUnit,@TechTime,0,1);
SET IDENTITY_INSERT dbo.Unit OFF;
-- Historical codes missing from the current catalog: preserve the TEST lookup contract.
-- Never guess a conversion where a SKU/barcode already exists (even if ambiguous).
SELECT DISTINCT d.ProductCode COLLATE DATABASE_DEFAULT Code INTO #MissingHistoryCodes
FROM [__SOURCE__].dbo.OrderDetail d JOIN [__SOURCE__].dbo.[Order] o ON o.ID=d.OrderID
WHERE o.OrderCategoryID=1 AND ISNULL(o.Total,0)>=0 AND d.Quantity>0
 AND ISNULL(d.Price,0)>=0 AND ISNULL(d.Total,0)>=0
 AND NOT EXISTS(SELECT 1 FROM dbo.ProductVariant v WHERE v.Sku=d.ProductCode COLLATE DATABASE_DEFAULT AND v.StoreId=1)
 AND NOT EXISTS(SELECT 1 FROM dbo.ProductVariantUnitBarcode b WHERE b.Barcode=d.ProductCode COLLATE DATABASE_DEFAULT AND b.StoreId=1);
IF EXISTS(SELECT 1 FROM #MissingHistoryCodes WHERE NULLIF(LTRIM(RTRIM(Code)),N'') IS NULL OR LEN(Code)>50)
 THROW 55421,'Historical source code is empty or exceeds 50 characters.',1;
SELECT CONVERT(int,CONVERT(bigint,@TechProduct)-1+ROW_NUMBER() OVER(ORDER BY Code)) Id,Code INTO #HistoryCatalog FROM #MissingHistoryCodes;
SET IDENTITY_INSERT dbo.Products ON;
INSERT dbo.Products(Id,Name,Alias,CategoryId,SupplierId,BaseUnitId,BasePrice,IsActive,IsSellable,CreatedAtUtc,IsDeleted,StoreId)
SELECT Id,N'[Legacy] '+Code,N'LEGACY-'+Code,@TechCategory,@TechSupplier,@TechUnit,0,0,0,@TechTime,0,1 FROM #HistoryCatalog;
SET IDENTITY_INSERT dbo.Products OFF;
SET IDENTITY_INSERT dbo.ProductVariant ON;
INSERT dbo.ProductVariant(Id,ProductId,Sku,ProductVariantName,CostPrice,Price,IsActive,HasInputInvoice,CreatedAtUtc,IsDeleted,StoreId)
SELECT Id,Id,Code,N'[Legacy] '+Code,0,0,0,0,@TechTime,0,1 FROM #HistoryCatalog;
SET IDENTITY_INSERT dbo.ProductVariant OFF;
INSERT dbo.ProductUnitConversion(ProductVariantId,UnitId,Factor,IsBaseUnit,IsDefaultForSale,Price,IsActive,SortOrder,CreatedAtUtc,IsDeleted,StoreId)
SELECT Id,@TechUnit,1,1,1,0,0,0,@TechTime,0,1 FROM #HistoryCatalog;
SET @TechProduct=(SELECT MAX(Id)+1 FROM dbo.Products);
SET IDENTITY_INSERT dbo.Products ON;
INSERT dbo.Products(Id,Name,Alias,CategoryId,SupplierId,BaseUnitId,BasePrice,IsActive,IsSellable,CreatedAtUtc,IsDeleted,StoreId)
VALUES(@TechProduct,N'[Legacy] Điều chỉnh giá',N'LEGACY-ADJUSTMENT-TECH',@TechCategory,@TechSupplier,@TechUnit,0,0,0,@TechTime,0,1);
SET IDENTITY_INSERT dbo.Products OFF;
SET IDENTITY_INSERT dbo.ProductVariant ON;
INSERT dbo.ProductVariant(Id,ProductId,Sku,ProductVariantName,CostPrice,Price,IsActive,HasInputInvoice,CreatedAtUtc,IsDeleted,StoreId)
VALUES(@TechProduct,@TechProduct,N'LEGACY-ADJUSTMENT',N'[Legacy] Điều chỉnh giá',0,0,0,0,@TechTime,0,1);
SET IDENTITY_INSERT dbo.ProductVariant OFF;
INSERT dbo.ProductUnitConversion(ProductVariantId,UnitId,Factor,IsBaseUnit,IsDefaultForSale,Price,IsActive,SortOrder,CreatedAtUtc,IsDeleted,StoreId)
VALUES(@TechProduct,@TechUnit,1,1,1,0,0,0,@TechTime,0,1);
SELECT N'INACTIVE_FINANCIAL_CATALOG' Report,@TechProduct ProductId,@TechCategory CategoryId,@TechSupplier SupplierId,@TechUnit UnitId;
SELECT N'INACTIVE_HISTORICAL_CODES' Report,Id,Code FROM #HistoryCatalog ORDER BY Code;
