/* GaoStore -> GaoApp invoice-stock, contract 2026-09-23.
   ONLY owns StoreId + keys starting GSTORE-IIS-V1| in the supplemental table.
   Read README before COMMIT. Run against the destination database.
   Source tables are read only. Requires GaoApp LegacyOutbound=4 support.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 30000;
-- SETTINGS BEGIN (runner replaces this block with typed SqlParameters)
DECLARE @Mode varchar(12) = 'PREVIEW'; -- PREVIEW / DRYRUN / COMMIT / VERIFY
DECLARE @SourceDatabase sysname = N'DataGaoStore';
DECLARE @ExpectedTargetDatabase sysname = N'GaoAppDb';
DECLARE @StoreId int = 1, @WarehouseId int = 1;
DECLARE @CutoffExclusive date = NULL; -- NULL: day after latest eligible document date
DECLARE @AllowCommit bit = 0, @AllowRetire bit = 0;
-- SETTINGS END
DECLARE @Prefix nvarchar(40)=N'GSTORE-IIS-V1|', @StartDate date='20250601';
DECLARE @Now datetime2(7)=SYSUTCDATETIME(), @IdentityOn bit=0;
IF @@TRANCOUNT<>0 THROW 51000,N'Run in a connection without an ambient transaction.',1;
IF DB_NAME()<>@ExpectedTargetDatabase OR DB_NAME()=@SourceDatabase
    THROW 51000,N'Wrong target database or source equals target.',1;
IF DB_ID(@SourceDatabase) IS NULL THROW 51000,N'Source database does not exist.',1;
IF @Mode NOT IN ('PREVIEW','DRYRUN','COMMIT','VERIFY') THROW 51000,N'Invalid mode.',1;
IF @Mode='COMMIT' AND @AllowCommit<>1 THROW 51000,N'COMMIT requires AllowCommit after reviewing the dry run.',1;
IF OBJECT_ID(N'dbo.InvoiceInputStockSupplementalMovements',N'U') IS NULL
    THROW 51000,N'Apply GaoApp supplemental-movement schema migration first.',1;
IF COL_LENGTH(N'dbo.InvoiceInputStockSupplementalMovements',N'LegacyInvoiceNumber') IS NULL
    THROW 51000,N'Apply AddInvoiceStockLegacyDocumentReferences schema migration first.',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id=OBJECT_ID(N'dbo.InvoiceInputStockSupplementalMovements') AND is_disabled=0)
    THROW 51000,N'Unexpected target triggers require review.',1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @LockResult int;
 EXEC @LockResult=sys.sp_getapplock @Resource=N'GSTORE-IIS-V1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @LockResult<0 THROW 51000,N'Could not acquire migration lock.',1;
 -- Same store lock used by issuance: no issuance can reserve halfway through the import.
 IF NOT EXISTS(SELECT 1 FROM dbo.Stores WITH(UPDLOCK,HOLDLOCK) WHERE Id=@StoreId AND IsDeleted=0)
     THROW 51000,N'Target store missing.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses w JOIN dbo.LegalEntities le ON le.Id=w.LegalEntityId
               WHERE w.Id=@WarehouseId AND w.StoreId=@StoreId AND w.IsDeleted=0
                 AND le.StoreId=@StoreId AND le.IsDeleted=0)
     THROW 51000,N'Target warehouse/legal entity does not belong to the store.',1;

 CREATE TABLE #Raw(
   LegacySourceKey nvarchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
   Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
   LocalAt datetime2(7) NOT NULL, RawQuantity decimal(28,4) NOT NULL,
   MovementType int NOT NULL, SourceReference nvarchar(250) NULL,
   LegacyOrderId bigint NULL,LegacyInvoiceNumber nvarchar(200) NULL,LegacyInvoiceSymbol nvarchar(200) NULL);
 DECLARE @SourceSql nvarchar(max),@Q nvarchar(258)=QUOTENAME(@SourceDatabase);
 SET @SourceSql=N'
 IF EXISTS(SELECT 1 FROM '+@Q+N'.dbo.SanPhamKhaiThue
           GROUP BY LTRIM(RTRIM(REPLACE(Code,NCHAR(160),N'' ''))) HAVING COUNT_BIG(*)>1)
     THROW 51000,N''Duplicate normalized opening product codes require review.'',1;
 IF EXISTS(SELECT 1 FROM '+@Q+N'.dbo.SanPhamKhaiThue WHERE COALESCE(SLDauKy,0)<>0 AND NULLIF(LTRIM(RTRIM(Code)),N'''') IS NULL)
     THROW 51000,N''Nonzero opening with empty product code.'',1;
 IF EXISTS(SELECT 1 FROM '+@Q+N'.dbo.Product WHERE NULLIF(LTRIM(RTRIM(InvoiceSeries)),N'''') IS NOT NULL AND CreatedDate IS NULL)
     THROW 51000,N''Input document with invoice series but no date.'',1;
 IF EXISTS(SELECT 1 FROM '+@Q+N'.dbo.InvoiceHead WHERE NULLIF(LTRIM(RTRIM(InvoiceNumber)),N'''') IS NOT NULL AND IssuedDate IS NULL)
     THROW 51000,N''Numbered output invoice has no issue date.'',1;
 IF @Cutoff IS NULL
 BEGIN
   SELECT @Cutoff=DATEADD(day,1,MAX(D)) FROM (
     SELECT CONVERT(date,CreatedDate) D FROM '+@Q+N'.dbo.Product
      WHERE CreatedDate>=@Start AND NULLIF(LTRIM(RTRIM(InvoiceSeries)),N'''') IS NOT NULL
     UNION ALL SELECT CONVERT(date,IssuedDate) FROM '+@Q+N'.dbo.InvoiceHead
      WHERE IssuedDate>=@Start AND NULLIF(LTRIM(RTRIM(InvoiceNumber)),N'''') IS NOT NULL
     UNION ALL SELECT @Start) dates;
 END;
 IF @Cutoff<=@Start THROW 51000,N''Cutoff must be after 2025-06-01.'',1;
 IF EXISTS(SELECT 1 FROM '+@Q+N'.dbo.Product WHERE CreatedDate>=@Start AND CreatedDate<@Cutoff
     AND NULLIF(LTRIM(RTRIM(InvoiceSeries)),N'''') IS NOT NULL
     AND ((Warranty IS NULL AND COALESCE(Quantity,0)<>0) OR (COALESCE(Warranty,0)<>0 AND NULLIF(LTRIM(RTRIM(Code)),N'''') IS NULL)))
     THROW 51000,N''Input has nonzero Quantity but null Warranty, or nonzero Warranty without code.'',1;
 IF EXISTS(SELECT 1 FROM '+@Q+N'.dbo.InvoiceDetail d JOIN '+@Q+N'.dbo.InvoiceHead h ON h.Id=d.OrderID
     WHERE h.IssuedDate>=@Start AND h.IssuedDate<@Cutoff AND NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'''') IS NOT NULL
       AND (d.Quantity IS NULL OR (d.Quantity<>0 AND NULLIF(LTRIM(RTRIM(d.ProductCode)),N'''') IS NULL)))
     THROW 51000,N''Output detail is missing quantity or product code.'',1;
 INSERT #Raw
 SELECT CONCAT(@Prefix,N''O|'',Id),LTRIM(RTRIM(REPLACE(Code,NCHAR(160),N'' ''))),@Start,SLDauKy,1,N''SanPhamKhaiThue.SLDauKy'',NULL,NULL,NULL
 FROM '+@Q+N'.dbo.SanPhamKhaiThue WHERE COALESCE(SLDauKy,0)<>0;
 INSERT #Raw
 SELECT CONCAT(@Prefix,N''I|'',ID),LTRIM(RTRIM(REPLACE(Code,NCHAR(160),N'' ''))),CreatedDate,Warranty,2,CONCAT(N''Product.ID='',ID,N''; series='',InvoiceSeries),NULL,NULLIF(LTRIM(RTRIM(InvoiceSeries)),N''''),NULLIF(LTRIM(RTRIM(InvoiceTemplateCode)),N'''')
 FROM '+@Q+N'.dbo.Product WHERE CreatedDate>=@Start AND CreatedDate<@Cutoff
   AND NULLIF(LTRIM(RTRIM(InvoiceSeries)),N'''') IS NOT NULL AND COALESCE(Warranty,0)<>0;
 INSERT #Raw
 SELECT CONCAT(@Prefix,N''X|'',d.ID),LTRIM(RTRIM(REPLACE(d.ProductCode,NCHAR(160),N'' ''))),h.IssuedDate,-d.Quantity,4,
   CONCAT(N''InvoiceHead.Id='',h.Id,N''; invoice='',h.InvoiceNumber,N''; detail='',d.ID),d.OrderID,NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N''''),NULLIF(LTRIM(RTRIM(h.InvoiceSeries)),N'''')
 FROM '+@Q+N'.dbo.InvoiceDetail d JOIN '+@Q+N'.dbo.InvoiceHead h ON h.Id=d.OrderID
 WHERE h.IssuedDate>=@Start AND h.IssuedDate<@Cutoff AND NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'''') IS NOT NULL AND d.Quantity<>0;
 SELECT N''SOURCE_QUALITY'' AS Report,
   (SELECT COUNT_BIG(*) FROM '+@Q+N'.dbo.Product WHERE CreatedDate>=@Start AND CreatedDate<@Cutoff AND NULLIF(LTRIM(RTRIM(InvoiceSeries)),N'''') IS NOT NULL AND Warranty IS NULL) AS NullWarrantyZeroQuantityRows,
   (SELECT COUNT_BIG(*) FROM '+@Q+N'.dbo.InvoiceHead h WHERE h.IssuedDate>=@Start AND h.IssuedDate<@Cutoff AND NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'''') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM '+@Q+N'.dbo.InvoiceDetail d WHERE d.OrderID=h.Id)) AS NumberedHeadsWithoutDetails;
 ';
 EXEC sys.sp_executesql @SourceSql,N'@Start date,@Cutoff date OUTPUT,@Prefix nvarchar(40)',@StartDate,@CutoffExclusive OUTPUT,@Prefix;
 IF NOT EXISTS(SELECT 1 FROM #Raw) THROW 51000,N'Empty source: refusing to clear previous migration.',1;
 CREATE INDEX IX_Raw_Code ON #Raw(Code);

 -- Materialize target paths to avoid repeated cross-database CTE scans.
 CREATE TABLE #Path(Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
                    VariantId int NOT NULL,Factor decimal(18,4) NOT NULL);
 INSERT #Path
 SELECT LTRIM(RTRIM(REPLACE(v.Sku,NCHAR(160),N' '))),v.Id,1 FROM dbo.ProductVariant v
 JOIN dbo.Products p ON p.Id=v.ProductId JOIN dbo.Unit u ON u.Id=p.BaseUnitId
 WHERE v.StoreId=@StoreId AND p.StoreId=@StoreId AND u.StoreId=@StoreId
   AND v.IsDeleted=0 AND p.IsDeleted=0 AND u.IsDeleted=0 AND NULLIF(LTRIM(RTRIM(v.Sku)),N'') IS NOT NULL;
 INSERT #Path
 SELECT LTRIM(RTRIM(REPLACE(b.Barcode,NCHAR(160),N' '))),v.Id,c.Factor
 FROM dbo.ProductVariantUnitBarcode b JOIN dbo.ProductUnitConversion c ON c.Id=b.ProductUnitConversionId
 JOIN dbo.ProductVariant v ON v.Id=c.ProductVariantId JOIN dbo.Products p ON p.Id=v.ProductId
 JOIN dbo.Unit u ON u.Id=p.BaseUnitId
 WHERE b.StoreId=@StoreId AND c.StoreId=@StoreId AND v.StoreId=@StoreId AND p.StoreId=@StoreId AND u.StoreId=@StoreId
   AND b.IsDeleted=0 AND c.IsDeleted=0 AND v.IsDeleted=0 AND p.IsDeleted=0 AND u.IsDeleted=0
   AND NULLIF(LTRIM(RTRIM(b.Barcode)),N'') IS NOT NULL;
 CREATE INDEX IX_Path_Code ON #Path(Code);
 SELECT Code,COUNT(DISTINCT VariantId) AS Variants,COUNT(DISTINCT Factor) AS Factors,
        MIN(VariantId) AS VariantId,MIN(Factor) AS Factor INTO #Map FROM #Path GROUP BY Code;
 CREATE UNIQUE CLUSTERED INDEX IX_Map_Code ON #Map(Code);
 IF EXISTS(SELECT 1 FROM #Map m JOIN #Raw r ON r.Code=m.Code WHERE m.Variants<>1 OR m.Factors<>1 OR m.Factor<=0)
     THROW 51000,N'Ambiguous product/factor or nonpositive conversion requires review.',1;
 SELECT r.* INTO #Skipped FROM #Raw r LEFT JOIN #Map m ON m.Code=r.Code WHERE m.Code IS NULL;
 IF EXISTS(SELECT 1 FROM #Raw r JOIN #Map m ON m.Code=r.Code WHERE TRY_CONVERT(decimal(18,4),r.RawQuantity) IS NULL)
     THROW 51000,N'Source quantity exceeds supported precision.',1;
 SELECT r.*,m.VariantId,m.Factor,CONVERT(decimal(18,4),r.RawQuantity)*m.Factor AS WideQuantity
 INTO #Converted FROM #Raw r JOIN #Map m ON m.Code=r.Code;
 IF EXISTS(SELECT 1 FROM #Converted WHERE TRY_CONVERT(decimal(18,4),WideQuantity) IS NULL
     OR CONVERT(decimal(37,8),TRY_CONVERT(decimal(18,4),WideQuantity))<>WideQuantity)
     THROW 51000,N'Conversion overflows or loses precision at 4 decimals.',1;
 CREATE TABLE #Expected(
   LegacySourceKey nvarchar(200) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
   StoreId int NOT NULL,WarehouseId int NOT NULL,ProductVariantId int NOT NULL,
   EffectiveAtUtc datetime2(7) NOT NULL,QuantityChange decimal(18,4) NOT NULL,
   MovementType int NOT NULL,SourcePeriod nvarchar(50) NOT NULL,Note nvarchar(1000) NOT NULL,
   LegacyOrderId bigint NULL,LegacyInvoiceNumber nvarchar(200) NULL,LegacyInvoiceSymbol nvarchar(200) NULL);
 INSERT #Expected
 SELECT LegacySourceKey,@StoreId,@WarehouseId,VariantId,DATEADD(hour,-7,LocalAt),CONVERT(decimal(18,4),WideQuantity),
   MovementType,CONVERT(char(7),LocalAt,126),
   CONCAT(N'GaoStore invoice-stock v1; code=',Code,N'; raw=',RawQuantity,N'; factor=',Factor,N'; ',SourceReference),LegacyOrderId,LegacyInvoiceNumber,LegacyInvoiceSymbol
 FROM #Converted;
 IF NOT EXISTS(SELECT 1 FROM #Expected) THROW 51000,N'No mapped movements: refusing import.',1;
 IF EXISTS(SELECT 1 FROM dbo.InvoiceInputStockSupplementalMovements
    WHERE StoreId=@StoreId AND LegacySourceKey LIKE @Prefix+N'%' AND WarehouseId<>@WarehouseId)
    THROW 51000,N'Existing migration belongs to another warehouse; do not move history implicitly.',1;
 IF EXISTS(SELECT 1 FROM dbo.InvoiceInputStockSupplementalMovements
    WHERE StoreId=@StoreId AND LegacySourceKey LIKE @Prefix+N'%' AND IsDeleted=0
      AND EffectiveAtUtc>=DATEADD(hour,-7,CONVERT(datetime2,@CutoffExclusive)))
    THROW 51000,N'Cutoff is older than existing migration. Refusing to truncate history.',1;

 DECLARE @ExpectedRows bigint=(SELECT COUNT_BIG(*) FROM #Expected),@Inserted bigint=0,@Updated bigint=0,@Retired bigint=0;
 SELECT e.LegacySourceKey INTO #Updates FROM #Expected e
 JOIN dbo.InvoiceInputStockSupplementalMovements t ON t.StoreId=e.StoreId AND t.LegacySourceKey=e.LegacySourceKey
 WHERE t.IsDeleted=1 OR EXISTS(
   SELECT e.WarehouseId,e.ProductVariantId,e.EffectiveAtUtc,e.QuantityChange,e.MovementType,e.SourcePeriod,e.Note,e.LegacyOrderId,e.LegacyInvoiceNumber,e.LegacyInvoiceSymbol
   EXCEPT SELECT t.WarehouseId,t.ProductVariantId,t.EffectiveAtUtc,t.QuantityChange,t.MovementType,t.SourcePeriod,t.Note,t.LegacyOrderId,t.LegacyInvoiceNumber,t.LegacyInvoiceSymbol);
 SELECT t.Id INTO #Retires FROM dbo.InvoiceInputStockSupplementalMovements t
 WHERE t.StoreId=@StoreId AND t.LegacySourceKey LIKE @Prefix+N'%' AND t.IsDeleted=0
   AND NOT EXISTS(SELECT 1 FROM #Expected e WHERE e.LegacySourceKey=t.LegacySourceKey);
 DECLARE @PlanInsert bigint=(SELECT COUNT_BIG(*) FROM #Expected e WHERE NOT EXISTS(
     SELECT 1 FROM dbo.InvoiceInputStockSupplementalMovements t WHERE t.StoreId=e.StoreId AND t.LegacySourceKey=e.LegacySourceKey)),
   @PlanUpdate bigint=(SELECT COUNT_BIG(*) FROM #Updates),@PlanRetire bigint=(SELECT COUNT_BIG(*) FROM #Retires);
 SELECT N'CONTRACT' AS Report,DB_NAME() AS TargetDatabase,@SourceDatabase AS SourceDatabase,@Mode AS Mode,
   @StoreId AS StoreId,@WarehouseId AS WarehouseId,@StartDate AS StartLocalInclusive,@CutoffExclusive AS CutoffLocalExclusive;
 SELECT N'STAGE' AS Report,(SELECT COUNT(DISTINCT Code) FROM #Raw) AS SourceCodes,
   (SELECT COUNT(DISTINCT Code) FROM #Converted) AS MappedCodes,(SELECT COUNT(DISTINCT Code) FROM #Skipped) AS SkippedCodes,
   (SELECT COUNT(DISTINCT Code) FROM #Converted WHERE Factor<>1) AS ConvertedCodes,
   (SELECT COUNT(DISTINCT ProductVariantId) FROM #Expected) AS TargetVariants,@ExpectedRows AS ExpectedMovements;
 SELECT N'PLAN' AS Report,@PlanInsert AS InsertRows,@PlanUpdate AS UpdateRows,@PlanRetire AS RetireRows,
   (SELECT COUNT_BIG(*) FROM dbo.InvoiceInputStockSupplementalMovements WHERE StoreId=@StoreId AND LegacySourceKey NOT LIKE @Prefix+N'%' AND IsDeleted=0) AS OtherSupplementalRows;
 SELECT N'SKIPPED_CODES' AS Report,Code,
   SUM(CASE WHEN MovementType=1 THEN RawQuantity ELSE 0 END) AS OpeningRawQty,
   SUM(CASE WHEN MovementType=2 THEN RawQuantity ELSE 0 END) AS InRawQty,
   -SUM(CASE WHEN MovementType=4 THEN RawQuantity ELSE 0 END) AS OutRawQty,
   SUM(CASE WHEN MovementType=2 THEN 1 ELSE 0 END) AS InRows,SUM(CASE WHEN MovementType=4 THEN 1 ELSE 0 END) AS OutRows
 FROM #Skipped GROUP BY Code ORDER BY Code;
 SELECT N'VARIANT_BALANCES' AS Report,e.ProductVariantId,v.Sku,MAX(u.Name) AS BaseUnit,
   SUM(CASE WHEN e.MovementType=1 THEN e.QuantityChange ELSE 0 END) AS OpeningQty,
   SUM(CASE WHEN e.MovementType=2 THEN e.QuantityChange ELSE 0 END) AS InQty,
   -SUM(CASE WHEN e.MovementType=4 THEN e.QuantityChange ELSE 0 END) AS OutQty,SUM(e.QuantityChange) AS ClosingQty
 FROM #Expected e JOIN dbo.ProductVariant v ON v.Id=e.ProductVariantId JOIN dbo.Products p ON p.Id=v.ProductId
 JOIN dbo.Unit u ON u.Id=p.BaseUnitId GROUP BY e.ProductVariantId,v.Sku ORDER BY e.ProductVariantId;
 SELECT N'VARIANT_MONTH_MOVEMENTS' AS Report,ProductVariantId,SourcePeriod,
   SUM(CASE WHEN MovementType=1 THEN QuantityChange ELSE 0 END) AS InitialOpeningQty,
   SUM(CASE WHEN MovementType=2 THEN QuantityChange ELSE 0 END) AS InQty,
   -SUM(CASE WHEN MovementType=4 THEN QuantityChange ELSE 0 END) AS OutQty,SUM(QuantityChange) AS NetChange
 FROM #Expected GROUP BY ProductVariantId,SourcePeriod ORDER BY ProductVariantId,SourcePeriod;
 SELECT N'NEGATIVE_SUMMARY' AS Report,COUNT_BIG(*) AS NegativeVariants FROM (
   SELECT ProductVariantId FROM #Expected GROUP BY ProductVariantId HAVING SUM(QuantityChange)<0) n;

 IF @Mode IN ('DRYRUN','COMMIT')
 BEGIN
   IF @PlanRetire>0 AND @AllowRetire<>1 THROW 51000,N'Source no longer supplies existing keys. Review RETIRE plan and explicitly allow retirement.',1;
   -- Snapshot rows outside the migration; verify that the writer never touches them.
   SELECT * INTO #OutsideBefore FROM dbo.InvoiceInputStockSupplementalMovements
    WHERE StoreId<>@StoreId OR LegacySourceKey NOT LIKE @Prefix+N'%';
   UPDATE t SET WarehouseId=e.WarehouseId,ProductVariantId=e.ProductVariantId,EffectiveAtUtc=e.EffectiveAtUtc,
     QuantityChange=e.QuantityChange,MovementType=e.MovementType,SourcePeriod=e.SourcePeriod,Note=e.Note,
     LegacyOrderId=e.LegacyOrderId,LegacyInvoiceNumber=e.LegacyInvoiceNumber,LegacyInvoiceSymbol=e.LegacyInvoiceSymbol,
     IsDeleted=0,DeletedAtUtc=NULL,DeletedBy=NULL,UpdatedAtUtc=@Now,UpdatedBy=NULL
   FROM dbo.InvoiceInputStockSupplementalMovements t JOIN #Expected e ON e.StoreId=t.StoreId AND e.LegacySourceKey=t.LegacySourceKey
   JOIN #Updates changed ON changed.LegacySourceKey=e.LegacySourceKey;
   SET @Updated=@@ROWCOUNT;
   INSERT dbo.InvoiceInputStockSupplementalMovements(
     StoreId,WarehouseId,ProductVariantId,EffectiveAtUtc,QuantityChange,MovementType,LegacySourceKey,SourcePeriod,Note,LegacyOrderId,LegacyInvoiceNumber,LegacyInvoiceSymbol,
     CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy)
   SELECT e.StoreId,e.WarehouseId,e.ProductVariantId,e.EffectiveAtUtc,e.QuantityChange,e.MovementType,e.LegacySourceKey,e.SourcePeriod,e.Note,e.LegacyOrderId,e.LegacyInvoiceNumber,e.LegacyInvoiceSymbol,
     @Now,NULL,NULL,NULL,0,NULL,NULL FROM #Expected e
   WHERE NOT EXISTS(SELECT 1 FROM dbo.InvoiceInputStockSupplementalMovements t WHERE t.StoreId=e.StoreId AND t.LegacySourceKey=e.LegacySourceKey);
   SET @Inserted=@@ROWCOUNT;
   UPDATE t SET IsDeleted=1,DeletedAtUtc=@Now,DeletedBy=NULL,UpdatedAtUtc=@Now,UpdatedBy=NULL
     FROM dbo.InvoiceInputStockSupplementalMovements t JOIN #Retires r ON r.Id=t.Id;
   SET @Retired=@@ROWCOUNT;
   IF @Inserted<>@PlanInsert OR @Updated<>@PlanUpdate OR @Retired<>@PlanRetire
      THROW 51000,N'Actual write counts differ from reviewed plan.',1;
   IF EXISTS(SELECT * FROM #OutsideBefore EXCEPT SELECT * FROM dbo.InvoiceInputStockSupplementalMovements WHERE StoreId<>@StoreId OR LegacySourceKey NOT LIKE @Prefix+N'%')
      OR EXISTS(SELECT * FROM dbo.InvoiceInputStockSupplementalMovements WHERE StoreId<>@StoreId OR LegacySourceKey NOT LIKE @Prefix+N'%' EXCEPT SELECT * FROM #OutsideBefore)
      THROW 51000,N'Rows outside migration changed.',1;
 END;

 IF @Mode IN ('VERIFY','DRYRUN','COMMIT')
 BEGIN
   IF EXISTS(SELECT * FROM #Expected EXCEPT
     SELECT LegacySourceKey,StoreId,WarehouseId,ProductVariantId,EffectiveAtUtc,QuantityChange,MovementType,SourcePeriod,Note,LegacyOrderId,LegacyInvoiceNumber,LegacyInvoiceSymbol
     FROM dbo.InvoiceInputStockSupplementalMovements WHERE StoreId=@StoreId AND LegacySourceKey LIKE @Prefix+N'%' AND IsDeleted=0)
     OR EXISTS(SELECT LegacySourceKey,StoreId,WarehouseId,ProductVariantId,EffectiveAtUtc,QuantityChange,MovementType,SourcePeriod,Note,LegacyOrderId,LegacyInvoiceNumber,LegacyInvoiceSymbol
     FROM dbo.InvoiceInputStockSupplementalMovements WHERE StoreId=@StoreId AND LegacySourceKey LIKE @Prefix+N'%' AND IsDeleted=0
     EXCEPT SELECT * FROM #Expected)
     THROW 51000,N'Exact movement verification failed.',1;
   IF (SELECT COUNT_BIG(*) FROM dbo.InvoiceInputStockSupplementalMovements WHERE StoreId=@StoreId AND LegacySourceKey LIKE @Prefix+N'%' AND IsDeleted=0)<>@ExpectedRows
     THROW 51000,N'Movement count verification failed.',1;
   -- Independent grouping from raw staged rows, not from the target totals.
   IF EXISTS(SELECT c.VariantId,SUM(c.WideQuantity) AS Qty FROM #Converted c GROUP BY c.VariantId
     EXCEPT SELECT ProductVariantId,SUM(QuantityChange) FROM dbo.InvoiceInputStockSupplementalMovements
     WHERE StoreId=@StoreId AND LegacySourceKey LIKE @Prefix+N'%' AND IsDeleted=0 GROUP BY ProductVariantId)
     THROW 51000,N'Variant balance verification failed.',1;
   SELECT N'EXACT_VERIFY_PASS' AS Report,@ExpectedRows AS VerifiedMovements;
   -- Identical rerun has no insert/update/retire candidates: same stable keys and payloads.
   IF EXISTS(SELECT 1 FROM #Expected e LEFT JOIN dbo.InvoiceInputStockSupplementalMovements t
     ON t.StoreId=e.StoreId AND t.LegacySourceKey=e.LegacySourceKey WHERE t.Id IS NULL OR t.IsDeleted=1)
     THROW 51000,N'Rerun idempotence check failed.',1;
   SELECT N'IDENTICAL_RERUN_PLAN' AS Report,CONVERT(bigint,0) AS InsertRows,CONVERT(bigint,0) AS UpdateRows,CONVERT(bigint,0) AS RetireRows;
 END;
 IF @Mode='COMMIT'
 BEGIN
   COMMIT;
   SELECT N'COMMIT_PASS' AS Report,@Inserted AS Inserted,@Updated AS Updated,@Retired AS Retired;
 END
 ELSE
 BEGIN
   ROLLBACK;
   SELECT CASE @Mode WHEN 'DRYRUN' THEN N'DRYRUN_ROLLED_BACK' WHEN 'VERIFY' THEN N'VERIFY_PASS' ELSE N'PREVIEW_ONLY' END AS Report;
 END;
 SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
 THROW;
END CATCH;