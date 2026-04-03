using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EntityName).HasMaxLength(150);
        builder.Property(x => x.EntityId).HasMaxLength(100);
        builder.Property(x => x.EntityDisplay).HasMaxLength(300);
        builder.Property(x => x.Summary).HasMaxLength(1000);
        builder.Property(x => x.TraceId).HasMaxLength(100);
        builder.Property(x => x.IpAddress).HasMaxLength(100);
        builder.Property(x => x.UserAgent).HasMaxLength(2000);
        builder.Property(x => x.Path).HasMaxLength(500);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.ActorUserName).HasMaxLength(200);

        builder.HasIndex(x => x.StoreId);
        builder.HasIndex(x => new { x.StoreId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.StoreId, x.ActorUserId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.StoreId, x.Module, x.ActionType, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.EntityName, x.EntityId, x.CreatedAtUtc });
        builder.HasIndex(x => x.TraceId);
    }
}