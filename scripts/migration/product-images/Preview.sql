-- Read-only mapping from the restored source to the CURRENT GaoApp catalogue.
-- __SOURCE__ is replaced only with a bracket-quoted database identifier.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 30000;
IF DB_NAME()=@SourceDatabase THROW 51000,N'Source and target must differ.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.Stores WHERE Id=@StoreId AND IsDeleted=0)
    THROW 51000,N'Target store missing.',1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
 BEGIN TRANSACTION;
 ;WITH Paths AS (
   SELECT LTRIM(RTRIM(REPLACE(v.Sku,NCHAR(160),N' '))) COLLATE SQL_Latin1_General_CP1_CI_AS Code,
          v.ProductId,v.Id VariantId
   FROM dbo.ProductVariant v JOIN dbo.Products p ON p.Id=v.ProductId
   WHERE v.StoreId=@StoreId AND p.StoreId=@StoreId AND v.IsDeleted=0 AND p.IsDeleted=0
   UNION
   SELECT LTRIM(RTRIM(REPLACE(b.Barcode,NCHAR(160),N' '))) COLLATE SQL_Latin1_General_CP1_CI_AS,
          v.ProductId,v.Id
   FROM dbo.ProductVariantUnitBarcode b
   JOIN dbo.ProductUnitConversion c ON c.Id=b.ProductUnitConversionId
   JOIN dbo.ProductVariant v ON v.Id=c.ProductVariantId
   JOIN dbo.Products p ON p.Id=v.ProductId
   WHERE b.StoreId=@StoreId AND c.StoreId=@StoreId AND v.StoreId=@StoreId AND p.StoreId=@StoreId
     AND b.IsDeleted=0 AND c.IsDeleted=0 AND v.IsDeleted=0 AND p.IsDeleted=0
 ), Mapping AS (
   SELECT Code,COUNT_BIG(*) CandidateCount,MIN(ProductId) ProductId,MIN(VariantId) VariantId
   FROM Paths WHERE NULLIF(Code,N'') IS NOT NULL GROUP BY Code
 )
 SELECT CONVERT(bigint,pd.Id) SourceProductDetailId,
        LTRIM(RTRIM(REPLACE(CONVERT(nvarchar(200),pd.Code),NCHAR(160),N' '))) SourceCode,
        CONVERT(nvarchar(max),pd.Image) SourceImage,
        COALESCE(m.CandidateCount,0) CandidateCount,m.ProductId,m.VariantId,
        p.Name ProductName,
        CASE WHEN m.ProductId=pd.Id THEN 0 ELSE 1 END SourceRank
 FROM __SOURCE__.dbo.ProductDetail pd
 LEFT JOIN Mapping m ON m.Code=LTRIM(RTRIM(REPLACE(CONVERT(nvarchar(200),pd.Code),NCHAR(160),N' '))) COLLATE SQL_Latin1_General_CP1_CI_AS
 LEFT JOIN dbo.Products p ON p.Id=m.ProductId AND p.StoreId=@StoreId
 WHERE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(max),pd.Image))),N'') IS NOT NULL
 ORDER BY m.ProductId,SourceRank,pd.Id;

 SELECT (SELECT COUNT_BIG(*) FROM dbo.ProductImages WHERE StoreId=@StoreId) ProductImageRows,
        (SELECT COUNT_BIG(*) FROM dbo.MediaAssets WHERE StoreId=@StoreId) MediaAssetRows,
        (SELECT COUNT_BIG(*) FROM dbo.ProductVariant WHERE StoreId=@StoreId AND PrimaryProductImageId IS NOT NULL) VariantsWithPrimaryImage;
 ROLLBACK;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
