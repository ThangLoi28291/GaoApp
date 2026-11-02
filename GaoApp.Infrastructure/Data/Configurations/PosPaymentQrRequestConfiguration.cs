using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class PosPaymentQrRequestConfiguration : IEntityTypeConfiguration<PosPaymentQrRequest>
{
    public void Configure(EntityTypeBuilder<PosPaymentQrRequest> builder)
    {
        builder.Property(x => x.AccountIdentitySnapshot).HasMaxLength(64);
        builder.Property(x => x.AccountVersionSnapshot).HasMaxLength(16);
        builder.ToTable(t => t.HasTrigger("TR_PosPaymentQrRequests_StorefrontIdentity"));
        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Property(x => x.Content)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.RequestCode)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.ProviderTransactionId)
            .HasMaxLength(200);

        builder.HasIndex(x => new { x.StoreId, x.RequestCode })
            .IsUnique();

        builder.HasIndex(x => new { x.StoreId, x.OrderId, x.Status });
        builder.HasIndex(x => new { x.StoreId, x.OrderId, x.ClientRequestId })
            .IsUnique().HasFilter("[ClientRequestId] IS NOT NULL");
        builder.HasOne<OrderPayment>().WithMany().HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
