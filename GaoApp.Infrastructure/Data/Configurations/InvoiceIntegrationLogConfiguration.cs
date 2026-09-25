using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InvoiceIntegrationLogConfiguration : IEntityTypeConfiguration<InvoiceIntegrationLog>
{
    public void Configure(EntityTypeBuilder<InvoiceIntegrationLog> b)
    {
        b.ToTable("InvoiceIntegrationLogs");

        b.HasKey(x => x.Id);

        b.Property(x => x.StoreId)
            .IsRequired();

        b.Property(x => x.InvoiceHeadId)
            .IsRequired();

        b.Property(x => x.AutoInvoiceOperationId)
            .IsRequired(false);

        b.Property(x => x.ActionType)
            .HasConversion<byte>()
            .IsRequired();

        b.Property(x => x.RequestUrl)
            .HasMaxLength(500);

        // RequestBody / ResponseBody để mặc định nvarchar(max)
        // Không set HasMaxLength vì log API có thể dài.
        b.Property(x => x.RequestBody);

        b.Property(x => x.ResponseBody);

        b.Property(x => x.IsSuccess)
            .IsRequired()
            .HasDefaultValue(false);

        b.Property(x => x.ErrorCode)
            .HasMaxLength(100);

        b.Property(x => x.ErrorMessage)
            .HasMaxLength(1000);

        b.Property(x => x.StartedAtUtc)
            .IsRequired();

        b.Property(x => x.DurationMs);

        b.HasOne(x => x.InvoiceHead)
            .WithMany(x => x.IntegrationLogs)
            .HasForeignKey(x => x.InvoiceHeadId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.StoreId, x.AutoInvoiceOperationId, x.StartedAtUtc });

        b.HasIndex(x => new { x.StoreId, x.InvoiceHeadId, x.ActionType });

        b.HasIndex(x => new { x.StoreId, x.StartedAtUtc });

        b.HasIndex(x => new { x.StoreId, x.IsSuccess, x.ActionType });
    }
}
