from pathlib import Path
# Generate explicit, reviewable SQL; the generated SQL runs without Python.
H = {
"StoreId":"@StoreId", "OrderId":"o.Id", "LegalEntityId":"@LegalEntityId", "InvoiceProviderSettingId":"ps.Id",
"LegacySourceId":"h.Id", "LegacyOrderCategoryId":"h.OrderCategoryID", "LegacyMergeId":"h.IdGop", "LegacySnapshotJson":"r.SourceJson",
"LegacyReadOnly":"CONVERT(bit,CASE WHEN h.OrderCategoryID=13 OR o.Id IS NULL OR (h.OrderCategoryID=8 AND NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NULL) THEN 1 ELSE 0 END)",
"InvoiceNumber":"NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'')", "InvoiceDate":"CASE WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NOT NULL THEN h.IssuedDate ELSE h.CreatedAt END",
"BuyerType":"CASE WHEN NULLIF(LTRIM(RTRIM(h.BuyerTaxCode)),N'') IS NOT NULL OR NULLIF(LTRIM(RTRIM(h.BuyerLegalName)),N'') IS NOT NULL THEN N'Business' WHEN h.LayHD=1 THEN N'Individual' ELSE N'NoInvoice' END",
"BuyerName":"h.buyerName", "BuyerLegalName":"h.BuyerLegalName", "BuyerTaxCode":"h.BuyerTaxCode", "BuyerAddress":"h.BuyerAddressLine", "BuyerEmail":"h.BuyerEmail", "BuyerPhone":"h.BuyerPhoneNumber",
"IsAutoInvoiceGroup":"CONVERT(bit,0)", "BuyerCitizenId":"NULL",
"LastIssuanceRelevantChangeAtUtc":"(SELECT MAX(v.ChangedAt) FROM (VALUES (DATEADD(hour,-7,h.CreatedAt)),(o.CompletedAtUtc),(ret.LastReturnAtUtc)) v(ChangedAt))",
"TotalQuantity":"COALESCE(t.Quantity,0)", "SubTotal":"COALESCE(h.Amount,0)", "VatAmount":"0", "GrandTotal":"COALESCE(h.Amount,0)", "Note":"h.Note",
"IsLocked":"CONVERT(bit,CASE WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NOT NULL OR h.OrderCategoryID IN(8,13) OR o.Id IS NULL THEN 1 ELSE 0 END)",
"LockedAtUtc":"CASE WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NOT NULL THEN DATEADD(hour,-7,h.IssuedDate) WHEN h.OrderCategoryID IN(8,13) OR o.Id IS NULL THEN DATEADD(hour,-7,h.CreatedAt) END",
"LockedByUserId":"NULL", "LockReason":"CASE WHEN h.OrderCategoryID=13 THEN N'Hóa đơn điều chỉnh GaoStore: chỉ lưu tra cứu theo yêu cầu.' WHEN o.Id IS NULL THEN N'Chưa liên kết Order GaoApp; giữ nguyên hóa đơn GaoStore để tra cứu.' WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NOT NULL THEN N'Hóa đơn đã phát hành tại GaoStore.' WHEN h.OrderCategoryID=8 THEN N'GaoStore đánh dấu đã phát hành nhưng thiếu số hóa đơn; cần đối chiếu, không phát hành lại.' END",
"ProviderStatus":"CONVERT(tinyint,CASE WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NULL THEN 0 ELSE 5 END)",
"TransactionUuid":"NULL", "ProviderCode":"N'VIETTEL'", "SupplierTaxCode":"h.SellerCode", "InvoiceType":"h.InvoiceType", "TemplateCode":"h.TemplateCode", "InvoiceSeries":"h.InvoiceSeries",
"ProviderInvoiceNo":"NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'')", "ProviderTransactionId":"NULL", "ReservationCode":"h.MaBiMat", "CodeOfTax":"h.MaCQT",
"IssuedAtUtc":"CASE WHEN NULLIF(LTRIM(RTRIM(h.InvoiceNumber)),N'') IS NOT NULL THEN DATEADD(hour,-7,h.IssuedDate) END",
"LastSyncedAtUtc":"NULL", "LastErrorCode":"NULL", "LastErrorMessage":"NULL", "PdfFilePath":"NULL", "ZipFilePath":"NULL",
"OfficialPdfStatus":"0", "OfficialPdfDownloadedAtUtc":"NULL", "OfficialPdfFileName":"NULL", "OfficialZipXmlStatus":"0", "OfficialZipXmlDownloadedAtUtc":"NULL", "OfficialZipXmlFileName":"NULL",
"EmailStatus":"0", "EmailSentAtUtc":"NULL", "LastEmailTo":"NULL", "EmailSendCount":"0", "LastEmailErrorMessage":"NULL",
"OriginalInvoiceHeadId":"NULL", "CorrectionType":"NULL", "OriginalInvoiceNo":"NULL", "OriginalInvoiceIssuedAtUtc":"NULL", "AdjustedNote":"NULL", "AdditionalReferenceDesc":"NULL", "AdditionalReferenceDateUtc":"NULL", "IsDeleted":"CONVERT(bit,0)"
}
D = {"StoreId":"@StoreId", "InvoiceHeadId":"hm.TargetId", "OrderLineId":"NULL", "OrderLegalEntityAllocationId":"NULL", "ProductVariantId":"m.VariantId", "SourceType":"CONVERT(tinyint,2)",
"LegacySourceId":"d.ID", "LegacyUnitFactor":"m.Factor", "LegacySnapshotJson":"r.SourceJson", "ItemName":"COALESCE(d.ItemName,N'[Chưa có tên]')", "UnitName":"d.Unit", "Quantity":"d.Quantity",
"UnitPrice":"COALESCE(d.UnitPrice,CASE WHEN d.Quantity=0 THEN 0 ELSE ROUND(d.Amount/d.Quantity,2) END)", "Amount":"d.Amount", "VatRate":"0", "VatAmount":"0", "TotalAmount":"d.Amount", "Note":"d.Note", "IsDeleted":"CONVERT(bit,0)"}
head_schema = "Id bigint, InvoiceNumber nvarchar(50), InvoiceType nvarchar(20), TemplateCode nvarchar(20), InvoiceSeries nvarchar(20), IssuedDate datetime, Amount decimal(18,2), SellerCode nvarchar(20), CreatedAt datetime, buyerName nvarchar(100), BuyerAddressLine nvarchar(250), BuyerLegalName nvarchar(250), BuyerTaxCode nvarchar(20), BuyerPhoneNumber nvarchar(15), BuyerEmail nvarchar(100), OrderCategoryID bigint, LayHD bit, MaBiMat nvarchar(50), MaCQT nvarchar(50), Note nvarchar(max), IdGop nvarchar(15)"
detail_schema = "ID bigint, OrderID bigint, ProductCode nvarchar(200), ItemName nvarchar(250), Unit nvarchar(100), Quantity decimal(18,3), UnitPrice decimal(18,2), Amount decimal(18,2), Note nvarchar(500)"
def cols(mapping, alias=None): return ','.join((alias+'.' if alias else '')+'['+c+']' for c in mapping)
def digest(mapping,alias): return "HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(SELECT "+cols(mapping,alias)+" FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)))"
sql = r"""/* GaoStore InvoiceHeads/InvoiceDetails migration. TEST before real cutover.
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
"""
sql+=f"SELECT h.* INTO #H FROM #SourceHead r CROSS APPLY OPENJSON(r.SourceJson) WITH ({head_schema}) h;\nSELECT d.* INTO #D FROM #SourceDetail r CROSS APPLY OPENJSON(r.SourceJson) WITH ({detail_schema}) d;\n".replace('\n','\n')
sql+=r"""
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
"""
sql+=f" SELECT TOP(0) CONVERT(int,Id) Id,{cols(H)},LegacyImportedHash INTO #ExpectedHead FROM dbo.InvoiceHeads;\n INSERT #ExpectedHead(Id,{cols(H)})\n SELECT hm.TargetId,"+',\n'.join(H.values())+r"""
 FROM #H h JOIN #SourceHead r ON r.LegacyId=h.Id JOIN #HeadMap hm ON hm.LegacyId=h.Id
 LEFT JOIN #Totals t ON t.OrderID=h.Id
 LEFT JOIN dbo.Orders o ON o.StoreId=@StoreId AND o.Id=h.Id AND o.OrderNumber=CONCAT(N'LEGACY-',h.Id) AND o.IsDeleted=0
 OUTER APPLY(SELECT MAX(sr.CompletedAtUtc) LastReturnAtUtc FROM dbo.SalesReturns sr WHERE sr.OrderId=o.Id AND sr.StoreId=@StoreId AND sr.IsDeleted=0 AND sr.Status=1)ret
 OUTER APPLY(SELECT MIN(s.Id) Id FROM dbo.InvoiceProviderSettings s WHERE s.StoreId=@StoreId AND s.IsDeleted=0 AND s.IsActive=1 AND s.ProviderCode=N'VIETTEL' AND s.SupplierTaxCode=h.SellerCode AND s.InvoiceType=h.InvoiceType AND s.TemplateCode=h.TemplateCode AND s.InvoiceSeries=h.InvoiceSeries HAVING COUNT_BIG(*)=1)ps;
"""
sql+=f" SELECT TOP(0) CONVERT(int,Id) Id,{cols(D)},LegacyImportedHash INTO #ExpectedDetail FROM dbo.InvoiceDetails;\n INSERT #ExpectedDetail(Id,{cols(D)})\n SELECT dm.TargetId,"+',\n'.join(D.values())+r"""
 FROM #D d JOIN #SourceDetail r ON r.LegacyId=d.ID JOIN #DetailMap dm ON dm.LegacyId=d.ID JOIN #HeadMap hm ON hm.LegacyId=d.OrderID
 LEFT JOIN #Map m ON m.Code=LTRIM(RTRIM(REPLACE(d.ProductCode,NCHAR(160),N' '))) COLLATE SQL_Latin1_General_CP1_CI_AS;
"""
for short,table,fields in [('Head','InvoiceHeads',H),('Detail','InvoiceDetails',D)]:
    sql+=f" UPDATE e SET LegacyImportedHash={digest(fields,'e')} FROM #Expected{short} e;\n"
    sql+=f" CREATE UNIQUE CLUSTERED INDEX IX_Expected{short} ON #Expected{short}(Id);\n"
    sql+=f" SELECT e.Id INTO #Changed{short} FROM #Expected{short} e JOIN dbo.{table} t ON t.Id=e.Id WHERE e.LegacyImportedHash<>t.LegacyImportedHash;\n"
    sql+=f" IF EXISTS(SELECT 1 FROM dbo.{table} t JOIN #Expected{short} e ON e.Id=t.Id WHERE t.LegacyImportedHash IS NULL OR t.LegacyImportedHash<>{digest(fields,'t')}) THROW 51000,N'{table}: imported row edited in GaoApp; refusing to overwrite.',1;\n"
