using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class AuditOutboxMessageConfiguration : IEntityTypeConfiguration<AuditOutboxMessage>
{
    public void Configure(EntityTypeBuilder<AuditOutboxMessage> builder)
    {
        builder.ToTable("AuditOutbox", table => table.HasCheckConstraint("CK_AuditOutbox_StoreId", "[StoreId] > 0"));
        builder.HasKey(x => x.EventId);
        builder.Property(x => x.EventId).ValueGeneratedNever();
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.LastErrorType).HasMaxLength(160);
        builder.HasIndex(x => new { x.StoreId, x.ProcessedAtUtc, x.NextAttemptAtUtc, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.ProcessedAtUtc, x.NextAttemptAtUtc, x.CreatedAtUtc });
    }
}
