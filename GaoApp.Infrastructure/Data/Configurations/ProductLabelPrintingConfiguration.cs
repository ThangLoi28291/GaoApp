using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class ProductLabelTemplateConfiguration : IEntityTypeConfiguration<ProductLabelTemplate>
{
    public void Configure(EntityTypeBuilder<ProductLabelTemplate> b)
    {
        b.ToTable("ProductLabelTemplates");
        b.Property(x => x.Name).HasMaxLength(100);
        b.HasIndex(x => new { x.StoreId, x.IsDeleted });
    }
}
public sealed class ProductLabelPrinterConfiguration : IEntityTypeConfiguration<ProductLabelPrinter>
{
    public void Configure(EntityTypeBuilder<ProductLabelPrinter> b)
    {
        b.ToTable("ProductLabelPrinters");
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.WindowsPrinterName).HasMaxLength(220);
        b.Property(x => x.PrintableWidthMm).HasPrecision(8, 2);
        b.Property(x => x.OffsetXmm).HasPrecision(8, 2);
        b.Property(x => x.OffsetYmm).HasPrecision(8, 2);
        b.HasIndex(x => new { x.StoreId, x.WindowsPrinterName }).IsUnique();
    }
}
public sealed class ProductLabelTaskConfiguration : IEntityTypeConfiguration<ProductLabelTask>
{
    public void Configure(EntityTypeBuilder<ProductLabelTask> b)
    {
        b.ToTable("ProductLabelTasks");
        b.Property(x => x.DocumentNo).HasMaxLength(50);
        b.Property(x => x.SourceHash).HasMaxLength(64);
        b.HasIndex(x => new { x.StoreId, x.StockDocumentId }).IsUnique();
        b.HasOne(x => x.StockDocument).WithMany().HasForeignKey(x => x.StockDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ProductLabelJobConfiguration : IEntityTypeConfiguration<ProductLabelJob>
{
    public void Configure(EntityTypeBuilder<ProductLabelJob> b)
    {
        b.ToTable("ProductLabelJobs");
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.RequestedByName).HasMaxLength(200);
        b.Property(x => x.ConfirmedByName).HasMaxLength(200);
        b.Property(x => x.Error).HasMaxLength(500);
        b.HasIndex(x => new { x.StoreId, x.RequestId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.PrinterId, x.Status, x.Id });
        b.HasIndex(x => new { x.StoreId, x.TaskId }).IsUnique()
            .HasFilter("[TaskId] IS NOT NULL AND [Status] IN (0, 1, 2, 3)");
        b.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Printer).WithMany().HasForeignKey(x => x.PrinterId).OnDelete(DeleteBehavior.Restrict);
    }
}
