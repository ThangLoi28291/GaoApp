using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class StorefrontCancellationConfiguration : IEntityTypeConfiguration<StorefrontCancellationRequest>,
    IEntityTypeConfiguration<StorefrontCancellationDecision>, IEntityTypeConfiguration<StorefrontCancellationReviewAcknowledgement>,
    IEntityTypeConfiguration<StorefrontRefundFunding>, IEntityTypeConfiguration<StorefrontRefundDecision>,
    IEntityTypeConfiguration<StorefrontQrClosureProof>, IEntityTypeConfiguration<StorefrontBankReviewRestriction>,
    IEntityTypeConfiguration<StorefrontBankScanAttempt>, IEntityTypeConfiguration<StorefrontBankScanCompletion>,
    IEntityTypeConfiguration<StorefrontRefundEvidence>, IEntityTypeConfiguration<StorefrontFulfillmentEvidence>
{
    private static void Base<T>(EntityTypeBuilder<T> b, string table) where T : BaseStoreEntity
    {
        b.ToTable(table, t => { t.HasCheckConstraint("CK_" + table + "_Live", "[IsDeleted]=0"); t.HasTrigger("TR_" + table + "_Source"); t.HasTrigger("TR_" + table + "_Immutable"); });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.StoreId, x.Id });
        b.Property(x => x.RowVersion).IsRowVersion(); b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StorefrontOrder>().WithMany().HasForeignKey("StoreId", "StorefrontOrderId").HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
    private static void Link<T, P>(EntityTypeBuilder<T> b, string id) where T : class where P : BaseStoreEntity
        => b.HasOne<P>().WithMany().HasForeignKey("StoreId", id).HasPrincipalKey("StoreId", "Id").OnDelete(DeleteBehavior.Restrict);
    private static void Actor<T>(EntityTypeBuilder<T> b, string id = "ActorUserId") where T : class
        => b.HasOne<UserInStore>().WithMany().HasForeignKey("StoreId", id).HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
    private static void Check<T>(EntityTypeBuilder<T> b, string suffix, string expression) where T : class
        => b.ToTable(t => t.HasCheckConstraint("CK_" + b.Metadata.GetTableName() + "_" + suffix, expression));
    private static void Text<T>(EntityTypeBuilder<T> b, string name, int length, bool required = true) where T : class
        => b.Property<string>(name).HasMaxLength(length).IsRequired(required);
    private static void Refund<T>(EntityTypeBuilder<T> b) where T : class
        => b.HasOne<StorefrontRefund>().WithMany().HasForeignKey("StoreId", "RefundId", "StorefrontOrderId")
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId }).OnDelete(DeleteBehavior.Restrict);
    public void Configure(EntityTypeBuilder<StorefrontCancellationRequest> b)
    {
        Base(b, "StorefrontCancellationRequests"); b.HasAlternateKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId });
        Text(b, nameof(StorefrontCancellationRequest.GuestTokenHash), 64); Text(b, nameof(StorefrontCancellationRequest.Reason), 1000);
        b.HasIndex(x => new { x.StoreId, x.ClientRequestId }).IsUnique(); b.HasIndex(x => new { x.StoreId, x.StorefrontOrderId, x.IsImmediate });
        Check(b, "Request", "[ClientRequestId]<>'00000000-0000-0000-0000-000000000000' AND DATALENGTH([GuestTokenHash])=128 AND LEN([Reason])>0");
    }
    public void Configure(EntityTypeBuilder<StorefrontCancellationDecision> b)
    {
        Base(b, "StorefrontCancellationDecisions"); b.HasAlternateKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId }); Actor(b);
        b.HasOne<StorefrontCancellationRequest>().WithMany().HasForeignKey(x => new { x.StoreId, x.CancellationRequestId, x.StorefrontOrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StorefrontFulfillmentEvidence>().WithMany().HasForeignKey(x => new { x.StoreId, x.ActualDeliveryEvidenceId, x.StorefrontOrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StorefrontFulfillmentEvidence>().WithMany().HasForeignKey(x => new { x.StoreId, x.ActualReturnEvidenceId, x.StorefrontOrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.CancellationRequestId }).IsUnique(); Text(b, nameof(StorefrontCancellationDecision.Reason), 1000);
        Check(b, "Decision", "LEN([Reason])>0 AND (([Kind]=1 AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NULL) OR ([Kind]=2 AND [AuthorisesPhysicalReturn]=0 AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NULL) OR ([Kind]=3 AND [AuthorisesPhysicalReturn]=0 AND [ActualDeliveryEvidenceId] IS NOT NULL AND [ActualReturnEvidenceId] IS NULL) OR ([Kind]=4 AND [AuthorisesPhysicalReturn]=0 AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NOT NULL))");
    }
    public void Configure(EntityTypeBuilder<StorefrontCancellationReviewAcknowledgement> b)
    {
        Base(b, "StorefrontCancellationReviewAcknowledgements"); Actor(b); Text(b, nameof(StorefrontCancellationReviewAcknowledgement.Reason), 1000);
        b.HasOne<StorefrontCancellationDecision>().WithMany().HasForeignKey(x => new { x.StoreId, x.CancellationDecisionId, x.StorefrontOrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.CancellationDecisionId }).IsUnique(); Check(b, "Reason", "LEN([Reason])>0");
    }
    public void Configure(EntityTypeBuilder<StorefrontRefundFunding> b)
    {
        Base(b, "StorefrontRefundFundings"); Refund(b); Link<StorefrontRefundFunding, StorefrontCodReceipt>(b, "CodReceiptId");
        Link<StorefrontRefundFunding, LegalEntity>(b, "ReceivingLegalEntityId");
        b.HasOne<BankVerifiedReceipt>().WithMany().HasForeignKey(x => new { x.StoreId, x.GlobalBankReceiptId })
            .HasPrincipalKey(x => new { x.OriginStoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.RefundId, x.StorefrontOrderId });
        b.HasIndex(x => new { x.StoreId, x.RefundId }).IsUnique();
        b.HasIndex(x => x.GlobalBankReceiptId).IsUnique().HasFilter("[GlobalBankReceiptId] IS NOT NULL");
        b.HasIndex(x => x.CodReceiptId).IsUnique().HasFilter("[CodReceiptId] IS NOT NULL");
        b.Property(x => x.Amount).HasPrecision(18, 2); b.Property(x => x.SourceSnapshotJson).IsRequired();
        Check(b, "Amount", "[Amount]>0 AND [Amount]<10000000000000000 AND [Amount]=ROUND([Amount],0)");
        Check(b, "Source", "(([Kind]=1 AND [GlobalBankReceiptId] IS NOT NULL AND [CodReceiptId] IS NULL AND [ReceivingLegalEntityId] IS NOT NULL) OR ([Kind]=2 AND [CodReceiptId] IS NOT NULL)) AND ISJSON([SourceSnapshotJson])=1");
    }
    public void Configure(EntityTypeBuilder<StorefrontRefundDecision> b)
    {
        Base(b, "StorefrontRefundDecisions"); Refund(b); Actor(b); Text(b, nameof(StorefrontRefundDecision.Reason), 1000);
        b.HasIndex(x => new { x.StoreId, x.RefundId, x.Id }); Check(b, "Decision", "([Kind]=1 OR [Kind]=2) AND LEN([Reason])>0");
    }
    public void Configure(EntityTypeBuilder<StorefrontQrClosureProof> b)
    {
        Base(b, "StorefrontQrClosureProofs"); Link<StorefrontQrClosureProof, StorefrontPaymentIntent>(b, "PaymentIntentId");
        Link<StorefrontQrClosureProof, StorefrontBankScanCompletion>(b, "ScanCompletionId");
        Link<StorefrontQrClosureProof, StorefrontBankObservation>(b, "CancellationObservationId");
        Text(b, nameof(StorefrontQrClosureProof.ProviderReference), 13); Text(b, nameof(StorefrontQrClosureProof.AccountIdentity), 64);
        Text(b, nameof(StorefrontQrClosureProof.Fingerprint), 64); b.Property(x => x.ProtectedEvidence).IsRequired();
        b.HasIndex(x => new { x.StoreId, x.PaymentIntentId, x.Fingerprint }).IsUnique();
        Check(b, "Proof", "DATALENGTH([ProviderReference])=26 AND DATALENGTH([AccountIdentity])=128 AND DATALENGTH([Fingerprint])=128 AND DATALENGTH([ProtectedEvidence])>0");
    }
    public void Configure(EntityTypeBuilder<StorefrontBankReviewRestriction> b)
    {
        Base(b, "StorefrontBankReviewRestrictions"); Link<StorefrontBankReviewRestriction, StorefrontPaymentIntent>(b, "PaymentIntentId");
        Text(b, nameof(StorefrontBankReviewRestriction.ReasonCode), 100); Text(b, nameof(StorefrontBankReviewRestriction.EvidenceKind), 50);
        Text(b, nameof(StorefrontBankReviewRestriction.EvidenceFingerprint), 64); b.Property(x => x.ProtectedEvidence).IsRequired();
        b.HasIndex(x => new { x.StoreId, x.PaymentIntentId, x.ReasonCode, x.EvidenceFingerprint }).IsUnique();
        Check(b, "Evidence", "LEN([ReasonCode])>0 AND LEN([EvidenceKind])>0 AND DATALENGTH([EvidenceFingerprint])=128 AND DATALENGTH([ProtectedEvidence])>0");
    }
    public void Configure(EntityTypeBuilder<StorefrontBankScanAttempt> b)
    {
        Base(b, "StorefrontBankScanAttempts"); Link<StorefrontBankScanAttempt, StorefrontPaymentIntent>(b, "PaymentIntentId");
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId, x.PaymentIntentId });
        Text(b, nameof(StorefrontBankScanAttempt.AccountIdentity), 64); Text(b, nameof(StorefrontBankScanAttempt.BindingFingerprint), 64);
        b.Property(x => x.RequestedWindowsJson).IsRequired(); b.HasIndex(x => new { x.StoreId, x.PaymentIntentId, x.Id });
        Check(b, "Scope", "DATALENGTH([AccountIdentity])=128 AND DATALENGTH([BindingFingerprint])=128 AND ISJSON([RequestedWindowsJson])=1 AND JSON_QUERY([RequestedWindowsJson],'$[0]') IS NOT NULL");
    }
    public void Configure(EntityTypeBuilder<StorefrontBankScanCompletion> b)
    {
        Base(b, "StorefrontBankScanCompletions"); Link<StorefrontBankScanCompletion, StorefrontPaymentIntent>(b, "PaymentIntentId");
        b.HasOne<StorefrontBankScanAttempt>().WithMany().HasForeignKey(x => new { x.StoreId, x.ScanAttemptId, x.StorefrontOrderId, x.PaymentIntentId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId, x.PaymentIntentId }).OnDelete(DeleteBehavior.Restrict);
        Text(b, nameof(StorefrontBankScanCompletion.Fingerprint), 64); b.Property(x => x.ProtectedEvidence).IsRequired();
        b.HasIndex(x => new { x.StoreId, x.ScanAttemptId }).IsUnique(); b.HasIndex(x => new { x.StoreId, x.PaymentIntentId, x.Id });
        Check(b, "Evidence", "DATALENGTH([Fingerprint])=128 AND DATALENGTH([ProtectedEvidence])>0");
    }
    public void Configure(EntityTypeBuilder<StorefrontFulfillmentEvidence> b)
        => b.HasAlternateKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId });
    public void Configure(EntityTypeBuilder<StorefrontRefundEvidence> b)
    {
        b.ToTable("StorefrontRefundEvidence", t => t.HasTrigger("TR_StorefrontRefundEvidence_SourceV7"));
        b.HasOne<StorefrontRefundFunding>().WithMany().HasForeignKey(x => new { x.StoreId, x.FundingId, x.RefundId, x.StorefrontOrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.RefundId, x.StorefrontOrderId }).OnDelete(DeleteBehavior.Restrict);
        Link<StorefrontRefundEvidence, TreasuryEntry>(b, "TreasuryEntryId"); Link<StorefrontRefundEvidence, POSShiftCashTransaction>(b, "POSShiftCashTransactionId");
        Link<StorefrontRefundEvidence, StoreBankAccount>(b, "PayerBankAccountId"); Link<StorefrontRefundEvidence, LegalEntity>(b, "PayerLegalEntityId");
        Text(b, nameof(StorefrontRefundEvidence.Fund), 10, false); Text(b, nameof(StorefrontRefundEvidence.VerificationMethod), 50, false);
        Text(b, nameof(StorefrontRefundEvidence.EvidenceReference), 1000, false);
        b.HasIndex(x => x.TreasuryEntryId).IsUnique().HasFilter("[TreasuryEntryId] IS NOT NULL");
        b.HasIndex(x => x.POSShiftCashTransactionId).IsUnique().HasFilter("[POSShiftCashTransactionId] IS NOT NULL");
        Check(b, "NativePayout", "([FundingId] IS NULL AND [PayoutSourceKind] IS NULL AND [TreasuryEntryId] IS NULL AND [POSShiftCashTransactionId] IS NULL AND [PayerBankAccountId] IS NULL AND [PayerLegalEntityId] IS NULL AND [Fund] IS NULL AND [VerificationMethod] IS NULL AND [EvidenceReference] IS NULL AND [ProtectedPayoutEvidence] IS NULL AND [SourceSnapshotJson] IS NULL) OR ([FundingId] IS NOT NULL AND [PayerLegalEntityId] IS NOT NULL AND [Fund] IS NOT NULL AND ([Fund]=N'cash' OR [Fund]=N'bank') AND [VerificationMethod] IS NOT NULL AND [EvidenceReference] IS NOT NULL AND LEN([EvidenceReference])>0 AND [ProtectedPayoutEvidence] IS NOT NULL AND DATALENGTH([ProtectedPayoutEvidence])>0 AND [SourceSnapshotJson] IS NOT NULL AND ISJSON([SourceSnapshotJson])=1 AND LEFT(LTRIM(REPLACE(REPLACE(REPLACE([SourceSnapshotJson],NCHAR(9),N''),NCHAR(10),N''),NCHAR(13),N'')),1)=N'{' AND (([PayoutSourceKind]=1 AND [POSShiftCashTransactionId] IS NOT NULL) OR ([PayoutSourceKind]=2 AND [TreasuryEntryId] IS NOT NULL)))");
    }
}

