-- Reviewed initial import. Run only through Invoke-Migration.ps1.
IF @@TRANCOUNT<>1 OR ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'GSTORE_INITIAL_IMPORT')),0)<>1
    THROW 55100,'Run through the transactional package runner.',1;
DECLARE @MigrationExecutionUtc datetime2(7)=SYSUTCDATETIME();

-- REAL source fingerprint captured from the actual source at execution time.
DECLARE @ExpectedProductDetailRows bigint=(SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.ProductDetail);
DECLARE @ExpectedProductRows bigint=(SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.Product);
DECLARE @ExpectedOrderRows bigint=(SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.[Order]);
DECLARE @ExpectedOrderDetailRows bigint=(SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.OrderDetail);
DECLARE @ExpectedHoaDonNhapRows bigint=(SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.HoaDonNhap);
DECLARE @ExpectedChiTietNhapKhoRows bigint=(SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.ChiTietNhapKho);

DECLARE @ExpectedProductChecksum int=(SELECT CHECKSUM_AGG(BINARY_CHECKSUM(ID,Code,CreatedDate,Warranty,Quantity,PurchasePrice)) FROM [__SOURCE__].dbo.Product);
DECLARE @ExpectedOrderChecksum int=(SELECT CHECKSUM_AGG(BINARY_CHECKSUM(ID,CreatedDate,Total,OrderCategoryID)) FROM [__SOURCE__].dbo.[Order]);
DECLARE @ExpectedOrderDetailChecksum int=(SELECT CHECKSUM_AGG(BINARY_CHECKSUM(ID,OrderID,ProductCode,Quantity,Price,Total,OrderCategoryID)) FROM [__SOURCE__].dbo.OrderDetail);
DECLARE @ExpectedHoaDonNhapChecksum int=(SELECT CHECKSUM_AGG(BINARY_CHECKSUM(MaHD,LoaiHD,TrangThai,NgayNhap,NgayXuLy,TenHD,Note,MaNVNhap,TenNV)) FROM [__SOURCE__].dbo.HoaDonNhap);
DECLARE @ExpectedChiTietNhapKhoChecksum int=(SELECT CHECKSUM_AGG(BINARY_CHECKSUM(MaHD,Code,Quantity,PurchasePrice,Name,Note)) FROM [__SOURCE__].dbo.ChiTietNhapKho);
-------------------------------------------------------------------------------
-- A. SOURCE / TARGET BASELINE GUARDS
-------------------------------------------------------------------------------
DECLARE @BeforeProducts bigint=(SELECT COUNT_BIG(*) FROM dbo.Products);
DECLARE @BeforeVariants bigint=(SELECT COUNT_BIG(*) FROM dbo.ProductVariant);
DECLARE @BeforeConversions bigint=(SELECT COUNT_BIG(*) FROM dbo.ProductUnitConversion);
DECLARE @BeforeBarcodes bigint=(SELECT COUNT_BIG(*) FROM dbo.ProductVariantUnitBarcode);
DECLARE @BeforeCustomers bigint=(SELECT COUNT_BIG(*) FROM dbo.Customers);
DECLARE @BeforeOrders bigint=(SELECT COUNT_BIG(*) FROM dbo.Orders);
DECLARE @BeforeOrderLines bigint=(SELECT COUNT_BIG(*) FROM dbo.OrderLines);
DECLARE @BeforeOrderPayments bigint=(SELECT COUNT_BIG(*) FROM dbo.OrderPayments);
DECLARE @BeforeReturns bigint=(SELECT COUNT_BIG(*) FROM dbo.SalesReturns);
DECLARE @BeforeReturnLines bigint=(SELECT COUNT_BIG(*) FROM dbo.SalesReturnLines);
DECLARE @BeforeReturnPayments bigint=(SELECT COUNT_BIG(*) FROM dbo.SalesReturnPayments);
DECLARE @BeforePOSShifts bigint=(SELECT COUNT_BIG(*) FROM dbo.POSShifts);
DECLARE @BeforePOSCash bigint=(SELECT COUNT_BIG(*) FROM dbo.POSShiftCashTransactions);
DECLARE @BeforeUsers bigint=(SELECT COUNT_BIG(*) FROM dbo.Users);

DECLARE @BeforeStockDocuments bigint=(SELECT COUNT_BIG(*) FROM dbo.StockDocument);
DECLARE @BeforeStockDocumentLines bigint=(SELECT COUNT_BIG(*) FROM dbo.StockDocumentLine);
DECLARE @BeforeTransactions bigint=(SELECT COUNT_BIG(*) FROM dbo.InventoryTransactions);
DECLARE @BeforeValuations bigint=(SELECT COUNT_BIG(*) FROM dbo.InventoryValuationEntries);
DECLARE @BeforeLayers bigint=(SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayers);
DECLARE @BeforeAllocations bigint=(SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayerAllocations);
DECLARE @BeforeBalances bigint=(SELECT COUNT_BIG(*) FROM dbo.InventoryBalances);

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Stores s
    JOIN dbo.Warehouses w ON w.Id=1
    JOIN dbo.LegalEntities le ON le.Id=w.LegalEntityId
    WHERE s.Id=1 AND s.IsDeleted=0
      AND w.StoreId=1 AND w.LegalEntityId=1 AND w.IsActive=1 AND w.IsDeleted=0
      AND w.AllowNegativeInventory=1
      AND le.StoreId=1 AND le.IsDeleted=0
)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: Store/Warehouse/LegalEntity foundation guard failed.',1;

-- Dynamic positive ID bases.  Use the greater of current identity and current MAX(Id)
-- so this same rule remains environment-specific and does not hard-code TEST IDs.
DECLARE @StockDocumentIdBase bigint;
DECLARE @StockDocumentLineIdBase bigint;
DECLARE @InventoryTransactionIdBase bigint;
DECLARE @InventoryValuationEntryIdBase bigint;
DECLARE @InventoryCostLayerIdBase bigint;
DECLARE @InventoryCostLayerAllocationIdBase bigint;
DECLARE @InventoryBalanceIdBase bigint;

SELECT @StockDocumentIdBase=MAX(v)
FROM
(
    SELECT COALESCE(CONVERT(bigint,IDENT_CURRENT(N'dbo.StockDocument')),0) v
    UNION ALL SELECT COALESCE(MAX(CONVERT(bigint,Id)),0) FROM dbo.StockDocument
)x;

SELECT @StockDocumentLineIdBase=MAX(v)
FROM
(
    SELECT COALESCE(CONVERT(bigint,IDENT_CURRENT(N'dbo.StockDocumentLine')),0) v
    UNION ALL SELECT COALESCE(MAX(CONVERT(bigint,Id)),0) FROM dbo.StockDocumentLine
)x;

SELECT @InventoryTransactionIdBase=MAX(v)
FROM
(
    SELECT COALESCE(CONVERT(bigint,IDENT_CURRENT(N'dbo.InventoryTransactions')),0) v
    UNION ALL SELECT COALESCE(MAX(CONVERT(bigint,Id)),0) FROM dbo.InventoryTransactions
)x;

SELECT @InventoryValuationEntryIdBase=MAX(v)
FROM
(
    SELECT COALESCE(CONVERT(bigint,IDENT_CURRENT(N'dbo.InventoryValuationEntries')),0) v
    UNION ALL SELECT COALESCE(MAX(CONVERT(bigint,Id)),0) FROM dbo.InventoryValuationEntries
)x;

SELECT @InventoryCostLayerIdBase=MAX(v)
FROM
(
    SELECT COALESCE(CONVERT(bigint,IDENT_CURRENT(N'dbo.InventoryCostLayers')),0) v
    UNION ALL SELECT COALESCE(MAX(CONVERT(bigint,Id)),0) FROM dbo.InventoryCostLayers
)x;

SELECT @InventoryCostLayerAllocationIdBase=MAX(v)
FROM
(
    SELECT COALESCE(CONVERT(bigint,IDENT_CURRENT(N'dbo.InventoryCostLayerAllocations')),0) v
    UNION ALL SELECT COALESCE(MAX(CONVERT(bigint,Id)),0) FROM dbo.InventoryCostLayerAllocations
)x;

SELECT @InventoryBalanceIdBase=MAX(v)
FROM
(
    SELECT COALESCE(CONVERT(bigint,IDENT_CURRENT(N'dbo.InventoryBalances')),0) v
    UNION ALL SELECT COALESCE(MAX(CONVERT(bigint,Id)),0) FROM dbo.InventoryBalances
)x;