sql+=r"""
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
"""
for short,table,fields in [('Head','InvoiceHeads',H),('Detail','InvoiceDetails',D)]:
    sql+=f" SELECT * INTO #Outside{short} FROM dbo.{table} WHERE StoreId<>@StoreId OR LegacySourceId IS NULL;\n"
    sql+=f" UPDATE t SET "+','.join(f'[{c}]=e.[{c}]' for c in fields)+f",LegacyImportedHash=e.LegacyImportedHash,UpdatedAtUtc=@Now FROM dbo.{table} t JOIN #Expected{short} e ON e.Id=t.Id JOIN #Changed{short} changed ON changed.Id=t.Id;\n"
    sql+=f" SET IDENTITY_INSERT dbo.{table} ON; SET @IdentityTable="+('1' if short=='Head' else '2')+';\n'
    created="DATEADD(hour,-7,CONVERT(datetime2(7),CONVERT(datetime,JSON_VALUE(e.LegacySnapshotJson,'$.CreatedAt'))))" if short=='Head' else "h.CreatedAtUtc"
    join='' if short=='Head' else ' JOIN dbo.InvoiceHeads h ON h.Id=e.InvoiceHeadId'
    sql+=f" INSERT dbo.{table}(Id,{cols(fields)},LegacyImportedHash,CreatedAtUtc) SELECT e.Id,{cols(fields,'e')},e.LegacyImportedHash,{created} FROM #Expected{short} e{join} WHERE NOT EXISTS(SELECT 1 FROM dbo.{table} t WHERE t.Id=e.Id);\n SET IDENTITY_INSERT dbo.{table} OFF; SET @IdentityTable=0;\n"
    sql+=f" IF EXISTS(SELECT * FROM #Outside{short} EXCEPT SELECT * FROM dbo.{table} WHERE StoreId<>@StoreId OR LegacySourceId IS NULL) OR EXISTS(SELECT * FROM dbo.{table} WHERE StoreId<>@StoreId OR LegacySourceId IS NULL EXCEPT SELECT * FROM #Outside{short}) THROW 51000,N'Non-migration rows changed.',1;\n"
sql+=" END;\n IF @Mode IN('DRYRUN','COMMIT','VERIFY') BEGIN\n"
for short,table,fields in [('Head','InvoiceHeads',H),('Detail','InvoiceDetails',D)]:
    sql+=f" IF EXISTS(SELECT Id,{cols(fields)},LegacyImportedHash FROM #Expected{short} EXCEPT SELECT Id,{cols(fields)},LegacyImportedHash FROM dbo.{table} WHERE StoreId=@StoreId AND LegacySourceId IS NOT NULL) OR EXISTS(SELECT Id,{cols(fields)},LegacyImportedHash FROM dbo.{table} WHERE StoreId=@StoreId AND LegacySourceId IS NOT NULL EXCEPT SELECT Id,{cols(fields)},LegacyImportedHash FROM #Expected{short}) THROW 51000,N'{table}: exact verification failed.',1;\n"
sql+=r"""
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
"""
Path(__file__).with_name('InvoiceMigration.sql').write_text(sql,encoding='utf-8')
