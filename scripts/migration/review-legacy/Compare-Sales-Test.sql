-- Read only. Execute in the isolated rehearsal, against the existing TEST reference.
-- Excludes runtime orders, stock/cost updates, generated child IDs and conversion IDs.
SET NOCOUNT ON;
SELECT N'ORDER_CONTRACT_DIFF' Report,COUNT_BIG(*) Mismatches
FROM dbo.Orders n FULL JOIN (SELECT * FROM GaoAppDb.dbo.Orders WHERE OrderNumber LIKE N'LEGACY-%') t ON t.Id=n.Id
WHERE n.Id IS NULL OR t.Id IS NULL OR EXISTS(
 SELECT n.OrderNumber,n.Status,n.PaymentStatus,n.CustomerId,n.Subtotal,n.DiscountTotal,n.OrderDiscount,n.GrandTotal,n.PaidTotal,n.BalanceDue,n.ChangeDue,n.POSShiftId,n.Note
 EXCEPT SELECT t.OrderNumber,t.Status,t.PaymentStatus,t.CustomerId,t.Subtotal,t.DiscountTotal,t.OrderDiscount,t.GrandTotal,t.PaidTotal,t.BalanceDue,t.ChangeDue,t.POSShiftId,t.Note);
SELECT N'LINE_CONTRACT_DIFF' Report,COUNT_BIG(*) Mismatches
FROM dbo.OrderLines n FULL JOIN (SELECT l.* FROM GaoAppDb.dbo.OrderLines l JOIN GaoAppDb.dbo.Orders o ON o.Id=l.OrderId WHERE o.OrderNumber LIKE N'LEGACY-%') t
 ON CASE WHEN t.Sku=N'LEGACY-ADJUSTMENT' THEN -t.OrderId ELSE t.Id END=CASE WHEN n.Sku=N'LEGACY-ADJUSTMENT' THEN -n.OrderId ELSE n.Id END
WHERE n.Id IS NULL OR t.Id IS NULL OR EXISTS(
 SELECT n.OrderId,n.ProductId,n.VariantId,n.ItemName,n.Sku,n.Barcode,n.UnitName,n.Quantity,n.BaseQuantity,n.Multiplier,n.SellingUnitId,n.SellingUnitName,n.BaseUnitId,n.BaseUnitName,n.UnitPrice,n.OriginalUnitPrice,n.LineDiscount,n.LineTotal
 EXCEPT SELECT t.OrderId,t.ProductId,t.VariantId,t.ItemName,t.Sku,t.Barcode,t.UnitName,t.Quantity,t.BaseQuantity,t.Multiplier,t.SellingUnitId,t.SellingUnitName,t.BaseUnitId,t.BaseUnitName,t.UnitPrice,t.OriginalUnitPrice,t.LineDiscount,t.LineTotal);
SELECT N'PAYMENT_CONTRACT_DIFF' Report,COUNT_BIG(*) Mismatches FROM (
 SELECT OrderId,Method,Amount,COUNT_BIG(*) n FROM dbo.OrderPayments GROUP BY OrderId,Method,Amount
 EXCEPT SELECT p.OrderId,p.Method,p.Amount,COUNT_BIG(*) FROM GaoAppDb.dbo.OrderPayments p JOIN GaoAppDb.dbo.Orders o ON o.Id=p.OrderId WHERE o.OrderNumber LIKE N'LEGACY-%' GROUP BY p.OrderId,p.Method,p.Amount
)d;
SELECT N'SHIFT_CONTRACT_DIFF' Report,COUNT_BIG(*) Mismatches
FROM dbo.POSShifts n FULL JOIN (SELECT * FROM GaoAppDb.dbo.POSShifts WHERE ShiftCode LIKE N'LEGACY-%') t ON t.Id=n.Id
WHERE n.Id IS NULL OR t.Id IS NULL OR EXISTS(
 SELECT n.TerminalId,n.OpenedByUserId,n.Status,n.ShiftCode,n.OpeningCash,n.CashSalesTotal,n.NonCashSalesTotal,n.CashRefundTotal,n.NonCashRefundTotal,n.RefundCount,n.VoidCount,n.CashInTotal,n.CashOutTotal,n.ClosingCashExpected,n.ClosingCashActual,n.ClosedByUserId,n.WarehouseId
 EXCEPT SELECT t.TerminalId,t.OpenedByUserId,t.Status,t.ShiftCode,t.OpeningCash,t.CashSalesTotal,t.NonCashSalesTotal,t.CashRefundTotal,t.NonCashRefundTotal,t.RefundCount,t.VoidCount,t.CashInTotal,t.CashOutTotal,t.ClosingCashExpected,t.ClosingCashActual,t.ClosedByUserId,t.WarehouseId);
SELECT N'RETURN_CONTRACT_DIFF' Report,COUNT_BIG(*) Mismatches
FROM dbo.SalesReturns n FULL JOIN (SELECT * FROM GaoAppDb.dbo.SalesReturns WHERE ReturnNumber LIKE N'LEGACY-RETURN-%') t ON t.Id=n.Id
WHERE n.Id IS NULL OR t.Id IS NULL OR EXISTS(
 SELECT n.ReturnNumber,n.OrderId,n.POSShiftId,n.Type,n.Status,n.ReturnSubtotal,n.RefundTotal,n.CreatedByUserId,n.CompletedByUserId
 EXCEPT SELECT t.ReturnNumber,t.OrderId,t.POSShiftId,t.Type,t.Status,t.ReturnSubtotal,t.RefundTotal,t.CreatedByUserId,t.CompletedByUserId);