CREATE TABLE #IdentityBefore(TableName sysname PRIMARY KEY,IdentityValue numeric(38,0) NULL);
INSERT INTO #IdentityBefore VALUES
(N'StockDocument',CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.StockDocument'))),
(N'StockDocumentLine',CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.StockDocumentLine'))),
(N'InventoryTransactions',CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.InventoryTransactions'))),
(N'InventoryValuationEntries',CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.InventoryValuationEntries'))),
(N'InventoryCostLayers',CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.InventoryCostLayers'))),
(N'InventoryCostLayerAllocations',CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.InventoryCostLayerAllocations'))),
(N'InventoryBalances',CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.InventoryBalances')));

SELECT N'STEP4V2_INITIAL_IMPORT_00_SOURCE_FINGERPRINT' ResultSet,
       @ExpectedProductDetailRows ProductDetailRows,@ExpectedProductRows ProductRows,
       @ExpectedOrderRows OrderRows,@ExpectedOrderDetailRows OrderDetailRows,
       @ExpectedHoaDonNhapRows HoaDonNhapRows,@ExpectedChiTietNhapKhoRows ChiTietNhapKhoRows,
       @ExpectedProductChecksum ProductChecksum,@ExpectedOrderChecksum OrderChecksum,
       @ExpectedOrderDetailChecksum OrderDetailChecksum,@ExpectedHoaDonNhapChecksum HoaDonNhapChecksum,
       @ExpectedChiTietNhapKhoChecksum ChiTietNhapKhoChecksum;

-------------------------------------------------------------------------------
-- B. APPROVED EXCEPTION LISTS / MASTER MAP
-------------------------------------------------------------------------------
DECLARE @Combo89 TABLE(Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS PRIMARY KEY);
INSERT INTO @Combo89(Code) VALUES
    (N'6936218505287'),
    (N'8893850261701'),
    (N'8934564100581'),
    (N'8934564100598'),
    (N'8934609106585'),
    (N'8934609108664'),
    (N'8934673100182'),
    (N'8934673300711'),
    (N'8934673300742'),
    (N'8934673303507'),
    (N'8934673303521'),
    (N'8934673304528'),
    (N'8934673320566'),
    (N'8934673320580'),
    (N'8934673323512'),
    (N'8934673400510'),
    (N'8934673434508'),
    (N'8934673435505'),
    (N'8935001709121'),
    (N'8935001714859'),
    (N'8935001716969'),
    (N'8935001717393'),
    (N'8935001719021'),
    (N'8935049590019'),
    (N'8935060600087'),
    (N'8935077400298'),
    (N'8935077401592'),
    (N'8935117702016'),
    (N'8935117702207'),
    (N'8935117702283'),
    (N'8935136868694'),
    (N'8935217411443'),
    (N'8935217411542'),
    (N'8935217411641'),
    (N'8935217430109'),
    (N'8935297105096'),
    (N'8935297105119'),
    (N'8935304202602'),
    (N'8935304202633'),
    (N'8935304202664'),
    (N'8935304203500'),
    (N'8935335400305'),
    (N'8935335400541'),
    (N'8935335400572'),
    (N'8935335401289'),
    (N'8935335401302'),
    (N'8935335401555'),
    (N'8935335401838'),
    (N'8936023024063'),
    (N'8936023024070'),
    (N'8936023024087'),
    (N'8936023024094'),
    (N'8936029721409'),
    (N'8936029722017'),
    (N'8936029722024'),
    (N'8936029722031'),
    (N'8936029722246'),
    (N'8936029722260'),
    (N'8936034610552'),
    (N'8936034877108'),
    (N'8936034877115'),
    (N'8936057393715'),
    (N'8936071091994'),
    (N'8936071092007'),
    (N'8936071092014'),
    (N'8936071092021'),
    (N'8936071092120'),
    (N'8936113380086'),
    (N'8936113380123'),
    (N'8936114080237'),
    (N'8936210413908'),
    (N'8938502525054'),
    (N'8938502525221'),
    (N'8938502525245'),
    (N'8938502525337'),
    (N'8938502525344'),
    (N'8938502525351'),
    (N'8938502525924'),
    (N'8938502525948'),
    (N'8938502525962'),
    (N'8938508788644'),
    (N'8938539202195'),
    (N'8938539202201'),
    (N'8992759124194'),
    (N'9556001317322'),
    (N'0984712434869'),
    (N'8850092280109'),
    (N'8934563168056'),
    (N'8938502525443');
IF (SELECT COUNT_BIG(*) FROM @Combo89)<>89
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: combo list !=89.',1;

DECLARE @ApprovedOrphan5 TABLE(Id bigint PRIMARY KEY);
INSERT INTO @ApprovedOrphan5 VALUES(606381),(606382),(606383),(606384),(606385);

DECLARE @ApprovedNegativeHeader9 TABLE(OrderId bigint PRIMARY KEY);
INSERT INTO @ApprovedNegativeHeader9 VALUES
(952088),(1131893),(1202874),(1458502),(1559005),(1589192),(1611416),(1612817),(1767174);

CREATE TABLE #DiscountCode(Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS PRIMARY KEY);
INSERT INTO #DiscountCode(Code)
SELECT DISTINCT CONVERT(nvarchar(200),od.ProductCode) COLLATE SQL_Latin1_General_CP1_CI_AS
FROM [__SOURCE__].dbo.OrderDetail od
WHERE od.OrderCategoryID=1 AND od.ProductCode IS NOT NULL
  AND (COALESCE(od.Price,0)<0 OR COALESCE(od.Total,0)<0);

CREATE TABLE #Master
(
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS PRIMARY KEY,
    ProductDetailId bigint NOT NULL,
    MasterCost decimal(38,6) NOT NULL,
    MasterCostWasNull bit NOT NULL
);
INSERT INTO #Master(Code,ProductDetailId,MasterCost,MasterCostWasNull)
SELECT CONVERT(nvarchar(200),pd.Code) COLLATE SQL_Latin1_General_CP1_CI_AS,
       CONVERT(bigint,pd.Id),CONVERT(decimal(38,6),COALESCE(pd.PurchasePrice,0)),
       CONVERT(bit,CASE WHEN pd.PurchasePrice IS NULL THEN 1 ELSE 0 END)
FROM [__SOURCE__].dbo.ProductDetail pd
WHERE pd.Code IS NOT NULL AND LTRIM(RTRIM(CONVERT(nvarchar(200),pd.Code)))<>N'';

CREATE TABLE #CandidatePath
(
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    ProductVariantId int NOT NULL,
    MatchPath nvarchar(10) NOT NULL
);
INSERT INTO #CandidatePath(Code,ProductVariantId,MatchPath)
SELECT m.Code,pv.Id,N'SKU'
FROM #Master m
JOIN dbo.ProductVariant pv ON pv.StoreId=1 AND pv.IsDeleted=0
 AND pv.Sku COLLATE SQL_Latin1_General_CP1_CI_AS=m.Code
UNION ALL
SELECT m.Code,puc.ProductVariantId,N'BARCODE'
FROM #Master m
JOIN dbo.ProductVariantUnitBarcode b ON b.StoreId=1 AND b.IsDeleted=0
 AND b.Barcode COLLATE SQL_Latin1_General_CP1_CI_AS=m.Code
JOIN dbo.ProductUnitConversion puc ON puc.Id=b.ProductUnitConversionId AND puc.StoreId=1 AND puc.IsDeleted=0
JOIN dbo.ProductVariant pv ON pv.Id=puc.ProductVariantId AND pv.StoreId=1 AND pv.IsDeleted=0;

CREATE TABLE #Map
(
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS PRIMARY KEY,
    CandidateVariantCount int NOT NULL,
    ProductVariantId int NULL
);
INSERT INTO #Map(Code,CandidateVariantCount,ProductVariantId)
SELECT m.Code,COUNT(DISTINCT cp.ProductVariantId),
       CASE WHEN COUNT(DISTINCT cp.ProductVariantId)=1 THEN MIN(cp.ProductVariantId) END
FROM #Master m LEFT JOIN #CandidatePath cp ON cp.Code=m.Code
GROUP BY m.Code;

IF EXISTS(SELECT 1 FROM #Map WHERE CandidateVariantCount>1)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: multi-target Product mapping found.',1;

-------------------------------------------------------------------------------
-- C. STAGE RECEIPT v2 — NO MASTER / NO TARGET EXCLUDED
-------------------------------------------------------------------------------
IF EXISTS
(
    SELECT 1
    FROM [__SOURCE__].dbo.HoaDonNhap h
    JOIN [__SOURCE__].dbo.ChiTietNhapKho d ON d.MaHD=h.MaHD
    WHERE h.LoaiHD=N'Hóa đơn nhập' AND h.TrangThai IN(0,1)
      AND (d.Code IS NULL OR LTRIM(RTRIM(CONVERT(nvarchar(200),d.Code)))=N'')
)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: selected legacy receipt has missing Code.',1;

CREATE TABLE #ReceiptSource
(
    LegacyMaHD nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    LegacyStatus int NOT NULL,
    DocumentDate datetime2(7) NOT NULL,
    ProcessedDate datetime2(7) NULL,
    DocumentTitle nvarchar(255) NULL,
    HeaderSourceNote nvarchar(1000) NULL,
    LegacyInputUserId nvarchar(100) NULL,
    LegacyInputUserName nvarchar(250) NULL,
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    SourceName nvarchar(500) NULL,
    SourceQty decimal(38,6) NULL,
    SourcePrice decimal(38,6) NULL,
    SourceLineNote nvarchar(500) NULL
);
INSERT INTO #ReceiptSource
SELECT CONVERT(nvarchar(50),h.MaHD),CONVERT(int,h.TrangThai),CONVERT(datetime2(7),h.NgayNhap),
       CONVERT(datetime2(7),h.NgayXuLy),LEFT(CONVERT(nvarchar(255),h.TenHD),255),
       LEFT(CONVERT(nvarchar(1000),h.Note),1000),CONVERT(nvarchar(100),h.MaNVNhap),
       LEFT(CONVERT(nvarchar(250),h.TenNV),250),
       CONVERT(nvarchar(200),d.Code) COLLATE SQL_Latin1_General_CP1_CI_AS,
       LEFT(CONVERT(nvarchar(500),d.Name),500),CONVERT(decimal(38,6),d.Quantity),
       CONVERT(decimal(38,6),d.PurchasePrice),LEFT(CONVERT(nvarchar(500),d.Note),500)
FROM [__SOURCE__].dbo.HoaDonNhap h
JOIN [__SOURCE__].dbo.ChiTietNhapKho d ON d.MaHD=h.MaHD
WHERE h.LoaiHD=N'Hóa đơn nhập' AND h.TrangThai IN(0,1);

IF EXISTS(SELECT 1 FROM #ReceiptSource WHERE Code IS NULL OR LTRIM(RTRIM(Code))=N'')
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: receipt source has missing Code.',1;
IF EXISTS(SELECT 1 FROM #ReceiptSource WHERE DocumentDate IS NULL)
 OR EXISTS(SELECT 1 FROM #ReceiptSource WHERE LegacyStatus=1 AND ProcessedDate IS NULL)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: receipt required date missing.',1;
IF EXISTS(SELECT 1 FROM #ReceiptSource WHERE SourceQty IS NOT NULL AND SourceQty<>ROUND(SourceQty,3))
 OR EXISTS(SELECT 1 FROM #ReceiptSource WHERE SourcePrice IS NOT NULL AND SourcePrice<>ROUND(SourcePrice,2))
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: receipt precision exceeds target contract.',1;

CREATE TABLE #ReceiptCode(Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS PRIMARY KEY);
INSERT INTO #ReceiptCode(Code)
SELECT DISTINCT rs.Code
FROM #ReceiptSource rs
JOIN #Master m ON m.Code=rs.Code
JOIN #Map mp ON mp.Code=rs.Code AND mp.CandidateVariantCount=1;

CREATE TABLE #ReceiptBarcodeOption
(
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS PRIMARY KEY,
    ConversionCount int NOT NULL,ProductUnitConversionId int NULL,UnitId int NULL,Factor decimal(18,4) NULL
);
INSERT INTO #ReceiptBarcodeOption
SELECT rc.Code,COUNT(DISTINCT puc.Id),
       CASE WHEN COUNT(DISTINCT puc.Id)=1 THEN MIN(puc.Id) END,
       CASE WHEN COUNT(DISTINCT puc.Id)=1 THEN MIN(puc.UnitId) END,
       CASE WHEN COUNT(DISTINCT puc.Id)=1 THEN MIN(puc.Factor) END
FROM #ReceiptCode rc
JOIN #Map mp ON mp.Code=rc.Code AND mp.CandidateVariantCount=1
JOIN dbo.ProductVariantUnitBarcode b ON b.StoreId=1 AND b.IsDeleted=0
 AND b.Barcode COLLATE SQL_Latin1_General_CP1_CI_AS=rc.Code
JOIN dbo.ProductUnitConversion puc ON puc.Id=b.ProductUnitConversionId AND puc.StoreId=1 AND puc.IsDeleted=0
 AND puc.ProductVariantId=mp.ProductVariantId
GROUP BY rc.Code;
IF EXISTS(SELECT 1 FROM #ReceiptBarcodeOption WHERE ConversionCount>1)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: receipt code maps to multiple barcode conversions.',1;

CREATE TABLE #ReceiptBaseOption
(
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS PRIMARY KEY,
    BaseCount int NOT NULL,ProductUnitConversionId int NULL,UnitId int NULL,Factor decimal(18,4) NULL
);
INSERT INTO #ReceiptBaseOption
SELECT rc.Code,COUNT_BIG(*),
       CASE WHEN COUNT_BIG(*)=1 THEN MIN(puc.Id) END,
       CASE WHEN COUNT_BIG(*)=1 THEN MIN(puc.UnitId) END,
       CASE WHEN COUNT_BIG(*)=1 THEN MIN(puc.Factor) END
FROM #ReceiptCode rc
JOIN #Map mp ON mp.Code=rc.Code AND mp.CandidateVariantCount=1
JOIN dbo.ProductUnitConversion puc ON puc.StoreId=1 AND puc.IsDeleted=0
 AND puc.ProductVariantId=mp.ProductVariantId AND puc.IsBaseUnit=1
GROUP BY rc.Code;

CREATE TABLE #ReceiptMap
(
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS PRIMARY KEY,
    ProductVariantId int NOT NULL,ProductUnitConversionId int NOT NULL,UnitId int NOT NULL,
    Factor decimal(18,4) NOT NULL,UnitName nvarchar(100) NULL,Sku nvarchar(100) NOT NULL,
    MappingPath nvarchar(20) NOT NULL
);
INSERT INTO #ReceiptMap
SELECT rc.Code,mp.ProductVariantId,
       CASE WHEN ISNULL(bo.ConversionCount,0)=1 THEN bo.ProductUnitConversionId ELSE ba.ProductUnitConversionId END,
       CASE WHEN ISNULL(bo.ConversionCount,0)=1 THEN bo.UnitId ELSE ba.UnitId END,
       CASE WHEN ISNULL(bo.ConversionCount,0)=1 THEN bo.Factor ELSE ba.Factor END,
       LEFT(u.Name,100),LEFT(COALESCE(pv.Sku,rc.Code),100),
       CASE WHEN ISNULL(bo.ConversionCount,0)=1 THEN N'BARCODE' ELSE N'BASE' END
FROM #ReceiptCode rc
JOIN #Map mp ON mp.Code=rc.Code AND mp.CandidateVariantCount=1
LEFT JOIN #ReceiptBarcodeOption bo ON bo.Code=rc.Code
LEFT JOIN #ReceiptBaseOption ba ON ba.Code=rc.Code
LEFT JOIN dbo.ProductVariant pv ON pv.Id=mp.ProductVariantId
LEFT JOIN dbo.Unit u ON u.Id=CASE WHEN ISNULL(bo.ConversionCount,0)=1 THEN bo.UnitId ELSE ba.UnitId END
WHERE ISNULL(bo.ConversionCount,0)=1 OR ISNULL(ba.BaseCount,0)=1;

IF (SELECT COUNT_BIG(*) FROM #ReceiptMap)<>(SELECT COUNT_BIG(*) FROM #ReceiptCode)
 OR EXISTS(SELECT 1 FROM #ReceiptMap WHERE ProductUnitConversionId IS NULL OR UnitId IS NULL OR Factor<=0)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: receipt conversion mapping incomplete.',1;

CREATE TABLE #ReceiptEligible
(
    LegacyMaHD nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    LegacyStatus int NOT NULL,DocumentDate datetime2(7) NOT NULL,ProcessedDate datetime2(7) NULL,
    DocumentTitle nvarchar(255) NULL,HeaderSourceNote nvarchar(1000) NULL,
    LegacyInputUserId nvarchar(100) NULL,LegacyInputUserName nvarchar(250) NULL,
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    SourceName nvarchar(500) NULL,SourceQty decimal(38,6) NULL,SourcePrice decimal(38,6) NULL,
    SourceLineNote nvarchar(500) NULL,ProductVariantId int NOT NULL,
    ProductUnitConversionId int NOT NULL,UnitId int NOT NULL,UnitName nvarchar(100) NULL,
    Factor decimal(18,4) NOT NULL,Sku nvarchar(100) NOT NULL,MappingPath nvarchar(20) NOT NULL
);
INSERT INTO #ReceiptEligible
SELECT rs.*,rm.ProductVariantId,rm.ProductUnitConversionId,rm.UnitId,rm.UnitName,rm.Factor,rm.Sku,rm.MappingPath
FROM #ReceiptSource rs JOIN #ReceiptMap rm ON rm.Code=rs.Code;

IF EXISTS(SELECT 1 FROM #ReceiptEligible WHERE COALESCE(SourceQty,0)*Factor<>ROUND(COALESCE(SourceQty,0)*Factor,3))
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: receipt BaseQuantity requires silent rounding.',1;

CREATE TABLE #ReceiptHeader
(
    StockDocumentId int PRIMARY KEY,LegacyMaHD nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS UNIQUE,
    DocumentNo nvarchar(50) NOT NULL,DocumentTitle nvarchar(255) NULL,TargetStatus int NOT NULL,
    DocumentDate datetime2(7) NOT NULL,ProcessedDate datetime2(7) NULL,
    SubtotalBeforeVat decimal(18,2) NOT NULL,TotalAmount decimal(18,2) NOT NULL,Note nvarchar(1000) NULL,
    SubmittedAtUtc datetime2(7) NULL,ApprovedAtUtc datetime2(7) NULL,ConfirmedAtUtc datetime2(7) NULL,
    ConfirmedLegalEntityId int NULL
);
;WITH H AS
(
    SELECT LegacyMaHD,MIN(LegacyStatus) LegacyStatus,MIN(DocumentDate) DocumentDate,
           MIN(ProcessedDate) ProcessedDate,MIN(DocumentTitle) DocumentTitle,
           MIN(HeaderSourceNote) HeaderSourceNote,MIN(LegacyInputUserId) LegacyInputUserId,
           MIN(LegacyInputUserName) LegacyInputUserName
    FROM #ReceiptEligible GROUP BY LegacyMaHD
),N AS
(
    SELECT *,ROW_NUMBER() OVER(ORDER BY LegacyMaHD) rn FROM H
)
INSERT INTO #ReceiptHeader
SELECT CONVERT(int,@StockDocumentIdBase+rn),LegacyMaHD,LEFT(CONCAT(N'LEGACY-HDN-',LegacyMaHD),50),
       LEFT(COALESCE(NULLIF(DocumentTitle,N''),CONCAT(N'Legacy Hóa đơn nhập ',LegacyMaHD)),255),
       CASE LegacyStatus WHEN 0 THEN 2 WHEN 1 THEN 3 END,DocumentDate,ProcessedDate,
       CONVERT(decimal(18,2),0),CONVERT(decimal(18,2),0),
       LEFT(CONCAT(N'LEGACY_HDN|MaHD=',LegacyMaHD,N'|LegacyStatus=',LegacyStatus,
                   N'|LegacyInputUserId=',COALESCE(LegacyInputUserId,N'NULL'),
                   N'|LegacyInputUserName=',COALESCE(LegacyInputUserName,N'NULL'),N'|PaymentState=UNKNOWN',
                   CASE WHEN HeaderSourceNote IS NULL THEN N'' ELSE CONCAT(N'|SourceNote=',HeaderSourceNote) END),1000),
       DocumentDate,CASE WHEN LegacyStatus=1 THEN ProcessedDate END,
       CASE WHEN LegacyStatus=1 THEN ProcessedDate END,CASE WHEN LegacyStatus=1 THEN 1 END
FROM N;

CREATE TABLE #ReceiptLine
(
    StockDocumentLineId int PRIMARY KEY,StockDocumentId int NOT NULL,ReceiptLineNo int NOT NULL,
    LegacyMaHD nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    ProductVariantId int NOT NULL,ProductUnitConversionId int NOT NULL,UnitId int NOT NULL,
    UnitName nvarchar(100) NULL,Factor decimal(18,4) NOT NULL,Quantity decimal(18,3) NOT NULL,
    BaseQuantity decimal(18,3) NOT NULL,UnitCost decimal(18,2) NOT NULL,LineTotal decimal(18,2) NOT NULL,
    ProductNameSnapshot nvarchar(250) NOT NULL,SkuSnapshot nvarchar(100) NULL,BarcodeSnapshot nvarchar(100) NULL,
    Note nvarchar(500) NULL
);
;WITH L AS
(
    SELECT re.*,h.StockDocumentId,
           ROW_NUMBER() OVER(PARTITION BY re.LegacyMaHD ORDER BY re.Code) ReceiptLineNo,
           ROW_NUMBER() OVER(ORDER BY re.LegacyMaHD,re.Code) GlobalRn
    FROM #ReceiptEligible re JOIN #ReceiptHeader h ON h.LegacyMaHD=re.LegacyMaHD
)
INSERT INTO #ReceiptLine
SELECT CONVERT(int,@StockDocumentLineIdBase+GlobalRn),StockDocumentId,CONVERT(int,ReceiptLineNo),LegacyMaHD,Code,
       ProductVariantId,ProductUnitConversionId,UnitId,UnitName,Factor,
       CONVERT(decimal(18,3),ROUND(COALESCE(SourceQty,0),3)),
       CONVERT(decimal(18,3),ROUND(COALESCE(SourceQty,0)*Factor,3)),
       CONVERT(decimal(18,2),ROUND(COALESCE(SourcePrice,0),2)),
       CONVERT(decimal(18,2),ROUND(COALESCE(SourceQty,0)*COALESCE(SourcePrice,0),2)),
       LEFT(COALESCE(NULLIF(SourceName,N''),CONCAT(N'[Legacy Receipt] ',Code)),250),
       LEFT(Sku,100),CASE WHEN MappingPath=N'BARCODE' THEN LEFT(Code,100) END,
       LEFT(CONCAT(N'LEGACY_RECEIPT|Code=',Code,N'|Map=',MappingPath,
                   N'|SourceQtyNull=',CASE WHEN SourceQty IS NULL THEN 1 ELSE 0 END,
                   N'|SourceCostMissing=',CASE WHEN SourcePrice IS NULL THEN 1 ELSE 0 END,
                   CASE WHEN SourceLineNote IS NULL THEN N'' ELSE CONCAT(N'|SourceNote=',SourceLineNote) END),500)
FROM L;

UPDATE h SET SubtotalBeforeVat=x.TotalAmount,TotalAmount=x.TotalAmount
FROM #ReceiptHeader h
JOIN(SELECT StockDocumentId,CONVERT(decimal(18,2),SUM(LineTotal)) TotalAmount FROM #ReceiptLine GROUP BY StockDocumentId)x
 ON x.StockDocumentId=h.StockDocumentId;

IF EXISTS(SELECT StockDocumentId,ReceiptLineNo FROM #ReceiptLine GROUP BY StockDocumentId,ReceiptLineNo HAVING COUNT_BIG(*)>1)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: receipt LineNo uniqueness failed.',1;

-------------------------------------------------------------------------------
-- D. STAGE HISTORICAL INVENTORY EVENTS
-------------------------------------------------------------------------------
-- Guard độc lập với TEST expected-count: source thật sau này cũng không được silently
-- bỏ movement chỉ vì thiếu ngày nguồn.
IF EXISTS
(
    SELECT 1
    FROM [__SOURCE__].dbo.Product p
    JOIN #Master m
      ON m.Code=CONVERT(nvarchar(200),p.Code) COLLATE SQL_Latin1_General_CP1_CI_AS
    JOIN #Map mp
      ON mp.Code=m.Code AND mp.CandidateVariantCount=1
    LEFT JOIN @Combo89 c ON c.Code=m.Code
    LEFT JOIN #DiscountCode dc ON dc.Code=m.Code
    WHERE c.Code IS NULL
      AND dc.Code IS NULL
      AND COALESCE(p.Warranty,p.Quantity,0)<>0
      AND p.CreatedDate IS NULL
)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: eligible Product movement thiếu Product.CreatedDate.',1;

IF EXISTS
(
    SELECT 1
    FROM [__SOURCE__].dbo.OrderDetail od
    JOIN [__SOURCE__].dbo.[Order] o ON o.ID=od.OrderID
    JOIN #Master m
      ON m.Code=CONVERT(nvarchar(200),od.ProductCode) COLLATE SQL_Latin1_General_CP1_CI_AS
    JOIN #Map mp
      ON mp.Code=m.Code AND mp.CandidateVariantCount=1
    LEFT JOIN @ApprovedOrphan5 orp ON orp.Id=od.ID
    LEFT JOIN @ApprovedNegativeHeader9 nh ON nh.OrderId=o.ID
    LEFT JOIN @Combo89 c ON c.Code=m.Code
    LEFT JOIN #DiscountCode dc ON dc.Code=m.Code
    WHERE od.OrderCategoryID=1
      AND o.OrderCategoryID=1
      AND orp.Id IS NULL
      AND NOT(COALESCE(od.Price,0)<0 OR COALESCE(od.Total,0)<0)
      AND (o.Total IS NULL OR o.Total>=0 OR nh.OrderId IS NOT NULL)
      AND c.Code IS NULL
      AND dc.Code IS NULL
      AND COALESCE(od.Quantity,0)<>0
      AND o.CreatedDate IS NULL
)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: eligible sale movement thiếu Order.CreatedDate.',1;

IF (SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.OrderDetail od LEFT JOIN [__SOURCE__].dbo.[Order] o ON o.ID=od.OrderID
    WHERE od.OrderCategoryID=1 AND o.ID IS NULL AND od.ID NOT IN(606381,606382,606383,606384,606385))<>0
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: unexpected Cat1 orphan OrderDetail.',1;

IF EXISTS
(
    SELECT 1 FROM [__SOURCE__].dbo.[Order] o
    JOIN [__SOURCE__].dbo.OrderDetail od ON od.OrderID=o.ID
    LEFT JOIN @ApprovedNegativeHeader9 a ON a.OrderId=o.ID
    WHERE o.OrderCategoryID=1 AND o.Total<0 AND od.OrderCategoryID=1
      AND NOT(COALESCE(od.Price,0)<0 OR COALESCE(od.Total,0)<0)
      AND a.OrderId IS NULL
)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: unapproved negative-total merchandise header found.',1;

CREATE TABLE #EventRaw
(
    EventFamily tinyint NOT NULL,EventKind tinyint NOT NULL,SourceId bigint NOT NULL,SourceOrderId bigint NULL,
    IsNegativeHeaderException bit NOT NULL,Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    ProductVariantId int NOT NULL,OccurredWallClock datetime2(7) NOT NULL,QuantityChange decimal(38,4) NOT NULL,
    MasterUnitCost decimal(38,6) NOT NULL,PRIMARY KEY(EventKind,SourceId)
);
INSERT INTO #EventRaw
SELECT 1,CASE WHEN q.Qty>0 THEN 1 ELSE 2 END,CONVERT(bigint,p.ID),NULL,0,m.Code,mp.ProductVariantId,
       CONVERT(datetime2(7),p.CreatedDate),q.Qty,m.MasterCost
FROM [__SOURCE__].dbo.Product p
JOIN #Master m ON m.Code=CONVERT(nvarchar(200),p.Code) COLLATE SQL_Latin1_General_CP1_CI_AS
JOIN #Map mp ON mp.Code=m.Code AND mp.CandidateVariantCount=1
LEFT JOIN @Combo89 c ON c.Code=m.Code
LEFT JOIN #DiscountCode dc ON dc.Code=m.Code
CROSS APPLY(SELECT CONVERT(decimal(38,4),COALESCE(p.Warranty,p.Quantity,0)) Qty)q
WHERE c.Code IS NULL AND dc.Code IS NULL AND q.Qty<>0 AND p.CreatedDate IS NOT NULL;

INSERT INTO #EventRaw
SELECT 2,3,CONVERT(bigint,od.ID),CONVERT(bigint,o.ID),CONVERT(bit,CASE WHEN nh.OrderId IS NULL THEN 0 ELSE 1 END),
       m.Code,mp.ProductVariantId,CONVERT(datetime2(7),o.CreatedDate),
       -CONVERT(decimal(38,4),COALESCE(od.Quantity,0)),m.MasterCost
FROM [__SOURCE__].dbo.OrderDetail od
JOIN [__SOURCE__].dbo.[Order] o ON o.ID=od.OrderID
JOIN #Master m ON m.Code=CONVERT(nvarchar(200),od.ProductCode) COLLATE SQL_Latin1_General_CP1_CI_AS
JOIN #Map mp ON mp.Code=m.Code AND mp.CandidateVariantCount=1
LEFT JOIN @ApprovedOrphan5 orp ON orp.Id=od.ID
LEFT JOIN @ApprovedNegativeHeader9 nh ON nh.OrderId=o.ID
LEFT JOIN @Combo89 c ON c.Code=m.Code
LEFT JOIN #DiscountCode dc ON dc.Code=m.Code
WHERE od.OrderCategoryID=1 AND o.OrderCategoryID=1 AND orp.Id IS NULL
  AND NOT(COALESCE(od.Price,0)<0 OR COALESCE(od.Total,0)<0)
  AND (o.Total IS NULL OR o.Total>=0 OR nh.OrderId IS NOT NULL)
  AND c.Code IS NULL AND dc.Code IS NULL AND COALESCE(od.Quantity,0)<>0 AND o.CreatedDate IS NOT NULL;

IF EXISTS
(
    SELECT ProductVariantId,OccurredWallClock
    FROM #EventRaw GROUP BY ProductVariantId,OccurredWallClock
    HAVING COUNT(DISTINCT EventFamily)>1
)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: cross Product/Sale exact timestamp collision found.',1;

-- Normal imported sale must still resolve target Order/OrderLine; 13 approved negative-header rows are inventory-only exceptions.
IF EXISTS
(
    SELECT 1 FROM #EventRaw e
    LEFT JOIN dbo.Orders o ON o.Id=e.SourceOrderId AND o.StoreId=1 AND o.IsDeleted=0
    LEFT JOIN dbo.OrderLines l ON l.Id=e.SourceId AND l.StoreId=1 AND l.IsDeleted=0
    WHERE e.EventKind=3 AND e.IsNegativeHeaderException=0
      AND (o.Id IS NULL OR l.Id IS NULL OR l.OrderId<>e.SourceOrderId OR l.VariantId<>e.ProductVariantId
           OR CONVERT(decimal(38,4),l.BaseQuantity)<>ABS(e.QuantityChange))
)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: normal sale target Order/OrderLine mapping mismatch.',1;

DECLARE @ExpectedNegativeHeaderLines bigint=(SELECT COUNT_BIG(*) FROM #EventRaw WHERE EventKind=3 AND IsNegativeHeaderException=1);

CREATE TABLE #Event
(
    TransactionId int PRIMARY KEY,EventSeq bigint NOT NULL,EventFamily tinyint NOT NULL,EventKind tinyint NOT NULL,
    SourceId bigint NOT NULL,SourceOrderId bigint NULL,IsNegativeHeaderException bit NOT NULL,
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,ProductVariantId int NOT NULL,
    OccurredWallClock datetime2(7) NOT NULL,QuantityChange decimal(38,4) NOT NULL,MasterUnitCost decimal(38,6) NOT NULL,
    BeforeQty decimal(38,4) NOT NULL,AfterQty decimal(38,4) NOT NULL,
    TransactionType int NOT NULL,ReferenceType int NOT NULL,ReferenceId nvarchar(64) NOT NULL,
    ReferenceLineId int NULL,ReferenceSubKey nvarchar(100) NULL
);
;WITH E AS
(
    SELECT r.*,
           ROW_NUMBER() OVER(ORDER BY r.ProductVariantId,r.OccurredWallClock,r.EventKind,r.SourceId) GlobalRn,
           ROW_NUMBER() OVER(PARTITION BY r.ProductVariantId ORDER BY r.OccurredWallClock,r.EventKind,r.SourceId) EventSeq,
           CONVERT(decimal(38,4),COALESCE(SUM(r.QuantityChange) OVER(PARTITION BY r.ProductVariantId ORDER BY r.OccurredWallClock,r.EventKind,r.SourceId ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0)) BeforeQty,
           CONVERT(decimal(38,4),SUM(r.QuantityChange) OVER(PARTITION BY r.ProductVariantId ORDER BY r.OccurredWallClock,r.EventKind,r.SourceId ROWS UNBOUNDED PRECEDING)) AfterQty
    FROM #EventRaw r
)
INSERT INTO #Event
SELECT CONVERT(int,@InventoryTransactionIdBase+GlobalRn),EventSeq,EventFamily,EventKind,SourceId,SourceOrderId,IsNegativeHeaderException,
       Code,ProductVariantId,OccurredWallClock,QuantityChange,MasterUnitCost,BeforeQty,AfterQty,
       CASE EventKind WHEN 1 THEN 1 WHEN 2 THEN 31 ELSE 20 END,
       CASE WHEN EventKind=3 AND IsNegativeHeaderException=0 THEN 1 ELSE 0 END,
       CASE WHEN EventKind IN(1,2) THEN N'LEGACY-PRODUCT'
            WHEN EventKind=3 AND IsNegativeHeaderException=1 THEN N'LEGACY-SALE-EXC'
            ELSE CONVERT(nvarchar(64),SourceOrderId) END,
       CONVERT(int,SourceId),
       CASE EventKind WHEN 1 THEN N'LEGACY-POS' WHEN 2 THEN N'LEGACY-NEG' ELSE N'LEGACY-SALE' END
FROM E;
CREATE INDEX IX_Event_VariantSeq ON #Event(ProductVariantId,EventSeq);

DECLARE @SupplyCountForIds bigint=(SELECT COUNT_BIG(*) FROM #Event WHERE QuantityChange>0);
DECLARE @DemandCountForIds bigint=(SELECT COUNT_BIG(*) FROM #Event WHERE QuantityChange<0);

-------------------------------------------------------------------------------
-- E. FIFO SUPPLY / DEMAND / MATCH STAGING
-------------------------------------------------------------------------------
CREATE TABLE #Supply
(
    ProductVariantId int NOT NULL,EventSeq bigint NOT NULL,TransactionId int NOT NULL,SourceId bigint NOT NULL,
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,OccurredWallClock datetime2(7) NOT NULL,
    Quantity decimal(38,4) NOT NULL,UnitCost decimal(38,6) NOT NULL,
    SupplyStart decimal(38,4) NOT NULL,SupplyEnd decimal(38,4) NOT NULL,
    InboundValuationId int NOT NULL,LayerId int NOT NULL,PRIMARY KEY(ProductVariantId,EventSeq)
);
;WITH S AS
(
    SELECT e.*,ROW_NUMBER() OVER(ORDER BY e.ProductVariantId,e.EventSeq) SupplyRn,
           CONVERT(decimal(38,4),COALESCE(SUM(e.QuantityChange) OVER(PARTITION BY e.ProductVariantId ORDER BY e.EventSeq ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0)) SupplyStart,
           CONVERT(decimal(38,4),SUM(e.QuantityChange) OVER(PARTITION BY e.ProductVariantId ORDER BY e.EventSeq ROWS UNBOUNDED PRECEDING)) SupplyEnd
    FROM #Event e WHERE e.QuantityChange>0
)
INSERT INTO #Supply
SELECT ProductVariantId,EventSeq,TransactionId,SourceId,Code,OccurredWallClock,QuantityChange,MasterUnitCost,
       SupplyStart,SupplyEnd,CONVERT(int,@InventoryValuationEntryIdBase+SupplyRn),CONVERT(int,@InventoryCostLayerIdBase+SupplyRn)
FROM S;
CREATE INDEX IX_Supply_Range ON #Supply(ProductVariantId,SupplyStart,SupplyEnd) INCLUDE(EventSeq,LayerId,UnitCost,Quantity);

CREATE TABLE #Demand
(
    ProductVariantId int NOT NULL,EventSeq bigint NOT NULL,TransactionId int NOT NULL,EventKind tinyint NOT NULL,
    SourceId bigint NOT NULL,SourceOrderId bigint NULL,IsNegativeHeaderException bit NOT NULL,
    Code nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,OccurredWallClock datetime2(7) NOT NULL,
    Quantity decimal(38,4) NOT NULL,ProvisionalUnitCost decimal(38,6) NOT NULL,BeforeQty decimal(38,4) NOT NULL,
    ActualQtyAtEvent decimal(38,4) NOT NULL,ProvisionalQtyAtEvent decimal(38,4) NOT NULL,
    DemandStart decimal(38,4) NOT NULL,DemandEnd decimal(38,4) NOT NULL,
    DemandRn bigint NOT NULL,ProvisionalValuationId int NOT NULL,ProvisionalAllocationId int NOT NULL,
    ReferenceType int NOT NULL,ReferenceId nvarchar(64) NOT NULL,ReferenceLineId int NULL,BaseReferenceSubKey nvarchar(100) NULL,
    PRIMARY KEY(ProductVariantId,EventSeq)
);
;WITH D AS
(
    SELECT e.*,ROW_NUMBER() OVER(ORDER BY e.ProductVariantId,e.EventSeq) DemandRn,
           CONVERT(decimal(38,4),COALESCE(SUM(ABS(e.QuantityChange)) OVER(PARTITION BY e.ProductVariantId ORDER BY e.EventSeq ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0)) DemandStart,
           CONVERT(decimal(38,4),SUM(ABS(e.QuantityChange)) OVER(PARTITION BY e.ProductVariantId ORDER BY e.EventSeq ROWS UNBOUNDED PRECEDING)) DemandEnd
    FROM #Event e WHERE e.QuantityChange<0
)
INSERT INTO #Demand
SELECT ProductVariantId,EventSeq,TransactionId,EventKind,SourceId,SourceOrderId,IsNegativeHeaderException,Code,OccurredWallClock,
       ABS(QuantityChange),MasterUnitCost,BeforeQty,
       CONVERT(decimal(38,4),CASE WHEN BeforeQty<=0 THEN 0 WHEN BeforeQty>=ABS(QuantityChange) THEN ABS(QuantityChange) ELSE BeforeQty END),
       CONVERT(decimal(38,4),ABS(QuantityChange)-CASE WHEN BeforeQty<=0 THEN 0 WHEN BeforeQty>=ABS(QuantityChange) THEN ABS(QuantityChange) ELSE BeforeQty END),
       DemandStart,DemandEnd,DemandRn,CONVERT(int,@InventoryValuationEntryIdBase+@SupplyCountForIds+DemandRn),CONVERT(int,@InventoryCostLayerAllocationIdBase+DemandRn),
       ReferenceType,ReferenceId,ReferenceLineId,ReferenceSubKey
FROM D;
CREATE INDEX IX_Demand_Range ON #Demand(ProductVariantId,DemandStart,DemandEnd) INCLUDE(EventSeq,DemandRn,Quantity,ProvisionalQtyAtEvent);

CREATE TABLE #Match
(
    MatchId bigint PRIMARY KEY,ProductVariantId int NOT NULL,SupplyEventSeq bigint NOT NULL,SupplySourceId bigint NOT NULL,
    SupplyLayerId int NOT NULL,SupplyOccurred datetime2(7) NOT NULL,DemandEventSeq bigint NOT NULL,DemandEventKind tinyint NOT NULL,
    DemandSourceId bigint NOT NULL,MatchedQty decimal(38,4) NOT NULL,SupplyUnitCost decimal(38,6) NOT NULL,
    ProvisionalUnitCost decimal(38,6) NOT NULL,MatchClass tinyint NOT NULL
);
;WITH M AS
(
    SELECT s.ProductVariantId,s.EventSeq SupplyEventSeq,s.SourceId SupplySourceId,s.LayerId SupplyLayerId,
           s.OccurredWallClock SupplyOccurred,d.EventSeq DemandEventSeq,d.EventKind DemandEventKind,d.SourceId DemandSourceId,
           CONVERT(decimal(38,4),(CASE WHEN s.SupplyEnd<d.DemandEnd THEN s.SupplyEnd ELSE d.DemandEnd END)-(CASE WHEN s.SupplyStart>d.DemandStart THEN s.SupplyStart ELSE d.DemandStart END)) MatchedQty,
           s.UnitCost SupplyUnitCost,d.ProvisionalUnitCost,
           CASE WHEN s.EventSeq<d.EventSeq THEN 1 ELSE 2 END MatchClass
    FROM #Demand d JOIN #Supply s ON s.ProductVariantId=d.ProductVariantId
      AND s.SupplyStart<d.DemandEnd AND s.SupplyEnd>d.DemandStart
    WHERE (CASE WHEN s.SupplyEnd<d.DemandEnd THEN s.SupplyEnd ELSE d.DemandEnd END)>(CASE WHEN s.SupplyStart>d.DemandStart THEN s.SupplyStart ELSE d.DemandStart END)
)
INSERT INTO #Match
SELECT ROW_NUMBER() OVER(ORDER BY ProductVariantId,DemandEventSeq,SupplyEventSeq),* FROM M;
CREATE INDEX IX_Match_Demand ON #Match(ProductVariantId,DemandEventSeq,MatchClass);
CREATE INDEX IX_Match_Supply ON #Match(ProductVariantId,SupplyEventSeq,MatchClass);

DECLARE @MatchCountForIds bigint=(SELECT COUNT_BIG(*) FROM #Match);

CREATE TABLE #DemandFinal
(
    ProductVariantId int NOT NULL,EventSeq bigint NOT NULL,ActualQtyAtEvent decimal(38,4) NOT NULL,
    ProvisionalQtyAtEvent decimal(38,4) NOT NULL,ActualMatchedQty decimal(38,4) NOT NULL,
    LaterResolvedQty decimal(38,4) NOT NULL,UnresolvedProvisionalQty decimal(38,4) NOT NULL,
    PRIMARY KEY(ProductVariantId,EventSeq)
);
INSERT INTO #DemandFinal
SELECT d.ProductVariantId,d.EventSeq,d.ActualQtyAtEvent,d.ProvisionalQtyAtEvent,
       CONVERT(decimal(38,4),COALESCE(SUM(CASE WHEN m.MatchClass=1 THEN m.MatchedQty ELSE 0 END),0)),
       CONVERT(decimal(38,4),COALESCE(SUM(CASE WHEN m.MatchClass=2 THEN m.MatchedQty ELSE 0 END),0)),
       CONVERT(decimal(38,4),d.ProvisionalQtyAtEvent-COALESCE(SUM(CASE WHEN m.MatchClass=2 THEN m.MatchedQty ELSE 0 END),0))
FROM #Demand d LEFT JOIN #Match m ON m.ProductVariantId=d.ProductVariantId AND m.DemandEventSeq=d.EventSeq
GROUP BY d.ProductVariantId,d.EventSeq,d.ActualQtyAtEvent,d.ProvisionalQtyAtEvent;

IF EXISTS(SELECT 1 FROM #DemandFinal WHERE ActualMatchedQty<>ActualQtyAtEvent OR LaterResolvedQty+UnresolvedProvisionalQty<>ProvisionalQtyAtEvent OR UnresolvedProvisionalQty<0)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: FIFO quantity invariant failed.',1;

CREATE TABLE #SupplyFinal
(
    ProductVariantId int NOT NULL,EventSeq bigint NOT NULL,ActualConsumedQty decimal(38,4) NOT NULL,
    ResolvedProvisionalQty decimal(38,4) NOT NULL,RemainingQty decimal(38,4) NOT NULL,
    RemainingOpenProvisionalQty decimal(38,4) NOT NULL,PRIMARY KEY(ProductVariantId,EventSeq)
);
INSERT INTO #SupplyFinal
SELECT s.ProductVariantId,s.EventSeq,
       CONVERT(decimal(38,4),COALESCE(SUM(CASE WHEN m.MatchClass=1 THEN m.MatchedQty ELSE 0 END),0)),
       CONVERT(decimal(38,4),COALESCE(SUM(CASE WHEN m.MatchClass=2 THEN m.MatchedQty ELSE 0 END),0)),
       CONVERT(decimal(38,4),s.Quantity-COALESCE(SUM(m.MatchedQty),0)),
       CONVERT(decimal(38,4),s.Quantity-COALESCE(SUM(CASE WHEN m.MatchClass=2 THEN m.MatchedQty ELSE 0 END),0))
FROM #Supply s LEFT JOIN #Match m ON m.ProductVariantId=s.ProductVariantId AND m.SupplyEventSeq=s.EventSeq
GROUP BY s.ProductVariantId,s.EventSeq,s.Quantity;
IF EXISTS(SELECT 1 FROM #SupplyFinal WHERE RemainingQty<0)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: negative FIFO RemainingQty.',1;

-------------------------------------------------------------------------------
-- F. VALUATION / TRANSACTION / LAYER / ALLOCATION / BALANCE STAGING
-------------------------------------------------------------------------------
CREATE TABLE #ValuationStage
(
    ValuationId int PRIMARY KEY,ProductVariantId int NOT NULL,EventSeq bigint NOT NULL,ValuationOrder bigint NOT NULL,
    InventoryTransactionId int NOT NULL,EntryType int NOT NULL,ReferenceType int NOT NULL,ReferenceId nvarchar(64) NOT NULL,
    ReferenceLineId int NULL,ReferenceSubKey nvarchar(100) NULL,Quantity decimal(38,4) NOT NULL,UnitCost decimal(38,6) NOT NULL,
    Amount decimal(38,4) NOT NULL,CostSourceType int NOT NULL,IsProvisional bit NOT NULL,CostFinalizedAtUtc datetime2(7) NULL,
    RevaluationOfEntryId int NULL,SourceValuationEntryId int NULL,SourceReferenceSubKey nvarchar(100) NULL,
    InventoryCostLayerId int NULL,Note nvarchar(1000) NULL,OccurredAtUtc datetime2(7) NOT NULL
);

-- Positive Product event => Opening valuation + FIFO layer.
INSERT INTO #ValuationStage
SELECT s.InboundValuationId,e.ProductVariantId,e.EventSeq,1,e.TransactionId,4,e.ReferenceType,e.ReferenceId,e.ReferenceLineId,
       N'LEGACY-OPENING',e.QuantityChange,e.MasterUnitCost,
       CONVERT(decimal(38,4),ROUND(e.QuantityChange*e.MasterUnitCost,4)),4,0,e.OccurredWallClock,
       NULL,NULL,NULL,s.LayerId,LEFT(CONCAT(N'LEGACY_PRODUCT_IN|Code=',e.Code,N'|ProductId=',e.SourceId),1000),e.OccurredWallClock
FROM #Event e JOIN #Supply s ON s.ProductVariantId=e.ProductVariantId AND s.EventSeq=e.EventSeq;

-- Actual outbound valuation fragments by FIFO layer.
;WITH A AS
(
    SELECT m.*,ROW_NUMBER() OVER(PARTITION BY m.ProductVariantId,m.DemandEventSeq ORDER BY m.SupplyEventSeq,m.MatchId) PartNo
    FROM #Match m WHERE m.MatchClass=1
)
INSERT INTO #ValuationStage
SELECT CONVERT(int,@InventoryValuationEntryIdBase+@SupplyCountForIds+@DemandCountForIds+MatchId),d.ProductVariantId,d.EventSeq,CONVERT(bigint,PartNo),d.TransactionId,2,
       d.ReferenceType,d.ReferenceId,d.ReferenceLineId,
       LEFT(CONCAT(d.BaseReferenceSubKey,N':FIFO:P',PartNo,N':S',SupplySourceId),100),
       -MatchedQty,SupplyUnitCost,CONVERT(decimal(38,4),-ROUND(MatchedQty*SupplyUnitCost,4)),4,0,d.OccurredWallClock,
       NULL,NULL,NULL,SupplyLayerId,
       LEFT(CONCAT(N'LEGACY_FIFO_OUT|Code=',d.Code,N'|Demand=',d.SourceId,N'|SupplyProduct=',SupplySourceId),1000),d.OccurredWallClock
FROM A JOIN #Demand d ON d.ProductVariantId=A.ProductVariantId AND d.EventSeq=A.DemandEventSeq;

-- Provisional outbound valuation: one row per outbound event that crosses below zero.
INSERT INTO #ValuationStage
SELECT d.ProvisionalValuationId,d.ProductVariantId,d.EventSeq,1000000,d.TransactionId,2,d.ReferenceType,d.ReferenceId,d.ReferenceLineId,
       LEFT(CONCAT(d.BaseReferenceSubKey,N':PROVISIONAL'),100),-d.ProvisionalQtyAtEvent,d.ProvisionalUnitCost,
       CONVERT(decimal(38,4),-ROUND(d.ProvisionalQtyAtEvent*d.ProvisionalUnitCost,4)),4,1,NULL,
       NULL,NULL,NULL,NULL,LEFT(CONCAT(N'LEGACY_PROVISIONAL_OUT|Code=',d.Code,N'|Demand=',d.SourceId),1000),d.OccurredWallClock
FROM #Demand d WHERE d.ProvisionalQtyAtEvent>0;

-- Revaluation when later supply cost differs from provisional cost.
-- Amount follows the Simulation-B economic rule: provisional cost - actual supply cost.
;WITH R AS
(
    SELECT m.*,ROW_NUMBER() OVER(PARTITION BY m.ProductVariantId,m.SupplyEventSeq ORDER BY m.DemandEventSeq,m.MatchId) ResolvePartNo
    FROM #Match m
    WHERE m.MatchClass=2 AND ROUND(m.MatchedQty*(m.ProvisionalUnitCost-m.SupplyUnitCost),4)<>0
)
INSERT INTO #ValuationStage
SELECT CONVERT(int,@InventoryValuationEntryIdBase+@SupplyCountForIds+@DemandCountForIds+@MatchCountForIds+MatchId),s.ProductVariantId,s.EventSeq,CONVERT(bigint,1000+ResolvePartNo),s.TransactionId,3,
       d.ReferenceType,d.ReferenceId,d.ReferenceLineId,LEFT(CONCAT(d.BaseReferenceSubKey,N':PROVISIONAL'),100),
       CONVERT(decimal(38,4),0),s.UnitCost,
       CONVERT(decimal(38,4),ROUND(R.MatchedQty*(R.ProvisionalUnitCost-R.SupplyUnitCost),4)),4,0,s.OccurredWallClock,
       d.ProvisionalValuationId,d.ProvisionalValuationId,LEFT(CONCAT(d.BaseReferenceSubKey,N':PROVISIONAL'),100),s.LayerId,
       LEFT(CONCAT(N'LEGACY_REVALUE|Demand=',d.SourceId,N'|SupplyProduct=',s.SourceId,N'|Qty=',R.MatchedQty),1000),s.OccurredWallClock
FROM R
JOIN #Supply s ON s.ProductVariantId=R.ProductVariantId AND s.EventSeq=R.SupplyEventSeq
JOIN #Demand d ON d.ProductVariantId=R.ProductVariantId AND d.EventSeq=R.DemandEventSeq;

CREATE TABLE #ValuationFinal
(
    ValuationId int PRIMARY KEY,ProductVariantId int NOT NULL,EventSeq bigint NOT NULL,ValuationOrder bigint NOT NULL,
    InventoryTransactionId int NOT NULL,EntryType int NOT NULL,ReferenceType int NOT NULL,ReferenceId nvarchar(64) NOT NULL,
    ReferenceLineId int NULL,ReferenceSubKey nvarchar(100) NULL,Quantity decimal(38,4) NOT NULL,UnitCost decimal(38,6) NOT NULL,
    Amount decimal(38,4) NOT NULL,RunningQtyAfter decimal(38,4) NOT NULL,RunningValueAfter decimal(38,4) NOT NULL,
    RunningAverageUnitCostAfter decimal(38,6) NOT NULL,CostSourceType int NOT NULL,IsProvisional bit NOT NULL,
    CostFinalizedAtUtc datetime2(7) NULL,RevaluationOfEntryId int NULL,SourceValuationEntryId int NULL,
    SourceReferenceSubKey nvarchar(100) NULL,InventoryCostLayerId int NULL,Note nvarchar(1000) NULL,OccurredAtUtc datetime2(7) NOT NULL
);
;WITH R AS
(
    SELECT v.*,
           CONVERT(decimal(38,4),SUM(v.Quantity) OVER(PARTITION BY v.ProductVariantId ORDER BY v.EventSeq,v.ValuationOrder,v.ValuationId ROWS UNBOUNDED PRECEDING)) RunQty,
           CONVERT(decimal(38,4),SUM(v.Amount) OVER(PARTITION BY v.ProductVariantId ORDER BY v.EventSeq,v.ValuationOrder,v.ValuationId ROWS UNBOUNDED PRECEDING)) RunValue
    FROM #ValuationStage v
)
INSERT INTO #ValuationFinal
SELECT ValuationId,ProductVariantId,EventSeq,ValuationOrder,InventoryTransactionId,EntryType,ReferenceType,ReferenceId,
       ReferenceLineId,ReferenceSubKey,Quantity,UnitCost,Amount,RunQty,RunValue,
       CONVERT(decimal(38,6),CASE WHEN RunQty=0 THEN 0 ELSE ROUND(RunValue/RunQty,4) END),
       CostSourceType,IsProvisional,CostFinalizedAtUtc,RevaluationOfEntryId,SourceValuationEntryId,
       SourceReferenceSubKey,InventoryCostLayerId,Note,OccurredAtUtc
FROM R;

-- Performance-critical index:
-- EV join và validation TOP(1) đều lookup theo InventoryTransactionId,
-- rồi lấy valuation cuối theo ValuationOrder/ValuationId.
CREATE INDEX IX_ValuationFinal_Transaction_Order
ON #ValuationFinal(InventoryTransactionId,ValuationOrder DESC,ValuationId DESC)
INCLUDE(RunningQtyAfter,RunningValueAfter,Amount,IsProvisional);

CREATE TABLE #TransactionStage
(
    TransactionId int PRIMARY KEY,ProductVariantId int NOT NULL,EventSeq bigint NOT NULL,TransactionType int NOT NULL,
    ReferenceType int NOT NULL,ReferenceId nvarchar(64) NOT NULL,ReferenceLineId int NULL,ReferenceSubKey nvarchar(100) NULL,
    QuantityChange decimal(38,4) NOT NULL,BeforeQty decimal(38,4) NOT NULL,AfterQty decimal(38,4) NOT NULL,
    UnitCostSnapshot decimal(38,6) NOT NULL,TotalCost decimal(38,4) NOT NULL,BeforeInventoryValue decimal(38,4) NOT NULL,
    AfterInventoryValue decimal(38,4) NOT NULL,RunningAverageUnitCostAfter decimal(38,6) NOT NULL,
    CostSourceType int NOT NULL,IsProvisionalCost bit NOT NULL,CostFinalizedAtUtc datetime2(7) NULL,
    OccurredAtUtc datetime2(7) NOT NULL,Note nvarchar(1000) NULL
);
;WITH EV AS
(
    SELECT e.ProductVariantId,e.EventSeq,SUM(v.Amount) EventValueChange,
           MAX(CASE WHEN v.IsProvisional=1 THEN 1 ELSE 0 END) HasProvisional
    FROM #Event e JOIN #ValuationFinal v ON v.InventoryTransactionId=e.TransactionId
    GROUP BY e.ProductVariantId,e.EventSeq
),R AS
(
    SELECT EV.*,
           CONVERT(decimal(38,4),COALESCE(SUM(EventValueChange) OVER(PARTITION BY ProductVariantId ORDER BY EventSeq ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0)) BeforeValue,
           CONVERT(decimal(38,4),SUM(EventValueChange) OVER(PARTITION BY ProductVariantId ORDER BY EventSeq ROWS UNBOUNDED PRECEDING)) AfterValue
    FROM EV
)
INSERT INTO #TransactionStage
SELECT e.TransactionId,e.ProductVariantId,e.EventSeq,e.TransactionType,e.ReferenceType,e.ReferenceId,e.ReferenceLineId,e.ReferenceSubKey,
       e.QuantityChange,e.BeforeQty,e.AfterQty,
       CONVERT(decimal(38,6),CASE WHEN e.EventKind=1 THEN e.MasterUnitCost
                                 WHEN e.QuantityChange=0 THEN 0 ELSE ROUND(ABS(r.EventValueChange/e.QuantityChange),4) END),
       CONVERT(decimal(38,4),r.EventValueChange),r.BeforeValue,r.AfterValue,
       CONVERT(decimal(38,6),CASE WHEN e.AfterQty=0 THEN 0 ELSE ROUND(r.AfterValue/e.AfterQty,4) END),
       4,CONVERT(bit,r.HasProvisional),CASE WHEN r.HasProvisional=1 THEN NULL ELSE e.OccurredWallClock END,e.OccurredWallClock,
       LEFT(CONCAT(CASE e.EventKind WHEN 1 THEN N'LEGACY_PRODUCT_IN' WHEN 2 THEN N'LEGACY_PRODUCT_ADJUSTMENT' ELSE N'LEGACY_SALE' END,
                   N'|Code=',e.Code,N'|SourceId=',e.SourceId,
                   CASE WHEN e.IsNegativeHeaderException=1 THEN N'|NEGATIVE_HEADER_EXCEPTION' ELSE N'' END),1000)
FROM #Event e JOIN R r ON r.ProductVariantId=e.ProductVariantId AND r.EventSeq=e.EventSeq;

-- Transaction and valuation running snapshots must agree at each event end.
IF EXISTS
(
    SELECT 1
    FROM #TransactionStage t
    OUTER APPLY
    (
        SELECT TOP(1) v.RunningQtyAfter,v.RunningValueAfter
        FROM #ValuationFinal v
        WHERE v.InventoryTransactionId=t.TransactionId
        ORDER BY v.ValuationOrder DESC,v.ValuationId DESC
    ) z
    WHERE z.RunningQtyAfter<>t.AfterQty OR z.RunningValueAfter<>t.AfterInventoryValue
)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: transaction/valuation running snapshot mismatch.',1;

CREATE TABLE #LayerStage
(
    LayerId int PRIMARY KEY,ProductVariantId int NOT NULL,TransactionId int NOT NULL,ValuationId int NOT NULL,
    ReferenceType int NOT NULL,ReferenceId nvarchar(64) NOT NULL,ReferenceLineId int NULL,ReferenceSubKey nvarchar(100) NULL,
    OriginalQuantity decimal(38,4) NOT NULL,RemainingQuantity decimal(38,4) NOT NULL,
    ResolvedProvisionalQty decimal(38,4) NOT NULL,RemainingOpenProvisionalQty decimal(38,4) NOT NULL,
    UnitCost decimal(38,6) NOT NULL,OccurredAtUtc datetime2(7) NOT NULL,Note nvarchar(1000) NULL
);
INSERT INTO #LayerStage
SELECT s.LayerId,e.ProductVariantId,e.TransactionId,s.InboundValuationId,e.ReferenceType,e.ReferenceId,e.ReferenceLineId,e.ReferenceSubKey,
       s.Quantity,sf.RemainingQty,sf.ResolvedProvisionalQty,sf.RemainingOpenProvisionalQty,s.UnitCost,s.OccurredWallClock,
       LEFT(CONCAT(N'LEGACY_FIFO_LAYER|Code=',e.Code,N'|ProductId=',e.SourceId),1000)
FROM #Supply s
JOIN #SupplyFinal sf ON sf.ProductVariantId=s.ProductVariantId AND sf.EventSeq=s.EventSeq
JOIN #Event e ON e.ProductVariantId=s.ProductVariantId AND e.EventSeq=s.EventSeq;

CREATE TABLE #ProvResolution
(
    ProductVariantId int NOT NULL,DemandEventSeq bigint NOT NULL,ResolvedQty decimal(38,4) NOT NULL,
    ResolvedAmount decimal(38,4) NOT NULL,FirstLayerId int NULL,LastLayerId int NULL,LastResolvedAt datetime2(7) NULL,
    PRIMARY KEY(ProductVariantId,DemandEventSeq)
);
;WITH X AS
(
    SELECT m.*,ROW_NUMBER() OVER(PARTITION BY m.ProductVariantId,m.DemandEventSeq ORDER BY m.SupplyEventSeq,m.MatchId) rnFirst,
           ROW_NUMBER() OVER(PARTITION BY m.ProductVariantId,m.DemandEventSeq ORDER BY m.SupplyEventSeq DESC,m.MatchId DESC) rnLast
    FROM #Match m WHERE m.MatchClass=2
),A AS
(
    SELECT ProductVariantId,DemandEventSeq,SUM(MatchedQty) ResolvedQty,
           CONVERT(decimal(38,4),SUM(ROUND(MatchedQty*SupplyUnitCost,4))) ResolvedAmount,
           MAX(CASE WHEN rnFirst=1 THEN SupplyLayerId END) FirstLayerId,
           MAX(CASE WHEN rnLast=1 THEN SupplyLayerId END) LastLayerId,
           MAX(CASE WHEN rnLast=1 THEN SupplyOccurred END) LastResolvedAt
    FROM X GROUP BY ProductVariantId,DemandEventSeq
)
INSERT INTO #ProvResolution SELECT * FROM A;

CREATE TABLE #AllocationStage
(
    AllocationId int PRIMARY KEY,ValuationId int NOT NULL,LayerId int NULL,Quantity decimal(38,4) NOT NULL,
    UnitCost decimal(38,6) NOT NULL,Amount decimal(38,4) NOT NULL,IsProvisional bit NOT NULL,IsResolved bit NOT NULL,
    ResolvedQuantity decimal(38,4) NOT NULL,ResolvedAmount decimal(38,4) NOT NULL,ResolvedAtUtc datetime2(7) NULL,
    ResolvedByLayerId int NULL,Note nvarchar(1000) NULL
);
-- Actual allocations: one per actual FIFO fragment.
INSERT INTO #AllocationStage
SELECT CONVERT(int,@InventoryCostLayerAllocationIdBase+@DemandCountForIds+m.MatchId),CONVERT(int,@InventoryValuationEntryIdBase+@SupplyCountForIds+@DemandCountForIds+m.MatchId),m.SupplyLayerId,m.MatchedQty,m.SupplyUnitCost,
       CONVERT(decimal(38,4),ROUND(m.MatchedQty*m.SupplyUnitCost,4)),0,1,m.MatchedQty,
       CONVERT(decimal(38,4),ROUND(m.MatchedQty*m.SupplyUnitCost,4)),d.OccurredWallClock,m.SupplyLayerId,
       LEFT(CONCAT(N'LEGACY_FIFO_ALLOC|Demand=',d.SourceId,N'|SupplyProduct=',m.SupplySourceId),1000)
FROM #Match m JOIN #Demand d ON d.ProductVariantId=m.ProductVariantId AND d.EventSeq=m.DemandEventSeq
WHERE m.MatchClass=1;
-- Provisional allocations: one per provisional outbound event, possibly partially/fully resolved by later supplies.
INSERT INTO #AllocationStage
SELECT d.ProvisionalAllocationId,d.ProvisionalValuationId,pr.FirstLayerId,d.ProvisionalQtyAtEvent,d.ProvisionalUnitCost,
       CONVERT(decimal(38,4),ROUND(d.ProvisionalQtyAtEvent*d.ProvisionalUnitCost,4)),1,
       CONVERT(bit,CASE WHEN COALESCE(pr.ResolvedQty,0)=d.ProvisionalQtyAtEvent THEN 1 ELSE 0 END),
       CONVERT(decimal(38,4),COALESCE(pr.ResolvedQty,0)),CONVERT(decimal(38,4),COALESCE(pr.ResolvedAmount,0)),
       pr.LastResolvedAt,pr.LastLayerId,LEFT(CONCAT(N'LEGACY_PROVISIONAL_ALLOC|Demand=',d.SourceId),1000)
FROM #Demand d LEFT JOIN #ProvResolution pr ON pr.ProductVariantId=d.ProductVariantId AND pr.DemandEventSeq=d.EventSeq
WHERE d.ProvisionalQtyAtEvent>0;

CREATE TABLE #VariantFinal
(
    ProductVariantId int PRIMARY KEY,
    FinalQty decimal(38,4) NOT NULL,
    FinalValue decimal(38,4) NOT NULL,
    LastValuationAtUtc datetime2(7) NULL,
    LastInboundUnitCost decimal(38,6) NULL,
    LastInboundAtUtc datetime2(7) NULL
);
;WITH Q AS
(
    SELECT ProductVariantId,CONVERT(decimal(38,4),SUM(QuantityChange)) FinalQty,MAX(OccurredWallClock) LastValuationAt
    FROM #Event
    GROUP BY ProductVariantId
),V AS
(
    SELECT ProductVariantId,CONVERT(decimal(38,4),SUM(Amount)) FinalValue
    FROM #ValuationFinal
    GROUP BY ProductVariantId
),LI AS
(
    SELECT ProductVariantId,MasterUnitCost,OccurredWallClock,
           ROW_NUMBER() OVER(PARTITION BY ProductVariantId ORDER BY EventSeq DESC) rn
    FROM #Event WHERE EventKind=1
)
INSERT INTO #VariantFinal
SELECT q.ProductVariantId,q.FinalQty,v.FinalValue,q.LastValuationAt,
       li.MasterUnitCost,li.OccurredWallClock
FROM Q q JOIN V v ON v.ProductVariantId=q.ProductVariantId
LEFT JOIN LI li ON li.ProductVariantId=q.ProductVariantId AND li.rn=1;

IF EXISTS(SELECT 1 FROM #VariantFinal WHERE FinalQty=0 AND FinalValue<>0)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: zero-qty variant has non-zero final value.',1;

CREATE TABLE #BalanceStage
(
    BalanceId int PRIMARY KEY,ProductVariantId int NOT NULL,OnHandQty decimal(38,4) NOT NULL,InventoryValue decimal(38,4) NOT NULL,
    AverageUnitCost decimal(38,6) NOT NULL,LastInboundUnitCost decimal(38,6) NULL,LastInboundAtUtc datetime2(7) NULL,
    LastValuationAtUtc datetime2(7) NULL
);
;WITH B AS
(
    SELECT f.*,ROW_NUMBER() OVER(ORDER BY f.ProductVariantId) rn
    FROM #VariantFinal f
    WHERE f.FinalQty<>0
)
INSERT INTO #BalanceStage
SELECT CONVERT(int,@InventoryBalanceIdBase+rn),ProductVariantId,FinalQty,FinalValue,
       CONVERT(decimal(38,6),ROUND(FinalValue/FinalQty,4)),
       LastInboundUnitCost,LastInboundAtUtc,LastValuationAtUtc
FROM B;

-------------------------------------------------------------------------------
-- G. STAGING VERIFICATION / EXACT PLAN
-------------------------------------------------------------------------------
DECLARE @StagedReceiptHeaders bigint=(SELECT COUNT_BIG(*) FROM #ReceiptHeader);
DECLARE @StagedReceiptLines bigint=(SELECT COUNT_BIG(*) FROM #ReceiptLine);
DECLARE @ExcludedReceiptLines bigint=(SELECT COUNT_BIG(*) FROM #ReceiptSource)-(SELECT COUNT_BIG(*) FROM #ReceiptEligible);
DECLARE @EmptyReceiptHeaders bigint=(SELECT COUNT_BIG(*) FROM(SELECT LegacyMaHD FROM #ReceiptSource GROUP BY LegacyMaHD)x)-@StagedReceiptHeaders;
DECLARE @FactorGt1Lines bigint=(SELECT COUNT_BIG(*) FROM #ReceiptEligible WHERE Factor<>1);
DECLARE @StagedTx bigint=(SELECT COUNT_BIG(*) FROM #TransactionStage);
DECLARE @StagedVal bigint=(SELECT COUNT_BIG(*) FROM #ValuationFinal);
DECLARE @StagedLayers bigint=(SELECT COUNT_BIG(*) FROM #LayerStage);
DECLARE @StagedAlloc bigint=(SELECT COUNT_BIG(*) FROM #AllocationStage);
DECLARE @StagedBal bigint=(SELECT COUNT_BIG(*) FROM #BalanceStage);
DECLARE @StagedNetQty decimal(38,4)=(SELECT CONVERT(decimal(38,4),SUM(QuantityChange)) FROM #Event);
DECLARE @StagedFinalValue decimal(38,4)=(SELECT CONVERT(decimal(38,4),SUM(InventoryValue)) FROM #BalanceStage);
DECLARE @OpenProv bigint=(SELECT COUNT_BIG(*) FROM #AllocationStage WHERE IsProvisional=1 AND IsResolved=0);
DECLARE @NegativeVariants bigint=(SELECT COUNT_BIG(*) FROM #BalanceStage WHERE OnHandQty<0);
DECLARE @NegativeQty decimal(38,4)=(SELECT CONVERT(decimal(38,4),SUM(CASE WHEN OnHandQty<0 THEN -OnHandQty ELSE 0 END)) FROM #BalanceStage);
DECLARE @RevaluationRows bigint=(SELECT COUNT_BIG(*) FROM #ValuationFinal WHERE EntryType=3);

-- Dynamic-ID range guard.
IF @StockDocumentIdBase+@StagedReceiptHeaders>2147483647
 OR @StockDocumentLineIdBase+@StagedReceiptLines>2147483647
 OR @InventoryTransactionIdBase+@StagedTx>2147483647
 OR @InventoryValuationEntryIdBase+@SupplyCountForIds+@DemandCountForIds+(2*@MatchCountForIds)>2147483647
 OR @InventoryCostLayerIdBase+@StagedLayers>2147483647
 OR @InventoryCostLayerAllocationIdBase+@DemandCountForIds+@MatchCountForIds>2147483647
 OR @InventoryBalanceIdBase+@StagedBal>2147483647
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: dynamic positive ID range would exceed INT.',1;

IF EXISTS(SELECT 1 FROM #LayerStage WHERE RemainingQuantity<0 OR ResolvedProvisionalQty<0 OR RemainingOpenProvisionalQty<0)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: staged layer invariant failed.',1;
IF EXISTS(SELECT 1 FROM #AllocationStage WHERE ResolvedQuantity>Quantity OR ResolvedQuantity<0)
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: staged allocation invariant failed.',1;

-- Final value formula: positive remaining layers - unresolved provisional allocations.
DECLARE @EconomicValue decimal(38,4)=
(
    SELECT CONVERT(decimal(38,4),
        COALESCE((SELECT SUM(ROUND(RemainingQuantity*UnitCost,4)) FROM #LayerStage),0)
       -COALESCE((SELECT SUM(ROUND((Quantity-ResolvedQuantity)*UnitCost,4)) FROM #AllocationStage WHERE IsProvisional=1 AND IsResolved=0),0))
);
IF @EconomicValue<>@StagedFinalValue
    THROW 51000,N'STEP4 V2 REAL COMMIT STOP: staged ledger final value != economic FIFO value.',1;

SELECT N'STEP4V2_INITIAL_IMPORT_00_STAGE_SUMMARY' ResultSet,
       @StagedReceiptHeaders ReceiptHeaders,@StagedReceiptLines ReceiptLines,@ExcludedReceiptLines ExcludedReceiptLines,
       @EmptyReceiptHeaders EmptyReceiptHeaders,@FactorGt1Lines FactorGt1Lines,
       @StagedTx InventoryTransactions,@StagedVal InventoryValuationEntries,@StagedLayers InventoryCostLayers,
       @StagedAlloc InventoryCostLayerAllocations,@StagedBal InventoryBalances,@OpenProv OpenProvisionalAllocations,
       @StagedNetQty FinalNetQty,@StagedFinalValue FinalInventoryValue,@EconomicValue EconomicFIFOValue,@RevaluationRows RevaluationRows;

-------------------------------------------------------------------------------
-- H. CAPTURE CURRENT STEP4 / SIDE-EFFECT SCOPE
-------------------------------------------------------------------------------
IF @Mode='PREVIEW' RETURN;

-- Recheck source + target immediately before persistent DML.
IF (SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.ProductDetail)<>@ExpectedProductDetailRows
 OR (SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.Product)<>@ExpectedProductRows
 OR (SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.[Order])<>@ExpectedOrderRows
 OR (SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.OrderDetail)<>@ExpectedOrderDetailRows
 OR (SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.HoaDonNhap)<>@ExpectedHoaDonNhapRows
 OR (SELECT COUNT_BIG(*) FROM [__SOURCE__].dbo.ChiTietNhapKho)<>@ExpectedChiTietNhapKhoRows
    THROW 51000,N'STEP4 V2 REAL STOP: source row-count fingerprint changed during staging.',1;

IF (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(ID,Code,CreatedDate,Warranty,Quantity,PurchasePrice)) FROM [__SOURCE__].dbo.Product)<>@ExpectedProductChecksum
 OR (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(ID,CreatedDate,Total,OrderCategoryID)) FROM [__SOURCE__].dbo.[Order])<>@ExpectedOrderChecksum
 OR (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(ID,OrderID,ProductCode,Quantity,Price,Total,OrderCategoryID)) FROM [__SOURCE__].dbo.OrderDetail)<>@ExpectedOrderDetailChecksum
 OR (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(MaHD,LoaiHD,TrangThai,NgayNhap,NgayXuLy,TenHD,Note,MaNVNhap,TenNV)) FROM [__SOURCE__].dbo.HoaDonNhap)<>@ExpectedHoaDonNhapChecksum
 OR (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(MaHD,Code,Quantity,PurchasePrice,Name,Note)) FROM [__SOURCE__].dbo.ChiTietNhapKho)<>@ExpectedChiTietNhapKhoChecksum
    THROW 51000,N'STEP4 V2 REAL STOP: source checksum fingerprint changed during staging.',1;

IF (SELECT COUNT_BIG(*) FROM dbo.StockDocument)<>@BeforeStockDocuments
 OR (SELECT COUNT_BIG(*) FROM dbo.StockDocumentLine)<>@BeforeStockDocumentLines
 OR (SELECT COUNT_BIG(*) FROM dbo.InventoryTransactions)<>@BeforeTransactions
 OR (SELECT COUNT_BIG(*) FROM dbo.InventoryValuationEntries)<>@BeforeValuations
 OR (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayers)<>@BeforeLayers
 OR (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayerAllocations)<>@BeforeAllocations
 OR (SELECT COUNT_BIG(*) FROM dbo.InventoryBalances)<>@BeforeBalances
    THROW 51000,N'STEP4 V2 REAL STOP: target Step4 row counts changed during staging.',1;

IF EXISTS
(
    SELECT 1
    FROM #IdentityBefore b
    WHERE ISNULL(b.IdentityValue,-1) <>
      CASE b.TableName
        WHEN N'StockDocument' THEN CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.StockDocument'))
        WHEN N'StockDocumentLine' THEN CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.StockDocumentLine'))
        WHEN N'InventoryTransactions' THEN CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.InventoryTransactions'))
        WHEN N'InventoryValuationEntries' THEN CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.InventoryValuationEntries'))
        WHEN N'InventoryCostLayers' THEN CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.InventoryCostLayers'))
        WHEN N'InventoryCostLayerAllocations' THEN CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.InventoryCostLayerAllocations'))
        WHEN N'InventoryBalances' THEN CONVERT(numeric(38,0),IDENT_CURRENT(N'dbo.InventoryBalances'))
      END
)
    THROW 51000,N'STEP4 V2 REAL STOP: target identity state changed during staging.',1;

    SET IDENTITY_INSERT dbo.StockDocument ON;
    INSERT INTO dbo.StockDocument
    (
        Id,DocumentNo,DocumentTitle,Type,Status,DocumentDate,WarehouseId,SupplierId,
        ReceiptSource,PurchaseOrderId,ReceivingSessionState,ReceivingOwnerUserId,ReceivingLeaseToken,
        ReceivingLeaseExpiresAtUtc,ReceivingLastSavedAtUtc,ReceivingRevision,DirectReceiptReason,
        HasVat,IncludeVatInInventoryCost,SubtotalBeforeVat,VatAmount,HasFreight,CapitalizeFreightInInventoryCost,
        FreightTotal,FreightPayeeName,FreightNote,IsFreightPaid,IsMerchandisePaid,MerchandisePayeeName,
        TotalAmount,Note,SubmittedAtUtc,SubmittedByUserId,ApprovedAtUtc,ApprovedByUserId,ApprovalNote,
        HasRevisionRequest,RevisionRequestNote,RevisionRequestedAtUtc,RevisionRequestedByUserId,
        RevisionResolvedAtUtc,RevisionResolvedByUserId,ConfirmedAtUtc,ConfirmedByUserId,ConfirmedLegalEntityId,
        CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy,StoreId
    )
    SELECT StockDocumentId,DocumentNo,DocumentTitle,1,TargetStatus,DocumentDate,1,NULL,0,NULL,
           0,NULL,NULL,NULL,NULL,0,N'Legacy Hóa đơn nhập historical migration v2',0,0,SubtotalBeforeVat,0,0,0,0,NULL,NULL,0,
           0,NULL,TotalAmount,Note,SubmittedAtUtc,NULL,ApprovedAtUtc,NULL,
           CASE WHEN TargetStatus=3 THEN N'LEGACY_CONFIRMED_IMPORT_V2_NO_REPOST' END,
           0,NULL,NULL,NULL,NULL,NULL,ConfirmedAtUtc,NULL,ConfirmedLegalEntityId,
           @MigrationExecutionUtc,NULL,NULL,NULL,0,NULL,NULL,1
    FROM #ReceiptHeader;
    IF @@ROWCOUNT<>@StagedReceiptHeaders THROW 51000,N'REAL COMMIT FAIL: receipt header insert mismatch.',1;
    SET IDENTITY_INSERT dbo.StockDocument OFF;

    SET IDENTITY_INSERT dbo.StockDocumentLine ON;
    INSERT INTO dbo.StockDocumentLine
    (
        Id,StockDocumentId,[LineNo],ProductVariantId,PurchaseOrderLineId,ReceiptAllocationKind,
        OutsidePoDecisionStatus,OutsidePoDecisionAtUtc,OutsidePoDecisionByUserId,OutsidePoDecisionNote,
        ProductUnitConversionId,TaxId,TaxNameSnapshot,UnitId,UnitNameSnapshot,Factor,Quantity,BaseQuantity,
        UnitCost,LineTotal,UnitPriceBeforeVat,TaxRate,VatAmount,UnitPriceAfterVat,FreightAllocation,
        ShortageDisposition,ShortageReason,ProductNameSnapshot,SkuSnapshot,BarcodeSnapshot,Note,
        CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy
    )
    SELECT StockDocumentLineId,StockDocumentId,ReceiptLineNo,ProductVariantId,NULL,0,0,NULL,NULL,NULL,
           ProductUnitConversionId,NULL,NULL,UnitId,UnitName,Factor,Quantity,BaseQuantity,UnitCost,LineTotal,
           UnitCost,0,0,UnitCost,0,0,NULL,ProductNameSnapshot,SkuSnapshot,BarcodeSnapshot,Note,
           @MigrationExecutionUtc,NULL,NULL,NULL,0,NULL,NULL
    FROM #ReceiptLine;
    IF @@ROWCOUNT<>@StagedReceiptLines THROW 51000,N'REAL COMMIT FAIL: receipt line insert mismatch.',1;
    SET IDENTITY_INSERT dbo.StockDocumentLine OFF;

    ---------------------------------------------------------------------------
    -- I4. INSERT HISTORICAL INVENTORY LEDGER v2
    ---------------------------------------------------------------------------
    SET IDENTITY_INSERT dbo.InventoryTransactions ON;
    INSERT INTO dbo.InventoryTransactions
    (
        Id,WarehouseId,ProductVariantId,TransactionType,ReferenceType,ReferenceId,ReferenceLineId,
        QuantityChange,BeforeQty,AfterQty,UnitCostSnapshot,TotalCost,BeforeInventoryValue,AfterInventoryValue,
        RunningAverageUnitCostAfter,CostSourceType,IsProvisionalCost,CostFinalizedAtUtc,OccurredAtUtc,Note,
        ReferenceSubKey,CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy,StoreId,IdempotencyKey
    )
    SELECT TransactionId,1,ProductVariantId,TransactionType,ReferenceType,ReferenceId,ReferenceLineId,
           QuantityChange,BeforeQty,AfterQty,UnitCostSnapshot,TotalCost,BeforeInventoryValue,AfterInventoryValue,
           RunningAverageUnitCostAfter,CostSourceType,IsProvisionalCost,CostFinalizedAtUtc,OccurredAtUtc,Note,
           ReferenceSubKey,@MigrationExecutionUtc,NULL,NULL,NULL,0,NULL,NULL,1,NULL
    FROM #TransactionStage;
    IF @@ROWCOUNT<>@StagedTx THROW 51000,N'REAL COMMIT FAIL: inventory transaction insert mismatch.',1;
    SET IDENTITY_INSERT dbo.InventoryTransactions OFF;

    SET IDENTITY_INSERT dbo.InventoryValuationEntries ON;
    INSERT INTO dbo.InventoryValuationEntries
    (
        Id,InventoryTransactionId,WarehouseId,ProductVariantId,EntryType,ReferenceType,ReferenceId,ReferenceLineId,
        ReferenceSubKey,Quantity,UnitCost,Amount,RunningQtyAfter,RunningValueAfter,RunningAverageUnitCostAfter,
        CostSourceType,IsProvisional,CostFinalizedAtUtc,RevaluationOfEntryId,SourceValuationEntryId,
        SourceReferenceSubKey,InventoryCostLayerId,Note,OccurredAtUtc,CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,
        IsDeleted,DeletedAtUtc,DeletedBy,StoreId
    )
    SELECT ValuationId,InventoryTransactionId,1,ProductVariantId,EntryType,ReferenceType,ReferenceId,ReferenceLineId,
           ReferenceSubKey,Quantity,UnitCost,Amount,RunningQtyAfter,RunningValueAfter,RunningAverageUnitCostAfter,
           CostSourceType,IsProvisional,CostFinalizedAtUtc,NULL,NULL,SourceReferenceSubKey,NULL,Note,OccurredAtUtc,
           @MigrationExecutionUtc,NULL,NULL,NULL,0,NULL,NULL,1
    FROM #ValuationFinal;
    IF @@ROWCOUNT<>@StagedVal THROW 51000,N'REAL COMMIT FAIL: valuation insert mismatch.',1;
    SET IDENTITY_INSERT dbo.InventoryValuationEntries OFF;

    SET IDENTITY_INSERT dbo.InventoryCostLayers ON;
    INSERT INTO dbo.InventoryCostLayers
    (
        Id,WarehouseId,ProductVariantId,InventoryTransactionId,InventoryValuationEntryId,ReferenceType,ReferenceId,
        ReferenceLineId,ReferenceSubKey,OriginalQuantity,RemainingQuantity,ResolvedProvisionalQty,
        RemainingOpenProvisionalQty,UnitCost,IsProvisionalSource,OccurredAtUtc,Note,
        CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy,StoreId
    )
    SELECT LayerId,1,ProductVariantId,TransactionId,ValuationId,ReferenceType,ReferenceId,ReferenceLineId,ReferenceSubKey,
           OriginalQuantity,RemainingQuantity,ResolvedProvisionalQty,RemainingOpenProvisionalQty,UnitCost,0,OccurredAtUtc,Note,
           @MigrationExecutionUtc,NULL,NULL,NULL,0,NULL,NULL,1
    FROM #LayerStage;
    IF @@ROWCOUNT<>@StagedLayers THROW 51000,N'REAL COMMIT FAIL: cost layer insert mismatch.',1;
    SET IDENTITY_INSERT dbo.InventoryCostLayers OFF;

    UPDATE v SET InventoryCostLayerId=s.InventoryCostLayerId,
                 RevaluationOfEntryId=s.RevaluationOfEntryId,
                 SourceValuationEntryId=s.SourceValuationEntryId
    FROM dbo.InventoryValuationEntries v JOIN #ValuationFinal s ON s.ValuationId=v.Id
    WHERE s.InventoryCostLayerId IS NOT NULL OR s.RevaluationOfEntryId IS NOT NULL OR s.SourceValuationEntryId IS NOT NULL;

    IF @@ROWCOUNT<>(SELECT COUNT_BIG(*) FROM #ValuationFinal WHERE InventoryCostLayerId IS NOT NULL OR RevaluationOfEntryId IS NOT NULL OR SourceValuationEntryId IS NOT NULL)
        THROW 51000,N'REAL COMMIT FAIL: valuation FK-link update mismatch.',1;

    SET IDENTITY_INSERT dbo.InventoryCostLayerAllocations ON;
    INSERT INTO dbo.InventoryCostLayerAllocations
    (
        Id,InventoryValuationEntryId,InventoryCostLayerId,ReverseOfAllocationId,Quantity,UnitCost,Amount,
        IsProvisional,IsResolved,ResolvedQuantity,ResolvedAmount,ResolvedAtUtc,ResolvedByInventoryCostLayerId,Note,
        CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy,StoreId
    )
    SELECT AllocationId,ValuationId,LayerId,NULL,Quantity,UnitCost,Amount,IsProvisional,IsResolved,
           ResolvedQuantity,ResolvedAmount,ResolvedAtUtc,ResolvedByLayerId,Note,
           @MigrationExecutionUtc,NULL,NULL,NULL,0,NULL,NULL,1
    FROM #AllocationStage;
    IF @@ROWCOUNT<>@StagedAlloc THROW 51000,N'REAL COMMIT FAIL: allocation insert mismatch.',1;
    SET IDENTITY_INSERT dbo.InventoryCostLayerAllocations OFF;

    SET IDENTITY_INSERT dbo.InventoryBalances ON;
    INSERT INTO dbo.InventoryBalances
    (
        Id,WarehouseId,ProductVariantId,OnHandQty,ReservedQty,InventoryValue,AverageUnitCost,
        LastInboundUnitCost,LastInboundAtUtc,LastValuationAtUtc,
        CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,IsDeleted,DeletedAtUtc,DeletedBy,StoreId
    )
    SELECT BalanceId,1,ProductVariantId,OnHandQty,0,InventoryValue,AverageUnitCost,
           LastInboundUnitCost,LastInboundAtUtc,LastValuationAtUtc,
           @MigrationExecutionUtc,NULL,NULL,NULL,0,NULL,NULL,1
    FROM #BalanceStage;
    IF @@ROWCOUNT<>@StagedBal THROW 51000,N'REAL COMMIT FAIL: balance insert mismatch.',1;
    SET IDENTITY_INSERT dbo.InventoryBalances OFF;

    ---------------------------------------------------------------------------
    -- I5. IN-TRANSACTION VERIFICATION
    ---------------------------------------------------------------------------
    IF (SELECT COUNT_BIG(*) FROM dbo.Products)<>@BeforeProducts
     OR (SELECT COUNT_BIG(*) FROM dbo.ProductVariant)<>@BeforeVariants
     OR (SELECT COUNT_BIG(*) FROM dbo.ProductUnitConversion)<>@BeforeConversions
     OR (SELECT COUNT_BIG(*) FROM dbo.ProductVariantUnitBarcode)<>@BeforeBarcodes
     OR (SELECT COUNT_BIG(*) FROM dbo.Customers)<>@BeforeCustomers
     OR (SELECT COUNT_BIG(*) FROM dbo.Orders)<>@BeforeOrders
     OR (SELECT COUNT_BIG(*) FROM dbo.OrderLines)<>@BeforeOrderLines
     OR (SELECT COUNT_BIG(*) FROM dbo.OrderPayments)<>@BeforeOrderPayments
     OR (SELECT COUNT_BIG(*) FROM dbo.SalesReturns)<>@BeforeReturns
     OR (SELECT COUNT_BIG(*) FROM dbo.SalesReturnLines)<>@BeforeReturnLines
     OR (SELECT COUNT_BIG(*) FROM dbo.SalesReturnPayments)<>@BeforeReturnPayments
     OR (SELECT COUNT_BIG(*) FROM dbo.POSShifts)<>@BeforePOSShifts
     OR (SELECT COUNT_BIG(*) FROM dbo.POSShiftCashTransactions)<>@BeforePOSCash
     OR (SELECT COUNT_BIG(*) FROM dbo.Users)<>@BeforeUsers
        THROW 51000,N'REAL COMMIT FAIL: protected Step1-3/catalog row count changed.',1;

    IF (SELECT COUNT_BIG(*) FROM dbo.StockDocument)<>@StagedReceiptHeaders
     OR (SELECT COUNT_BIG(*) FROM dbo.StockDocumentLine)<>@StagedReceiptLines
     OR (SELECT COUNT_BIG(*) FROM dbo.InventoryTransactions)<>@StagedTx
     OR (SELECT COUNT_BIG(*) FROM dbo.InventoryValuationEntries)<>@StagedVal
     OR (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayers)<>@StagedLayers
     OR (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayerAllocations)<>@StagedAlloc
     OR (SELECT COUNT_BIG(*) FROM dbo.InventoryBalances)<>@StagedBal
        THROW 51000,N'REAL COMMIT FAIL: target table count mismatch after v2 reload.',1;

    IF (SELECT CONVERT(decimal(38,4),SUM(OnHandQty)) FROM dbo.InventoryBalances)<>@StagedNetQty
     OR (SELECT CONVERT(decimal(38,4),SUM(InventoryValue)) FROM dbo.InventoryBalances)<>@StagedFinalValue
        THROW 51000,N'REAL COMMIT FAIL: final qty/value mismatch.',1;

    IF (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayerAllocations WHERE IsProvisional=1 AND IsResolved=0)<>@OpenProv
     OR EXISTS(SELECT 1 FROM dbo.InventoryCostLayers WHERE RemainingQuantity<0)
     OR EXISTS(SELECT 1 FROM dbo.InventoryCostLayerAllocations WHERE ResolvedQuantity>Quantity)
        THROW 51000,N'REAL COMMIT FAIL: target FIFO/provisional invariant mismatch.',1;

    IF EXISTS
    (
        SELECT 1 FROM
        (
            SELECT t.ProductVariantId,t.OccurredAtUtc,t.Id,t.BeforeQty,t.AfterQty,
                   LAG(t.AfterQty) OVER(PARTITION BY t.ProductVariantId ORDER BY t.OccurredAtUtc,t.Id) PrevAfter
            FROM dbo.InventoryTransactions t
        )x
        WHERE BeforeQty<>COALESCE(PrevAfter,0)
    )
        THROW 51000,N'REAL COMMIT FAIL: InventoryTransaction running qty continuity failed.',1;

    IF (SELECT COUNT_BIG(*) FROM dbo.InventoryTransactions WHERE TransactionType=20 AND ReferenceType=0 AND ReferenceId=N'LEGACY-SALE-EXC')<>@ExpectedNegativeHeaderLines
        THROW 51000,N'REAL COMMIT FAIL: negative-header inventory-only exception target count differs from staged approved exceptions.',1;

    IF EXISTS(SELECT 1 FROM dbo.StockDocument WHERE DocumentNo NOT LIKE N'LEGACY-HDN-%')
        THROW 51000,N'REAL COMMIT FAIL: v2 receipt reload unexpectedly contains non-legacy TEST receipt.',1;

    IF EXISTS(SELECT 1 FROM dbo.StockDocumentInputInvoiceDetailReconciliation WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader))
     OR EXISTS(SELECT 1 FROM dbo.InputInvoiceSupplierResolutionEvent WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader))
     OR EXISTS(SELECT 1 FROM dbo.ProductBarcodeVerificationRequests WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader))
     OR EXISTS(SELECT 1 FROM dbo.ProductLabelTasks WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader))
     OR EXISTS(SELECT 1 FROM dbo.PurchaseOrderActions WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader))
     OR EXISTS(SELECT 1 FROM dbo.PurchasePayables WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader))
     OR EXISTS(SELECT 1 FROM dbo.PurchaseReceiptAuditEvents WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader) OR StockDocumentLineId IN(SELECT StockDocumentLineId FROM #ReceiptLine))
     OR EXISTS(SELECT 1 FROM dbo.PurchaseReceivingActions WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader) OR StockDocumentLineId IN(SELECT StockDocumentLineId FROM #ReceiptLine))
     OR EXISTS(SELECT 1 FROM dbo.StockDocumentInputInvoiceMap WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader))
     OR EXISTS(SELECT 1 FROM dbo.StockDocumentInputInvoiceReconciliation WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader))
     OR EXISTS(SELECT 1 FROM dbo.StockDocumentLineInputInvoiceMap WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader) OR StockDocumentLineId IN(SELECT StockDocumentLineId FROM #ReceiptLine))
     OR EXISTS(SELECT 1 FROM dbo.StockDocumentProvisionalItems WHERE StockDocumentId IN(SELECT StockDocumentId FROM #ReceiptHeader) OR ResolvedStockDocumentLineId IN(SELECT StockDocumentLineId FROM #ReceiptLine))
        THROW 51000,N'REAL COMMIT FAIL: historical direct-import receipt unexpectedly generated side effects.',1;

    SELECT N'STEP4V2_INITIAL_IMPORT_02_IN_TRANSACTION_PASS' ResultSet,
           (SELECT COUNT_BIG(*) FROM dbo.StockDocument) StockDocument,
           (SELECT COUNT_BIG(*) FROM dbo.StockDocumentLine) StockDocumentLine,
           (SELECT COUNT_BIG(*) FROM dbo.InventoryTransactions) InventoryTransactions,
           (SELECT COUNT_BIG(*) FROM dbo.InventoryValuationEntries) InventoryValuationEntries,
           (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayers) InventoryCostLayers,
           (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayerAllocations) InventoryCostLayerAllocations,
           (SELECT COUNT_BIG(*) FROM dbo.InventoryBalances) InventoryBalances,
           (SELECT CONVERT(decimal(38,4),SUM(OnHandQty)) FROM dbo.InventoryBalances) FinalNetQty,
           (SELECT CONVERT(decimal(38,4),SUM(InventoryValue)) FROM dbo.InventoryBalances) FinalInventoryValue,
           (SELECT MIN(OccurredAtUtc) FROM dbo.InventoryTransactions) MinOccurredAt,
           (SELECT MAX(OccurredAtUtc) FROM dbo.InventoryTransactions) MaxOccurredAt,
           (SELECT COUNT_BIG(DISTINCT OccurredAtUtc) FROM dbo.InventoryTransactions) DistinctOccurredAt;

-- J. POST-COMMIT VERIFICATION
-------------------------------------------------------------------------------
IF (SELECT COUNT_BIG(*) FROM dbo.Products)<>@BeforeProducts
 OR (SELECT COUNT_BIG(*) FROM dbo.ProductVariant)<>@BeforeVariants
 OR (SELECT COUNT_BIG(*) FROM dbo.ProductUnitConversion)<>@BeforeConversions
 OR (SELECT COUNT_BIG(*) FROM dbo.ProductVariantUnitBarcode)<>@BeforeBarcodes
 OR (SELECT COUNT_BIG(*) FROM dbo.Customers)<>@BeforeCustomers
 OR (SELECT COUNT_BIG(*) FROM dbo.Orders)<>@BeforeOrders
 OR (SELECT COUNT_BIG(*) FROM dbo.OrderLines)<>@BeforeOrderLines
 OR (SELECT COUNT_BIG(*) FROM dbo.OrderPayments)<>@BeforeOrderPayments
 OR (SELECT COUNT_BIG(*) FROM dbo.SalesReturns)<>@BeforeReturns
 OR (SELECT COUNT_BIG(*) FROM dbo.SalesReturnLines)<>@BeforeReturnLines
 OR (SELECT COUNT_BIG(*) FROM dbo.SalesReturnPayments)<>@BeforeReturnPayments
 OR (SELECT COUNT_BIG(*) FROM dbo.POSShifts)<>@BeforePOSShifts
 OR (SELECT COUNT_BIG(*) FROM dbo.POSShiftCashTransactions)<>@BeforePOSCash
 OR (SELECT COUNT_BIG(*) FROM dbo.Users)<>@BeforeUsers
    THROW 51000,N'STEP4 V2 REAL COMMIT FAIL: protected Step1-3/catalog row count changed.',1;

IF (SELECT COUNT_BIG(*) FROM dbo.StockDocument)<>@StagedReceiptHeaders
 OR (SELECT COUNT_BIG(*) FROM dbo.StockDocumentLine)<>@StagedReceiptLines
 OR (SELECT COUNT_BIG(*) FROM dbo.InventoryTransactions)<>@StagedTx
 OR (SELECT COUNT_BIG(*) FROM dbo.InventoryValuationEntries)<>@StagedVal
 OR (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayers)<>@StagedLayers
 OR (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayerAllocations)<>@StagedAlloc
 OR (SELECT COUNT_BIG(*) FROM dbo.InventoryBalances)<>@StagedBal
    THROW 51000,N'STEP4 V2 REAL COMMIT FAIL: before-runner-COMMIT Step4 row counts mismatch.',1;

IF (SELECT CONVERT(decimal(38,4),SUM(OnHandQty)) FROM dbo.InventoryBalances)<>@StagedNetQty
 OR (SELECT CONVERT(decimal(38,4),SUM(InventoryValue)) FROM dbo.InventoryBalances)<>@StagedFinalValue
    THROW 51000,N'STEP4 V2 REAL COMMIT FAIL: before-runner-COMMIT final qty/value mismatch.',1;

IF EXISTS(SELECT 1 FROM dbo.StockDocument WHERE DocumentNo NOT LIKE N'LEGACY-HDN-%')
    THROW 51000,N'STEP4 V2 REAL COMMIT FAIL: non-legacy TEST receipt survived canonical rebuild.',1;

IF EXISTS(SELECT 1 FROM dbo.StockDocumentInputInvoiceDetailReconciliation WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument))
 OR EXISTS(SELECT 1 FROM dbo.InputInvoiceSupplierResolutionEvent WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument))
 OR EXISTS(SELECT 1 FROM dbo.ProductBarcodeVerificationRequests WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument))
 OR EXISTS(SELECT 1 FROM dbo.ProductLabelTasks WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument))
 OR EXISTS(SELECT 1 FROM dbo.PurchaseOrderActions WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument))
 OR EXISTS(SELECT 1 FROM dbo.PurchasePayables WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument))
 OR EXISTS(SELECT 1 FROM dbo.PurchaseReceiptAuditEvents WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument) OR StockDocumentLineId IN(SELECT Id FROM dbo.StockDocumentLine))
 OR EXISTS(SELECT 1 FROM dbo.PurchaseReceivingActions WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument) OR StockDocumentLineId IN(SELECT Id FROM dbo.StockDocumentLine))
 OR EXISTS(SELECT 1 FROM dbo.StockDocumentInputInvoiceMap WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument))
 OR EXISTS(SELECT 1 FROM dbo.StockDocumentInputInvoiceReconciliation WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument))
 OR EXISTS(SELECT 1 FROM dbo.StockDocumentLineInputInvoiceMap WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument) OR StockDocumentLineId IN(SELECT Id FROM dbo.StockDocumentLine))
 OR EXISTS(SELECT 1 FROM dbo.StockDocumentProvisionalItems WHERE StockDocumentId IN(SELECT Id FROM dbo.StockDocument) OR ResolvedStockDocumentLineId IN(SELECT Id FROM dbo.StockDocumentLine))
    THROW 51000,N'STEP4 V2 REAL COMMIT FAIL: direct historical receipt unexpectedly has side-effect rows.',1;

IF (SELECT COUNT_BIG(*) FROM dbo.InventoryTransactions WHERE TransactionType=20 AND ReferenceType=0 AND ReferenceId=N'LEGACY-SALE-EXC')<>@ExpectedNegativeHeaderLines
    THROW 51000,N'STEP4 V2 REAL COMMIT FAIL: negative-header inventory-only exception count differs from staged approved exceptions.',1;

-- Explicit positive IDs must have advanced identity to the highest committed Id.
IF CONVERT(bigint,IDENT_CURRENT(N'dbo.StockDocument'))<>(SELECT MAX(CONVERT(bigint,Id)) FROM dbo.StockDocument)
 OR CONVERT(bigint,IDENT_CURRENT(N'dbo.StockDocumentLine'))<>(SELECT MAX(CONVERT(bigint,Id)) FROM dbo.StockDocumentLine)
 OR CONVERT(bigint,IDENT_CURRENT(N'dbo.InventoryTransactions'))<>(SELECT MAX(CONVERT(bigint,Id)) FROM dbo.InventoryTransactions)
 OR CONVERT(bigint,IDENT_CURRENT(N'dbo.InventoryValuationEntries'))<>(SELECT MAX(CONVERT(bigint,Id)) FROM dbo.InventoryValuationEntries)
 OR CONVERT(bigint,IDENT_CURRENT(N'dbo.InventoryCostLayers'))<>(SELECT MAX(CONVERT(bigint,Id)) FROM dbo.InventoryCostLayers)
 OR CONVERT(bigint,IDENT_CURRENT(N'dbo.InventoryCostLayerAllocations'))<>(SELECT MAX(CONVERT(bigint,Id)) FROM dbo.InventoryCostLayerAllocations)
 OR CONVERT(bigint,IDENT_CURRENT(N'dbo.InventoryBalances'))<>(SELECT MAX(CONVERT(bigint,Id)) FROM dbo.InventoryBalances)
    THROW 51000,N'STEP4 V2 REAL COMMIT FAIL: identity/max-id before-runner-COMMIT mismatch.',1;

SELECT N'STEP4V2_INITIAL_IMPORT_03_POST_COMMIT_PASS' ResultSet,
       (SELECT COUNT_BIG(*) FROM dbo.StockDocument) StockDocument,
       (SELECT COUNT_BIG(*) FROM dbo.StockDocumentLine) StockDocumentLine,
       (SELECT COUNT_BIG(*) FROM dbo.InventoryTransactions) InventoryTransactions,
       (SELECT COUNT_BIG(*) FROM dbo.InventoryValuationEntries) InventoryValuationEntries,
       (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayers) InventoryCostLayers,
       (SELECT COUNT_BIG(*) FROM dbo.InventoryCostLayerAllocations) InventoryCostLayerAllocations,
       (SELECT COUNT_BIG(*) FROM dbo.InventoryBalances) InventoryBalances,
       (SELECT CONVERT(decimal(38,4),SUM(OnHandQty)) FROM dbo.InventoryBalances) FinalNetQty,
       (SELECT CONVERT(decimal(38,4),SUM(InventoryValue)) FROM dbo.InventoryBalances) FinalInventoryValue,
       (SELECT MIN(OccurredAtUtc) FROM dbo.InventoryTransactions) MinOccurredAt,
       (SELECT MAX(OccurredAtUtc) FROM dbo.InventoryTransactions) MaxOccurredAt,
       (SELECT COUNT_BIG(DISTINCT OccurredAtUtc) FROM dbo.InventoryTransactions) DistinctOccurredAt,
       N'Protected Step1-3/catalog unchanged; Step4 staged; runner controls final transaction.' Note;

SELECT N'STEP4V2_INITIAL_IMPORT_COMPLETE' ResultSet,
       N'STAGED_VERIFIED: transaction is still pending; runner decides COMMIT or ROLLBACK.' Message,
       SYSDATETIMEOFFSET() CompletedAt;