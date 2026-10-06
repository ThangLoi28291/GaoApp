using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class TreasuryEntryConfiguration : IEntityTypeConfiguration<TreasuryEntry>
{
    public void Configure(EntityTypeBuilder<TreasuryEntry> b)
    {
        b.ToTable("TreasuryEntries", t => {
            t.HasCheckConstraint("CK_TreasuryEntries_Amount", "[Amount] <> 0");
            t.HasCheckConstraint("CK_TreasuryEntries_Transfer", "[TargetFund] IS NULL OR (([TargetFund] <> [Fund] OR ([Fund] = 'cash' AND [IsVoucherLink] = 1)) AND [OperatingExpenseId] IS NULL AND [PurchasePayableId] IS NULL)");
        });
        b.Property(x => x.Fund).HasMaxLength(40).IsRequired();
        b.Property(x => x.TargetFund).HasMaxLength(40);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => new { x.StoreId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.OccurredAtUtc });
        b.HasIndex(x => new { x.StoreId, x.ReversalOfId }).IsUnique().HasFilter("[ReversalOfId] IS NOT NULL");
        b.HasIndex(x => new { x.StoreId, x.POSShiftCashTransactionId }).IsUnique().HasFilter("[POSShiftCashTransactionId] IS NOT NULL");
        b.HasOne<OperatingExpense>().WithMany().HasForeignKey(x => x.OperatingExpenseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PurchasePayable>().WithMany().HasForeignKey(x => x.PurchasePayableId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<POSShiftCashTransaction>().WithMany().HasForeignKey(x => x.POSShiftCashTransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TreasuryEntry>().WithMany().HasForeignKey(x => x.ReversalOfId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public sealed class TreasuryOpeningBalanceConfiguration : IEntityTypeConfiguration<TreasuryOpeningBalance>
{
    public void Configure(EntityTypeBuilder<TreasuryOpeningBalance> b)
    {
        b.ToTable("TreasuryOpeningBalances");
        b.Property(x => x.Fund).HasMaxLength(40).IsRequired();
        b.Property(x => x.AsOfDate).HasColumnType("date");
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Note).HasMaxLength(1000).IsRequired();
        b.HasIndex(x => new { x.StoreId, x.Fund }).IsUnique();
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}
