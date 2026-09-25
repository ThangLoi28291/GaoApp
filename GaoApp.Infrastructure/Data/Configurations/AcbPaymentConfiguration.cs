using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class AcbPaymentConfiguration : IEntityTypeConfiguration<StoreAcbSettings>,
    IEntityTypeConfiguration<AcbQrSession>, IEntityTypeConfiguration<AcbPaymentTransaction>, IEntityTypeConfiguration<AcbCallbackReceipt>,
    IEntityTypeConfiguration<AcbQrNotificationItem>, IEntityTypeConfiguration<AcbCallbackRoute>, IEntityTypeConfiguration<AcbCallbackRouteChange>
{
    public void Configure(EntityTypeBuilder<AcbCallbackRoute> b)
    {
        b.ToTable("AcbCallbackRoutes");
        b.HasIndex(x => x.Host).IsUnique();
        b.HasOne<Store>().WithMany().HasForeignKey(x => x.TargetStoreId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<AcbCallbackRouteChange> b)
    {
        b.ToTable("AcbCallbackRouteChanges");
        b.HasOne(x => x.Route).WithMany().HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.RouteId, x.ChangedAtUtc });
    }
    public void Configure(EntityTypeBuilder<AcbQrNotificationItem> b)
    {
        b.ToTable("AcbQrNotificationItems");
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.BusinessDate).HasColumnType("date");
        b.HasIndex(x => new { x.ReceiptId, x.Position }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.BusinessDate, x.RequestCode, x.ProviderOrderId });
        b.HasOne(x => x.Receipt).WithMany(x => x.Items).HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<AcbCallbackReceipt> b)
    {
        b.ToTable("AcbCallbackReceipts");
        b.Property(x => x.RequestCode).HasDefaultValue("TRANSACTION_UPDATE");
        b.Property(x => x.TotalPages).HasDefaultValue(1);
        b.HasIndex(x => new { x.StoreId, x.RequestCode, x.ClientRequestId, x.Page }).IsUnique();
        b.HasIndex(x => new { x.ProcessedAtUtc, x.NextAttemptAtUtc });
    }
    public void Configure(EntityTypeBuilder<StoreAcbSettings> b)
    {
        b.ToTable("StoreAcbSettings");
        b.HasIndex(x => x.StoreId).IsUnique();
        b.HasOne<StoreBankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<AcbQrSession> b)
    {
        b.ToTable("AcbQrSessions");
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.HasIndex(x => new { x.StoreId, x.ProviderOrderId }).IsUnique();
        b.HasIndex(x => x.QrRequestId).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.OrderId, x.Status });
        b.HasOne<PosPaymentQrRequest>().WithMany().HasForeignKey(x => x.QrRequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AcbCallbackReceipt>().WithMany().HasForeignKey(x => x.ConfirmationCallbackReceiptId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<AcbPaymentTransaction> b)
    {
        b.ToTable("AcbPaymentTransactions");
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.HasIndex(x => new { x.SessionId, x.TransactionNumber }).IsUnique();
        b.HasOne<AcbQrSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