SELECT N'RETURN_LINE_CONTRACT_DIFF' Report,COUNT_BIG(*) Mismatches FROM (
 SELECT SalesReturnId,OrderLineId,ReturnQuantity,ReturnBaseQuantity,RefundUnitAmount,RefundLineTotal,Action,COUNT_BIG(*) n FROM dbo.SalesReturnLines GROUP BY SalesReturnId,OrderLineId,ReturnQuantity,ReturnBaseQuantity,RefundUnitAmount,RefundLineTotal,Action
 EXCEPT SELECT l.SalesReturnId,l.OrderLineId,l.ReturnQuantity,l.ReturnBaseQuantity,l.RefundUnitAmount,l.RefundLineTotal,l.Action,COUNT_BIG(*) FROM GaoAppDb.dbo.SalesReturnLines l JOIN GaoAppDb.dbo.SalesReturns r ON r.Id=l.SalesReturnId WHERE r.ReturnNumber LIKE N'LEGACY-RETURN-%' GROUP BY l.SalesReturnId,l.OrderLineId,l.ReturnQuantity,l.ReturnBaseQuantity,l.RefundUnitAmount,l.RefundLineTotal,l.Action
)d;
SELECT N'SOURCE_PAYMENT_AGGREGATES' Report,COUNT_BIG(*) PaymentCount,SUM(Amount) Amount FROM dbo.OrderPayments;
SELECT N'SOURCE_CASH_AGGREGATES' Report,COUNT_BIG(*) CashCount,SUM(Amount) Amount FROM dbo.POSShiftCashTransactions;
SELECT N'SOURCE_RETURN_AGGREGATES' Report,COUNT_BIG(*) ReturnCount,SUM(ReturnSubtotal) ReturnSubtotal,SUM(RefundTotal) RefundTotal FROM dbo.SalesReturns;

SELECT POSShiftId,Type,Amount,Reason,Note,CreatedByUserId,COUNT_BIG(*) n INTO #NCash FROM dbo.POSShiftCashTransactions
GROUP BY POSShiftId,Type,Amount,Reason,Note,CreatedByUserId;
SELECT POSShiftId,Type,Amount,Reason,Note,CreatedByUserId,COUNT_BIG(*) n INTO #TCash FROM GaoAppDb.dbo.POSShiftCashTransactions WHERE Note LIKE N'LegacyCashId=%'
GROUP BY POSShiftId,Type,Amount,Reason,Note,CreatedByUserId;
SELECT N'CASH_CONTRACT_DIFF' Report,
 (SELECT COUNT_BIG(*) FROM(SELECT * FROM #NCash EXCEPT SELECT * FROM #TCash)a)
 +(SELECT COUNT_BIG(*) FROM(SELECT * FROM #TCash EXCEPT SELECT * FROM #NCash)b) Mismatches;
SELECT SalesReturnId,Method,Amount,COUNT_BIG(*) n INTO #NRefund FROM dbo.SalesReturnPayments GROUP BY SalesReturnId,Method,Amount;
SELECT p.SalesReturnId,p.Method,p.Amount,COUNT_BIG(*) n INTO #TRefund FROM GaoAppDb.dbo.SalesReturnPayments p
JOIN GaoAppDb.dbo.SalesReturns r ON r.Id=p.SalesReturnId WHERE r.ReturnNumber LIKE N'LEGACY-RETURN-%' GROUP BY p.SalesReturnId,p.Method,p.Amount;
SELECT N'RETURN_PAYMENT_CONTRACT_DIFF' Report,
 (SELECT COUNT_BIG(*) FROM(SELECT * FROM #NRefund EXCEPT SELECT * FROM #TRefund)a)
 +(SELECT COUNT_BIG(*) FROM(SELECT * FROM #TRefund EXCEPT SELECT * FROM #NRefund)b) Mismatches;
SELECT N'PAYMENT_REVERSE_CONTRACT_DIFF' Report,COUNT_BIG(*) Mismatches FROM (
 SELECT p.OrderId,p.Method,p.Amount,COUNT_BIG(*) n FROM GaoAppDb.dbo.OrderPayments p JOIN GaoAppDb.dbo.Orders o ON o.Id=p.OrderId WHERE o.OrderNumber LIKE N'LEGACY-%' GROUP BY p.OrderId,p.Method,p.Amount
 EXCEPT SELECT OrderId,Method,Amount,COUNT_BIG(*) FROM dbo.OrderPayments GROUP BY OrderId,Method,Amount
)d;
SELECT N'DATE_CONTRACT_DIFF_OVER_1MS' Report,
 (SELECT COUNT_BIG(*) FROM dbo.Orders n JOIN GaoAppDb.dbo.Orders t ON t.Id=n.Id WHERE ABS(DATEDIFF_BIG(MICROSECOND,n.CreatedAtUtc,t.CreatedAtUtc))>1000 OR ABS(DATEDIFF_BIG(MICROSECOND,n.CompletedAtUtc,t.CompletedAtUtc))>1000)
 +(SELECT COUNT_BIG(*) FROM dbo.POSShifts n JOIN GaoAppDb.dbo.POSShifts t ON t.Id=n.Id WHERE ABS(DATEDIFF_BIG(MICROSECOND,n.OpenedAtUtc,t.OpenedAtUtc))>1000 OR ABS(DATEDIFF_BIG(MICROSECOND,n.ClosedAtUtc,t.ClosedAtUtc))>1000)
 +(SELECT COUNT_BIG(*) FROM dbo.POSShiftCashTransactions n JOIN GaoAppDb.dbo.POSShiftCashTransactions t ON t.Note=n.Note WHERE ABS(DATEDIFF_BIG(MICROSECOND,n.CreatedAtUtc,t.CreatedAtUtc))>1000) Mismatches;
