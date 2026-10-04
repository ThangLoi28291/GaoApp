using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace GaoApp.Infrastructure.Data.Configurations;
public sealed class CustomerDepositConfiguration : IEntityTypeConfiguration<CustomerDeposit>
{
    public void Configure(EntityTypeBuilder<CustomerDeposit> b)
    {
        b.ToTable("CustomerDeposits");
        b.Property(x => x.Balance).HasPrecision(18, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.CustomerId });
    }
}
public sealed class CustomerDepositEntryConfiguration : IEntityTypeConfiguration<CustomerDepositEntry>
{
    public void Configure(EntityTypeBuilder<CustomerDepositEntry> b)
    {
        b.ToTable("CustomerDepositEntries");
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<CustomerDeposit>().WithMany().HasForeignKey(x => x.CustomerDepositId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<POSShift>().WithMany().HasForeignKey(x => x.POSShiftId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoreBankAccount>().WithMany().HasForeignKey(x => x.StoreBankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.ClientRequestId }).IsUnique().HasFilter("[ClientRequestId] IS NOT NULL");
        b.HasIndex(x => new { x.StoreId, x.OrderId, x.Kind }).IsUnique().HasFilter("[OrderId] IS NOT NULL AND [Kind] IN ('Apply','Void')");
        b.HasOne<SalesReturn>().WithMany().HasForeignKey(x => x.SalesReturnId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.SalesReturnId }).IsUnique().HasFilter("[SalesReturnId] IS NOT NULL");
        b.HasIndex(x => new { x.StoreId, x.CustomerDepositId, x.Id });
    }
}

public sealed class OrderCustomerDepositConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.HasOne<CustomerDeposit>().WithMany().HasForeignKey(x => x.CustomerDepositId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CashTransactionDepositConfiguration : IEntityTypeConfiguration<POSShiftCashTransaction>
{
    public void Configure(EntityTypeBuilder<POSShiftCashTransaction> b)
    {
        b.HasOne<CustomerDepositEntry>().WithMany().HasForeignKey(x => x.CustomerDepositEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.CustomerDepositEntryId }).IsUnique().HasFilter("[CustomerDepositEntryId] IS NOT NULL");
    }
}
