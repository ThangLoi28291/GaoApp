using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class CustomerReceivableConfiguration : IEntityTypeConfiguration<CustomerReceivableEntry>
{
    public void Configure(EntityTypeBuilder<CustomerReceivableEntry> b)
    {
        b.ToTable("CustomerReceivableEntries");
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesReturn>().WithMany().HasForeignKey(x => x.SalesReturnId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Receipt).WithMany(x => x.Entries).HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.CustomerId, x.Id });
        b.HasIndex(x => new { x.StoreId, x.OrderId, x.Kind }).IsUnique().HasFilter("[Kind] IN ('Sale','Void')");
        b.HasIndex(x => new { x.StoreId, x.OrderId, x.ReceiptId }).IsUnique().HasFilter("[ReceiptId] IS NOT NULL");
        b.HasIndex(x => new { x.StoreId, x.SalesReturnId }).IsUnique().HasFilter("[SalesReturnId] IS NOT NULL");
    }
}

public sealed class CustomerDebtReceiptConfiguration : IEntityTypeConfiguration<CustomerDebtReceipt>
{
    public void Configure(EntityTypeBuilder<CustomerDebtReceipt> b)
    {
        b.ToTable("CustomerDebtReceipts");
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.StoreId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.CustomerId, x.Id });
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<POSShift>().WithMany().HasForeignKey(x => x.POSShiftId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoreBankAccount>().WithMany().HasForeignKey(x => x.StoreBankAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
