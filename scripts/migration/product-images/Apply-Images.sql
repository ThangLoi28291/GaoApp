-- The runner owns the transaction. Only image metadata and the primary-image FK are written.
SET NOCOUNT ON;
IF @@TRANCOUNT=0 THROW 55400,N'Image import requires the runner transaction.',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE is_disabled=0 AND parent_id IN
 (OBJECT_ID(N'dbo.MediaAssets'),OBJECT_ID(N'dbo.ProductImages'),OBJECT_ID(N'dbo.ProductVariant')))
 THROW 55400,N'Unexpected target trigger.',1;
SELECT ProductId,VariantId,StoragePath,OriginalFileName,ContentType,SizeBytes,Sha256,
       ProductName AS AltText,IsPrimary,SortOrder
INTO #ImagePlan
FROM OPENJSON(@PlanJson) WITH (
 ProductId int,VariantId int,StoragePath nvarchar(260),OriginalFileName nvarchar(260),
 ContentType nvarchar(100),SizeBytes bigint,Sha256 varchar(64),ProductName nvarchar(200),IsPrimary bit,SortOrder int
);
IF NOT EXISTS(SELECT 1 FROM #ImagePlan) THROW 55400,N'Empty image plan.',1;
IF EXISTS(SELECT 1 FROM #ImagePlan WHERE ProductId IS NULL OR VariantId IS NULL OR
 StoragePath IS NULL OR Sha256 IS NULL OR SizeBytes<=0 OR IsPrimary IS NULL OR SortOrder<0)
 THROW 55400,N'Invalid image payload.',1;
IF EXISTS(SELECT 1 FROM #ImagePlan GROUP BY ProductId,StoragePath HAVING COUNT_BIG(*)<>1)
 THROW 55400,N'Duplicate image link.',1;
IF EXISTS(SELECT 1 FROM #ImagePlan GROUP BY ProductId HAVING SUM(CONVERT(int,IsPrimary))<>1 OR
 MIN(SortOrder)<>0 OR MAX(SortOrder)<>COUNT_BIG(*)-1 OR COUNT(DISTINCT SortOrder)<>COUNT_BIG(*))
 OR EXISTS(SELECT 1 FROM #ImagePlan WHERE IsPrimary=1 AND SortOrder<>0)
 THROW 55400,N'Invalid image ranking.',1;
IF EXISTS(SELECT 1 FROM #ImagePlan e LEFT JOIN dbo.ProductVariant v ON v.Id=e.VariantId
 LEFT JOIN dbo.Products p ON p.Id=e.ProductId
 WHERE v.Id IS NULL OR p.Id IS NULL OR v.ProductId<>p.Id OR v.StoreId<>@StoreId OR p.StoreId<>@StoreId
 OR v.IsDeleted<>0 OR p.IsDeleted<>0 OR p.Name<>e.AltText)
 THROW 55400,N'Image mapping differs from current catalogue.',1;
SELECT DISTINCT StoragePath,OriginalFileName,ContentType,SizeBytes,Sha256 INTO #ImageFiles FROM #ImagePlan;
IF EXISTS(SELECT 1 FROM #ImageFiles GROUP BY StoragePath HAVING COUNT_BIG(*)<>1)
 THROW 55400,N'Conflicting metadata for the same file.',1;

IF @Apply=1
BEGIN
 IF EXISTS(SELECT 1 FROM dbo.ProductImages) OR EXISTS(SELECT 1 FROM dbo.MediaAssets)
 OR EXISTS(SELECT 1 FROM dbo.ProductVariant WHERE PrimaryProductImageId IS NOT NULL)
 THROW 55400,N'Initial image import requires empty image tables and no primary-image links. No deletion is performed.',1;
 DECLARE @Now datetime2(7)=SYSUTCDATETIME();
 INSERT dbo.MediaAssets(StoragePath,OriginalFileName,ContentType,SizeBytes,Sha256,IsTemp,
   TempToken,ExpireAtUtc,CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy,StoreId)
 SELECT StoragePath,OriginalFileName,ContentType,SizeBytes,Sha256,0,NULL,NULL,@Now,NULL,NULL,NULL,0,NULL,NULL,@StoreId
 FROM #ImageFiles;
 INSERT dbo.ProductImages(ProductId,MediaAssetId,IsPrimary,SortOrder,AltText,
   CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy,StoreId)
 SELECT e.ProductId,m.Id,e.IsPrimary,e.SortOrder,e.AltText,@Now,NULL,NULL,NULL,0,NULL,NULL,@StoreId
 FROM #ImagePlan e JOIN dbo.MediaAssets m ON m.StoreId=@StoreId AND m.StoragePath=e.StoragePath;
 UPDATE v SET PrimaryProductImageId=pi.Id
 FROM dbo.ProductVariant v JOIN #ImagePlan e ON e.VariantId=v.Id AND e.IsPrimary=1
 JOIN dbo.ProductImages pi ON pi.ProductId=e.ProductId AND pi.StoreId=@StoreId AND pi.IsPrimary=1
 WHERE v.StoreId=@StoreId;
END;

IF (SELECT COUNT_BIG(*) FROM dbo.MediaAssets)<>(SELECT COUNT_BIG(*) FROM #ImageFiles)
 OR (SELECT COUNT_BIG(*) FROM dbo.ProductImages)<>(SELECT COUNT_BIG(*) FROM #ImagePlan)
 THROW 55400,N'Image metadata row counts differ.',1;
IF EXISTS(SELECT StoragePath,OriginalFileName,ContentType,SizeBytes,Sha256 FROM #ImageFiles
 EXCEPT SELECT StoragePath,OriginalFileName,ContentType,SizeBytes,Sha256 FROM dbo.MediaAssets WHERE StoreId=@StoreId AND IsDeleted=0 AND IsTemp=0)
 OR EXISTS(SELECT StoragePath,OriginalFileName,ContentType,SizeBytes,Sha256 FROM dbo.MediaAssets WHERE StoreId=@StoreId AND IsDeleted=0 AND IsTemp=0
 EXCEPT SELECT StoragePath,OriginalFileName,ContentType,SizeBytes,Sha256 FROM #ImageFiles)
 THROW 55400,N'Exact media verification failed.',1;
IF EXISTS(SELECT ProductId,StoragePath,IsPrimary,SortOrder,AltText FROM #ImagePlan
 EXCEPT SELECT pi.ProductId,m.StoragePath,pi.IsPrimary,pi.SortOrder,pi.AltText
 FROM dbo.ProductImages pi JOIN dbo.MediaAssets m ON m.Id=pi.MediaAssetId
 WHERE pi.StoreId=@StoreId AND m.StoreId=@StoreId AND pi.IsDeleted=0 AND m.IsDeleted=0)
 OR EXISTS(SELECT pi.ProductId,m.StoragePath,pi.IsPrimary,pi.SortOrder,pi.AltText
 FROM dbo.ProductImages pi JOIN dbo.MediaAssets m ON m.Id=pi.MediaAssetId
 WHERE pi.StoreId=@StoreId AND m.StoreId=@StoreId AND pi.IsDeleted=0 AND m.IsDeleted=0
 EXCEPT SELECT ProductId,StoragePath,IsPrimary,SortOrder,AltText FROM #ImagePlan)
 THROW 55400,N'Exact product image verification failed.',1;
IF EXISTS(SELECT 1 FROM #ImagePlan e JOIN dbo.ProductVariant v ON v.Id=e.VariantId
 LEFT JOIN dbo.ProductImages pi ON pi.Id=v.PrimaryProductImageId
 LEFT JOIN dbo.MediaAssets m ON m.Id=pi.MediaAssetId
 WHERE e.IsPrimary=1 AND (pi.Id IS NULL OR pi.ProductId<>e.ProductId OR pi.IsPrimary<>1 OR m.StoragePath<>e.StoragePath))
 OR (SELECT COUNT_BIG(*) FROM dbo.ProductVariant WHERE PrimaryProductImageId IS NOT NULL)
 <> (SELECT COUNT_BIG(*) FROM #ImagePlan WHERE IsPrimary=1)
 THROW 55400,N'Variant primary image verification failed.',1;
SELECT N'IMAGE_EXACT_VERIFY_PASS' Report,(SELECT COUNT_BIG(*) FROM #ImageFiles) MediaAssets,
 (SELECT COUNT_BIG(*) FROM #ImagePlan) ProductImages,
 (SELECT COUNT_BIG(*) FROM #ImagePlan WHERE IsPrimary=1) PrimaryImages;
