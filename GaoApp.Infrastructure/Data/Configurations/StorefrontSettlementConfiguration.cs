using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class StorefrontSettlementConfiguration : IEntityTypeConfiguration<StorefrontServiceRevenueSetting>,
    IEntityTypeConfiguration<StorefrontServiceRevenue>, IEntityTypeConfiguration<StorefrontSaleBinding>,
    IEntityTypeConfiguration<StorefrontCodCollection>, IEntityTypeConfiguration<StorefrontCodBankRouting>,
    IEntityTypeConfiguration<StorefrontCodReceipt>, IEntityTypeConfiguration<StorefrontCarrierWithholding>,
    IEntityTypeConfiguration<StorefrontNotificationAttestation>, IEntityTypeConfiguration<StorefrontServiceFunding>,
    IEntityTypeConfiguration<StorefrontInvoiceWork>, IEntityTypeConfiguration<StorefrontNotificationCertification>, IEntityTypeConfiguration<BankVerifiedReceipt>,
    IEntityTypeConfiguration<OrderLine>, IEntityTypeConfiguration<StorefrontInventoryIssueSource>
{
    private static void Base<T>(EntityTypeBuilder<T> b, string table, bool immutable = true, bool parent = true) where T : BaseStoreEntity
    {
        b.ToTable(table, t => { t.HasCheckConstraint("CK_" + table + "_Live", "[IsDeleted]=0"); t.HasTrigger("TR_" + table + "_Source"); if (immutable) t.HasTrigger("TR_" + table + "_Immutable"); });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.StoreId, x.Id });
        b.Property(x => x.RowVersion).IsRowVersion(); b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        if (parent) b.HasOne<StorefrontOrder>().WithMany().HasForeignKey("StoreId", "StorefrontOrderId").HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
    private static void Link<T, P>(EntityTypeBuilder<T> b, string name) where T : BaseStoreEntity where P : BaseStoreEntity
        => b.HasOne<P>().WithMany().HasForeignKey("StoreId", name).HasPrincipalKey("StoreId", "Id").OnDelete(DeleteBehavior.Restrict);
    private static void Actor<T>(EntityTypeBuilder<T> b, string name = "ActorUserId") where T : BaseStoreEntity
        => b.HasOne<UserInStore>().WithMany().HasForeignKey("StoreId", name).HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
    private static void Money<T>(EntityTypeBuilder<T> b, string name = "Amount", bool positive = true) where T : BaseStoreEntity
    {
        b.Property<decimal>(name).HasPrecision(18, 2); b.ToTable(t => t.HasCheckConstraint("CK_" + b.Metadata.GetTableName() + "_" + name,
            "[" + name + "]" + (positive ? ">0" : ">=0") + " AND [" + name + "]<10000000000000000 AND [" + name + "]=ROUND([" + name + "],0)"));
    }
    private static void Json<T>(EntityTypeBuilder<T> b, string name) where T : BaseStoreEntity
    { b.Property<string>(name).IsRequired(); b.ToTable(t => t.HasCheckConstraint("CK_" + b.Metadata.GetTableName() + "_" + name, "ISJSON([" + name + "])=1")); }
    public void Configure(EntityTypeBuilder<StorefrontServiceRevenueSetting> b)
    {
        Base(b, "StorefrontServiceRevenueSettings", false, false); Link<StorefrontServiceRevenueSetting, LegalEntity>(b, "LegalEntityId"); Link<StorefrontServiceRevenueSetting, Tax>(b, "TaxId");
        b.HasIndex(x => x.StoreId).IsUnique(); b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_StorefrontServiceRevenueSettings_Route", "[InvoiceRoute] IN (1,2)"));
    }
    public void Configure(EntityTypeBuilder<StorefrontServiceRevenue> b)
    {
        Base(b, "StorefrontServiceRevenues"); Link<StorefrontServiceRevenue, LegalEntity>(b, "LegalEntityId"); Link<StorefrontServiceRevenue, Tax>(b, "TaxId"); Actor(b);
        b.HasOne<POSShift>().WithMany().HasForeignKey(x => new { x.StoreId, x.SettlingShiftId, x.ActorUserId }).HasPrincipalKey(x => new { x.StoreId, x.Id, x.OpenedByUserId }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.StorefrontOrderId }).IsUnique(); b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        Money(b, "GrossAmount"); b.Property(x => x.NetAmount).HasPrecision(18, 2); b.Property(x => x.TaxAmount).HasPrecision(18, 2); b.Property(x => x.TaxRate).HasPrecision(5, 2); Json(b, "SourceSnapshotJson");
        b.ToTable(t => t.HasCheckConstraint("CK_StorefrontServiceRevenues_Arithmetic", "[GrossAmount]=[NetAmount]+[TaxAmount] AND [NetAmount]>=0 AND [TaxAmount]>=0 AND [TaxRate]>=0 AND [TaxRate]<=100 AND [InvoiceRoute] IN (1,2)"));
    }
    public void Configure(EntityTypeBuilder<StorefrontSaleBinding> b)
    {
        Base(b, "StorefrontSaleBindings"); Actor(b); Link<StorefrontSaleBinding, Order>(b, "SaleOrderId");
        b.HasAlternateKey(x => new { x.StoreId, x.StorefrontOrderId, x.SaleOrderId, x.SettlingShiftId, x.ActorUserId });
        b.HasIndex(x => new { x.StoreId, x.StorefrontOrderId }).IsUnique(); b.HasIndex(x => new { x.StoreId, x.SaleOrderId }).IsUnique();
        b.HasOne<POSShift>().WithMany().HasForeignKey(x => new { x.StoreId, x.SettlingShiftId, x.ActorUserId }).HasPrincipalKey(x => new { x.StoreId, x.Id, x.OpenedByUserId }).OnDelete(DeleteBehavior.Restrict); Json(b, "SourceSnapshotJson");
    }
    public void Configure(EntityTypeBuilder<StorefrontCodCollection> b)
    {
        Base(b, "StorefrontCodCollections"); Actor(b, "RecordedByUserId"); Money(b); b.Property(x => x.EvidenceReference).HasMaxLength(500).IsRequired();
        b.HasOne<StorefrontFulfillmentSource>().WithMany().HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.FulfillmentSourceId }).HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<StorefrontCodBankRouting> b)
    {
        Base(b, "StorefrontCodBankRoutings"); Actor(b); Link<StorefrontCodBankRouting, LegalEntity>(b, "ReceivingLegalEntityId");
        b.HasOne<BankVerifiedReceipt>().WithMany().HasForeignKey(x => new { x.StoreId, x.GlobalBankReceiptId }).HasPrincipalKey(x => new { x.OriginStoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.GlobalBankReceiptId).IsUnique(); b.Property(x => x.EvidenceReference).HasMaxLength(500).IsRequired(); Json(b, "AuthoritySnapshotJson");
    }
    public void Configure(EntityTypeBuilder<StorefrontCodReceipt> b)
    {
        Base(b, "StorefrontCodReceipts"); Actor(b); Money(b); Json(b, "SourceSnapshotJson");
        b.HasOne<BankVerifiedReceipt>().WithMany().HasForeignKey(x => new { x.StoreId, x.GlobalBankReceiptId }).HasPrincipalKey(x => new { x.OriginStoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        Link<StorefrontCodReceipt, StorefrontCodBankRouting>(b, "CodBankRoutingId"); Link<StorefrontCodReceipt, TreasuryEntry>(b, "TreasuryEntryId"); Link<StorefrontCodReceipt, POSShiftCashTransaction>(b, "POSShiftCashTransactionId");
        b.HasIndex(x => x.GlobalBankReceiptId).IsUnique().HasFilter("[GlobalBankReceiptId] IS NOT NULL"); b.HasIndex(x => new { x.StoreId, x.TreasuryEntryId }).IsUnique().HasFilter("[TreasuryEntryId] IS NOT NULL");
        b.HasIndex(x => new { x.StoreId, x.POSShiftCashTransactionId }).IsUnique().HasFilter("[POSShiftCashTransactionId] IS NOT NULL");
        b.ToTable(t => t.HasCheckConstraint("CK_StorefrontCodReceipts_Source", "([GlobalBankReceiptId] IS NOT NULL AND [CodBankRoutingId] IS NOT NULL AND [TreasuryEntryId] IS NULL AND [POSShiftCashTransactionId] IS NULL) OR ([GlobalBankReceiptId] IS NULL AND [CodBankRoutingId] IS NULL AND [POSShiftCashTransactionId] IS NOT NULL)"));
    }
    public void Configure(EntityTypeBuilder<StorefrontCarrierWithholding> b)
    {
        Base(b, "StorefrontCarrierWithholdings"); Actor(b); Money(b); Link<StorefrontCarrierWithholding, OperatingExpense>(b, "OperatingExpenseId"); Link<StorefrontCarrierWithholding, LegalEntity>(b, "LegalEntityId");
        b.Property(x => x.CarrierEvidenceReference).HasMaxLength(500).IsRequired(); b.HasIndex(x => new { x.StoreId, x.OperatingExpenseId }).IsUnique();
    }
    public void Configure(EntityTypeBuilder<StorefrontNotificationAttestation> b)
    {
        Base(b, "StorefrontNotificationAttestations", true, false);
        Link<StorefrontNotificationAttestation, BankNotificationReceipt>(b, "BankNotificationReceiptId"); Link<StorefrontNotificationAttestation, BankNotificationDevice>(b, "DeviceId"); Link<StorefrontNotificationAttestation, StoreBankAccount>(b, "BankAccountId");
        b.HasIndex(x => new { x.StoreId, x.BankNotificationReceiptId }).IsUnique();
        foreach (var name in new[] { "AccountIdentity", "PayloadHash", "EvidenceKey" }) b.Property<string>(name).HasMaxLength(64).IsRequired();
        b.Property(x => x.AccountVersion).HasMaxLength(16).IsRequired(); b.Property(x => x.SourcePackage).HasMaxLength(100).IsRequired(); b.Property(x => x.NativeTransactionId).HasMaxLength(100).IsRequired(); Json(b, "AccountSnapshotJson"); Json(b, "CollectionAuthoritySnapshotJson");
    }
    public void Configure(EntityTypeBuilder<StorefrontServiceFunding> b)
    {
        Base(b, "StorefrontServiceFundings"); Money(b); Link<StorefrontServiceFunding, StorefrontServiceRevenue>(b, "ServiceRevenueId");
        Link<StorefrontServiceFunding, StorefrontCodReceipt>(b, "CodReceiptId"); Link<StorefrontServiceFunding, StorefrontCarrierWithholding>(b, "CarrierWithholdingId");
        b.HasOne<BankVerifiedReceipt>().WithMany().HasForeignKey(x => new { x.StoreId, x.GlobalBankReceiptId }).HasPrincipalKey(x => new { x.OriginStoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_StorefrontServiceFundings_Source", "(CASE WHEN [GlobalBankReceiptId] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [CodReceiptId] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [CarrierWithholdingId] IS NOT NULL THEN 1 ELSE 0 END)=1"));
    }
    public void Configure(EntityTypeBuilder<StorefrontInvoiceWork> b)
    {
        Base(b, "StorefrontInvoiceWorks", false); Link<StorefrontInvoiceWork, Order>(b, "SaleOrderId"); Link<StorefrontInvoiceWork, StorefrontServiceRevenue>(b, "ServiceRevenueId");
        b.HasIndex(x => new { x.StoreId, x.StorefrontOrderId }).IsUnique(); b.Property(x => x.LastErrorCode).HasMaxLength(100);
    }
    public void Configure(EntityTypeBuilder<StorefrontNotificationCertification> b)
    {
        Base(b, "StorefrontNotificationCertifications", true, false);
        Link<StorefrontNotificationCertification, StorefrontNotificationAttestation>(b, "NotificationAttestationId");
        b.HasOne<BankVerifiedReceipt>().WithMany().HasForeignKey(x => new { x.StoreId, x.GlobalBankReceiptId }).HasPrincipalKey(x => new { x.OriginStoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.NotificationAttestationId }).IsUnique(); b.HasIndex(x => x.GlobalBankReceiptId).IsUnique();
    }
    public void Configure(EntityTypeBuilder<BankVerifiedReceipt> b)
    {
        b.Property(x => x.RoutingProvenance).HasDefaultValue(BankReceiptRoutingProvenance.Unproven);
        b.Property(x => x.AccountVersion).HasMaxLength(16).HasDefaultValue("").IsRequired();
        b.ToTable(t => { t.HasTrigger("TR_BankVerifiedReceipts_CodSource");
            t.HasCheckConstraint("CK_BankVerifiedReceipts_CodProvenance", "[RoutingProvenance] IN (0,1,2) AND ([NativeCollectionAuthorityProofJson] IS NULL OR ISJSON([NativeCollectionAuthorityProofJson])=1)"); });
    }
    public void Configure(EntityTypeBuilder<OrderLine> b)
    {
        b.Property(x => x.StorefrontRoundingAdjustment).HasPrecision(18, 0).HasDefaultValue(0m);
        b.HasOne<StorefrontOrderLine>().WithMany().HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.StorefrontOrderLineId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.StorefrontOrderLineId }).IsUnique().HasFilter("[StorefrontOrderLineId] IS NOT NULL");
        b.ToTable(t => t.HasTrigger("TR_OrderLines_StorefrontSource"));
    }
    public void Configure(EntityTypeBuilder<StorefrontInventoryIssueSource> b)
    {
        Base(b, "StorefrontInventoryIssueSources");
        Link<StorefrontInventoryIssueSource, OrderInventoryIssue>(b, "OrderInventoryIssueId"); Link<StorefrontInventoryIssueSource, OrderInventoryIssueLine>(b, "OrderInventoryIssueLineId");
        Link<StorefrontInventoryIssueSource, InventoryValuationEntry>(b, "InventoryValuationEntryId"); Link<StorefrontInventoryIssueSource, InventoryCostLayerAllocation>(b, "InventoryCostLayerAllocationId");
        b.Property(x => x.BaselineResolvedQuantity).HasPrecision(18, 4); b.Property(x => x.UnresolvedQuantity).HasPrecision(18, 4);
        b.HasIndex(x => new { x.StoreId, x.InventoryCostLayerAllocationId }).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_StorefrontInventoryIssueSources_Quantity", "[BaselineResolvedQuantity]>=0 AND [UnresolvedQuantity]>0"));
    }
}
