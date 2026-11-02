using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class BankNotificationConfiguration : IEntityTypeConfiguration<BankNotificationDevice>,
    IEntityTypeConfiguration<BankNotificationWait>, IEntityTypeConfiguration<BankNotificationReceipt>,
    IEntityTypeConfiguration<BankNotificationDismissal>
{
    public void Configure(EntityTypeBuilder<BankNotificationDevice> b)
    {
        b.ToTable("BankNotificationDevices");
        b.HasOne<StoreBankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.BankAccountId, x.Enabled });
    }
    public void Configure(EntityTypeBuilder<BankNotificationWait> b)
    {
        b.ToTable("BankNotificationWaits");
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.HasIndex(x => new { x.StoreId, x.QrId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.BankAccountId, x.ExpiresAtUtc });
        b.HasOne<PosPaymentQrRequest>().WithMany().HasForeignKey(x => x.QrId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoreBankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<POSShift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<POSTerminal>().WithMany().HasForeignKey(x => x.TerminalId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<BankNotificationReceipt> b)
    {
        b.ToTable("BankNotificationReceipts");
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Content).HasMaxLength(12000);
        b.HasIndex(x => new { x.StoreId, x.EvidenceKey }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.BankAccountId, x.PostedAtUtc });
        b.HasOne<BankNotificationDevice>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoreBankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankNotificationWait>().WithMany().HasForeignKey(x => x.AppliedWaitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OrderPayment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<BankNotificationDismissal> b)
    {
        b.ToTable("BankNotificationDismissals");
        b.HasIndex(x => new { x.StoreId, x.ReceiptId, x.WaitId }).IsUnique();
        b.HasOne<BankNotificationReceipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankNotificationWait>().WithMany().HasForeignKey(x => x.WaitId).OnDelete(DeleteBehavior.Restrict);
    }
}
