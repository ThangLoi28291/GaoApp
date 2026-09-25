-- Initial historical sales import. Run through ../Invoke-Migration.ps1 only.
SET NOCOUNT ON; SET XACT_ABORT ON;
IF @@TRANCOUNT<>1 OR ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'GSTORE_INITIAL_IMPORT')),0)<>1
 THROW 55400,'Run through the transactional initial-import runner.',1;
DECLARE @Now datetime2(7)=SYSUTCDATETIME();
DECLARE @TechVariant int=(SELECT Id FROM dbo.ProductVariant WHERE StoreId=1 AND Sku=N'LEGACY-ADJUSTMENT' AND IsDeleted=0);
IF @TechVariant IS NULL THROW 55401,'Missing the inactive LEGACY-ADJUSTMENT catalog prerequisite.',1;
DECLARE @FallbackCustomer int=(SELECT Id FROM dbo.Customers WHERE StoreId=1 AND OldCustomerId=8 AND IsDeleted=0);
IF @FallbackCustomer IS NULL THROW 55402,'Missing customer legacy ID 8.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE Id=1 AND StoreId=1 AND LegalEntityId=1 AND IsDeleted=0)
 THROW 55403,'Store/Warehouse/LegalEntity mapping must be 1/1/1.',1;

SELECT o.*,CONVERT(datetime2(7),DATEADD(HOUR,-7,CONVERT(datetime2(7),o.CreatedDate))) UtcDate,
 COALESCE(c.Id,@FallbackCustomer) TargetCustomerId
