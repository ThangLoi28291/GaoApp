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
