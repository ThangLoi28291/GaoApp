using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Data.Migrations;

// Frozen WEB05 proof definitions. Never backfill, relabel or infer historical bank ownership.
public static class StorefrontQrBankSchemaSqlCatalog
{
    public const string MigrationId="20261031210000_AddStorefrontQrBankEvidence";
    public const string PreflightSql="""
IF EXISTS(SELECT 1 FROM dbo.AcbQrSessions WHERE IsDeleted=0 AND Status NOT IN(3,4))
 THROW 51160,'Resolve pending or review legacy ACB QR under the previous version before upgrading; historical account binding cannot be fabricated.',1;
IF EXISTS(SELECT 1 FROM dbo.StorefrontPaymentIntents) OR EXISTS(SELECT 1 FROM dbo.StorefrontBankReceipts) OR EXISTS(SELECT 1 FROM dbo.StorefrontPaymentAllocations)
 THROW 51160,'Existing website bank evidence must be reviewed before enabling the new global receipt registry; no automatic relabeling is allowed.',1;
""";
    public const string DownGuardSql="""
IF EXISTS(SELECT 1 FROM dbo.BankPaymentReferences) OR EXISTS(SELECT 1 FROM dbo.BankVerifiedReceipts) OR EXISTS(SELECT 1 FROM dbo.BankReceiptConflicts)
 OR EXISTS(SELECT 1 FROM dbo.BankReceiptConsumptions) OR EXISTS(SELECT 1 FROM dbo.StorefrontQrProcessing) OR EXISTS(SELECT 1 FROM dbo.StorefrontBankObservations)
 OR EXISTS(SELECT 1 FROM dbo.StorefrontUnattributedBankReceipts) OR EXISTS(SELECT 1 FROM dbo.AcbQrSessions WHERE BankBindingSnapshotJson IS NOT NULL)
 OR EXISTS(SELECT 1 FROM dbo.OrderPayments WHERE BankVerifiedReceiptId IS NOT NULL) OR EXISTS(SELECT 1 FROM dbo.AcbPaymentTransactions WHERE BankVerifiedReceiptId IS NOT NULL)
 OR EXISTS(SELECT 1 FROM dbo.StorefrontBankReceipts WHERE GlobalBankReceiptId IS NOT NULL)
 THROW 51161,'Bank identity, receipt or intent evidence exists; downgrade would destroy financial proof. Export and resolve it before downgrade.',1;
""";
    public const string ReplaceEventDropSql="DROP TRIGGER [dbo].[TR_StorefrontEvents_CheckoutPlaced];";
    private static string Columns(string alias,IEnumerable<string> columns)=>string.Join(',',columns.SelectMany(column=>new[]{$"CONVERT(varbinary(max),{alias}.[{column}])",$"DATALENGTH(CONVERT(varbinary(max),{alias}.[{column}]))"}));
    private static string Immutable(string table)=>$"CREATE TRIGGER [dbo].[TR_{table}_BankEvidence] ON [dbo].[{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51162,'Bank source evidence is append-only.',1; END;";
    public static string ReferenceSql=>"""
CREATE TRIGGER [dbo].[TR_BankPaymentReferences_BankEvidence] ON [dbo].[BankPaymentReferences] AFTER INSERT,UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted) THROW 51162,'Bank reference routing is immutable.',1;
 IF EXISTS(SELECT 1 FROM inserted r WHERE r.IsDeleted<>0 OR r.Provider COLLATE Latin1_General_100_BIN2<>N'ACB' OR DATALENGTH(r.Provider)<>6 OR DATALENGTH(r.AccountIdentity)<>128 OR
  (r.Channel=1 AND NOT EXISTS(SELECT 1 FROM PosPaymentQrRequests q WHERE q.StoreId=r.OriginStoreId AND q.Id=r.PosQrRequestId AND
      CONVERT(varbinary(max),q.RequestCode)=CONVERT(varbinary(max),r.Reference) AND DATALENGTH(q.RequestCode)=DATALENGTH(r.Reference))) OR
  (r.Channel=2 AND (DATALENGTH(r.Reference)<>26 OR LEFT(r.Reference,1) COLLATE Latin1_General_100_BIN2<>N'W' OR SUBSTRING(r.Reference,2,12) COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%' OR
      NOT EXISTS(SELECT 1 FROM StorefrontPaymentIntents p WHERE p.StoreId=r.OriginStoreId AND p.Id=r.StorefrontPaymentIntentId AND p.Provider=r.Provider AND
        CONVERT(varbinary(max),p.ProviderReference)=CONVERT(varbinary(max),r.Reference) AND DATALENGTH(p.ProviderReference)=DATALENGTH(r.Reference) AND
        JSON_VALUE(p.BindingSnapshotJson,'$.accountIdentity')=r.AccountIdentity AND r.NotificationIdentityMappingVerified=CASE WHEN JSON_VALUE(p.BindingSnapshotJson,'$.notificationIdentityMappingVerified')=N'true' THEN 1 ELSE 0 END))) OR
  (r.Channel=2 AND r.NotificationIdentityMappingVerified=0 AND
    (EXISTS(SELECT 1 FROM BankNotificationDevices d WHERE d.BankAccountFingerprint=r.AccountIdentity AND d.Enabled=1 AND d.IsDeleted=0) OR
     EXISTS(SELECT 1 FROM BankNotificationReceipts n JOIN BankNotificationDevices d ON d.Id=n.DeviceId WHERE n.PaymentId IS NOT NULL AND d.BankAccountFingerprint=r.AccountIdentity))))
 THROW 51162,'A bank reference must match its real immutable POS or website source.',1;
END;
""";
    public static string ReceiptSql
    {
        get
        {
            string[] fields=["Id","Provider","AccountIdentity","ExternalTransactionId","OriginStoreId","BankAccountId","ProviderReference","Channel","AssetConfidence","IdentityKind","CrossProtocolMappingVerified","Amount","Status","Fingerprint","ProtectedEvidence","RawBankTime","TimePrecision","BankBusinessDate","OccurredAtUtc","VerifiedAtUtc","CreatedAtUtc","CreatedBy","IsDeleted"];
            return $"""
CREATE TRIGGER [dbo].[TR_BankVerifiedReceipts_BankEvidence] ON [dbo].[BankVerifiedReceipts] AFTER INSERT,UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT {Columns("d",fields)} FROM deleted d EXCEPT SELECT {Columns("i",fields)} FROM inserted i)
 THROW 51163,'Verified bank identity, amount, time precision and protected evidence are immutable.',1;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.NeedsReview=1 AND
   (i.NeedsReview=0 OR d.ReviewCode IS NOT NULL AND d.ReviewCode<>N'BANK_TIME_UNKNOWN' AND (i.ReviewCode IS NULL OR i.ReviewCode=N'BANK_TIME_UNKNOWN')))
 THROW 51163,'A financial review latch cannot be cleared or downgraded by replay.',1;
 IF EXISTS(SELECT 1 FROM inserted i WHERE i.IsDeleted<>0 OR i.Amount<=0 OR i.AssetConfidence NOT BETWEEN 0 AND 3 OR i.AssetConfidence<>1 AND i.NeedsReview=0 OR i.IdentityKind NOT IN(0,1) OR DATALENGTH(i.AccountIdentity)<>128 OR DATALENGTH(i.ExternalTransactionId)=0 OR
   DATALENGTH(i.ExternalTransactionId)<>DATALENGTH(LTRIM(RTRIM(i.ExternalTransactionId))) OR
   NOT EXISTS(SELECT 1 FROM StoreBankAccounts b WHERE b.Id=i.BankAccountId AND b.StoreId=i.OriginStoreId) OR
   i.Channel=0 AND i.NeedsReview=0 OR i.TimePrecision=0 AND i.NeedsReview=0 OR
   i.Channel=2 AND NOT EXISTS(SELECT 1 FROM BankPaymentReferences r JOIN StorefrontPaymentIntents p ON p.Id=r.StorefrontPaymentIntentId AND p.StoreId=r.OriginStoreId
      WHERE r.Provider=i.Provider AND r.Reference=i.ProviderReference AND r.OriginStoreId=i.OriginStoreId AND r.Channel=2 AND p.BankAccountId=i.BankAccountId
        AND JSON_VALUE(p.BindingSnapshotJson,'$.accountIdentity')=i.AccountIdentity))
 THROW 51163,'Bank receipt receiving account and channel must have trusted evidence; unknowns require review.',1;
 UPDATE o SET PaymentState=3,Revision=Revision+1 FROM StorefrontOrders o JOIN StorefrontPaymentIntents p ON p.StoreId=o.StoreId AND p.StorefrontOrderId=o.Id
 JOIN inserted i ON i.OriginStoreId=p.StoreId AND i.Provider=p.Provider AND i.ProviderReference=p.ProviderReference COLLATE Latin1_General_100_BIN2 AND DATALENGTH(i.ProviderReference)=DATALENGTH(p.ProviderReference) WHERE i.NeedsReview=1 AND o.PaymentState<>3;
 UPDATE s SET Status=5,ReviewReason=N'Bằng chứng ngân hàng cần đối soát toàn cục.' FROM AcbQrSessions s JOIN inserted i ON i.OriginStoreId=s.StoreId AND i.ProviderReference=s.ProviderOrderId COLLATE Latin1_General_100_BIN2 AND DATALENGTH(i.ProviderReference)=DATALENGTH(s.ProviderOrderId)
 WHERE i.NeedsReview=1 AND (i.AssetConfidence<>1 OR i.ReviewCode IS NULL OR i.ReviewCode<>N'BANK_TIME_UNKNOWN') AND s.Status<>5;
END;
""";
        }
    }
    public static string ConsumptionSql=>"""
CREATE TRIGGER [dbo].[TR_BankReceiptConsumptions_BankEvidence] ON [dbo].[BankReceiptConsumptions] AFTER INSERT,UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted) THROW 51164,'A bank receipt consumption is immutable.',1;
 IF EXISTS(SELECT 1 FROM inserted c JOIN BankVerifiedReceipts r ON r.Id=c.BankVerifiedReceiptId WHERE c.IsDeleted<>0 OR c.OriginStoreId<>r.OriginStoreId OR c.Channel<>r.Channel OR r.AssetConfidence<>1 OR
   r.NeedsReview=1 AND NOT(c.Channel=1 AND r.ReviewCode=N'BANK_TIME_UNKNOWN') OR
   c.Channel=1 AND NOT EXISTS(SELECT 1 FROM OrderPayments p JOIN BankPaymentReferences k ON k.Provider=r.Provider AND k.Reference=r.ProviderReference AND k.Channel=1
     JOIN PosPaymentQrRequests q ON q.Id=k.PosQrRequestId AND q.StoreId=k.OriginStoreId
     WHERE p.Id=c.PosPaymentId AND p.StoreId=c.OriginStoreId AND p.BankVerifiedReceiptId=r.Id AND p.Amount=r.Amount AND p.Method=1
       AND p.ReferenceCode COLLATE Latin1_General_100_BIN2=r.ProviderReference AND DATALENGTH(p.ReferenceCode)=DATALENGTH(r.ProviderReference) AND p.OrderId=q.OrderId AND p.IsDeleted=0) OR
   c.Channel=2 AND NOT EXISTS(SELECT 1 FROM StorefrontPaymentAllocations a JOIN StorefrontBankReceipts b ON b.StoreId=a.StoreId AND b.Id=a.BankReceiptId
      JOIN StorefrontPaymentIntents p ON p.StoreId=a.StoreId AND p.Id=a.PaymentIntentId
      WHERE a.Id=c.StorefrontAllocationId AND a.StoreId=c.OriginStoreId AND b.GlobalBankReceiptId=r.Id AND a.Amount=r.Amount AND p.ProviderReference COLLATE Latin1_General_100_BIN2=r.ProviderReference AND DATALENGTH(p.ProviderReference)=DATALENGTH(r.ProviderReference)))
 THROW 51164,'Bank money can be consumed once, by the exact verified channel, order, account and amount.',1;
END;
""";
    public static string OrderPaymentSql=>"""
CREATE TRIGGER [dbo].[TR_OrderPayments_BankSource] ON [dbo].[OrderPayments] AFTER INSERT,UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE d.BankVerifiedReceiptId IS NOT NULL AND
   (i.Id IS NULL OR i.BankVerifiedReceiptId IS NULL OR i.BankVerifiedReceiptId<>d.BankVerifiedReceiptId OR i.StoreId<>d.StoreId OR i.OrderId<>d.OrderId OR
    i.Amount<>d.Amount OR i.Method<>d.Method OR i.IsDeleted<>d.IsDeleted OR CONVERT(varbinary(max),ISNULL(i.ReferenceCode,N''))<>CONVERT(varbinary(max),ISNULL(d.ReferenceCode,N'')) OR DATALENGTH(ISNULL(i.ReferenceCode,N''))<>DATALENGTH(ISNULL(d.ReferenceCode,N''))))
 THROW 51165,'An externally received bank payment mirror retains its source and amount.',1;
 IF EXISTS(SELECT 1 FROM inserted p JOIN BankVerifiedReceipts r ON r.Id=p.BankVerifiedReceiptId WHERE p.IsDeleted<>0 OR p.StoreId<>r.OriginStoreId OR p.Method<>1 OR p.Amount<=0 OR p.Amount>r.Amount OR r.AssetConfidence<>1 OR
   r.Channel=1 AND NOT EXISTS(SELECT 1 FROM BankPaymentReferences k JOIN PosPaymentQrRequests q ON q.Id=k.PosQrRequestId AND q.StoreId=k.OriginStoreId
      WHERE k.Provider=r.Provider AND k.Reference=r.ProviderReference AND k.Channel=1 AND k.OriginStoreId=p.StoreId AND q.OrderId=p.OrderId AND p.ReferenceCode COLLATE Latin1_General_100_BIN2=r.ProviderReference AND DATALENGTH(p.ReferenceCode)=DATALENGTH(r.ProviderReference) AND p.Amount=r.Amount) OR
   r.Channel=2 AND NOT EXISTS(SELECT 1 FROM BankReceiptConsumptions c JOIN StorefrontPaymentAllocations a ON a.Id=c.StorefrontAllocationId AND a.StoreId=c.OriginStoreId
     JOIN StorefrontSettlementLinks l ON l.StoreId=a.StoreId AND l.StorefrontOrderId=a.StorefrontOrderId AND l.SaleOrderId=p.OrderId
     WHERE c.BankVerifiedReceiptId=r.Id AND c.Channel=2 AND c.OriginStoreId=p.StoreId) OR r.Channel=0)
 THROW 51165,'A sale mirror requires the actual receipt consumption and real settlement source.',1;
 IF EXISTS(SELECT 1 FROM inserted p JOIN BankVerifiedReceipts r ON r.Id=p.BankVerifiedReceiptId WHERE
   (SELECT SUM(x.Amount) FROM OrderPayments x WHERE x.BankVerifiedReceiptId=r.Id AND x.IsDeleted=0)>r.Amount)
 THROW 51165,'Bank sale mirrors cannot exceed the original receipt budget.',1;
END;
""";
    public static string BankReceiptSourceSql=>"""
CREATE TRIGGER [dbo].[TR_StorefrontBankReceipts_GlobalSource] ON [dbo].[StorefrontBankReceipts] AFTER INSERT,UPDATE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE ISNULL(i.GlobalBankReceiptId,0)<>ISNULL(d.GlobalBankReceiptId,0))
 THROW 51166,'A website bank receipt cannot be relabeled to another global identity.',1;
 IF EXISTS(SELECT 1 FROM inserted b WHERE b.GlobalBankReceiptId IS NULL OR NOT EXISTS(SELECT 1 FROM BankVerifiedReceipts r JOIN BankPaymentReferences k
   ON k.Provider=r.Provider AND k.Reference=r.ProviderReference AND k.Channel=2 JOIN StorefrontPaymentIntents p ON p.Id=k.StorefrontPaymentIntentId AND p.StoreId=k.OriginStoreId
   WHERE r.Id=b.GlobalBankReceiptId AND r.OriginStoreId=b.StoreId AND r.Channel=2 AND r.BankAccountId=b.BankAccountId AND p.ReceivingLegalEntityId=b.ReceivingLegalEntityId
    AND r.Provider=b.Provider AND r.ExternalTransactionId=b.ExternalTransactionId COLLATE Latin1_General_100_BIN2 AND DATALENGTH(r.ExternalTransactionId)=DATALENGTH(b.ExternalTransactionId) AND r.Amount=b.Amount AND r.Fingerprint=b.EvidenceFingerprint AND r.OccurredAtUtc=b.OccurredAtUtc))
 THROW 51166,'Website receipt snapshots must identify the real global receiving account, owner, amount and bank date.',1;
END;
""";
    public static string AllocationSql=>"""
CREATE TRIGGER [dbo].[TR_StorefrontPaymentAllocations_BankSource] ON [dbo].[StorefrontPaymentAllocations] AFTER INSERT AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted a JOIN StorefrontBankReceipts b ON b.StoreId=a.StoreId AND b.Id=a.BankReceiptId
    JOIN StorefrontPaymentIntents p ON p.StoreId=a.StoreId AND p.Id=a.PaymentIntentId
    LEFT JOIN BankVerifiedReceipts r ON r.Id=b.GlobalBankReceiptId
    WHERE r.Id IS NULL OR r.AssetConfidence<>1 OR r.NeedsReview=1 OR b.NeedsReview=1 OR r.OriginStoreId<>a.StoreId OR r.Channel<>2 OR r.ProviderReference<>p.ProviderReference COLLATE Latin1_General_100_BIN2 OR DATALENGTH(r.ProviderReference)<>DATALENGTH(p.ProviderReference)
      OR r.BankAccountId<>p.BankAccountId OR r.AccountIdentity<>JSON_VALUE(p.BindingSnapshotJson,'$.accountIdentity') OR r.Amount<>a.Amount OR r.TimePrecision=0)
 THROW 51167,'A website payment allocation requires exact, dated, unresolved-review-free bank proof.',1;
END;
""";
    public static string IntentSql=>"""
CREATE TRIGGER [dbo].[TR_StorefrontPaymentIntents_BankBinding] ON [dbo].[StorefrontPaymentIntents] AFTER INSERT AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted p JOIN StorefrontOrders o ON o.StoreId=p.StoreId AND o.Id=p.StorefrontOrderId WHERE
   o.PaymentMethod<>1 OR o.Phase<>0 OR o.PaymentState<>1 OR p.Provider<>N'ACB' OR DATALENGTH(p.ProviderReference)<>26 OR
   LEFT(p.ProviderReference,1) COLLATE Latin1_General_100_BIN2<>N'W' OR SUBSTRING(p.ProviderReference,2,12) COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%' OR
   TRY_CONVERT(int,JSON_VALUE(p.BindingSnapshotJson,'$.bankAccountId')) IS NULL OR TRY_CONVERT(int,JSON_VALUE(p.BindingSnapshotJson,'$.bankAccountId'))<>p.BankAccountId OR
   TRY_CONVERT(int,JSON_VALUE(p.BindingSnapshotJson,'$.receivingLegalEntityId')) IS NULL OR TRY_CONVERT(int,JSON_VALUE(p.BindingSnapshotJson,'$.receivingLegalEntityId'))<>p.ReceivingLegalEntityId OR
   JSON_VALUE(p.BindingSnapshotJson,'$.accountIdentity') IS NULL OR LEN(JSON_VALUE(p.BindingSnapshotJson,'$.accountIdentity'))<>64 OR
   NULLIF(JSON_VALUE(p.BindingSnapshotJson,'$.registrationEvidence'),N'') IS NULL OR
   NULLIF(JSON_VALUE(p.BindingSnapshotJson,'$.merchantId'),N'') IS NULL OR LEN(JSON_VALUE(p.BindingSnapshotJson,'$.merchantId'))>30 OR
   NULLIF(JSON_VALUE(p.BindingSnapshotJson,'$.beneficiaryName'),N'') IS NULL OR LEN(JSON_VALUE(p.BindingSnapshotJson,'$.beneficiaryName'))>30 OR
   (JSON_VALUE(p.BindingSnapshotJson,'$.notificationIdentityMappingVerified')=N'true' AND NULLIF(JSON_VALUE(p.BindingSnapshotJson,'$.notificationIdentityMappingEvidence'),N'') IS NULL) OR
   (ISNULL(JSON_VALUE(p.BindingSnapshotJson,'$.notificationIdentityMappingVerified'),N'false')<>N'true' AND
    (EXISTS(SELECT 1 FROM BankNotificationDevices d WHERE d.BankAccountFingerprint=JSON_VALUE(p.BindingSnapshotJson,'$.accountIdentity') AND d.Enabled=1 AND d.IsDeleted=0) OR
     EXISTS(SELECT 1 FROM BankNotificationReceipts n JOIN BankNotificationDevices d ON d.Id=n.DeviceId WHERE n.PaymentId IS NOT NULL AND d.BankAccountFingerprint=JSON_VALUE(p.BindingSnapshotJson,'$.accountIdentity')))) OR
   NOT EXISTS(SELECT 1 FROM StorefrontFulfillmentSources s WHERE s.StoreId=p.StoreId AND s.StorefrontOrderId=p.StorefrontOrderId) OR
   EXISTS(SELECT 1 FROM StorefrontFulfillmentSources s WHERE s.StoreId=p.StoreId AND s.StorefrontOrderId=p.StorefrontOrderId AND
     (NOT EXISTS(SELECT 1 FROM OPENJSON(p.BindingSnapshotJson,'$.sourceLegalEntityIds') owners WHERE TRY_CONVERT(int,owners.value)=s.SourceLegalEntityId) OR
      NOT EXISTS(SELECT 1 FROM StorefrontBankCollectionAuthorities c WHERE c.StoreId=p.StoreId AND c.BankAccountId=p.BankAccountId AND c.ReceivingLegalEntityId=p.ReceivingLegalEntityId AND c.SourceLegalEntityId=s.SourceLegalEntityId AND c.IsActive=1 AND c.IsDeleted=0))) OR
   EXISTS(SELECT 1 FROM OPENJSON(p.BindingSnapshotJson,'$.sourceLegalEntityIds') owners WHERE NOT EXISTS(SELECT 1 FROM StorefrontFulfillmentSources s WHERE s.StoreId=p.StoreId AND s.StorefrontOrderId=p.StorefrontOrderId AND s.SourceLegalEntityId=TRY_CONVERT(int,owners.value))))
 THROW 51168,'QR binding requires the actual sealed allocation owners and their configured collection authority.',1;
END;
""";
    public static string OrderSql=>"""
CREATE TRIGGER [dbo].[TR_StorefrontOrders_QrPayment] ON [dbo].[StorefrontOrders] AFTER UPDATE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted o JOIN deleted d ON d.Id=o.Id WHERE o.PaymentMethod=1 AND
    (o.PaymentState=2 AND d.PaymentState<>2 OR o.Phase<>d.Phase AND o.Phase IN(1,2,3,4,5)) AND
    (o.PaymentState<>2 OR NOT EXISTS(SELECT 1 FROM StorefrontPaymentAllocations a JOIN StorefrontBankReceipts b ON b.Id=a.BankReceiptId AND b.StoreId=a.StoreId
      JOIN BankVerifiedReceipts r ON r.Id=b.GlobalBankReceiptId JOIN BankReceiptConsumptions c ON c.BankVerifiedReceiptId=r.Id AND c.StorefrontAllocationId=a.Id AND c.Channel=2
      WHERE a.StoreId=o.StoreId AND a.StorefrontOrderId=o.Id AND a.Amount=o.TotalDue AND r.AssetConfidence=1 AND r.NeedsReview=0) OR
      EXISTS(SELECT 1 FROM StorefrontPaymentIntents p JOIN BankVerifiedReceipts r ON r.OriginStoreId=p.StoreId AND r.ProviderReference=p.ProviderReference COLLATE Latin1_General_100_BIN2 AND DATALENGTH(r.ProviderReference)=DATALENGTH(p.ProviderReference) WHERE p.StoreId=o.StoreId AND p.StorefrontOrderId=o.Id AND r.NeedsReview=1)))
 THROW 51169,'QR payment and preparation require a committed exact bank allocation and no unresolved review.',1;
END;
""";
    public static string ProcessingSql=>"""
CREATE TRIGGER [dbo].[TR_StorefrontQrProcessing_Binding] ON [dbo].[StorefrontQrProcessing] AFTER INSERT,UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL OR i.StoreId<>d.StoreId OR i.PaymentIntentId<>d.PaymentIntentId OR i.IsDeleted<>d.IsDeleted OR
   CONVERT(varbinary(max),i.TraceNumber)<>CONVERT(varbinary(max),d.TraceNumber) OR DATALENGTH(i.TraceNumber)<>DATALENGTH(d.TraceNumber) OR
   d.ProviderTraceNumber<>N'' AND (CONVERT(varbinary(max),i.ProviderTraceNumber)<>CONVERT(varbinary(max),d.ProviderTraceNumber) OR DATALENGTH(i.ProviderTraceNumber)<>DATALENGTH(d.ProviderTraceNumber)) OR
   d.VirtualAccount<>N'' AND (CONVERT(varbinary(max),i.VirtualAccount)<>CONVERT(varbinary(max),d.VirtualAccount) OR DATALENGTH(i.VirtualAccount)<>DATALENGTH(d.VirtualAccount)))
 THROW 51170,'QR processing preserves the original request, provider trace and virtual account.',1;
 IF EXISTS(SELECT 1 FROM inserted WHERE State NOT BETWEEN 0 AND 5 OR Attempts<0 OR IsDeleted<>0 OR DATALENGTH(QrPng)>262144)
 THROW 51170,'QR processing exceeds its reviewed bounds.',1;
END;
""";
    public static string AccountSql=>"""
CREATE TRIGGER [dbo].[TR_StoreBankAccounts_StorefrontBinding] ON [dbo].[StoreBankAccounts] AFTER UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT 1 FROM StorefrontPaymentIntents p WHERE p.StoreId=d.StoreId AND p.BankAccountId=d.Id AND p.IsActive=1) AND
   (i.Id IS NULL OR i.StoreId<>d.StoreId OR i.IsActive<>d.IsActive OR i.IsDeleted<>d.IsDeleted OR CONVERT(varbinary(max),i.BankCode)<>CONVERT(varbinary(max),d.BankCode) OR DATALENGTH(i.BankCode)<>DATALENGTH(d.BankCode) OR
    CONVERT(varbinary(max),i.AccountNumber)<>CONVERT(varbinary(max),d.AccountNumber) OR DATALENGTH(i.AccountNumber)<>DATALENGTH(d.AccountNumber)))
 THROW 51150,'Resolve active website QR before changing its physical receiving account.',1;
END;
""";
    public static string NotificationDeviceSql=>"""
CREATE TRIGGER [dbo].[TR_BankNotificationDevices_StorefrontIdentity] ON [dbo].[BankNotificationDevices] AFTER INSERT,UPDATE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted d WHERE d.Enabled=1 AND d.IsDeleted=0 AND
   EXISTS(SELECT 1 FROM BankPaymentReferences r WHERE r.AccountIdentity=d.BankAccountFingerprint AND r.Channel=2 AND r.NotificationIdentityMappingVerified=0) AND
   NOT EXISTS(SELECT 1 FROM BankPaymentReferences r WHERE r.AccountIdentity=d.BankAccountFingerprint AND r.Channel=2 AND r.NotificationIdentityMappingVerified=1))
 THROW 51173,'This physical account has website payment evidence but no verified cross-protocol transaction identity; use a separate receiving account or resolve registration first.',1;
END;
""";
    public static string AcbSettingsSql
    {
        get
        {
            string[] fields=["Id","StoreId","IsDeleted","Enabled","BankAccountId","TokenEndpoint","ApiBaseUrl","QrEndpoint","ClientId","TokenScope","XService","XProviderId","XOwnerNumber","XOwnerType","VirtualAccountPrefix","MerchantId","BeneficiaryName"];
            return $"CREATE TRIGGER [dbo].[TR_StoreAcbSettings_StorefrontBinding] ON [dbo].[StoreAcbSettings] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT {Columns("d",fields)} FROM deleted d WHERE EXISTS(SELECT 1 FROM StorefrontPaymentIntents p WHERE p.StoreId=d.StoreId AND p.IsActive=1) EXCEPT SELECT {Columns("i",fields)} FROM inserted i WHERE EXISTS(SELECT 1 FROM StorefrontPaymentIntents p WHERE p.StoreId=i.StoreId AND p.IsActive=1)) THROW 51150,'Resolve active website QR before changing its bank environment or channel binding; credential rotation remains available.',1; END;";
        }
    }
    public static string PosBindingSql=>"""
CREATE TRIGGER [dbo].[TR_AcbQrSessions_BankBinding] ON [dbo].[AcbQrSessions] AFTER UPDATE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE
  (d.BankBindingSnapshotJson IS NULL AND i.BankBindingSnapshotJson IS NOT NULL) OR
  (d.BankBindingSnapshotJson IS NOT NULL AND (i.BankBindingSnapshotJson IS NULL OR CONVERT(varbinary(max),i.BankBindingSnapshotJson)<>CONVERT(varbinary(max),d.BankBindingSnapshotJson) OR DATALENGTH(i.BankBindingSnapshotJson)<>DATALENGTH(d.BankBindingSnapshotJson))))
 THROW 51171,'Original bank-account binding cannot be rewritten or inferred for legacy POS history.',1;
END;
""";
    public static string RouteSql=>"""
CREATE TRIGGER [dbo].[TR_AcbCallbackRoutes_StorefrontBinding] ON [dbo].[AcbCallbackRoutes] AFTER UPDATE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.TargetStoreId IS NOT NULL AND i.TargetStoreId IS NOT NULL AND i.TargetStoreId<>d.TargetStoreId AND
  (EXISTS(SELECT 1 FROM StorefrontPaymentIntents p WHERE p.StoreId=d.TargetStoreId AND p.IsActive=1) OR EXISTS(SELECT 1 FROM BankVerifiedReceipts r WHERE r.OriginStoreId=d.TargetStoreId AND r.NeedsReview=1)))
 THROW 51172,'A callback tenant with active website QR or unresolved bank evidence cannot be rerouted.',1;
END;
""";
    public static string EventSql
    {
        get
        {
            var original=StorefrontCheckoutSchemaSqlCatalog.EventSql;
            const string old="o.Phase<>1 OR o.PaymentMethod<>0 OR o.PaymentState<>0 OR i.Revision<>1";
            const string replacement="(o.PaymentMethod=0 AND (o.Phase<>1 OR o.PaymentState<>0) OR o.PaymentMethod=1 AND (o.Phase<>0 OR o.PaymentState<>1 OR NOT EXISTS(SELECT 1 FROM StorefrontPaymentIntents p JOIN StorefrontQrProcessing q ON q.StoreId=p.StoreId AND q.PaymentIntentId=p.Id JOIN BankPaymentReferences r ON r.StorefrontPaymentIntentId=p.Id AND r.OriginStoreId=p.StoreId AND r.Channel=2 WHERE p.StoreId=o.StoreId AND p.StorefrontOrderId=o.Id AND p.AmountDue=o.TotalDue AND p.ExpiresAtUtc=o.PaymentExpiresAtUtc))) OR o.PaymentMethod NOT IN(0,1) OR i.Revision<>1";
            if(!original.Contains(old,StringComparison.Ordinal))throw new InvalidOperationException("Unexpected trusted WEB04 event guard.");
            return original.Replace(old,replacement,StringComparison.Ordinal).Replace("COD order-created proof","Checkout order-created proof",StringComparison.Ordinal);
        }
    }
    public static IReadOnlyList<(string Table,string Name,string Sql)> Triggers=>[
        ("BankPaymentReferences","TR_BankPaymentReferences_BankEvidence",ReferenceSql),("BankVerifiedReceipts","TR_BankVerifiedReceipts_BankEvidence",ReceiptSql),
        ("BankReceiptConflicts","TR_BankReceiptConflicts_BankEvidence",Immutable("BankReceiptConflicts")),("BankReceiptConsumptions","TR_BankReceiptConsumptions_BankEvidence",ConsumptionSql),
        ("StorefrontBankObservations","TR_StorefrontBankObservations_BankEvidence",Immutable("StorefrontBankObservations")),("StorefrontUnattributedBankReceipts","TR_StorefrontUnattributedBankReceipts_BankEvidence",Immutable("StorefrontUnattributedBankReceipts")),
        ("StorefrontQrProcessing","TR_StorefrontQrProcessing_Binding",ProcessingSql),("StoreBankAccounts","TR_StoreBankAccounts_StorefrontBinding",AccountSql),
        ("StoreAcbSettings","TR_StoreAcbSettings_StorefrontBinding",AcbSettingsSql),("AcbQrSessions","TR_AcbQrSessions_BankBinding",PosBindingSql),("AcbCallbackRoutes","TR_AcbCallbackRoutes_StorefrontBinding",RouteSql),
        ("BankNotificationDevices","TR_BankNotificationDevices_StorefrontIdentity",NotificationDeviceSql),
        ("OrderPayments","TR_OrderPayments_BankSource",OrderPaymentSql),("StorefrontOrders","TR_StorefrontOrders_QrPayment",OrderSql),
        ("StorefrontBankReceipts","TR_StorefrontBankReceipts_GlobalSource",BankReceiptSourceSql),("StorefrontPaymentAllocations","TR_StorefrontPaymentAllocations_BankSource",AllocationSql),
        ("StorefrontPaymentIntents","TR_StorefrontPaymentIntents_BankBinding",IntentSql),("StorefrontEvents",StorefrontCheckoutSchemaSqlCatalog.EventTriggerName,EventSql)];
    internal static bool TryGetTrigger(string migrationId,SqlOperation operation,out(string Table,DatabaseTriggerSchema Trigger) result)
    {
        result=default;if(migrationId!=MigrationId||operation.SuppressTransaction)return false;
        foreach(var trigger in Triggers)if(trigger.Sql==operation.Sql){result=(trigger.Table,new(DatabaseSchemaNormalization.NormalizeIdentifier(trigger.Name),DeliverySchemaSqlCatalog.NormalizeDefinition(trigger.Sql),false,false,true,true));return true;}
        return false;
    }
}