INTO #SourceOrders FROM [__SOURCE__].dbo.[Order] o
LEFT JOIN dbo.Customers c ON c.StoreId=1 AND c.OldCustomerId=o.CustomerID AND c.IsDeleted=0
WHERE o.OrderCategoryID=1 AND ISNULL(o.Total,0)>=0;
CREATE UNIQUE CLUSTERED INDEX IX_SourceOrders ON #SourceOrders(ID);
SELECT r.*,DATEADD(HOUR,-7,CONVERT(datetime2(7),r.CreatedDate)) UtcDate
INTO #SourceReturns FROM [__SOURCE__].dbo.[Order] r
WHERE r.OrderCategoryID=3 AND r.OrderIDMuaHang>0;
CREATE UNIQUE CLUSTERED INDEX IX_SourceReturns ON #SourceReturns(ID);
IF EXISTS(SELECT 1 FROM #SourceOrders WHERE CreatedDate IS NULL OR UserID IS NULL OR ID>2147483647)
 OR EXISTS(SELECT 1 FROM #SourceReturns WHERE CreatedDate IS NULL OR UserID IS NULL OR ID>2147483647)
 THROW 55404,'Missing sales/return source date or actor, or legacy ID exceeds INT.',1;
IF EXISTS(SELECT 1 FROM #SourceReturns r LEFT JOIN #SourceOrders o ON o.ID=r.OrderIDMuaHang WHERE o.ID IS NULL)
 THROW 55405,'A linked return has no eligible original sale; review it explicitly.',1;
IF EXISTS(SELECT 1 FROM #SourceOrders WHERE LEN(Note)>500)
 OR EXISTS(SELECT 1 FROM #SourceReturns WHERE Total<0 OR LEN(Note)>1000)
 THROW 55417,'Negative linked return or source note exceeds target length.',1;

-- Customer and employee identities are independent. Never overwrite GaoApp accounts.
SELECT DISTINCT UserID INTO #Actors FROM #SourceOrders UNION SELECT UserID FROM #SourceReturns;
IF EXISTS(SELECT 1 FROM #Actors a LEFT JOIN dbo.Users u ON u.Id=a.UserID
 LEFT JOIN [__SOURCE__].dbo.[User] s ON s.ID=a.UserID
 WHERE u.Id IS NULL OR s.ID IS NULL OR NULLIF(LTRIM(RTRIM(u.UserName)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(s.UserName)),N'') IS NULL
 OR UPPER(LTRIM(RTRIM(u.UserName)))<>UPPER(LTRIM(RTRIM(s.UserName))) COLLATE DATABASE_DEFAULT)
 THROW 55406,'Legacy employee ID/login mapping differs from preserved GaoApp users.',1;

-- Unique code -> selling conversion. Retain inactive, non-deleted historical catalog entries.
SELECT Code,MIN(ConversionId) ConversionId INTO #CodeMap FROM (
 SELECT pv.Sku COLLATE DATABASE_DEFAULT Code,c.Id ConversionId
 FROM dbo.ProductVariant pv JOIN dbo.ProductUnitConversion c ON c.ProductVariantId=pv.Id
 WHERE pv.StoreId=1 AND pv.IsDeleted=0 AND c.StoreId=1 AND c.IsDeleted=0 AND c.IsBaseUnit=1
 UNION
 SELECT b.Barcode COLLATE DATABASE_DEFAULT,c.Id FROM dbo.ProductVariantUnitBarcode b
 JOIN dbo.ProductUnitConversion c ON c.Id=b.ProductUnitConversionId
 JOIN dbo.ProductVariant v ON v.Id=c.ProductVariantId
 WHERE b.StoreId=1 AND b.IsDeleted=0 AND c.StoreId=1 AND c.IsDeleted=0 AND v.StoreId=1 AND v.IsDeleted=0
) candidates GROUP BY Code HAVING COUNT(DISTINCT ConversionId)=1;
CREATE UNIQUE CLUSTERED INDEX IX_CodeMap ON #CodeMap(Code);

-- Missing catalog codes can still have a historical name on incoming Product rows.
-- Preserve a name only when all nonempty source names agree; do not guess between names.
SELECT Code COLLATE DATABASE_DEFAULT Code,MAX(LTRIM(RTRIM(Name))) Name,
 COUNT(DISTINCT LTRIM(RTRIM(Name))) NameVariants INTO #HistoricalNames
FROM [__SOURCE__].dbo.Product WHERE NULLIF(LTRIM(RTRIM(Name)),N'') IS NOT NULL
GROUP BY Code COLLATE DATABASE_DEFAULT;
CREATE UNIQUE CLUSTERED INDEX IX_HistoricalNames ON #HistoricalNames(Code);

SELECT d.*,o.UtcDate,o.UserID,c.Id ConversionId,c.ProductVariantId,c.UnitId SellingUnitId,c.Factor,
 pv.ProductId,pv.Sku TargetSku,COALESCE(hn.Name,pv.ProductVariantName) ItemName,u.Name SellingUnitName,p.BaseUnitId,bu.Name BaseUnitName,
 CONVERT(decimal(18,2),CASE WHEN ISNULL(d.Price,0)*CONVERT(decimal(28,4),d.Quantity)>=ISNULL(d.Total,0)
 THEN ISNULL(d.Price,0) ELSE CEILING(ISNULL(d.Total,0)*100.0/d.Quantity)/100.0 END) EffectivePrice
INTO #SourceLines FROM [__SOURCE__].dbo.OrderDetail d
JOIN #SourceOrders o ON o.ID=d.OrderID
LEFT JOIN #CodeMap m ON m.Code=d.ProductCode COLLATE DATABASE_DEFAULT
LEFT JOIN dbo.ProductUnitConversion c ON c.Id=m.ConversionId
LEFT JOIN dbo.ProductVariant pv ON pv.Id=c.ProductVariantId
LEFT JOIN dbo.Products p ON p.Id=pv.ProductId
LEFT JOIN #HistoricalNames hn ON hn.Code=d.ProductCode COLLATE DATABASE_DEFAULT AND hn.NameVariants=1 AND p.Alias=N'LEGACY-'+pv.Sku
LEFT JOIN dbo.Unit u ON u.Id=c.UnitId LEFT JOIN dbo.Unit bu ON bu.Id=p.BaseUnitId
WHERE d.Quantity>0 AND ISNULL(d.Price,0)>=0 AND ISNULL(d.Total,0)>=0;
CREATE UNIQUE CLUSTERED INDEX IX_SourceLines ON #SourceLines(ID);
IF EXISTS(SELECT 1 FROM #SourceLines WHERE ConversionId IS NULL OR Factor<=0 OR ID>2147483647
 OR LEN(ItemName)>200 OR LEN(TargetSku)>50 OR LEN(ProductCode)>50 OR LEN(SellingUnitName)>50 OR LEN(BaseUnitName)>100)
 THROW 55407,'Sales line mapping ambiguous/missing or exceeds target contract; no partial order import.',1;

-- Preserve original row IDs across all line categories belonging to eligible sales.
SELECT CONVERT(int,ID) Id,CONVERT(int,OrderID) OrderId,ProductId,ProductVariantId VariantId,
 ItemName,SellingUnitName UnitName,TargetSku Sku,CONVERT(nvarchar(50),ProductCode) Barcode,
 CONVERT(decimal(18,4),Quantity) Quantity,CONVERT(decimal(18,4),Quantity*Factor) BaseQuantity,
 SellingUnitId,ConversionId ProductUnitConversionId,SellingUnitName,BaseUnitId,BaseUnitName,
 CONVERT(decimal(18,6),Factor) Multiplier,CONVERT(nvarchar(100),ProductCode) ScannedBarcode,
 EffectivePrice UnitPrice,CONVERT(decimal(18,2),Quantity*EffectivePrice-ISNULL(Total,0)) LineDiscount,
 CONVERT(decimal(18,2),ISNULL(Total,0)) LineTotal,CONVERT(decimal(18,2),ISNULL(Price,0)) OriginalUnitPrice,
 CONVERT(decimal(18,2),0) PromotionDiscount,CONVERT(decimal(18,2),0) ComboAllocatedDiscount,
 CONVERT(nvarchar(1000),N'Legacy import - cost chưa migrate') CostSnapshotNote,
 UtcDate CreatedAtUtc,CONVERT(int,UserID) CreatedBy,CONVERT(bit,0) IsDeleted,1 StoreId
INTO #StageOrderLines FROM #SourceLines;
CREATE UNIQUE CLUSTERED INDEX IX_StageOrderLines_Id ON #StageOrderLines(Id);
CREATE INDEX IX_StageOrderLines_Order ON #StageOrderLines(OrderId);
SELECT o.ID,ISNULL(SUM(l.LineTotal),0) LineSum INTO #LineSums FROM #SourceOrders o
LEFT JOIN #StageOrderLines l ON l.OrderId=o.ID GROUP BY o.ID;
-- Negative source lines encode an explicit order discount. Retain that discount
-- even when rounding in the old header requires a positive financial adjustment.
SELECT d.OrderID,SUM(-CONVERT(decimal(18,2),d.Total)) ExplicitDiscount INTO #SourceDiscount
FROM [__SOURCE__].dbo.OrderDetail d JOIN #SourceOrders o ON o.ID=d.OrderID
WHERE d.Total<0 GROUP BY d.OrderID;
UPDATE s SET LineSum=s.LineSum-ISNULL(d.ExplicitDiscount,0)
FROM #LineSums s LEFT JOIN #SourceDiscount d ON d.OrderID=s.ID;
DECLARE @LastSourceLine bigint=(SELECT ISNULL(MAX(ID),0) FROM [__SOURCE__].dbo.OrderDetail);
IF @LastSourceLine+(SELECT COUNT_BIG(*) FROM #LineSums s JOIN #SourceOrders o ON o.ID=s.ID WHERE o.Total>s.LineSum)>2147483647
 THROW 55408,'Synthetic adjustment line IDs would exceed INT.',1;
INSERT #StageOrderLines
SELECT CONVERT(int,@LastSourceLine+ROW_NUMBER() OVER(ORDER BY o.ID)),CONVERT(int,o.ID),p.Id,v.Id,
 N'[Legacy] Điều chỉnh giá',u.Name,v.Sku,NULL,1,1,u.Id,c.Id,u.Name,u.Id,u.Name,1,NULL,
 o.Total-s.LineSum,0,o.Total-s.LineSum,o.Total-s.LineSum,0,0,
 N'Legacy import - technical financial adjustment; no inventory/cost',o.UtcDate,CONVERT(int,o.UserID),0,1
FROM #SourceOrders o JOIN #LineSums s ON s.ID=o.ID
JOIN dbo.ProductVariant v ON v.Id=@TechVariant JOIN dbo.Products p ON p.Id=v.ProductId
JOIN dbo.ProductUnitConversion c ON c.ProductVariantId=v.Id AND c.IsBaseUnit=1 AND c.IsDeleted=0
JOIN dbo.Unit u ON u.Id=c.UnitId WHERE o.Total>s.LineSum;

-- Historical cash ledger excludes transfer-only entries and return cash to avoid double counting.
SELECT ID,UserID,CaLamID,CreatedDate,UtcDate INTO #Activity FROM #SourceOrders
UNION ALL SELECT ID,UserID,CaLamID,CreatedDate,UtcDate FROM #SourceReturns;
CREATE INDEX IX_Activity_Shift ON #Activity(CaLamID) INCLUDE(UtcDate);
IF EXISTS(SELECT 1 FROM [__SOURCE__].dbo.LoaiThuChi GROUP BY MaThuChi HAVING COUNT_BIG(*)>1)
 THROW 55418,'Duplicate legacy cash reason code would multiply cash transactions.',1;
IF EXISTS(SELECT 1 FROM [__SOURCE__].dbo.ChiTietThuChi c
 LEFT JOIN [__SOURCE__].dbo.LoaiThuChi t ON t.MaThuChi=c.MaThuChi
 WHERE c.SoTien>0 AND (c.MaThuChi LIKE 'Thu-%' OR c.MaThuChi LIKE 'Chi-%')
 AND c.MaThuChi NOT IN('Chi-TienKhachTraHang','Chi-TienChuyenKhoan')
 AND (LEN(COALESCE(t.TenThuChi,c.MaThuChi))>200 OR LEN(CONCAT(N'LegacyCashId=',c.ID,N' | ',c.NoiDung))>500))
 THROW 55419,'Cash source reason/note would be truncated.',1;
SELECT c.*,anchor.UseShiftOpening,CONVERT(int,CASE WHEN c.MaThuChi LIKE 'Thu-%' THEN 1 ELSE 2 END) TargetType,
 CONVERT(nvarchar(200),COALESCE(t.TenThuChi,c.MaThuChi)) Reason,
 CONCAT(CONVERT(nvarchar(max),N'LegacyCashId='),c.ID,
 CASE WHEN anchor.UseShiftOpening=1 THEN
 CONCAT(N' | Legacy cash date-only; original NgayThang=',CONVERT(nvarchar(19),CONVERT(datetime2(7),c.NgayThang),120),N'; timestamp anchored to legacy shift opening') ELSE N'' END,
 CASE WHEN NULLIF(LTRIM(RTRIM(c.NoiDung)),N'') IS NOT NULL THEN N' | '+LTRIM(RTRIM(c.NoiDung)) ELSE N'' END) TargetNote
INTO #SourceCash FROM [__SOURCE__].dbo.ChiTietThuChi c
LEFT JOIN [__SOURCE__].dbo.LoaiThuChi t ON t.MaThuChi=c.MaThuChi
LEFT JOIN [__SOURCE__].dbo.ManagementJob cm ON cm.ID=c.CaLamID
CROSS APPLY(SELECT CONVERT(bit,CASE WHEN cm.ChotCa IS NULL
 AND NOT EXISTS(SELECT 1 FROM #Activity ac WHERE ac.CaLamID=c.CaLamID) THEN 1 ELSE 0 END) UseShiftOpening) anchor
WHERE c.SoTien>0 AND (c.MaThuChi LIKE 'Thu-%' OR c.MaThuChi LIKE 'Chi-%')
AND c.MaThuChi NOT IN('Chi-TienKhachTraHang','Chi-TienChuyenKhoan');
CREATE INDEX IX_SourceCash_Shift ON #SourceCash(CaLamID);
IF EXISTS(SELECT 1 FROM #SourceCash WHERE LEN(TargetNote)>500)
 THROW 55423,'Cash provenance note exceeds target length.',1;

SELECT m.* INTO #ActualShift FROM [__SOURCE__].dbo.ManagementJob m
WHERE EXISTS(SELECT 1 FROM #Activity a WHERE a.CaLamID=m.ID)
 OR EXISTS(SELECT 1 FROM #SourceCash c WHERE c.CaLamID=m.ID);
IF EXISTS(SELECT 1 FROM #ActualShift WHERE LEN(GhiChu)>300)
 THROW 55422,'Shift note would be truncated.',1;
IF EXISTS(SELECT 1 FROM #Activity a LEFT JOIN #ActualShift s ON s.ID=a.CaLamID WHERE a.CaLamID>0 AND s.ID IS NULL)
 THROW 55409,'Positive source shift reference is missing.',1;
IF EXISTS(SELECT 1 FROM #ActualShift s LEFT JOIN dbo.Users u ON u.Id=s.UserID
 LEFT JOIN [__SOURCE__].dbo.[User] lu ON lu.ID=s.UserID WHERE u.Id IS NULL OR lu.ID IS NULL
 OR NULLIF(LTRIM(RTRIM(u.UserName)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(lu.UserName)),N'') IS NULL
 OR UPPER(LTRIM(RTRIM(u.UserName)))<>UPPER(LTRIM(RTRIM(lu.UserName))) COLLATE DATABASE_DEFAULT)
 THROW 55410,'Actual-shift actor does not map to a preserved employee.',1;
DECLARE @LastShift bigint=(SELECT ISNULL(MAX(ID),0) FROM [__SOURCE__].dbo.ManagementJob);
SELECT CONVERT(int,@LastShift+ROW_NUMBER() OVER(ORDER BY CONVERT(date,CreatedDate),UserID)) Id,
 CONVERT(date,CreatedDate) SourceDay,UserID,MIN(UtcDate) FirstUtc,MAX(UtcDate) LastUtc
INTO #SyntheticShift FROM #Activity WHERE ISNULL(CaLamID,0)<=0 GROUP BY CONVERT(date,CreatedDate),UserID;
SELECT CONVERT(int,a.ID) Id,COALESCE(t.Id,ut.Id) TerminalId,CONVERT(int,a.UserID) OpenedByUserId,
 COALESCE(DATEADD(HOUR,-7,CONVERT(datetime2(7),a.NhanCa)),x.FirstUtc) OpenedAtUtc,
 CONVERT(tinyint,2) Status,CONVERT(nvarchar(30),CONCAT(N'LEGACY-CL-',a.ID)) ShiftCode,
 CONVERT(decimal(18,2),ISNULL(a.TienNhanCa,0)) OpeningCash,
 CONVERT(decimal(18,2),0) CashSalesTotal,CONVERT(decimal(18,2),0) NonCashSalesTotal,
 CONVERT(decimal(18,2),0) CashRefundTotal,CONVERT(decimal(18,2),0) NonCashRefundTotal,
 CONVERT(int,0) RefundCount,CONVERT(int,0) VoidCount,
 CONVERT(decimal(18,2),0) CashInTotal,CONVERT(decimal(18,2),0) CashOutTotal,
 CONVERT(decimal(18,2),0) ClosingCashExpected,CONVERT(decimal(18,2),a.TienChotCa) ClosingCashActual,
 CONVERT(int,a.UserID) ClosedByUserId,COALESCE(DATEADD(HOUR,-7,CONVERT(datetime2(7),a.ChotCa)),x.LastUtc,DATEADD(HOUR,-7,CONVERT(datetime2(7),a.NhanCa))) ClosedAtUtc,
 CONVERT(nvarchar(300),a.GhiChu) CloseNote,1 WarehouseId,
 COALESCE(DATEADD(HOUR,-7,CONVERT(datetime2(7),a.NhanCa)),x.FirstUtc) CreatedAtUtc,CONVERT(bit,0) IsDeleted,1 StoreId
INTO #StagePOSShifts FROM #ActualShift a
LEFT JOIN dbo.POSTerminals t ON t.StoreId=1 AND t.Code=CONCAT(N'LEGACY-KET-',a.KetID) AND t.IsDeleted=0
LEFT JOIN dbo.POSTerminals ut ON ut.StoreId=1 AND ut.Code=N'LEGACY-UNKNOWN' AND ut.IsDeleted=0
OUTER APPLY(SELECT MIN(UtcDate) FirstUtc,MAX(UtcDate) LastUtc FROM #Activity ac WHERE ac.CaLamID=a.ID)x;
INSERT #StagePOSShifts
SELECT s.Id,t.Id,CONVERT(int,s.UserID),s.FirstUtc,2,CONCAT(N'LEGACY-SYN-',s.Id),0,0,0,0,0,0,0,0,0,0,NULL,
 CONVERT(int,s.UserID),s.LastUtc,N'Legacy synthetic shift: source CaLamID missing/0/-1',1,s.FirstUtc,0,1
FROM #SyntheticShift s LEFT JOIN dbo.POSTerminals t ON t.StoreId=1 AND t.Code=N'LEGACY-UNKNOWN' AND t.IsDeleted=0;
IF EXISTS(SELECT 1 FROM #StagePOSShifts WHERE TerminalId IS NULL OR OpenedAtUtc IS NULL OR ClosedAtUtc IS NULL OR ClosedAtUtc<OpenedAtUtc)
 THROW 55411,'Cannot safely establish historical shift terminal or time interval.',1;

SELECT CONVERT(int,a.ID) ActivityId,CONVERT(int,CASE WHEN a.CaLamID>0 THEN a.CaLamID ELSE s.Id END) ShiftId
INTO #ActivityShift FROM #Activity a LEFT JOIN #SyntheticShift s ON s.UserID=a.UserID AND s.SourceDay=CONVERT(date,a.CreatedDate);
SELECT CONVERT(int,o.ID) Id,CONVERT(nvarchar(30),CONCAT(N'LEGACY-',o.ID)) OrderNumber,
 CONVERT(tinyint,2) Status,CONVERT(tinyint,2) PaymentStatus,o.TargetCustomerId CustomerId,
 CONVERT(decimal(18,2),ISNULL(l.GrossTotal,0)) Subtotal,CONVERT(decimal(18,2),ISNULL(l.DiscountTotal,0)) DiscountTotal,
 CONVERT(decimal(18,2),ISNULL(l.NetTotal,0)-ISNULL(o.Total,0)) OrderDiscount,
 CONVERT(decimal(18,2),ISNULL(o.Total,0)) GrandTotal,CONVERT(decimal(18,2),ISNULL(o.Total,0)) PaidTotal,
 CONVERT(decimal(18,2),0) BalanceDue,CONVERT(decimal(18,2),0) ChangeDue,
 CONVERT(nvarchar(500),o.Note) Note,sm.ShiftId POSShiftId,o.UtcDate CompletedAtUtc,
 CONVERT(bit,0) HasReservation,CONVERT(decimal(18,2),0) VoucherDiscountTotal,
 CONVERT(decimal(18,2),0) PromotionDiscountTotal,CONVERT(decimal(18,2),0) ComboDiscountTotal,
 o.UtcDate CreatedAtUtc,CONVERT(int,o.UserID) CreatedBy,CONVERT(bit,0) IsDeleted,1 StoreId
INTO #StageOrders FROM #SourceOrders o JOIN #ActivityShift sm ON sm.ActivityId=o.ID
OUTER APPLY(SELECT SUM(Quantity*UnitPrice) GrossTotal,SUM(LineDiscount) DiscountTotal,SUM(LineTotal) NetTotal FROM #StageOrderLines WHERE OrderId=o.ID)l;
CREATE UNIQUE CLUSTERED INDEX IX_StageOrders ON #StageOrders(Id);
CREATE INDEX IX_StageOrders_Shift ON #StageOrders(POSShiftId);
IF EXISTS(SELECT 1 FROM #StageOrders WHERE OrderDiscount<0 OR Subtotal-DiscountTotal-OrderDiscount<>GrandTotal)
 THROW 55412,'Order financial equation failed.',1;
SELECT CONVERT(int,o.ID) OrderId,CONVERT(int,CASE WHEN o.Status=1 THEN 1 ELSE 0 END) Method,
 CONVERT(decimal(18,2),o.Total) Amount,o.UtcDate PaidAtUtc,o.UtcDate CreatedAtUtc,
 CONVERT(int,o.UserID) CreatedBy,CONVERT(bit,0) IsDeleted,1 StoreId
INTO #StageOrderPayments FROM #SourceOrders o WHERE o.Total>0;
CREATE INDEX IX_StageOrderPayments_Order ON #StageOrderPayments(OrderId);

IF EXISTS(SELECT 1 FROM #SourceCash c LEFT JOIN #ActualShift a ON a.ID=c.CaLamID WHERE a.ID IS NULL OR c.NgayThang IS NULL)
 THROW 55413,'Eligible legacy cash row has missing shift/date.',1;
SELECT CONVERT(int,c.CaLamID) POSShiftId,c.TargetType Type,CONVERT(decimal(18,2),c.SoTien) Amount,
 c.Reason,c.TargetNote Note,CONVERT(int,a.UserID) CreatedByUserId,
 CASE WHEN c.UseShiftOpening=1 THEN sh.OpenedAtUtc
 ELSE DATEADD(HOUR,-7,CONVERT(datetime2(7),c.NgayThang)) END CreatedAtUtc,CONVERT(bit,0) IsDeleted,1 StoreId
INTO #StagePOSShiftCashTransactions FROM #SourceCash c JOIN #ActualShift a ON a.ID=c.CaLamID
JOIN #StagePOSShifts sh ON sh.Id=a.ID;
CREATE INDEX IX_StageCash_Shift ON #StagePOSShiftCashTransactions(POSShiftId);

SELECT r.ID ReturnId,d.ID SourceLineId,l.* INTO #ReturnSourceLines
FROM #SourceReturns r JOIN [__SOURCE__].dbo.OrderDetail d ON d.OrderIDTraHang=r.ID AND d.OrderID=r.OrderIDMuaHang
JOIN #StageOrderLines l ON l.Id=d.ID;
CREATE INDEX IX_ReturnSourceLines_Return ON #ReturnSourceLines(ReturnId);
IF EXISTS(SELECT 1 FROM #SourceReturns r WHERE NOT EXISTS(SELECT 1 FROM #ReturnSourceLines l WHERE l.ReturnId=r.ID))
 THROW 55414,'Linked return lacks reciprocal imported sale lines.',1;
IF EXISTS(SELECT 1 FROM #SourceReturns r JOIN [__SOURCE__].dbo.OrderDetail d ON d.OrderIDTraHang=r.ID
 LEFT JOIN #StageOrderLines l ON l.Id=d.ID WHERE d.OrderID<>r.OrderIDMuaHang OR l.Id IS NULL)
 THROW 55415,'Linked return line mapping incomplete.',1;
SELECT CONVERT(int,r.ID) Id,CONVERT(nvarchar(30),CONCAT(N'LEGACY-RETURN-',r.ID)) ReturnNumber,
 CONVERT(int,r.OrderIDMuaHang) OrderId,sm.ShiftId POSShiftId,3 Type,1 Status,
 CONVERT(nvarchar(500),N'Legacy linked return import') Reason,CONVERT(nvarchar(1000),r.Note) Note,
 CONVERT(decimal(18,2),x.Subtotal) ReturnSubtotal,CONVERT(decimal(18,2),ISNULL(r.Total,0)) RefundTotal,
 CONVERT(int,r.UserID) CreatedByUserId,CONVERT(int,r.UserID) CompletedByUserId,
 r.UtcDate CompletedAtUtc,r.UtcDate CreatedAtUtc,CONVERT(int,r.UserID) CreatedBy,CONVERT(bit,0) IsDeleted,1 StoreId
INTO #StageSalesReturns FROM #SourceReturns r JOIN #ActivityShift sm ON sm.ActivityId=r.ID
CROSS APPLY(SELECT SUM(LineTotal) Subtotal FROM #ReturnSourceLines l WHERE l.ReturnId=r.ID)x;
CREATE INDEX IX_StageReturns_Shift ON #StageSalesReturns(POSShiftId);
SELECT CONVERT(int,r.ID) SalesReturnId,l.Id OrderLineId,l.ProductId,l.VariantId,l.ItemName,l.UnitName,
 CONVERT(decimal(18,3),l.Quantity) ReturnQuantity,CONVERT(decimal(18,3),l.BaseQuantity) ReturnBaseQuantity,
 CONVERT(decimal(18,2),l.LineTotal/l.Quantity) RefundUnitAmount,l.LineTotal RefundLineTotal,
 0 Action,CONVERT(nvarchar(500),N'Legacy return import') Reason,CONVERT(decimal(18,6),0) UnitCostSnapshot,
 CONVERT(decimal(18,4),0) LineCostTotal,CONVERT(bit,1) IsProvisionalCost,r.UtcDate CreatedAtUtc,
 CONVERT(int,r.UserID) CreatedBy,CONVERT(bit,0) IsDeleted,1 StoreId
INTO #StageSalesReturnLines FROM #SourceReturns r JOIN #ReturnSourceLines l ON l.ReturnId=r.ID;
SELECT CONVERT(int,r.ID) SalesReturnId,CONVERT(int,CASE WHEN r.Status=1 THEN 1 ELSE 0 END) Method,
 CONVERT(decimal(18,2),r.Total) Amount,r.UtcDate PaidAtUtc,r.UtcDate CreatedAtUtc,
 CONVERT(int,r.UserID) CreatedBy,CONVERT(bit,0) IsDeleted,1 StoreId
INTO #StageSalesReturnPayments FROM #SourceReturns r WHERE r.Total>0;
CREATE INDEX IX_StageReturnPayments_Return ON #StageSalesReturnPayments(SalesReturnId);

UPDATE s SET CashSalesTotal=ISNULL(p.CashTotal,0),NonCashSalesTotal=ISNULL(p.BankTotal,0),
 CashInTotal=ISNULL(c.CashIn,0),CashOutTotal=ISNULL(c.CashOut,0),
 CashRefundTotal=ISNULL(r.CashTotal,0),NonCashRefundTotal=ISNULL(r.BankTotal,0),RefundCount=ISNULL(r.ReturnCount,0),
 ClosingCashExpected=s.OpeningCash+ISNULL(p.CashTotal,0)+ISNULL(c.CashIn,0)-ISNULL(c.CashOut,0)-ISNULL(r.CashTotal,0)
FROM #StagePOSShifts s
OUTER APPLY(SELECT SUM(CASE WHEN p.Method=0 THEN p.Amount ELSE 0 END) CashTotal,SUM(CASE WHEN p.Method<>0 THEN p.Amount ELSE 0 END) BankTotal FROM #StageOrderPayments p JOIN #StageOrders o ON o.Id=p.OrderId WHERE o.POSShiftId=s.Id)p
OUTER APPLY(SELECT SUM(CASE WHEN Type=1 THEN Amount ELSE 0 END) CashIn,SUM(CASE WHEN Type=2 THEN Amount ELSE 0 END) CashOut FROM #StagePOSShiftCashTransactions WHERE POSShiftId=s.Id)c
OUTER APPLY(SELECT SUM(CASE WHEN p.Method=0 THEN p.Amount ELSE 0 END) CashTotal,SUM(CASE WHEN p.Method<>0 THEN p.Amount ELSE 0 END) BankTotal,COUNT(*) ReturnCount FROM #StageSalesReturns r LEFT JOIN #StageSalesReturnPayments p ON p.SalesReturnId=r.Id WHERE r.POSShiftId=s.Id)r;

-- Existing TEST contract: historical cash may be incomplete. Keep the raw amount
-- in the report/note and normalize only the nonnegative target display field.
SELECT Id,ShiftCode,ClosingCashExpected RawClosingCashExpected INTO #NegativeCashShifts
FROM #StagePOSShifts WHERE ClosingCashExpected<0;
UPDATE s SET CloseNote=CONCAT(N'Legacy cash flow not fully reconcilable; RawClosingCashExpected=',s.ClosingCashExpected,
 N'; normalized to 0',CASE WHEN NULLIF(s.CloseNote,N'') IS NOT NULL THEN N' | '+s.CloseNote ELSE N'' END),ClosingCashExpected=0
FROM #StagePOSShifts s WHERE s.ClosingCashExpected<0;

SELECT N'SALES_PLAN' Report,(SELECT COUNT_BIG(*) FROM #StageOrders) Orders,
 (SELECT COUNT_BIG(*) FROM #StageOrderLines) Lines,(SELECT COUNT_BIG(*) FROM #StageOrderPayments) Payments,
 (SELECT COUNT_BIG(*) FROM #StagePOSShifts) Shifts,(SELECT COUNT_BIG(*) FROM #StagePOSShiftCashTransactions) CashTransactions,
 (SELECT COUNT_BIG(*) FROM #StageSalesReturns) LinkedReturns,(SELECT COUNT_BIG(*) FROM #StageSalesReturnLines) ReturnLines,
 (SELECT SUM(GrandTotal) FROM #StageOrders) GrandTotal;
SELECT N'NEGATIVE_SALE_HEADERS_EXCLUDED' Report,ID,Total FROM [__SOURCE__].dbo.[Order] WHERE OrderCategoryID=1 AND Total<0;
SELECT N'FINANCIAL_ADJUSTMENTS' Report,OrderId,LineTotal FROM #StageOrderLines WHERE VariantId=@TechVariant;
SELECT N'CUSTOMER_FALLBACK' Report,o.ID LegacyOrderId,o.CustomerID LegacyCustomerId FROM #SourceOrders o
WHERE NOT EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.StoreId=1 AND c.OldCustomerId=o.CustomerID AND c.IsDeleted=0);
SELECT N'HISTORICAL_NEGATIVE_CASH_NORMALIZED' Report,* FROM #NegativeCashShifts ORDER BY Id;
IF @Mode='PREVIEW' RETURN;

-- Generic insertion and exact comparison use the explicit columns of each staged contract.
-- Identity columns of sales/source shifts are supplied, other child IDs are generated by SQL Server.
DECLARE @Tables TABLE(Seq int PRIMARY KEY,Name sysname,KeepId bit);
INSERT @Tables VALUES(1,'POSShifts',1),(2,'Orders',1),(3,'OrderLines',1),(4,'OrderPayments',0),
 (5,'POSShiftCashTransactions',0),(6,'SalesReturns',1),(7,'SalesReturnLines',0),(8,'SalesReturnPayments',0);
DECLARE @Name sysname,@KeepId bit,@Columns nvarchar(max),@Sql nvarchar(max);
DECLARE target_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT Name,KeepId FROM @Tables ORDER BY Seq;
OPEN target_cursor; FETCH NEXT FROM target_cursor INTO @Name,@KeepId;
WHILE @@FETCH_STATUS=0
BEGIN
 SELECT @Columns=STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(name)),N',') WITHIN GROUP(ORDER BY column_id)
 FROM tempdb.sys.columns WHERE object_id=OBJECT_ID(N'tempdb..#Stage'+@Name);
 SET @Sql=CASE WHEN @KeepId=1 THEN N'SET IDENTITY_INSERT dbo.'+QUOTENAME(@Name)+N' ON;' ELSE N'' END
 +N'INSERT dbo.'+QUOTENAME(@Name)+N'('+@Columns+N') SELECT '+@Columns+N' FROM '+QUOTENAME(N'#Stage'+@Name)+N';'
 +CASE WHEN @KeepId=1 THEN N'SET IDENTITY_INSERT dbo.'+QUOTENAME(@Name)+N' OFF;' ELSE N'' END
 +N'IF (SELECT COUNT_BIG(*) FROM dbo.'+QUOTENAME(@Name)+N')<>(SELECT COUNT_BIG(*) FROM '+QUOTENAME(N'#Stage'+@Name)+N')
 OR EXISTS(SELECT '+@Columns+N' FROM '+QUOTENAME(N'#Stage'+@Name)+N' EXCEPT SELECT '+@Columns+N' FROM dbo.'+QUOTENAME(@Name)+N')
 OR EXISTS(SELECT '+@Columns+N' FROM dbo.'+QUOTENAME(@Name)+N' EXCEPT SELECT '+@Columns+N' FROM '+QUOTENAME(N'#Stage'+@Name)+N')
 THROW 55416,''Imported table differs from stage: '+@Name+N''',1;';
 EXEC sys.sp_executesql @Sql;
 FETCH NEXT FROM target_cursor INTO @Name,@KeepId;
END;
CLOSE target_cursor; DEALLOCATE target_cursor;
SELECT N'SALES_IMPORT_EXACT_PASS' Report,(SELECT COUNT_BIG(*) FROM dbo.Orders) Orders,(SELECT COUNT_BIG(*) FROM dbo.OrderLines) Lines;
