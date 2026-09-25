-- Run via ../Invoke-Migration.ps1. Caller holds source locks and owns the transaction.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT<>1 OR ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'GSTORE_INITIAL_IMPORT')),0)<>1
 THROW 55301,'Run through the initial-import package runner.',1;
IF OBJECT_ID(N'dbo.LegacyReturnArchives',N'U') IS NULL
 THROW 55302,'Apply AddLegacyReturnArchive schema first.',1;

DECLARE @ImportUtc datetime2(7)=SYSUTCDATETIME();
SELECT o.ID LegacyOrderId,DATEADD(HOUR,-7,CONVERT(datetime2(7),o.CreatedDate)) OccurredAtUtc,
 o.CustomerID LegacyCustomerId,CONVERT(nvarchar(400),c.Name) CustomerName,
 o.UserID LegacyUserId,CONVERT(nvarchar(400),u.Name) EmployeeName,
 CONVERT(decimal(18,2),o.Total) SourceTotal,o.Status SourcePaymentFlag,
 (SELECT o.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES) HeaderJson
INTO #ArchiveHeaders
FROM [__SOURCE__].dbo.[Order] o
LEFT JOIN [__SOURCE__].dbo.[User] c ON c.ID=o.CustomerID
LEFT JOIN [__SOURCE__].dbo.[User] u ON u.ID=o.UserID
WHERE o.OrderCategoryID=3 AND COALESCE(o.OrderIDMuaHang,0)<=0;
CREATE UNIQUE CLUSTERED INDEX IX_ArchiveHeaders ON #ArchiveHeaders(LegacyOrderId);

-- A source line may point to the return directly or through OrderIDTraHang.
-- UNION removes only the duplicate association to the same line, never distinct source lines.
SELECT h.LegacyOrderId,d.ID DetailId INTO #ArchiveDetailKeys
FROM #ArchiveHeaders h JOIN [__SOURCE__].dbo.OrderDetail d ON d.OrderID=h.LegacyOrderId
UNION
SELECT h.LegacyOrderId,d.ID FROM #ArchiveHeaders h JOIN [__SOURCE__].dbo.OrderDetail d ON d.OrderIDTraHang=h.LegacyOrderId;
CREATE UNIQUE CLUSTERED INDEX IX_ArchiveDetailKeys ON #ArchiveDetailKeys(LegacyOrderId,DetailId);

SELECT h.*,
 (SELECT d.* FROM #ArchiveDetailKeys k JOIN [__SOURCE__].dbo.OrderDetail d ON d.ID=k.DetailId
  WHERE k.LegacyOrderId=h.LegacyOrderId ORDER BY d.ID FOR JSON PATH,INCLUDE_NULL_VALUES) DetailsJson
INTO #ArchivePlan FROM #ArchiveHeaders h;

SELECT N'RETURN_ARCHIVE_PLAN' Report,COUNT_BIG(*) Headers,
 (SELECT COUNT_BIG(*) FROM #ArchiveDetailKeys) SourceDetailAssociations,
 SUM(CASE WHEN DetailsJson=N'[]' THEN 1 ELSE 0 END) HeadersWithoutDetails,
 MIN(OccurredAtUtc) FirstUtc,MAX(OccurredAtUtc) LastUtc,SUM(SourceTotal) SourceTotal
FROM #ArchivePlan;
SELECT N'ARCHIVE_WITHOUT_DETAILS' Report,LegacyOrderId FROM #ArchivePlan WHERE DetailsJson=N'[]';
IF @Mode='PREVIEW' RETURN;

INSERT dbo.LegacyReturnArchives(StoreId,LegacyOrderId,OccurredAtUtc,LegacyCustomerId,CustomerName,
 LegacyUserId,EmployeeName,SourceTotal,SourcePaymentFlag,HeaderJson,DetailsJson,ImportedAtUtc)
SELECT 1,LegacyOrderId,OccurredAtUtc,LegacyCustomerId,CustomerName,LegacyUserId,EmployeeName,
 SourceTotal,SourcePaymentFlag,HeaderJson,DetailsJson,@ImportUtc FROM #ArchivePlan;

IF EXISTS(
 SELECT LegacyOrderId,OccurredAtUtc,LegacyCustomerId,CustomerName COLLATE Latin1_General_100_BIN2,
 LegacyUserId,EmployeeName COLLATE Latin1_General_100_BIN2,SourceTotal,SourcePaymentFlag,
 HeaderJson COLLATE Latin1_General_100_BIN2,DetailsJson COLLATE Latin1_General_100_BIN2 FROM #ArchivePlan
 EXCEPT
 SELECT LegacyOrderId,OccurredAtUtc,LegacyCustomerId,CustomerName COLLATE Latin1_General_100_BIN2,
 LegacyUserId,EmployeeName COLLATE Latin1_General_100_BIN2,SourceTotal,SourcePaymentFlag,
 HeaderJson COLLATE Latin1_General_100_BIN2,DetailsJson COLLATE Latin1_General_100_BIN2
 FROM dbo.LegacyReturnArchives WHERE StoreId=1
) OR EXISTS(
 SELECT LegacyOrderId,OccurredAtUtc,LegacyCustomerId,CustomerName COLLATE Latin1_General_100_BIN2,
 LegacyUserId,EmployeeName COLLATE Latin1_General_100_BIN2,SourceTotal,SourcePaymentFlag,
 HeaderJson COLLATE Latin1_General_100_BIN2,DetailsJson COLLATE Latin1_General_100_BIN2
 FROM dbo.LegacyReturnArchives WHERE StoreId=1
 EXCEPT
 SELECT LegacyOrderId,OccurredAtUtc,LegacyCustomerId,CustomerName COLLATE Latin1_General_100_BIN2,
 LegacyUserId,EmployeeName COLLATE Latin1_General_100_BIN2,SourceTotal,SourcePaymentFlag,
 HeaderJson COLLATE Latin1_General_100_BIN2,DetailsJson COLLATE Latin1_General_100_BIN2 FROM #ArchivePlan
) THROW 55303,'Return archive differs from the complete source snapshot.',1;
SELECT N'RETURN_ARCHIVE_EXACT_PASS' Report,COUNT_BIG(*) Headers FROM dbo.LegacyReturnArchives WHERE StoreId=1;
