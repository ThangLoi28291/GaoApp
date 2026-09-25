using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class AutoInvoiceSettingsConfiguration : IEntityTypeConfiguration<AutoInvoiceSettings>
{
    public void Configure(EntityTypeBuilder<AutoInvoiceSettings> b)
    {
        b.ToTable("AutoInvoiceSettings");
        b.HasKey(x => x.Id);
        b.Property(x => x.StoreId).IsRequired();
        b.Property(x => x.ScopeMode).HasConversion<byte>().IsRequired();
        b.Property(x => x.SeparateAmountThreshold).HasPrecision(18, 2);
        b.Property(x => x.GroupTargetAmount).HasPrecision(18, 2);
        b.Property(x => x.ClosingTimeLocal).HasColumnType("time");
        b.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.StoreId, x.IsDeleted }).IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}

public sealed class AutoInvoiceOperationConfiguration : IEntityTypeConfiguration<AutoInvoiceOperation>
{
    public void Configure(EntityTypeBuilder<AutoInvoiceOperation> b)
    {
        b.ToTable("AutoInvoiceOperations");
        b.HasKey(x => x.Id);
        b.Property(x => x.StoreId).IsRequired();
        b.Property(x => x.Kind).HasConversion<byte>().IsRequired();
        b.Property(x => x.Status).HasConversion<byte>().IsRequired();
        b.Property(x => x.GroupKey).HasMaxLength(500);
        b.Property(x => x.TransactionUuid).HasMaxLength(36);
        b.Property(x => x.RequestedByUserName).HasMaxLength(200);
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.Property(x => x.ErrorCode).HasMaxLength(100);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.HasIndex(x => new { x.StoreId, x.Status, x.NextAttemptAtUtc });
        b.HasIndex(x => new { x.StoreId, x.InvoiceHeadId, x.IsDeleted });
        b.HasOne<InvoiceHead>()
            .WithMany()
            .HasForeignKey(x => x.InvoiceHeadId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AutoInvoiceOperationSourceConfiguration : IEntityTypeConfiguration<AutoInvoiceOperationSource>
{
    public void Configure(EntityTypeBuilder<AutoInvoiceOperationSource> b)
    {
        b.ToTable("AutoInvoiceOperationSources");
        b.HasKey(x => x.Id);
        b.Property(x => x.StoreId).IsRequired();
        b.Property(x => x.Status).HasConversion<byte>().IsRequired();
        b.Property(x => x.SourceSnapshotJson).HasMaxLength(2000);
        b.Property(x => x.ErrorCode).HasMaxLength(100);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.HasOne(x => x.AutoInvoiceOperation)
            .WithMany(x => x.Sources)
            .HasForeignKey(x => x.AutoInvoiceOperationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<InvoiceHead>()
            .WithMany()
            .HasForeignKey(x => x.InvoiceHeadId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.InvoiceHeadId })
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.StoreId, x.AutoInvoiceOperationId, x.Status });
    }
}

public sealed class AutoInvoiceWorkerStateConfiguration : IEntityTypeConfiguration<AutoInvoiceWorkerState>
{
    public void Configure(EntityTypeBuilder<AutoInvoiceWorkerState> b)
    {
        b.ToTable("AutoInvoiceWorkerStates");
        b.HasKey(x => x.Id);
        b.Property(x => x.StoreId).IsRequired();
        b.Property(x => x.WorkerName).HasMaxLength(100).IsRequired();
        b.Property(x => x.WorkerInstanceId).HasMaxLength(100);
        b.Property(x => x.LastErrorCode).HasMaxLength(100);
        b.Property(x => x.LastErrorMessage).HasMaxLength(2000);
        b.Property(x => x.LastResult).HasMaxLength(1000);
        b.HasIndex(x => new { x.StoreId, x.WorkerName }).IsUnique();
    }
}
