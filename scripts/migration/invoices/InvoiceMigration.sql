/* GaoStore InvoiceHeads/InvoiceDetails migration. TEST before real cutover.
   Source is read-only. No provider API, financial posting, stock posting or email.
   Owns only LegacySourceId rows in the selected store; preserves raw source JSON.
*/
SET NOCOUNT ON; SET XACT_ABORT ON; SET LOCK_TIMEOUT 30000;
-- SETTINGS BEGIN
DECLARE @Mode varchar(12)='PREVIEW';
DECLARE @SourceDatabase sysname=N'DataGaoStore',@ExpectedTargetDatabase sysname=N'GaoAppDb';
DECLARE @StoreId int=1,@LegalEntityId int=1,@WarehouseId int=1,@AllowCommit bit=0;
-- SETTINGS END
IF DB_NAME()<>@ExpectedTargetDatabase OR DB_NAME()=@SourceDatabase THROW 51000,N'Wrong target/source database.',1;
IF @@TRANCOUNT<>0 THROW 51000,N'Ambient transaction is not supported.',1;
IF @Mode NOT IN('PREVIEW','DRYRUN','COMMIT','VERIFY') THROW 51000,N'Invalid mode.',1;
IF @Mode='COMMIT' AND @AllowCommit<>1 THROW 51000,N'Review DRYRUN before allowing COMMIT.',1;
IF COL_LENGTH(N'dbo.InvoiceHeads',N'LegacySourceId') IS NULL THROW 51000,N'Apply AddLegacyInvoiceImport schema first.',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id IN(OBJECT_ID(N'dbo.InvoiceHeads'),OBJECT_ID(N'dbo.InvoiceDetails')) AND is_disabled=0) THROW 51000,N'Unexpected target trigger.',1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
DECLARE @Now datetime2=SYSUTCDATETIME(),@IdentityTable int=0;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @Lock int;
 EXEC @Lock=sys.sp_getapplock @Resource=N'GSTORE-IIS-V1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @Lock<0 THROW 51000,N'Migration lock unavailable.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.Stores WITH(UPDLOCK,HOLDLOCK) WHERE Id=@StoreId AND IsDeleted=0) THROW 51000,N'Store missing.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.LegalEntities le JOIN dbo.Warehouses w ON w.Id=le.DefaultWarehouseId AND w.LegalEntityId=le.Id WHERE le.StoreId=@StoreId AND le.Id=@LegalEntityId AND w.Id=@WarehouseId AND w.StoreId=@StoreId AND le.IsDeleted=0 AND w.IsDeleted=0) THROW 51000,N'Legal entity/default warehouse mismatch.',1;
 CREATE TABLE #SourceHead(LegacyId bigint PRIMARY KEY,SourceJson nvarchar(max) NOT NULL);
 CREATE TABLE #SourceDetail(LegacyId bigint PRIMARY KEY,SourceJson nvarchar(max) NOT NULL);
 DECLARE @Q nvarchar(258)=QUOTENAME(@SourceDatabase),@SourceSql nvarchar(max);
 SET @SourceSql=N'INSERT #SourceHead SELECT h.Id,(SELECT h.* FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER) FROM '+@Q+N'.dbo.InvoiceHead h;
 INSERT #SourceDetail SELECT d.ID,(SELECT d.* FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER) FROM '+@Q+N'.dbo.InvoiceDetail d;';
 EXEC sys.sp_executesql @SourceSql;
 IF NOT EXISTS(SELECT 1 FROM #SourceHead) THROW 51000,N'Empty source.',1;
SELECT h.* INTO #H FROM #SourceHead r CROSS APPLY OPENJSON(r.SourceJson) WITH (Id bigint, InvoiceNumber nvarchar(50), InvoiceType nvarchar(20), TemplateCode nvarchar(20), InvoiceSeries nvarchar(20), IssuedDate datetime, Amount decimal(18,2), SellerCode nvarchar(20), CreatedAt datetime, buyerName nvarchar(100), BuyerAddressLine nvarchar(250), BuyerLegalName nvarchar(250), BuyerTaxCode nvarchar(20), BuyerPhoneNumber nvarchar(15), BuyerEmail nvarchar(100), OrderCategoryID bigint, LayHD bit, MaBiMat nvarchar(50), MaCQT nvarchar(50), Note nvarchar(max), IdGop nvarchar(15)) h;
SELECT d.* INTO #D FROM #SourceDetail r CROSS APPLY OPENJSON(r.SourceJson) WITH (ID bigint, OrderID bigint, ProductCode nvarchar(200), ItemName nvarchar(250), Unit nvarchar(100), Quantity decimal(18,3), UnitPrice decimal(18,2), Amount decimal(18,2), Note nvarchar(500)) d;

 CREATE UNIQUE CLUSTERED INDEX IX_H ON #H(Id); CREATE UNIQUE CLUSTERED INDEX IX_D ON #D(ID); CREATE INDEX IX_D_Order ON #D(OrderID);
 IF EXISTS(SELECT 1 FROM #D d LEFT JOIN #H h ON h.Id=d.OrderID WHERE h.Id IS NULL) THROW 51000,N'Orphan source detail requires review.',1;
 -- BEGIN REVIEWED 20260929 LONG NOTES
 -- Two explicitly approved display-only shortenings. Raw source JSON is never modified.
 DECLARE @ReviewedLongNotes TABLE(Id bigint PRIMARY KEY);
 INSERT @ReviewedLongNotes VALUES(1828849),(1828927);
 IF EXISTS(SELECT 1 FROM @ReviewedLongNotes a LEFT JOIN #H h ON h.Id=a.Id
   WHERE h.Id IS NULL OR h.OrderCategoryID IS NULL OR h.OrderCategoryID<>12
     OR h.CreatedAt IS NULL OR CONVERT(date,h.CreatedAt)<>'20260912'
     OR h.InvoiceNumber IS NOT NULL OR h.IssuedDate IS NOT NULL
     OR h.Note IS NULL OR LEN(h.Note)<>519 OR DATALENGTH(h.Note)>1038)
   THROW 51000,N'REVIEWED_LONG_NOTE_CHANGED: the two approved draft invoices no longer match review.',1;
 DECLARE @NoteSuffix nvarchar(50)=N'… [Xem ghi chú gốc]';
 SELECT h.Id LegacySourceId,h.Note OriginalNote,CONVERT(nvarchar(500),NULL) DisplayNote
 INTO #ReviewedLongNoteReport FROM #H h JOIN @ReviewedLongNotes a ON a.Id=h.Id;
 -- Use UTF-16 storage units so nvarchar(500) never overflows; avoid a split surrogate pair.
 UPDATE r SET DisplayNote=
   CASE WHEN UNICODE(RIGHT(p.Prefix,1)) BETWEEN 55296 AND 56319
        THEN LEFT(p.Prefix,LEN(p.Prefix+N'#')-2) ELSE p.Prefix END+@NoteSuffix
 FROM #ReviewedLongNoteReport r
 CROSS APPLY(SELECT LEFT(r.OriginalNote COLLATE Latin1_General_100_BIN2,500-DATALENGTH(@NoteSuffix)/2) Prefix)p;
 UPDATE h SET Note=r.DisplayNote FROM #H h JOIN #ReviewedLongNoteReport r ON r.LegacySourceId=h.Id;
 IF EXISTS(SELECT 1 FROM #ReviewedLongNoteReport WHERE DisplayNote IS NULL OR DATALENGTH(DisplayNote)>1000)
   THROW 51000,N'REVIEWED_LONG_NOTE_DISPLAY_INVALID: reviewed note exceeds target storage.',1;
 -- END REVIEWED 20260929 LONG NOTES
 IF EXISTS(SELECT 1 FROM #H WHERE CreatedAt IS NULL OR LEN(Note)>500 OR LEN(InvoiceNumber)>35 OR (NULLIF(LTRIM(RTRIM(InvoiceNumber)),N'') IS NOT NULL AND (IssuedDate IS NULL OR IssuedDate<'20000101'))) THROW 51000,N'Header date/length invalid; no silent truncation.',1;
 IF EXISTS(SELECT 1 FROM #D WHERE Quantity IS NULL OR Amount IS NULL OR (UnitPrice IS NULL AND Quantity=0 AND Amount<>0)) THROW 51000,N'Detail quantity/amount invalid.',1;
 SELECT LTRIM(RTRIM(REPLACE(v.Sku,NCHAR(160),N' '))) COLLATE SQL_Latin1_General_CP1_CI_AS Code,v.Id VariantId,CONVERT(decimal(18,4),1) Factor INTO #Paths
 FROM dbo.ProductVariant v JOIN dbo.Products p ON p.Id=v.ProductId JOIN dbo.Unit u ON u.Id=p.BaseUnitId WHERE v.StoreId=@StoreId AND p.StoreId=@StoreId AND u.StoreId=@StoreId AND v.IsDeleted=0 AND p.IsDeleted=0 AND u.IsDeleted=0
 UNION ALL SELECT LTRIM(RTRIM(REPLACE(b.Barcode,NCHAR(160),N' '))),v.Id,c.Factor FROM dbo.ProductVariantUnitBarcode b JOIN dbo.ProductUnitConversion c ON c.Id=b.ProductUnitConversionId JOIN dbo.ProductVariant v ON v.Id=c.ProductVariantId JOIN dbo.Products p ON p.Id=v.ProductId JOIN dbo.Unit u ON u.Id=p.BaseUnitId WHERE b.StoreId=@StoreId AND c.StoreId=@StoreId AND v.StoreId=@StoreId AND p.StoreId=@StoreId AND u.StoreId=@StoreId AND b.IsDeleted=0 AND c.IsDeleted=0 AND v.IsDeleted=0 AND p.IsDeleted=0 AND u.IsDeleted=0;
 SELECT Code,MIN(VariantId) VariantId,MIN(Factor) Factor,COUNT(DISTINCT VariantId) Variants,COUNT(DISTINCT Factor) Factors INTO #Map FROM #Paths GROUP BY Code;
 CREATE UNIQUE CLUSTERED INDEX IX_Map ON #Map(Code);
 IF EXISTS(SELECT 1 FROM #D d JOIN #Map m ON m.Code=LTRIM(RTRIM(REPLACE(d.ProductCode,NCHAR(160),N' '))) COLLATE SQL_Latin1_General_CP1_CI_AS WHERE m.Variants<>1 OR m.Factors<>1 OR m.Factor<=0) THROW 51000,N'Ambiguous product/unit mapping.',1;
 IF EXISTS(SELECT 1 FROM dbo.InvoiceHeads t WHERE t.StoreId=@StoreId AND t.LegacySourceId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM #H h WHERE h.Id=t.LegacySourceId))
 OR EXISTS(SELECT 1 FROM dbo.InvoiceDetails t WHERE t.StoreId=@StoreId AND t.LegacySourceId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM #D d WHERE d.ID=t.LegacySourceId)) THROW 51000,N'Source removed previously imported data; review before changing history.',1;
 DECLARE @MaxHead bigint=(SELECT COALESCE(MAX(Id),0) FROM dbo.InvoiceHeads WITH(UPDLOCK,HOLDLOCK)),@MaxDetail bigint=(SELECT COALESCE(MAX(Id),0) FROM dbo.InvoiceDetails WITH(UPDLOCK,HOLDLOCK));
 SELECT h.Id LegacyId,COALESCE(CONVERT(bigint,t.Id),@MaxHead+SUM(CONVERT(bigint,CASE WHEN t.Id IS NULL THEN 1 ELSE 0 END)) OVER(ORDER BY h.Id ROWS UNBOUNDED PRECEDING)) TargetId INTO #HeadMap FROM #H h LEFT JOIN dbo.InvoiceHeads t ON t.StoreId=@StoreId AND t.LegacySourceId=h.Id;
 SELECT d.ID LegacyId,COALESCE(CONVERT(bigint,t.Id),@MaxDetail+SUM(CONVERT(bigint,CASE WHEN t.Id IS NULL THEN 1 ELSE 0 END)) OVER(ORDER BY d.ID ROWS UNBOUNDED PRECEDING)) TargetId INTO #DetailMap FROM #D d LEFT JOIN dbo.InvoiceDetails t ON t.StoreId=@StoreId AND t.LegacySourceId=d.ID;
 CREATE UNIQUE CLUSTERED INDEX IX_HeadMap ON #HeadMap(LegacyId); CREATE UNIQUE CLUSTERED INDEX IX_DetailMap ON #DetailMap(LegacyId);
 IF EXISTS(SELECT 1 FROM #HeadMap WHERE TargetId>2147483647) OR EXISTS(SELECT 1 FROM #DetailMap WHERE TargetId>2147483647) THROW 51000,N'Target identity would overflow.',1;
 SELECT OrderID,SUM(Quantity) Quantity,SUM(Amount) Amount,COUNT_BIG(*) Lines INTO #Totals FROM #D GROUP BY OrderID;
 SELECT TOP(0) CONVERT(int,Id) Id,[StoreId],[OrderId],[LegalEntityId],[InvoiceProviderSettingId],[LegacySourceId],[LegacyOrderCategoryId],[LegacyMergeId],[LegacySnapshotJson],[LegacyReadOnly],[InvoiceNumber],[InvoiceDate],[BuyerType],[BuyerName],[BuyerLegalName],[BuyerTaxCode],[BuyerAddress],[BuyerEmail],[BuyerPhone],[IsAutoInvoiceGroup],[BuyerCitizenId],[LastIssuanceRelevantChangeAtUtc],[TotalQuantity],[SubTotal],[VatAmount],[GrandTotal],[Note],[IsLocked],[LockedAtUtc],[LockedByUserId],[LockReason],[ProviderStatus],[TransactionUuid],[ProviderCode],[SupplierTaxCode],[InvoiceType],[TemplateCode],[InvoiceSeries],[ProviderInvoiceNo],[ProviderTransactionId],[ReservationCode],[CodeOfTax],[IssuedAtUtc],[LastSyncedAtUtc],[LastErrorCode],[LastErrorMessage],[PdfFilePath],[ZipFilePath],[OfficialPdfStatus],[OfficialPdfDownloadedAtUtc],[OfficialPdfFileName],[OfficialZipXmlStatus],[OfficialZipXmlDownloadedAtUtc],[OfficialZipXmlFileName],[EmailStatus],[EmailSentAtUtc],[LastEmailTo],[EmailSendCount],[LastEmailErrorMessage],[OriginalInvoiceHeadId],[CorrectionType],[OriginalInvoiceNo],[OriginalInvoiceIssuedAtUtc],[AdjustedNote],[AdditionalReferenceDesc],[AdditionalReferenceDateUtc],[IsDeleted],LegacyImportedHash INTO #ExpectedHead FROM dbo.InvoiceHeads;
 INSERT #ExpectedHead(Id,[StoreId],[OrderId],[LegalEntityId],[InvoiceProviderSettingId],[LegacySourceId],[LegacyOrderCategoryId],[LegacyMergeId],[LegacySnapshotJson],[LegacyReadOnly],[InvoiceNumber],[InvoiceDate],[BuyerType],[BuyerName],[BuyerLegalName],[BuyerTaxCode],[BuyerAddress],[BuyerEmail],[BuyerPhone],[IsAutoInvoiceGroup],[BuyerCitizenId],[LastIssuanceRelevantChangeAtUtc],[TotalQuantity],[SubTotal],[VatAmount],[GrandTotal],[Note],[IsLocked],[LockedAtUtc],[LockedByUserId],[LockReason],[ProviderStatus],[TransactionUuid],[ProviderCode],[SupplierTaxCode],[InvoiceType],[TemplateCode],[InvoiceSeries],[ProviderInvoiceNo],[ProviderTransactionId],[ReservationCode],[CodeOfTax],[IssuedAtUtc],[LastSyncedAtUtc],[LastErrorCode],[LastErrorMessage],[PdfFilePath],[ZipFilePath],[OfficialPdfStatus],[OfficialPdfDownloadedAtUtc],[OfficialPdfFileName],[OfficialZipXmlStatus],[OfficialZipXmlDownloadedAtUtc],[OfficialZipXmlFileName],[EmailStatus],[EmailSentAtUtc],[LastEmailTo],[EmailSendCount],[LastEmailErrorMessage],[OriginalInvoiceHeadId],[CorrectionType],[OriginalInvoiceNo],[OriginalInvoiceIssuedAtUtc],[AdjustedNote],[AdditionalReferenceDesc],[AdditionalReferenceDateUtc],[IsDeleted])
 SELECT hm.TargetId,@StoreId,
o.Id,
@LegalEntityId,
ps.Id,
h.Id,
h.OrderCategoryID,
h.IdGop,
r.SourceJson,
CONVERT(bit,CASE WHEN h.OrderCategoryID=13 OR o.Id IS NULL OR (h.OrderCategoryID=8 AND NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NULL) THEN 1 ELSE 0 END),
NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N''),
CASE WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NOT NULL THEN h.IssuedDate ELSE h.CreatedAt END,
CASE WHEN NULLIF(LTRIM(RTRIM(h.BuyerTaxCode)),N'') IS NOT NULL OR NULLIF(LTRIM(RTRIM(h.BuyerLegalName)),N'') IS NOT NULL THEN N'Business' WHEN h.LayHD=1 THEN N'Individual' ELSE N'NoInvoice' END,
h.buyerName,
h.BuyerLegalName,
h.BuyerTaxCode,
h.BuyerAddressLine,
h.BuyerEmail,
h.BuyerPhoneNumber,
CONVERT(bit,0),
NULL,
(SELECT MAX(v.ChangedAt) FROM (VALUES (DATEADD(hour,-7,h.CreatedAt)),(o.CompletedAtUtc),(ret.LastReturnAtUtc)) v(ChangedAt)),
COALESCE(t.Quantity,0),
COALESCE(h.Amount,0),
0,
COALESCE(h.Amount,0),
h.Note,
CONVERT(bit,CASE WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NOT NULL OR h.OrderCategoryID IN(8,13) OR o.Id IS NULL THEN 1 ELSE 0 END),
CASE WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NOT NULL THEN DATEADD(hour,-7,h.IssuedDate) WHEN h.OrderCategoryID IN(8,13) OR o.Id IS NULL THEN DATEADD(hour,-7,h.CreatedAt) END,
NULL,
CASE WHEN h.OrderCategoryID=13 THEN N'Hóa đơn điều chỉnh GaoStore: chỉ lưu tra cứu theo yêu cầu.' WHEN o.Id IS NULL THEN N'Chưa liên kết Order GaoApp; giữ nguyên hóa đơn GaoStore để tra cứu.' WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NOT NULL THEN N'Hóa đơn đã phát hành tại GaoStore.' WHEN h.OrderCategoryID=8 THEN N'GaoStore đánh dấu đã phát hành nhưng thiếu số hóa đơn; cần đối chiếu, không phát hành lại.' END,
CONVERT(tinyint,CASE WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NULL THEN 0 ELSE 5 END),
NULL,
N'VIETTEL',
h.SellerCode,
h.InvoiceType,
h.TemplateCode,
h.InvoiceSeries,
NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N''),
NULL,
h.MaBiMat,
h.MaCQT,
CASE WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NOT NULL THEN DATEADD(hour,-7,h.IssuedDate) END,
NULL,
NULL,
NULL,
NULL,
NULL,
0,
NULL,
NULL,
0,
NULL,
NULL,
0,
NULL,
NULL,
0,
NULL,
NULL,
NULL,
NULL,
NULL,
NULL,
NULL,
NULL,
CONVERT(bit,0)
 FROM #H h JOIN #SourceHead r ON r.LegacyId=h.Id JOIN #HeadMap hm ON hm.LegacyId=h.Id
 LEFT JOIN #Totals t ON t.OrderID=h.Id
 LEFT JOIN dbo.Orders o ON o.StoreId=@StoreId AND o.Id=h.Id AND o.OrderNumber=CONCAT(N'LEGACY-',h.Id) AND o.IsDeleted=0
 OUTER APPLY(SELECT MAX(sr.CompletedAtUtc) LastReturnAtUtc FROM dbo.SalesReturns sr WHERE sr.OrderId=o.Id AND sr.StoreId=@StoreId AND sr.IsDeleted=0 AND sr.Status=1)ret
 OUTER APPLY(SELECT MIN(s.Id) Id FROM dbo.InvoiceProviderSettings s WHERE s.StoreId=@StoreId AND s.IsDeleted=0 AND s.IsActive=1 AND s.ProviderCode=N'VIETTEL' AND s.SupplierTaxCode=h.SellerCode AND s.InvoiceType=h.InvoiceType AND s.TemplateCode=h.TemplateCode AND s.InvoiceSeries=h.InvoiceSeries HAVING COUNT_BIG(*)=1)ps;
 SELECT TOP(0) CONVERT(int,Id) Id,[StoreId],[InvoiceHeadId],[OrderLineId],[OrderLegalEntityAllocationId],[ProductVariantId],[SourceType],[LegacySourceId],[LegacyUnitFactor],[LegacySnapshotJson],[ItemName],[UnitName],[Quantity],[UnitPrice],[Amount],[VatRate],[VatAmount],[TotalAmount],[Note],[IsDeleted],LegacyImportedHash INTO #ExpectedDetail FROM dbo.InvoiceDetails;
 INSERT #ExpectedDetail(Id,[StoreId],[InvoiceHeadId],[OrderLineId],[OrderLegalEntityAllocationId],[ProductVariantId],[SourceType],[LegacySourceId],[LegacyUnitFactor],[LegacySnapshotJson],[ItemName],[UnitName],[Quantity],[UnitPrice],[Amount],[VatRate],[VatAmount],[TotalAmount],[Note],[IsDeleted])
 SELECT dm.TargetId,@StoreId,
hm.TargetId,
NULL,
NULL,
m.VariantId,
CONVERT(tinyint,2),
d.ID,
m.Factor,
r.SourceJson,
COALESCE(d.ItemName,N'[Chưa có tên]'),
d.Unit,
d.Quantity,
COALESCE(d.UnitPrice,CASE WHEN d.Quantity=0 THEN 0 ELSE ROUND(d.Amount/d.Quantity,2) END),
d.Amount,
0,
0,
d.Amount,
d.Note,
CONVERT(bit,0)
 FROM #D d JOIN #SourceDetail r ON r.LegacyId=d.ID JOIN #DetailMap dm ON dm.LegacyId=d.ID JOIN #HeadMap hm ON hm.LegacyId=d.OrderID
 LEFT JOIN #Map m ON m.Code=LTRIM(RTRIM(REPLACE(d.ProductCode,NCHAR(160),N' '))) COLLATE SQL_Latin1_General_CP1_CI_AS;
 UPDATE e SET LegacyImportedHash=HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(SELECT e.[StoreId],e.[OrderId],e.[LegalEntityId],e.[InvoiceProviderSettingId],e.[LegacySourceId],e.[LegacyOrderCategoryId],e.[LegacyMergeId],e.[LegacySnapshotJson],e.[LegacyReadOnly],e.[InvoiceNumber],e.[InvoiceDate],e.[BuyerType],e.[BuyerName],e.[BuyerLegalName],e.[BuyerTaxCode],e.[BuyerAddress],e.[BuyerEmail],e.[BuyerPhone],e.[IsAutoInvoiceGroup],e.[BuyerCitizenId],e.[LastIssuanceRelevantChangeAtUtc],e.[TotalQuantity],e.[SubTotal],e.[VatAmount],e.[GrandTotal],e.[Note],e.[IsLocked],e.[LockedAtUtc],e.[LockedByUserId],e.[LockReason],e.[ProviderStatus],e.[TransactionUuid],e.[ProviderCode],e.[SupplierTaxCode],e.[InvoiceType],e.[TemplateCode],e.[InvoiceSeries],e.[ProviderInvoiceNo],e.[ProviderTransactionId],e.[ReservationCode],e.[CodeOfTax],e.[IssuedAtUtc],e.[LastSyncedAtUtc],e.[LastErrorCode],e.[LastErrorMessage],e.[PdfFilePath],e.[ZipFilePath],e.[OfficialPdfStatus],e.[OfficialPdfDownloadedAtUtc],e.[OfficialPdfFileName],e.[OfficialZipXmlStatus],e.[OfficialZipXmlDownloadedAtUtc],e.[OfficialZipXmlFileName],e.[EmailStatus],e.[EmailSentAtUtc],e.[LastEmailTo],e.[EmailSendCount],e.[LastEmailErrorMessage],e.[OriginalInvoiceHeadId],e.[CorrectionType],e.[OriginalInvoiceNo],e.[OriginalInvoiceIssuedAtUtc],e.[AdjustedNote],e.[AdditionalReferenceDesc],e.[AdditionalReferenceDateUtc],e.[IsDeleted] FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER))) FROM #ExpectedHead e;
 CREATE UNIQUE CLUSTERED INDEX IX_ExpectedHead ON #ExpectedHead(Id);
 SELECT e.Id INTO #ChangedHead FROM #ExpectedHead e JOIN dbo.InvoiceHeads t ON t.Id=e.Id WHERE e.LegacyImportedHash<>t.LegacyImportedHash;
 IF EXISTS(SELECT 1 FROM dbo.InvoiceHeads t JOIN #ExpectedHead e ON e.Id=t.Id WHERE t.LegacyImportedHash IS NULL OR t.LegacyImportedHash<>HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(SELECT t.[StoreId],t.[OrderId],t.[LegalEntityId],t.[InvoiceProviderSettingId],t.[LegacySourceId],t.[LegacyOrderCategoryId],t.[LegacyMergeId],t.[LegacySnapshotJson],t.[LegacyReadOnly],t.[InvoiceNumber],t.[InvoiceDate],t.[BuyerType],t.[BuyerName],t.[BuyerLegalName],t.[BuyerTaxCode],t.[BuyerAddress],t.[BuyerEmail],t.[BuyerPhone],t.[IsAutoInvoiceGroup],t.[BuyerCitizenId],t.[LastIssuanceRelevantChangeAtUtc],t.[TotalQuantity],t.[SubTotal],t.[VatAmount],t.[GrandTotal],t.[Note],t.[IsLocked],t.[LockedAtUtc],t.[LockedByUserId],t.[LockReason],t.[ProviderStatus],t.[TransactionUuid],t.[ProviderCode],t.[SupplierTaxCode],t.[InvoiceType],t.[TemplateCode],t.[InvoiceSeries],t.[ProviderInvoiceNo],t.[ProviderTransactionId],t.[ReservationCode],t.[CodeOfTax],t.[IssuedAtUtc],t.[LastSyncedAtUtc],t.[LastErrorCode],t.[LastErrorMessage],t.[PdfFilePath],t.[ZipFilePath],t.[OfficialPdfStatus],t.[OfficialPdfDownloadedAtUtc],t.[OfficialPdfFileName],t.[OfficialZipXmlStatus],t.[OfficialZipXmlDownloadedAtUtc],t.[OfficialZipXmlFileName],t.[EmailStatus],t.[EmailSentAtUtc],t.[LastEmailTo],t.[EmailSendCount],t.[LastEmailErrorMessage],t.[OriginalInvoiceHeadId],t.[CorrectionType],t.[OriginalInvoiceNo],t.[OriginalInvoiceIssuedAtUtc],t.[AdjustedNote],t.[AdditionalReferenceDesc],t.[AdditionalReferenceDateUtc],t.[IsDeleted] FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)))) THROW 51000,N'InvoiceHeads: imported row edited in GaoApp; refusing to overwrite.',1;
 UPDATE e SET LegacyImportedHash=HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(SELECT e.[StoreId],e.[InvoiceHeadId],e.[OrderLineId],e.[OrderLegalEntityAllocationId],e.[ProductVariantId],e.[SourceType],e.[LegacySourceId],e.[LegacyUnitFactor],e.[LegacySnapshotJson],e.[ItemName],e.[UnitName],e.[Quantity],e.[UnitPrice],e.[Amount],e.[VatRate],e.[VatAmount],e.[TotalAmount],e.[Note],e.[IsDeleted] FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER))) FROM #ExpectedDetail e;
 CREATE UNIQUE CLUSTERED INDEX IX_ExpectedDetail ON #ExpectedDetail(Id);
 SELECT e.Id INTO #ChangedDetail FROM #ExpectedDetail e JOIN dbo.InvoiceDetails t ON t.Id=e.Id WHERE e.LegacyImportedHash<>t.LegacyImportedHash;
 IF EXISTS(SELECT 1 FROM dbo.InvoiceDetails t JOIN #ExpectedDetail e ON e.Id=t.Id WHERE t.LegacyImportedHash IS NULL OR t.LegacyImportedHash<>HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(SELECT t.[StoreId],t.[InvoiceHeadId],t.[OrderLineId],t.[OrderLegalEntityAllocationId],t.[ProductVariantId],t.[SourceType],t.[LegacySourceId],t.[LegacyUnitFactor],t.[LegacySnapshotJson],t.[ItemName],t.[UnitName],t.[Quantity],t.[UnitPrice],t.[Amount],t.[VatRate],t.[VatAmount],t.[TotalAmount],t.[Note],t.[IsDeleted] FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)))) THROW 51000,N'InvoiceDetails: imported row edited in GaoApp; refusing to overwrite.',1;

 IF EXISTS(SELECT 1 FROM #ExpectedHead e JOIN dbo.InvoiceHeads t ON t.StoreId=e.StoreId AND t.OrderId=e.OrderId AND t.OriginalInvoiceHeadId IS NULL AND t.IsDeleted=0 AND (t.LegalEntityId=e.LegalEntityId OR t.LegalEntityId IS NULL) WHERE t.Id<>e.Id) THROW 51000,N'Existing target invoice already owns this Order; review collision.',1;
 SELECT N'CONTRACT' Report,@Mode Mode,DB_NAME() TargetDatabase,@SourceDatabase SourceDatabase,@StoreId StoreId,@LegalEntityId LegalEntityId,@WarehouseId WarehouseId;
 SELECT N'COUNTS' Report,(SELECT COUNT_BIG(*) FROM #H) Heads,(SELECT COUNT_BIG(*) FROM #D) Details,(SELECT COUNT_BIG(*) FROM #ExpectedHead WHERE ProviderStatus=5) Issued,(SELECT COUNT_BIG(*) FROM #ExpectedHead WHERE ProviderStatus=0) Drafts;
 SELECT N'PLAN' Report,
 (SELECT COUNT_BIG(*) FROM #ExpectedHead e WHERE NOT EXISTS(SELECT 1 FROM dbo.InvoiceHeads t WHERE t.Id=e.Id)) InsertHeads,(SELECT COUNT_BIG(*) FROM #ChangedHead) UpdateHeads,
 (SELECT COUNT_BIG(*) FROM #ExpectedDetail e WHERE NOT EXISTS(SELECT 1 FROM dbo.InvoiceDetails t WHERE t.Id=e.Id)) InsertDetails,(SELECT COUNT_BIG(*) FROM #ChangedDetail) UpdateDetails;
 SELECT N'QUALITY' Report,(SELECT COUNT_BIG(*) FROM #H WHERE OrderCategoryID=8 AND NULLIF(LTRIM(RTRIM(InvoiceNumber)),N'') IS NULL) IssuedCategoryMissingNumber,(SELECT COUNT_BIG(*) FROM #ExpectedHead WHERE OrderId IS NULL) UnlinkedOrders,(SELECT COUNT_BIG(*) FROM #H WHERE OrderCategoryID=13) ArchiveCorrections,
 (SELECT COUNT_BIG(*) FROM #ExpectedHead WHERE InvoiceProviderSettingId IS NULL) UnmappedProviderHeads,(SELECT COUNT_BIG(*) FROM #ExpectedDetail WHERE ProductVariantId IS NULL) UnmappedProductLines,
 (SELECT COUNT_BIG(*) FROM #D WHERE UnitPrice IS NULL) DerivedUnitPriceLines,(SELECT COUNT_BIG(*) FROM #H WHERE Amount IS NULL) NullHeaderAmounts,
 (SELECT COUNT_BIG(*) FROM #H h LEFT JOIN #Totals t ON t.OrderID=h.Id WHERE h.Amount<>COALESCE(t.Amount,0)) HeaderDetailAmountDifferences;
 SELECT N'UNLINKED_ORDERS' Report,LegacySourceId,LegacyOrderCategoryId,InvoiceNumber,LegacyReadOnly FROM #ExpectedHead WHERE OrderId IS NULL ORDER BY LegacySourceId;
 SELECT N'AMOUNT_DIFFERENCES' Report,h.Id LegacySourceId,h.Amount HeaderAmount,t.Amount DetailAmount FROM #H h LEFT JOIN #Totals t ON t.OrderID=h.Id WHERE h.Amount<>COALESCE(t.Amount,0) ORDER BY h.Id;
 SELECT N'CATEGORY_COUNTS' Report,LegacyOrderCategoryId,ProviderStatus,COUNT_BIG(*) Heads FROM #ExpectedHead GROUP BY LegacyOrderCategoryId,ProviderStatus ORDER BY LegacyOrderCategoryId,ProviderStatus;
 SELECT N'UNMAPPED_PRODUCTS' Report,d.ProductCode,COUNT_BIG(*) Lines FROM #D d JOIN #ExpectedDetail e ON e.LegacySourceId=d.ID WHERE e.ProductVariantId IS NULL GROUP BY d.ProductCode;
 -- BEGIN REVIEWED 20260929 ORIGINAL NOTE VERIFICATION
 IF EXISTS(SELECT 1 FROM #ReviewedLongNoteReport r LEFT JOIN #ExpectedHead e ON e.LegacySourceId=r.LegacySourceId
   OUTER APPLY OPENJSON(e.LegacySnapshotJson) WITH(Note nvarchar(max)) raw
   WHERE e.Id IS NULL OR raw.Note IS NULL
     OR DATALENGTH(raw.Note)<>DATALENGTH(r.OriginalNote)
     OR raw.Note COLLATE Latin1_General_100_BIN2<>r.OriginalNote COLLATE Latin1_General_100_BIN2
     OR e.Note IS NULL OR DATALENGTH(e.Note)<>DATALENGTH(r.DisplayNote)
     OR e.Note COLLATE Latin1_General_100_BIN2<>r.DisplayNote COLLATE Latin1_General_100_BIN2)
   THROW 51000,N'REVIEWED_ORIGINAL_NOTE_NOT_PRESERVED: full source note or display note differs.',1;
 SELECT N'LONG_NOTE_ADJUSTMENTS' Report,LegacySourceId,LEN(OriginalNote) OriginalLength,
   LEN(DisplayNote) DisplayLength,CONVERT(bit,1) OriginalNotePreserved,OriginalNote,DisplayNote
 FROM #ReviewedLongNoteReport ORDER BY LegacySourceId;
 -- END REVIEWED 20260929 ORIGINAL NOTE VERIFICATION
 -- Assert the already imported stock source matches each linked issued invoice line exactly.
 IF EXISTS(SELECT 1 FROM #ExpectedDetail d JOIN #ExpectedHead h ON h.Id=d.InvoiceHeadId
 JOIN dbo.InvoiceInputStockSupplementalMovements s ON s.StoreId=@StoreId AND s.LegacySourceKey=CONCAT(N'GSTORE-IIS-V1|X|',d.LegacySourceId) AND s.IsDeleted=0
 WHERE s.MovementType<>4 OR h.ProviderStatus<>5 OR s.QuantityChange<>-d.Quantity*d.LegacyUnitFactor OR s.ProductVariantId<>d.ProductVariantId OR s.EffectiveAtUtc<>h.IssuedAtUtc OR s.WarehouseId<>@WarehouseId) THROW 51000,N'Invoice/stock migration disagree. Reconcile source snapshots first.',1;
 IF EXISTS(SELECT 1 FROM #ExpectedDetail d JOIN #ExpectedHead h ON h.Id=d.InvoiceHeadId WHERE h.ProviderStatus=5 AND d.Quantity<>0 AND d.ProductVariantId IS NOT NULL
 AND NOT EXISTS(SELECT 1 FROM dbo.InvoiceInputStockSupplementalMovements s WHERE s.StoreId=@StoreId AND s.LegacySourceKey=CONCAT(N'GSTORE-IIS-V1|X|',d.LegacySourceId) AND s.IsDeleted=0)) THROW 51000,N'Run invoice-stock migration for this snapshot first, then migrate invoices.',1;
 IF @Mode IN('DRYRUN','COMMIT')
 BEGIN
 SELECT * INTO #OutsideHead FROM dbo.InvoiceHeads WHERE StoreId<>@StoreId OR LegacySourceId IS NULL;
 UPDATE t SET [StoreId]=e.[StoreId],[OrderId]=e.[OrderId],[LegalEntityId]=e.[LegalEntityId],[InvoiceProviderSettingId]=e.[InvoiceProviderSettingId],[LegacySourceId]=e.[LegacySourceId],[LegacyOrderCategoryId]=e.[LegacyOrderCategoryId],[LegacyMergeId]=e.[LegacyMergeId],[LegacySnapshotJson]=e.[LegacySnapshotJson],[LegacyReadOnly]=e.[LegacyReadOnly],[InvoiceNumber]=e.[InvoiceNumber],[InvoiceDate]=e.[InvoiceDate],[BuyerType]=e.[BuyerType],[BuyerName]=e.[BuyerName],[BuyerLegalName]=e.[BuyerLegalName],[BuyerTaxCode]=e.[BuyerTaxCode],[BuyerAddress]=e.[BuyerAddress],[BuyerEmail]=e.[BuyerEmail],[BuyerPhone]=e.[BuyerPhone],[IsAutoInvoiceGroup]=e.[IsAutoInvoiceGroup],[BuyerCitizenId]=e.[BuyerCitizenId],[LastIssuanceRelevantChangeAtUtc]=e.[LastIssuanceRelevantChangeAtUtc],[TotalQuantity]=e.[TotalQuantity],[SubTotal]=e.[SubTotal],[VatAmount]=e.[VatAmount],[GrandTotal]=e.[GrandTotal],[Note]=e.[Note],[IsLocked]=e.[IsLocked],[LockedAtUtc]=e.[LockedAtUtc],[LockedByUserId]=e.[LockedByUserId],[LockReason]=e.[LockReason],[ProviderStatus]=e.[ProviderStatus],[TransactionUuid]=e.[TransactionUuid],[ProviderCode]=e.[ProviderCode],[SupplierTaxCode]=e.[SupplierTaxCode],[InvoiceType]=e.[InvoiceType],[TemplateCode]=e.[TemplateCode],[InvoiceSeries]=e.[InvoiceSeries],[ProviderInvoiceNo]=e.[ProviderInvoiceNo],[ProviderTransactionId]=e.[ProviderTransactionId],[ReservationCode]=e.[ReservationCode],[CodeOfTax]=e.[CodeOfTax],[IssuedAtUtc]=e.[IssuedAtUtc],[LastSyncedAtUtc]=e.[LastSyncedAtUtc],[LastErrorCode]=e.[LastErrorCode],[LastErrorMessage]=e.[LastErrorMessage],[PdfFilePath]=e.[PdfFilePath],[ZipFilePath]=e.[ZipFilePath],[OfficialPdfStatus]=e.[OfficialPdfStatus],[OfficialPdfDownloadedAtUtc]=e.[OfficialPdfDownloadedAtUtc],[OfficialPdfFileName]=e.[OfficialPdfFileName],[OfficialZipXmlStatus]=e.[OfficialZipXmlStatus],[OfficialZipXmlDownloadedAtUtc]=e.[OfficialZipXmlDownloadedAtUtc],[OfficialZipXmlFileName]=e.[OfficialZipXmlFileName],[EmailStatus]=e.[EmailStatus],[EmailSentAtUtc]=e.[EmailSentAtUtc],[LastEmailTo]=e.[LastEmailTo],[EmailSendCount]=e.[EmailSendCount],[LastEmailErrorMessage]=e.[LastEmailErrorMessage],[OriginalInvoiceHeadId]=e.[OriginalInvoiceHeadId],[CorrectionType]=e.[CorrectionType],[OriginalInvoiceNo]=e.[OriginalInvoiceNo],[OriginalInvoiceIssuedAtUtc]=e.[OriginalInvoiceIssuedAtUtc],[AdjustedNote]=e.[AdjustedNote],[AdditionalReferenceDesc]=e.[AdditionalReferenceDesc],[AdditionalReferenceDateUtc]=e.[AdditionalReferenceDateUtc],[IsDeleted]=e.[IsDeleted],LegacyImportedHash=e.LegacyImportedHash,UpdatedAtUtc=@Now FROM dbo.InvoiceHeads t JOIN #ExpectedHead e ON e.Id=t.Id JOIN #ChangedHead changed ON changed.Id=t.Id;
 SET IDENTITY_INSERT dbo.InvoiceHeads ON; SET @IdentityTable=1;
 INSERT dbo.InvoiceHeads(Id,[StoreId],[OrderId],[LegalEntityId],[InvoiceProviderSettingId],[LegacySourceId],[LegacyOrderCategoryId],[LegacyMergeId],[LegacySnapshotJson],[LegacyReadOnly],[InvoiceNumber],[InvoiceDate],[BuyerType],[BuyerName],[BuyerLegalName],[BuyerTaxCode],[BuyerAddress],[BuyerEmail],[BuyerPhone],[IsAutoInvoiceGroup],[BuyerCitizenId],[LastIssuanceRelevantChangeAtUtc],[TotalQuantity],[SubTotal],[VatAmount],[GrandTotal],[Note],[IsLocked],[LockedAtUtc],[LockedByUserId],[LockReason],[ProviderStatus],[TransactionUuid],[ProviderCode],[SupplierTaxCode],[InvoiceType],[TemplateCode],[InvoiceSeries],[ProviderInvoiceNo],[ProviderTransactionId],[ReservationCode],[CodeOfTax],[IssuedAtUtc],[LastSyncedAtUtc],[LastErrorCode],[LastErrorMessage],[PdfFilePath],[ZipFilePath],[OfficialPdfStatus],[OfficialPdfDownloadedAtUtc],[OfficialPdfFileName],[OfficialZipXmlStatus],[OfficialZipXmlDownloadedAtUtc],[OfficialZipXmlFileName],[EmailStatus],[EmailSentAtUtc],[LastEmailTo],[EmailSendCount],[LastEmailErrorMessage],[OriginalInvoiceHeadId],[CorrectionType],[OriginalInvoiceNo],[OriginalInvoiceIssuedAtUtc],[AdjustedNote],[AdditionalReferenceDesc],[AdditionalReferenceDateUtc],[IsDeleted],LegacyImportedHash,CreatedAtUtc) SELECT e.Id,e.[StoreId],e.[OrderId],e.[LegalEntityId],e.[InvoiceProviderSettingId],e.[LegacySourceId],e.[LegacyOrderCategoryId],e.[LegacyMergeId],e.[LegacySnapshotJson],e.[LegacyReadOnly],e.[InvoiceNumber],e.[InvoiceDate],e.[BuyerType],e.[BuyerName],e.[BuyerLegalName],e.[BuyerTaxCode],e.[BuyerAddress],e.[BuyerEmail],e.[BuyerPhone],e.[IsAutoInvoiceGroup],e.[BuyerCitizenId],e.[LastIssuanceRelevantChangeAtUtc],e.[TotalQuantity],e.[SubTotal],e.[VatAmount],e.[GrandTotal],e.[Note],e.[IsLocked],e.[LockedAtUtc],e.[LockedByUserId],e.[LockReason],e.[ProviderStatus],e.[TransactionUuid],e.[ProviderCode],e.[SupplierTaxCode],e.[InvoiceType],e.[TemplateCode],e.[InvoiceSeries],e.[ProviderInvoiceNo],e.[ProviderTransactionId],e.[ReservationCode],e.[CodeOfTax],e.[IssuedAtUtc],e.[LastSyncedAtUtc],e.[LastErrorCode],e.[LastErrorMessage],e.[PdfFilePath],e.[ZipFilePath],e.[OfficialPdfStatus],e.[OfficialPdfDownloadedAtUtc],e.[OfficialPdfFileName],e.[OfficialZipXmlStatus],e.[OfficialZipXmlDownloadedAtUtc],e.[OfficialZipXmlFileName],e.[EmailStatus],e.[EmailSentAtUtc],e.[LastEmailTo],e.[EmailSendCount],e.[LastEmailErrorMessage],e.[OriginalInvoiceHeadId],e.[CorrectionType],e.[OriginalInvoiceNo],e.[OriginalInvoiceIssuedAtUtc],e.[AdjustedNote],e.[AdditionalReferenceDesc],e.[AdditionalReferenceDateUtc],e.[IsDeleted],e.LegacyImportedHash,DATEADD(hour,-7,CONVERT(datetime2(7),CONVERT(datetime,JSON_VALUE(e.LegacySnapshotJson,'$.CreatedAt')))) FROM #ExpectedHead e WHERE NOT EXISTS(SELECT 1 FROM dbo.InvoiceHeads t WHERE t.Id=e.Id);
 SET IDENTITY_INSERT dbo.InvoiceHeads OFF; SET @IdentityTable=0;
 IF EXISTS(SELECT * FROM #OutsideHead EXCEPT SELECT * FROM dbo.InvoiceHeads WHERE StoreId<>@StoreId OR LegacySourceId IS NULL) OR EXISTS(SELECT * FROM dbo.InvoiceHeads WHERE StoreId<>@StoreId OR LegacySourceId IS NULL EXCEPT SELECT * FROM #OutsideHead) THROW 51000,N'Non-migration rows changed.',1;
 SELECT * INTO #OutsideDetail FROM dbo.InvoiceDetails WHERE StoreId<>@StoreId OR LegacySourceId IS NULL;
 UPDATE t SET [StoreId]=e.[StoreId],[InvoiceHeadId]=e.[InvoiceHeadId],[OrderLineId]=e.[OrderLineId],[OrderLegalEntityAllocationId]=e.[OrderLegalEntityAllocationId],[ProductVariantId]=e.[ProductVariantId],[SourceType]=e.[SourceType],[LegacySourceId]=e.[LegacySourceId],[LegacyUnitFactor]=e.[LegacyUnitFactor],[LegacySnapshotJson]=e.[LegacySnapshotJson],[ItemName]=e.[ItemName],[UnitName]=e.[UnitName],[Quantity]=e.[Quantity],[UnitPrice]=e.[UnitPrice],[Amount]=e.[Amount],[VatRate]=e.[VatRate],[VatAmount]=e.[VatAmount],[TotalAmount]=e.[TotalAmount],[Note]=e.[Note],[IsDeleted]=e.[IsDeleted],LegacyImportedHash=e.LegacyImportedHash,UpdatedAtUtc=@Now FROM dbo.InvoiceDetails t JOIN #ExpectedDetail e ON e.Id=t.Id JOIN #ChangedDetail changed ON changed.Id=t.Id;
 SET IDENTITY_INSERT dbo.InvoiceDetails ON; SET @IdentityTable=2;
 INSERT dbo.InvoiceDetails(Id,[StoreId],[InvoiceHeadId],[OrderLineId],[OrderLegalEntityAllocationId],[ProductVariantId],[SourceType],[LegacySourceId],[LegacyUnitFactor],[LegacySnapshotJson],[ItemName],[UnitName],[Quantity],[UnitPrice],[Amount],[VatRate],[VatAmount],[TotalAmount],[Note],[IsDeleted],LegacyImportedHash,CreatedAtUtc) SELECT e.Id,e.[StoreId],e.[InvoiceHeadId],e.[OrderLineId],e.[OrderLegalEntityAllocationId],e.[ProductVariantId],e.[SourceType],e.[LegacySourceId],e.[LegacyUnitFactor],e.[LegacySnapshotJson],e.[ItemName],e.[UnitName],e.[Quantity],e.[UnitPrice],e.[Amount],e.[VatRate],e.[VatAmount],e.[TotalAmount],e.[Note],e.[IsDeleted],e.LegacyImportedHash,h.CreatedAtUtc FROM #ExpectedDetail e JOIN dbo.InvoiceHeads h ON h.Id=e.InvoiceHeadId WHERE NOT EXISTS(SELECT 1 FROM dbo.InvoiceDetails t WHERE t.Id=e.Id);
 SET IDENTITY_INSERT dbo.InvoiceDetails OFF; SET @IdentityTable=0;
 IF EXISTS(SELECT * FROM #OutsideDetail EXCEPT SELECT * FROM dbo.InvoiceDetails WHERE StoreId<>@StoreId OR LegacySourceId IS NULL) OR EXISTS(SELECT * FROM dbo.InvoiceDetails WHERE StoreId<>@StoreId OR LegacySourceId IS NULL EXCEPT SELECT * FROM #OutsideDetail) THROW 51000,N'Non-migration rows changed.',1;
 END;
 IF @Mode IN('DRYRUN','COMMIT','VERIFY') BEGIN
 IF EXISTS(SELECT Id,[StoreId],[OrderId],[LegalEntityId],[InvoiceProviderSettingId],[LegacySourceId],[LegacyOrderCategoryId],[LegacyMergeId],[LegacySnapshotJson],[LegacyReadOnly],[InvoiceNumber],[InvoiceDate],[BuyerType],[BuyerName],[BuyerLegalName],[BuyerTaxCode],[BuyerAddress],[BuyerEmail],[BuyerPhone],[IsAutoInvoiceGroup],[BuyerCitizenId],[LastIssuanceRelevantChangeAtUtc],[TotalQuantity],[SubTotal],[VatAmount],[GrandTotal],[Note],[IsLocked],[LockedAtUtc],[LockedByUserId],[LockReason],[ProviderStatus],[TransactionUuid],[ProviderCode],[SupplierTaxCode],[InvoiceType],[TemplateCode],[InvoiceSeries],[ProviderInvoiceNo],[ProviderTransactionId],[ReservationCode],[CodeOfTax],[IssuedAtUtc],[LastSyncedAtUtc],[LastErrorCode],[LastErrorMessage],[PdfFilePath],[ZipFilePath],[OfficialPdfStatus],[OfficialPdfDownloadedAtUtc],[OfficialPdfFileName],[OfficialZipXmlStatus],[OfficialZipXmlDownloadedAtUtc],[OfficialZipXmlFileName],[EmailStatus],[EmailSentAtUtc],[LastEmailTo],[EmailSendCount],[LastEmailErrorMessage],[OriginalInvoiceHeadId],[CorrectionType],[OriginalInvoiceNo],[OriginalInvoiceIssuedAtUtc],[AdjustedNote],[AdditionalReferenceDesc],[AdditionalReferenceDateUtc],[IsDeleted],LegacyImportedHash FROM #ExpectedHead EXCEPT SELECT Id,[StoreId],[OrderId],[LegalEntityId],[InvoiceProviderSettingId],[LegacySourceId],[LegacyOrderCategoryId],[LegacyMergeId],[LegacySnapshotJson],[LegacyReadOnly],[InvoiceNumber],[InvoiceDate],[BuyerType],[BuyerName],[BuyerLegalName],[BuyerTaxCode],[BuyerAddress],[BuyerEmail],[BuyerPhone],[IsAutoInvoiceGroup],[BuyerCitizenId],[LastIssuanceRelevantChangeAtUtc],[TotalQuantity],[SubTotal],[VatAmount],[GrandTotal],[Note],[IsLocked],[LockedAtUtc],[LockedByUserId],[LockReason],[ProviderStatus],[TransactionUuid],[ProviderCode],[SupplierTaxCode],[InvoiceType],[TemplateCode],[InvoiceSeries],[ProviderInvoiceNo],[ProviderTransactionId],[ReservationCode],[CodeOfTax],[IssuedAtUtc],[LastSyncedAtUtc],[LastErrorCode],[LastErrorMessage],[PdfFilePath],[ZipFilePath],[OfficialPdfStatus],[OfficialPdfDownloadedAtUtc],[OfficialPdfFileName],[OfficialZipXmlStatus],[OfficialZipXmlDownloadedAtUtc],[OfficialZipXmlFileName],[EmailStatus],[EmailSentAtUtc],[LastEmailTo],[EmailSendCount],[LastEmailErrorMessage],[OriginalInvoiceHeadId],[CorrectionType],[OriginalInvoiceNo],[OriginalInvoiceIssuedAtUtc],[AdjustedNote],[AdditionalReferenceDesc],[AdditionalReferenceDateUtc],[IsDeleted],LegacyImportedHash FROM dbo.InvoiceHeads WHERE StoreId=@StoreId AND LegacySourceId IS NOT NULL) OR EXISTS(SELECT Id,[StoreId],[OrderId],[LegalEntityId],[InvoiceProviderSettingId],[LegacySourceId],[LegacyOrderCategoryId],[LegacyMergeId],[LegacySnapshotJson],[LegacyReadOnly],[InvoiceNumber],[InvoiceDate],[BuyerType],[BuyerName],[BuyerLegalName],[BuyerTaxCode],[BuyerAddress],[BuyerEmail],[BuyerPhone],[IsAutoInvoiceGroup],[BuyerCitizenId],[LastIssuanceRelevantChangeAtUtc],[TotalQuantity],[SubTotal],[VatAmount],[GrandTotal],[Note],[IsLocked],[LockedAtUtc],[LockedByUserId],[LockReason],[ProviderStatus],[TransactionUuid],[ProviderCode],[SupplierTaxCode],[InvoiceType],[TemplateCode],[InvoiceSeries],[ProviderInvoiceNo],[ProviderTransactionId],[ReservationCode],[CodeOfTax],[IssuedAtUtc],[LastSyncedAtUtc],[LastErrorCode],[LastErrorMessage],[PdfFilePath],[ZipFilePath],[OfficialPdfStatus],[OfficialPdfDownloadedAtUtc],[OfficialPdfFileName],[OfficialZipXmlStatus],[OfficialZipXmlDownloadedAtUtc],[OfficialZipXmlFileName],[EmailStatus],[EmailSentAtUtc],[LastEmailTo],[EmailSendCount],[LastEmailErrorMessage],[OriginalInvoiceHeadId],[CorrectionType],[OriginalInvoiceNo],[OriginalInvoiceIssuedAtUtc],[AdjustedNote],[AdditionalReferenceDesc],[AdditionalReferenceDateUtc],[IsDeleted],LegacyImportedHash FROM dbo.InvoiceHeads WHERE StoreId=@StoreId AND LegacySourceId IS NOT NULL EXCEPT SELECT Id,[StoreId],[OrderId],[LegalEntityId],[InvoiceProviderSettingId],[LegacySourceId],[LegacyOrderCategoryId],[LegacyMergeId],[LegacySnapshotJson],[LegacyReadOnly],[InvoiceNumber],[InvoiceDate],[BuyerType],[BuyerName],[BuyerLegalName],[BuyerTaxCode],[BuyerAddress],[BuyerEmail],[BuyerPhone],[IsAutoInvoiceGroup],[BuyerCitizenId],[LastIssuanceRelevantChangeAtUtc],[TotalQuantity],[SubTotal],[VatAmount],[GrandTotal],[Note],[IsLocked],[LockedAtUtc],[LockedByUserId],[LockReason],[ProviderStatus],[TransactionUuid],[ProviderCode],[SupplierTaxCode],[InvoiceType],[TemplateCode],[InvoiceSeries],[ProviderInvoiceNo],[ProviderTransactionId],[ReservationCode],[CodeOfTax],[IssuedAtUtc],[LastSyncedAtUtc],[LastErrorCode],[LastErrorMessage],[PdfFilePath],[ZipFilePath],[OfficialPdfStatus],[OfficialPdfDownloadedAtUtc],[OfficialPdfFileName],[OfficialZipXmlStatus],[OfficialZipXmlDownloadedAtUtc],[OfficialZipXmlFileName],[EmailStatus],[EmailSentAtUtc],[LastEmailTo],[EmailSendCount],[LastEmailErrorMessage],[OriginalInvoiceHeadId],[CorrectionType],[OriginalInvoiceNo],[OriginalInvoiceIssuedAtUtc],[AdjustedNote],[AdditionalReferenceDesc],[AdditionalReferenceDateUtc],[IsDeleted],LegacyImportedHash FROM #ExpectedHead) THROW 51000,N'InvoiceHeads: exact verification failed.',1;
 IF EXISTS(SELECT Id,[StoreId],[InvoiceHeadId],[OrderLineId],[OrderLegalEntityAllocationId],[ProductVariantId],[SourceType],[LegacySourceId],[LegacyUnitFactor],[LegacySnapshotJson],[ItemName],[UnitName],[Quantity],[UnitPrice],[Amount],[VatRate],[VatAmount],[TotalAmount],[Note],[IsDeleted],LegacyImportedHash FROM #ExpectedDetail EXCEPT SELECT Id,[StoreId],[InvoiceHeadId],[OrderLineId],[OrderLegalEntityAllocationId],[ProductVariantId],[SourceType],[LegacySourceId],[LegacyUnitFactor],[LegacySnapshotJson],[ItemName],[UnitName],[Quantity],[UnitPrice],[Amount],[VatRate],[VatAmount],[TotalAmount],[Note],[IsDeleted],LegacyImportedHash FROM dbo.InvoiceDetails WHERE StoreId=@StoreId AND LegacySourceId IS NOT NULL) OR EXISTS(SELECT Id,[StoreId],[InvoiceHeadId],[OrderLineId],[OrderLegalEntityAllocationId],[ProductVariantId],[SourceType],[LegacySourceId],[LegacyUnitFactor],[LegacySnapshotJson],[ItemName],[UnitName],[Quantity],[UnitPrice],[Amount],[VatRate],[VatAmount],[TotalAmount],[Note],[IsDeleted],LegacyImportedHash FROM dbo.InvoiceDetails WHERE StoreId=@StoreId AND LegacySourceId IS NOT NULL EXCEPT SELECT Id,[StoreId],[InvoiceHeadId],[OrderLineId],[OrderLegalEntityAllocationId],[ProductVariantId],[SourceType],[LegacySourceId],[LegacyUnitFactor],[LegacySnapshotJson],[ItemName],[UnitName],[Quantity],[UnitPrice],[Amount],[VatRate],[VatAmount],[TotalAmount],[Note],[IsDeleted],LegacyImportedHash FROM #ExpectedDetail) THROW 51000,N'InvoiceDetails: exact verification failed.',1;

 SELECT N'EXACT_VERIFY_PASS' Report,(SELECT COUNT_BIG(*) FROM #ExpectedHead) Heads,(SELECT COUNT_BIG(*) FROM #ExpectedDetail) Details;
 END;
 IF @Mode='COMMIT' BEGIN COMMIT; SELECT N'COMMIT_PASS' Report; END
 ELSE BEGIN ROLLBACK; SELECT CASE WHEN @Mode='DRYRUN' THEN N'DRYRUN_ROLLED_BACK' WHEN @Mode='VERIFY' THEN N'VERIFY_PASS' ELSE N'PREVIEW_ONLY' END Report; END;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 IF @IdentityTable=1 SET IDENTITY_INSERT dbo.InvoiceHeads OFF;
 IF @IdentityTable=2 SET IDENTITY_INSERT dbo.InvoiceDetails OFF;
 THROW;
END CATCH;
