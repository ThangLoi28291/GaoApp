using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class BankPaymentEvidenceConfiguration : IEntityTypeConfiguration<BankPaymentReference>,
    IEntityTypeConfiguration<BankVerifiedReceipt>,IEntityTypeConfiguration<BankReceiptConflict>,IEntityTypeConfiguration<BankReceiptConsumption>,
    IEntityTypeConfiguration<StorefrontQrProcessing>,IEntityTypeConfiguration<StorefrontBankObservation>,IEntityTypeConfiguration<StorefrontUnattributedBankReceipt>
{
    private static void Base<T>(EntityTypeBuilder<T> b,string table) where T:BaseEntity
    {b.ToTable(table,t=>t.HasTrigger("TR_"+table+"_BankEvidence"));b.HasKey(x=>x.Id);b.Property(x=>x.RowVersion).IsRowVersion();b.Property(x=>x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");}
    public void Configure(EntityTypeBuilder<BankPaymentReference> b)
    {
        Base(b,"BankPaymentReferences");b.Property(x=>x.Provider).HasMaxLength(30).IsRequired();b.Property(x=>x.Reference).HasMaxLength(80).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.HasIndex(x=>new{x.Provider,x.Reference}).IsUnique();
        b.Property(x=>x.AccountIdentity).HasMaxLength(64).IsRequired();b.HasIndex(x=>new{x.AccountIdentity,x.Channel,x.NotificationIdentityMappingVerified});
        b.HasOne<Store>().WithMany().HasForeignKey(x=>x.OriginStoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PosPaymentQrRequest>().WithMany().HasForeignKey(x=>x.PosQrRequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StorefrontPaymentIntent>().WithMany().HasForeignKey(x=>x.StorefrontPaymentIntentId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t=>t.HasCheckConstraint("CK_BankPaymentReferences_Channel","([Channel]=1 AND [PosQrRequestId] IS NOT NULL AND [StorefrontPaymentIntentId] IS NULL) OR ([Channel]=2 AND [PosQrRequestId] IS NULL AND [StorefrontPaymentIntentId] IS NOT NULL)"));
    }
    public void Configure(EntityTypeBuilder<BankVerifiedReceipt> b)
    {
        Base(b,"BankVerifiedReceipts");b.Property(x=>x.Provider).HasMaxLength(30).IsRequired();b.Property(x=>x.AccountIdentity).HasMaxLength(64).IsRequired();
        b.Property(x=>x.ExternalTransactionId).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2").IsRequired();b.Property(x=>x.ProviderReference).HasMaxLength(300).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x=>x.Amount).HasPrecision(18,2);b.Property(x=>x.Status).HasMaxLength(50).IsRequired();b.Property(x=>x.Fingerprint).HasMaxLength(64).IsRequired();b.Property(x=>x.ProtectedEvidence).IsRequired();
        b.Property(x=>x.RawBankTime).HasMaxLength(100).IsRequired();b.Property(x=>x.BankBusinessDate).HasColumnType("date");b.Property(x=>x.ReviewCode).HasMaxLength(100);
        b.Property(x=>x.AssetConfidence).HasDefaultValue(BankReceiptAssetConfidence.Unproven);
        b.HasIndex(x=>new{x.Provider,x.AccountIdentity,x.ExternalTransactionId}).IsUnique();b.HasIndex(x=>new{x.OriginStoreId,x.ProviderReference});
        b.HasAlternateKey(x=>new{x.OriginStoreId,x.Id});
        b.HasOne<Store>().WithMany().HasForeignKey(x=>x.OriginStoreId).OnDelete(DeleteBehavior.Restrict);b.HasOne<StoreBankAccount>().WithMany().HasForeignKey(x=>x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t=>{t.HasCheckConstraint("CK_BankVerifiedReceipts_Amount","[Amount]>=0 AND [Amount]=ROUND([Amount],0)");t.HasCheckConstraint("CK_BankVerifiedReceipts_Time","([TimePrecision]=0 AND [OccurredAtUtc] IS NULL AND [BankBusinessDate] IS NULL) OR ([TimePrecision] IN (1,2) AND [OccurredAtUtc] IS NOT NULL AND [BankBusinessDate] IS NOT NULL)");});
    }
    public void Configure(EntityTypeBuilder<BankReceiptConflict> b)
    {
        Base(b,"BankReceiptConflicts");b.Property(x=>x.Fingerprint).HasMaxLength(64).IsRequired();b.Property(x=>x.ProtectedEvidence).IsRequired();b.Property(x=>x.AttemptedReference).HasMaxLength(300).IsRequired();
        b.Property(x=>x.ObservedAmount).HasPrecision(18,2);
        b.HasOne<BankVerifiedReceipt>().WithMany().HasForeignKey(x=>x.BankVerifiedReceiptId).OnDelete(DeleteBehavior.Restrict);b.HasIndex(x=>new{x.BankVerifiedReceiptId,x.Fingerprint,x.AttemptedStoreId}).IsUnique();
    }
    public void Configure(EntityTypeBuilder<BankReceiptConsumption> b)
    {
        Base(b,"BankReceiptConsumptions");b.HasOne<BankVerifiedReceipt>().WithMany().HasForeignKey(x=>new{x.OriginStoreId,x.BankVerifiedReceiptId}).HasPrincipalKey(x=>new{x.OriginStoreId,x.Id}).OnDelete(DeleteBehavior.Restrict);b.HasIndex(x=>x.BankVerifiedReceiptId).IsUnique();
        b.HasOne<OrderPayment>().WithMany().HasForeignKey(x=>x.PosPaymentId).OnDelete(DeleteBehavior.Restrict);b.HasIndex(x=>x.PosPaymentId).IsUnique().HasFilter("[PosPaymentId] IS NOT NULL");
        b.HasOne<StorefrontPaymentAllocation>().WithMany().HasForeignKey(x=>x.StorefrontAllocationId).OnDelete(DeleteBehavior.Restrict);b.HasIndex(x=>x.StorefrontAllocationId).IsUnique().HasFilter("[StorefrontAllocationId] IS NOT NULL");
        b.ToTable(t=>t.HasCheckConstraint("CK_BankReceiptConsumptions_Channel","([Channel]=1 AND [PosPaymentId] IS NOT NULL AND [StorefrontAllocationId] IS NULL) OR ([Channel]=2 AND [PosPaymentId] IS NULL AND [StorefrontAllocationId] IS NOT NULL)"));
    }
    public void Configure(EntityTypeBuilder<StorefrontQrProcessing> b)
    {
        b.ToTable("StorefrontQrProcessing",t=>t.HasTrigger("TR_StorefrontQrProcessing_Binding"));b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.StoreId,x.Id});b.Property(x=>x.RowVersion).IsRowVersion();
        b.Property(x=>x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");b.Property(x=>x.NextScanBankDate).HasColumnType("date");b.Property(x=>x.RequestedBankDate).HasColumnType("date");
        b.Property(x=>x.TraceNumber).HasMaxLength(100).IsRequired();b.Property(x=>x.ProviderTraceNumber).HasMaxLength(100).IsRequired();b.Property(x=>x.VirtualAccount).HasMaxLength(100).IsRequired();b.Property(x=>x.QrPng).HasMaxLength(262144);b.Property(x=>x.ReviewCode).HasMaxLength(100);
        b.HasIndex(x=>new{x.StoreId,x.PaymentIntentId}).IsUnique();b.HasIndex(x=>new{x.NextAttemptAtUtc,x.State});
        b.HasOne(x=>x.Store).WithMany().HasForeignKey(x=>x.StoreId).OnDelete(DeleteBehavior.Restrict);b.HasOne<StorefrontPaymentIntent>().WithMany().HasForeignKey(x=>new{x.StoreId,x.PaymentIntentId}).HasPrincipalKey(x=>new{x.StoreId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<StorefrontBankObservation> b)
    {
        b.ToTable("StorefrontBankObservations",t=>t.HasTrigger("TR_StorefrontBankObservations_BankEvidence"));b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.StoreId,x.Id});b.Property(x=>x.RowVersion).IsRowVersion();
        b.Property(x=>x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x=>x.Kind).HasMaxLength(30).IsRequired();b.Property(x=>x.Fingerprint).HasMaxLength(64).IsRequired();b.Property(x=>x.ProtectedEvidence).IsRequired();
        b.HasOne(x=>x.Store).WithMany().HasForeignKey(x=>x.StoreId).OnDelete(DeleteBehavior.Restrict);b.HasOne<StorefrontPaymentIntent>().WithMany().HasForeignKey(x=>new{x.StoreId,x.PaymentIntentId}).HasPrincipalKey(x=>new{x.StoreId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.StoreId,x.PaymentIntentId,x.Fingerprint}).IsUnique();
    }
    public void Configure(EntityTypeBuilder<StorefrontUnattributedBankReceipt> b)
    {
        b.ToTable("StorefrontUnattributedBankReceipts",t=>t.HasTrigger("TR_StorefrontUnattributedBankReceipts_BankEvidence"));b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.StoreId,x.Id});b.Property(x=>x.RowVersion).IsRowVersion();
        b.Property(x=>x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x=>x.Fingerprint).HasMaxLength(64).IsRequired();b.Property(x=>x.ExternalTransactionId).HasMaxLength(100).IsRequired();b.Property(x=>x.Amount).HasPrecision(18,2);
        b.Property(x=>x.RequestedAccountIdentity).HasMaxLength(64).IsRequired();b.Property(x=>x.ReportedAccountIdentity).HasMaxLength(64).IsRequired();b.Property(x=>x.RawBankTime).HasMaxLength(100).IsRequired();
        b.Property(x=>x.BankBusinessDate).HasColumnType("date");b.Property(x=>x.ProtectedEvidence).IsRequired();b.HasIndex(x=>new{x.StoreId,x.PaymentIntentId,x.Fingerprint}).IsUnique();
        b.HasOne(x=>x.Store).WithMany().HasForeignKey(x=>x.StoreId).OnDelete(DeleteBehavior.Restrict);b.HasOne<StorefrontPaymentIntent>().WithMany().HasForeignKey(x=>new{x.StoreId,x.PaymentIntentId}).HasPrincipalKey(x=>new{x.StoreId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
