namespace GaoApp.Infrastructure.Data.Migrations;

public static partial class StorefrontCancellationSchemaSqlCatalog
{
    private static readonly int[] PayerWhitespace = [9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288];
    private static string UnsupportedPayerCharactersSql => "PATINDEX(N'%[^ -~'+" + string.Join('+',PayerWhitespace.Select(c=>$"NCHAR({c})")) + "+N']%',CONCAT(b.BankCode,b.AccountNumber) COLLATE Latin1_General_100_BIN2)>0";
    private static string PayerIdentitySql
    {
        get
        {
            var canonical = "CONCAT(b.BankCode,N'|',b.AccountNumber)";
            foreach (var codepoint in PayerWhitespace) canonical = $"REPLACE({canonical},NCHAR({codepoint}),N'')";
            return $"CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),UPPER({canonical} COLLATE Latin1_General_100_BIN2) COLLATE Latin1_General_100_BIN2_UTF8)),2)";
        }
    }
    private static string PayoutDefinition => $$"""
CREATE TRIGGER [dbo].[TR_StorefrontRefundEvidence_SourceV7] ON [dbo].[StorefrontRefundEvidence] AFTER INSERT AS BEGIN
 SET NOCOUNT ON;
 DECLARE @parent int; SELECT @parent=o.Id FROM StorefrontOrders o WITH(UPDLOCK,HOLDLOCK) JOIN inserted i ON i.StoreId=o.StoreId AND i.StorefrontOrderId=o.Id;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN StorefrontRefundFundings f ON f.StoreId=i.StoreId AND f.Id=i.FundingId AND f.RefundId=i.RefundId AND f.StorefrontOrderId=i.StorefrontOrderId
   LEFT JOIN StorefrontRefunds r ON r.StoreId=f.StoreId AND r.Id=f.RefundId
   LEFT JOIN StorefrontOrders o ON o.StoreId=f.StoreId AND o.Id=f.StorefrontOrderId
   WHERE f.Id IS NULL OR r.State<>2 OR o.Phase<>6 OR f.ReceivingLegalEntityId IS NULL OR i.PayerLegalEntityId<>f.ReceivingLegalEntityId
   OR i.Amount<=0 OR i.Amount<>ROUND(i.Amount,0) OR i.Amount>=10000000000000000 OR i.IsDeleted<>0
   OR i.SourceSnapshotJson IS NULL OR ISJSON(i.SourceSnapshotJson)<>1 OR LEFT(LTRIM(REPLACE(REPLACE(REPLACE(i.SourceSnapshotJson,NCHAR(9),N''),NCHAR(10),N''),NCHAR(13),N'')),1)<>N'{'
   OR i.BankReceiptId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM StorefrontBankReceipts b WHERE b.StoreId=i.StoreId AND b.Id=i.BankReceiptId AND b.GlobalBankReceiptId=f.GlobalBankReceiptId)
   OR i.EvidenceFingerprint<>CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),CONCAT(i.SourceSnapshotJson,N'|',i.EvidenceReference) COLLATE Latin1_General_100_BIN2_UTF8)),2)
   OR NOT EXISTS(SELECT 1 FROM UserInStores u JOIN Users a ON a.Id=u.UserId WHERE u.StoreId=i.StoreId AND u.UserId=i.ConfirmedByUserId AND u.IsActive=1 AND u.IsDeleted=0 AND a.IsActive=1 AND a.IsDeleted=0)
   OR NOT EXISTS(SELECT 1 FROM LegalEntities l WHERE l.StoreId=i.StoreId AND l.Id=i.PayerLegalEntityId AND l.IsActive=1 AND l.IsDeleted=0)
   OR NOT EXISTS(SELECT 1 FROM StorefrontRefundDecisions d WHERE d.StoreId=i.StoreId AND d.RefundId=i.RefundId AND d.Kind=1 AND NOT EXISTS(SELECT 1 FROM StorefrontRefundDecisions n WHERE n.StoreId=d.StoreId AND n.RefundId=d.RefundId AND n.Id>d.Id))
   OR NOT ({{NoDelivered}}) OR NOT ({{NoIncident}}) OR ((NOT ({{NoChildCustody}}) OR NOT ({{NoDispatch}})) AND NOT (({{CompleteDispatch}}) AND ({{FullyReturned}}))))
 THROW 51300,'Actual refund requires approved outstanding source, real actor and completed safe custody.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN StorefrontRefundFundings f ON f.StoreId=i.StoreId AND f.Id=i.FundingId
   WHERE (SELECT ISNULL(SUM(e.Amount),0) FROM StorefrontRefundEvidence e WHERE e.StoreId=i.StoreId AND e.FundingId=f.Id)>f.Amount
   OR (SELECT ISNULL(SUM(e.Amount),0) FROM StorefrontRefundEvidence e WHERE e.StoreId=i.StoreId AND e.RefundId=i.RefundId)>(SELECT Amount FROM StorefrontRefunds WHERE StoreId=i.StoreId AND Id=i.RefundId)
   OR EXISTS(SELECT 1 FROM StorefrontCancellationRequests q WHERE q.StoreId=i.StoreId AND q.StorefrontOrderId=i.StorefrontOrderId AND q.IsImmediate=0 AND NOT EXISTS(SELECT 1 FROM StorefrontCancellationDecisions d WHERE d.StoreId=q.StoreId AND d.CancellationRequestId=q.Id)))
 THROW 51300,'Refund executions cannot exceed the one actual incoming source or unresolved custody request.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN StorefrontRefundFundings f ON f.StoreId=i.StoreId AND f.Id=i.FundingId
   LEFT JOIN BankVerifiedReceipts b ON b.Id=f.GlobalBankReceiptId AND b.OriginStoreId=f.StoreId
   WHERE b.Id IS NULL OR b.AssetConfidence<>1 OR b.Amount<>f.Amount OR b.IsDeleted<>0
   OR EXISTS(SELECT 1 FROM BankReceiptConflicts c WHERE c.BankVerifiedReceiptId=b.Id)
   OR f.Kind=2 AND (b.TimePrecision=0 OR b.RoutingProvenance<>1)
   OR f.Kind=1 AND (b.RoutingProvenance<>2 OR b.Channel<>2 OR EXISTS(SELECT 1 FROM StorefrontPaymentIntents p JOIN StorefrontBankReviewRestrictions c ON c.StoreId=p.StoreId AND c.PaymentIntentId=p.Id WHERE p.StoreId=f.StoreId AND p.StorefrontOrderId=f.StorefrontOrderId)
     OR EXISTS(SELECT 1 FROM StorefrontPaymentIntents p JOIN StorefrontUnattributedBankReceipts c ON c.StoreId=p.StoreId AND c.PaymentIntentId=p.Id WHERE p.StoreId=f.StoreId AND p.StorefrontOrderId=f.StorefrontOrderId)))
 THROW 51300,'Incoming confidence, routing or bank restrictions do not permit actual refund execution.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN StorefrontRefundFundings f ON f.StoreId=i.StoreId AND f.Id=i.FundingId JOIN StorefrontPaymentIntents p ON p.StoreId=f.StoreId AND p.StorefrontOrderId=f.StorefrontOrderId
   JOIN StorefrontBankScanAttempts a ON a.StoreId=p.StoreId AND a.PaymentIntentId=p.Id
   WHERE f.Kind=1 AND NOT EXISTS(SELECT 1 FROM StorefrontBankScanAttempts n WHERE n.StoreId=a.StoreId AND n.PaymentIntentId=a.PaymentIntentId AND n.Id>a.Id)
     AND NOT EXISTS(SELECT 1 FROM StorefrontBankScanCompletions c WHERE c.StoreId=a.StoreId AND c.ScanAttemptId=a.Id))
 THROW 51300,'The newest bank retrieval is incomplete; retain the liability without claiming a payout.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN StorefrontRefundFundings f ON f.StoreId=i.StoreId AND f.Id=i.FundingId JOIN BankVerifiedReceipts b ON b.Id=f.GlobalBankReceiptId AND b.OriginStoreId=f.StoreId
   WHERE i.ExecutedAtUtc<CASE WHEN b.TimePrecision=2 THEN b.OccurredAtUtc WHEN b.TimePrecision=1 AND DATEADD(day,1,b.OccurredAtUtc)<b.VerifiedAtUtc THEN DATEADD(day,1,b.OccurredAtUtc) ELSE b.VerifiedAtUtc END)
 THROW 51300,'Payout precedes the actual timestamp or the proven bank-day/observation bound.',1;
 IF EXISTS(SELECT 1 FROM inserted i
   LEFT JOIN POSShiftCashTransactions c ON c.StoreId=i.StoreId AND c.Id=i.POSShiftCashTransactionId
   LEFT JOIN TreasuryEntries t ON t.StoreId=i.StoreId AND t.Id=i.TreasuryEntryId
   OUTER APPLY OPENJSON(i.SourceSnapshotJson) WITH(canonicalSourceIdentity nvarchar(100),fund nvarchar(10),amount decimal(18,2),
      treasuryEntryId int,cashTransactionId int,payerLegalEntityId int,payerBankAccountId int,verificationMethod nvarchar(50))s
   WHERE s.amount IS NULL OR s.amount<>i.Amount OR s.fund IS NULL OR s.fund COLLATE Latin1_General_100_BIN2<>i.Fund COLLATE Latin1_General_100_BIN2
   OR s.payerLegalEntityId IS NULL OR s.payerLegalEntityId<>i.PayerLegalEntityId
   OR ISNULL(s.treasuryEntryId,0)<>ISNULL(i.TreasuryEntryId,0) OR ISNULL(s.cashTransactionId,0)<>ISNULL(i.POSShiftCashTransactionId,0)
   OR ISNULL(s.payerBankAccountId,0)<>ISNULL(i.PayerBankAccountId,0) OR s.verificationMethod IS NULL OR s.verificationMethod<>i.VerificationMethod
   OR s.canonicalSourceIdentity IS NULL OR s.canonicalSourceIdentity COLLATE Latin1_General_100_BIN2<>
     CASE WHEN c.Id IS NOT NULL THEN CONCAT(N'CASH:',i.StoreId,N':',c.Id) ELSE CONCAT(N'TREASURY:',i.StoreId,N':',t.Id) END COLLATE Latin1_General_100_BIN2
   OR i.POSShiftCashTransactionId IS NOT NULL AND (c.Id IS NULL OR c.IsDeleted<>0 OR c.Type<>2 OR c.Amount<>i.Amount OR c.CustomerDebtReceiptId IS NOT NULL OR c.CustomerDepositEntryId IS NOT NULL OR c.Reason IN(N'Thu công nợ khách hàng',N'Nhận cọc khách hàng',N'Hoàn cọc khách hàng') OR i.Fund<>N'cash' OR i.PayerBankAccountId IS NOT NULL OR i.ExecutedAtUtc<>c.CreatedAtUtc
     OR EXISTS(SELECT 1 FROM POSCashAdjustmentRequests a WHERE a.StoreId=c.StoreId AND a.TransactionId=c.Id AND a.Status=0)
     OR EXISTS(SELECT 1 FROM TreasuryEntries a WHERE a.StoreId=c.StoreId AND a.POSShiftCashTransactionId=c.Id AND (i.TreasuryEntryId IS NULL OR a.Id<>i.TreasuryEntryId)))
   OR i.TreasuryEntryId IS NOT NULL AND (t.Id IS NULL OR t.IsDeleted<>0 OR t.TargetFund IS NOT NULL OR t.ReversalOfId IS NOT NULL OR t.ReversedAtUtc IS NOT NULL OR t.OperatingExpenseId IS NOT NULL OR t.PurchasePayableId IS NOT NULL OR t.Amount<>-i.Amount OR t.Fund<>i.Fund
     OR EXISTS(SELECT 1 FROM TreasuryEntries a WHERE a.StoreId=t.StoreId AND a.ReversalOfId=t.Id)
     OR ISNULL(t.POSShiftCashTransactionId,0)<>ISNULL(i.POSShiftCashTransactionId,0)
     OR t.IsVoucherLink<>CASE WHEN i.POSShiftCashTransactionId IS NULL THEN 0 ELSE 1 END
     OR i.POSShiftCashTransactionId IS NULL AND CONVERT(date,DATEADD(hour,7,t.OccurredAtUtc))<>CONVERT(date,DATEADD(hour,7,i.ExecutedAtUtc)))
   OR i.POSShiftCashTransactionId IS NULL AND i.TreasuryEntryId IS NULL
   OR i.PayoutSourceKind=1 AND i.POSShiftCashTransactionId IS NULL OR i.PayoutSourceKind=2 AND i.TreasuryEntryId IS NULL)
 THROW 51300,'Refund evidence claims the full real debit, exact cash/treasury alias and immutable payer/source snapshot.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN POSShiftCashTransactions c ON c.StoreId=i.StoreId AND c.Id=i.POSShiftCashTransactionId
   LEFT JOIN TreasuryEntries t ON t.StoreId=i.StoreId AND t.Id=i.TreasuryEntryId LEFT JOIN StoreBankAccounts b ON b.StoreId=i.StoreId AND b.Id=i.PayerBankAccountId
   OUTER APPLY OPENJSON(i.SourceSnapshotJson)WITH(cashVersion nvarchar(24),treasuryVersion nvarchar(24),payerAccountVersion nvarchar(24))v
   WHERE c.Id IS NOT NULL AND (v.cashVersion IS NULL OR DATALENGTH(v.cashVersion)<>24 OR CAST(N'<v>'+ISNULL(v.cashVersion,N'')+N'</v>' AS xml).value('xs:base64Binary(/v[1])','varbinary(8)')<>c.RowVersion)
   OR t.Id IS NOT NULL AND (v.treasuryVersion IS NULL OR DATALENGTH(v.treasuryVersion)<>24 OR CAST(N'<v>'+ISNULL(v.treasuryVersion,N'')+N'</v>' AS xml).value('xs:base64Binary(/v[1])','varbinary(8)')<>t.RowVersion)
   OR b.Id IS NOT NULL AND (v.payerAccountVersion IS NULL OR DATALENGTH(v.payerAccountVersion)<>24 OR CAST(N'<v>'+ISNULL(v.payerAccountVersion,N'')+N'</v>' AS xml).value('xs:base64Binary(/v[1])','varbinary(8)')<>b.RowVersion))
 THROW 51300,'Payout source/cash/treasury/payer versions must be the locked actual versions.',1;
 IF EXISTS(SELECT 1 FROM inserted i WHERE i.Fund=N'cash' AND (i.PayerBankAccountId IS NOT NULL OR i.ExternalPayoutKey COLLATE Latin1_General_100_BIN2<>
   CASE WHEN i.POSShiftCashTransactionId IS NOT NULL THEN CONCAT(N'CASH:',i.StoreId,N':',i.POSShiftCashTransactionId) ELSE CONCAT(N'TREASURY:',i.StoreId,N':',i.TreasuryEntryId) END COLLATE Latin1_General_100_BIN2))
 THROW 51300,'Equivalent cash and linked treasury sources share one canonical payout identity.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN StoreBankAccounts b ON b.StoreId=i.StoreId AND b.Id=i.PayerBankAccountId
   WHERE i.Fund=N'bank' AND (b.Id IS NULL OR b.IsDeleted<>0 OR b.IsActive<>1 OR {{UnsupportedPayerCharactersSql}} OR i.POSShiftCashTransactionId IS NOT NULL OR i.VerificationMethod<>N'STAFF_VERIFIED_BANK_DEBIT'
     OR JSON_VALUE(i.SourceSnapshotJson,'$.externalTransactionId') IS NULL OR LEN(JSON_VALUE(i.SourceSnapshotJson,'$.externalTransactionId'))=0
     OR JSON_VALUE(i.SourceSnapshotJson,'$.payerAccountIdentity') IS NULL
     OR JSON_VALUE(i.SourceSnapshotJson,'$.payerAccountIdentity') COLLATE Latin1_General_100_BIN2<>{{PayerIdentitySql}} COLLATE Latin1_General_100_BIN2
     OR i.ExternalPayoutKey<>CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),CONCAT(N'BANK-REFUND|',JSON_VALUE(i.SourceSnapshotJson,'$.payerAccountIdentity'),N'|',JSON_VALUE(i.SourceSnapshotJson,'$.externalTransactionId')) COLLATE Latin1_General_100_BIN2_UTF8)),2)))
 THROW 51300,'Bank payout requires an explicit active payer and global physical-account/external-transaction claim.',1;
END;
""";
    private static string OrderDefinition => $$"""
CREATE TRIGGER [dbo].[TR_StorefrontOrders_CancellationV7] ON [dbo].[StorefrontOrders] AFTER INSERT,UPDATE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted o LEFT JOIN deleted old ON old.StoreId=o.StoreId AND old.Id=o.Id WHERE o.Phase=6 AND (old.Id IS NULL OR old.Phase<>6) AND
   (NOT ({{NoDelivered}}) OR NOT ({{NoIncident}}) OR EXISTS(SELECT 1 FROM InventoryReservations r WHERE r.StoreId=o.StoreId AND r.StorefrontOrderId=o.Id AND r.ReferenceType=12 AND r.Status=1) OR NOT (({{NoChildCustody}}) AND ({{NoDispatch}}) AND
     (EXISTS(SELECT 1 FROM StorefrontCancellationRequests r WHERE r.StoreId=o.StoreId AND r.StorefrontOrderId=o.Id AND r.IsImmediate=1)
       OR EXISTS(SELECT 1 FROM StorefrontCancellationDecisions d WHERE d.StoreId=o.StoreId AND d.StorefrontOrderId=o.Id AND d.Kind=1 AND d.AuthorisesPhysicalReturn=0)
       OR EXISTS(SELECT 1 FROM StorefrontQrClosureProofs p WHERE p.StoreId=o.StoreId AND p.StorefrontOrderId=o.Id)) OR (({{CompleteDispatch}}) AND ({{FullyReturned}})))))
 THROW 51300,'Cancellation requires no real dispatch plus authorisation, or all original dispatch slices actually returned.',1;
 IF EXISTS(SELECT 1 FROM inserted o JOIN deleted d ON d.StoreId=o.StoreId AND d.Id=o.Id WHERE d.Phase=6 AND o.Phase<>6)
 THROW 51300,'Cancelled orders cannot be reopened by late bank processing.',1;
 IF EXISTS(SELECT 1 FROM inserted o WHERE o.Phase=5 AND (EXISTS(SELECT 1 FROM StorefrontCancellationRequests r WHERE r.StoreId=o.StoreId AND r.StorefrontOrderId=o.Id AND r.IsImmediate=0 AND NOT EXISTS(SELECT 1 FROM StorefrontCancellationDecisions d WHERE d.StoreId=r.StoreId AND d.CancellationRequestId=r.Id))
   OR EXISTS(SELECT 1 FROM StorefrontCancellationDecisions d WHERE d.StoreId=o.StoreId AND d.StorefrontOrderId=o.Id AND (d.AuthorisesPhysicalReturn=1 OR d.Kind=3 AND NOT EXISTS(SELECT 1 FROM StorefrontCancellationReviewAcknowledgements a WHERE a.StoreId=d.StoreId AND a.CancellationDecisionId=d.Id)))))
 THROW 51300,'Unresolved cancellation, recall or actual-delivery review prevents financial completion.',1;
END;
""";
    private static string RefundDefinition => """
CREATE TRIGGER [dbo].[TR_StorefrontRefunds_ObligationV7] ON [dbo].[StorefrontRefunds] AFTER INSERT,UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT d.Id,d.StoreId,d.StorefrontOrderId,d.Amount,d.GoodsAmount,d.ShippingAmount,d.UnallocatedReceiptAmount,CONVERT(varbinary(max),d.Reason),DATALENGTH(d.Reason),d.IsDeleted FROM deleted d
   EXCEPT SELECT i.Id,i.StoreId,i.StorefrontOrderId,i.Amount,i.GoodsAmount,i.ShippingAmount,i.UnallocatedReceiptAmount,CONVERT(varbinary(max),i.Reason),DATALENGTH(i.Reason),i.IsDeleted FROM inserted i)
 THROW 51300,'Refund obligation/source amount and history cannot be erased or repriced.',1;
 IF EXISTS(SELECT 1 FROM inserted i WHERE (i.State=2 AND NOT EXISTS(SELECT 1 FROM StorefrontRefundDecisions d WHERE d.StoreId=i.StoreId AND d.RefundId=i.Id AND d.Kind=1 AND NOT EXISTS(SELECT 1 FROM StorefrontRefundDecisions n WHERE n.StoreId=d.StoreId AND n.RefundId=d.RefundId AND n.Id>d.Id)))
   OR i.State=3 AND (SELECT ISNULL(SUM(e.Amount),0) FROM StorefrontRefundEvidence e WHERE e.StoreId=i.StoreId AND e.RefundId=i.Id AND e.FundingId IS NOT NULL)<>i.Amount
   OR i.State=4 AND NOT EXISTS(SELECT 1 FROM StorefrontRefundDecisions d WHERE d.StoreId=i.StoreId AND d.RefundId=i.Id AND d.Kind=2 AND NOT EXISTS(SELECT 1 FROM StorefrontRefundDecisions n WHERE n.StoreId=d.StoreId AND n.RefundId=d.RefundId AND n.Id>d.Id)))
 THROW 51300,'Refund decisions remain append-only; executed status requires the exact real debit budget.',1;
END;
""";
    private const string DispatchDefinition = """
CREATE TRIGGER [dbo].[TR_InventoryTransactions_CancellationV7] ON [dbo].[InventoryTransactions] AFTER INSERT AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i JOIN DeliveryOrders c ON c.StoreId=i.StoreId AND i.ReferenceType=11 AND i.ReferenceId=CONVERT(nvarchar(50),c.Id)
   WHERE c.SourceKind=1 AND i.TransactionType=80 AND (EXISTS(SELECT 1 FROM StorefrontCancellationRequests r WHERE r.StoreId=c.StoreId AND r.StorefrontOrderId=c.StorefrontOrderId AND r.IsImmediate=0 AND NOT EXISTS(SELECT 1 FROM StorefrontCancellationDecisions d WHERE d.StoreId=r.StoreId AND d.CancellationRequestId=r.Id))
     OR EXISTS(SELECT 1 FROM StorefrontCancellationDecisions d WHERE d.StoreId=c.StoreId AND d.StorefrontOrderId=c.StorefrontOrderId AND d.AuthorisesPhysicalReturn=1)))
 THROW 51300,'Pending cancellation or authorised recall forbids another physical export.',1;
END;
""";
    private const string TreasuryDefinition = """
CREATE TRIGGER [dbo].[TR_TreasuryEntries_RefundClaimV7] ON [dbo].[TreasuryEntries] AFTER INSERT,UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i JOIN StorefrontRefundEvidence e ON e.StoreId=i.StoreId AND e.FundingId IS NOT NULL AND
   (e.TreasuryEntryId=i.Id OR e.POSShiftCashTransactionId=i.POSShiftCashTransactionId OR e.TreasuryEntryId=i.ReversalOfId
     OR EXISTS(SELECT 1 FROM TreasuryEntries original WHERE original.StoreId=i.StoreId AND original.Id=i.ReversalOfId AND original.POSShiftCashTransactionId=e.POSShiftCashTransactionId))
   WHERE i.IsDeleted<>0 OR i.TargetFund IS NOT NULL OR i.ReversalOfId IS NOT NULL OR i.ReversedAtUtc IS NOT NULL OR i.OperatingExpenseId IS NOT NULL OR i.PurchasePayableId IS NOT NULL
     OR i.Amount<>-e.Amount OR i.Fund<>e.Fund OR ISNULL(i.POSShiftCashTransactionId,0)<>ISNULL(e.POSShiftCashTransactionId,0)
     OR i.IsVoucherLink<>CASE WHEN e.POSShiftCashTransactionId IS NULL THEN 0 ELSE 1 END)
   OR EXISTS(SELECT 1 FROM deleted d JOIN StorefrontRefundEvidence e ON e.StoreId=d.StoreId AND e.FundingId IS NOT NULL AND (e.TreasuryEntryId=d.Id OR e.POSShiftCashTransactionId=d.POSShiftCashTransactionId) LEFT JOIN inserted i ON i.Id=d.Id
     WHERE i.Id IS NULL OR i.StoreId<>d.StoreId OR i.Amount<>d.Amount OR ISNULL(i.POSShiftCashTransactionId,0)<>ISNULL(d.POSShiftCashTransactionId,0) OR i.Fund<>d.Fund OR i.OccurredAtUtc<>d.OccurredAtUtc)
 THROW 51300,'Claimed refund debit cannot be reversed, transferred, reclassified or used for another expense.',1;
END;
""";
    private const string CashDefinition = """
CREATE TRIGGER [dbo].[TR_POSShiftCashTransactions_RefundClaimV7] ON [dbo].[POSShiftCashTransactions] AFTER UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT d.Id,d.StoreId,d.POSShiftId,d.Type,d.Amount,d.CreatedAtUtc,d.CreatedByUserId,d.CustomerDepositEntryId,d.CustomerDebtReceiptId,d.IsDeleted FROM deleted d
   WHERE EXISTS(SELECT 1 FROM StorefrontRefundEvidence e WHERE e.StoreId=d.StoreId AND e.POSShiftCashTransactionId=d.Id AND e.FundingId IS NOT NULL)
   EXCEPT SELECT i.Id,i.StoreId,i.POSShiftId,i.Type,i.Amount,i.CreatedAtUtc,i.CreatedByUserId,i.CustomerDepositEntryId,i.CustomerDebtReceiptId,i.IsDeleted FROM inserted i)
 THROW 51300,'Actual cash refund source and drawer facts are immutable after its claim.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN StorefrontRefundEvidence e ON e.StoreId=i.StoreId AND e.POSShiftCashTransactionId=i.Id AND e.FundingId IS NOT NULL
   WHERE i.Reason IN(N'Thu công nợ khách hàng',N'Nhận cọc khách hàng',N'Hoàn cọc khách hàng'))
 THROW 51300,'Claimed cash refund cannot be reclassified as a protected customer debt or deposit voucher.',1;
END;
""";
}

