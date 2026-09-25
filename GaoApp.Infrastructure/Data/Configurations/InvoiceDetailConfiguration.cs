using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InvoiceDetailConfiguration : IEntityTypeConfiguration<InvoiceDetail>
{
    public void Configure(EntityTypeBuilder<InvoiceDetail> b)
    {
        b.ToTable("InvoiceDetails");

        b.HasKey(x => x.Id);
        b.Property(x => x.LegacyUnitFactor).HasPrecision(18, 4);
        b.Property(x => x.LegacyImportedHash).HasColumnType("binary(32)");
        b.HasIndex(x => new { x.StoreId, x.LegacySourceId }).IsUnique().HasFilter("[LegacySourceId] IS NOT NULL");

        b.Property(x => x.StoreId)
            .IsRequired();

        b.Property(x => x.InvoiceHeadId)
            .IsRequired();

        b.Property(x => x.SourceType)
            .HasConversion<byte>()
            .IsRequired();

        b.Property(x => x.ItemName)
            .IsRequired()
            .HasMaxLength(250);

        b.Property(x => x.UnitName)
            .HasMaxLength(100);

        b.Property(x => x.Quantity)
            .HasPrecision(18, 3);

        b.Property(x => x.UnitPrice)
            .HasPrecision(18, 2);

        b.Property(x => x.Amount)
            .HasPrecision(18, 2);

        b.Property(x => x.VatRate)
            .HasPrecision(9, 2);

        b.Property(x => x.VatAmount)
            .HasPrecision(18, 2);

        b.Property(x => x.TotalAmount)
            .HasPrecision(18, 2);

        b.Property(x => x.Note)
            .HasMaxLength(500);

        // FK InvoiceDetail -> InvoiceHead
        b.HasOne(x => x.InvoiceHead)
            .WithMany(x => x.Details)
            .HasForeignKey(x => x.InvoiceHeadId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK InvoiceDetail -> OrderLine
        b.HasOne(x => x.OrderLine)
            .WithMany()
            .HasForeignKey(x => x.OrderLineId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.OrderLegalEntityAllocation)
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.OrderLegalEntityAllocationId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FK InvoiceDetail -> ProductVariant
        b.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        // Không cho sinh trùng dòng hóa đơn từ cùng 1 OrderLine.
        // Chỉ áp dụng khi OrderLineId có giá trị và dòng chưa bị xóa mềm.
        b.HasIndex(x => new { x.StoreId, x.InvoiceHeadId, x.OrderLineId })
            .IsUnique()
            .HasFilter("[OrderLineId] IS NOT NULL AND [IsDeleted] = 0");

        b.HasIndex(x => new { x.StoreId, x.InvoiceHeadId, x.OrderLegalEntityAllocationId })
            .IsUnique()
            .HasFilter("[OrderLegalEntityAllocationId] IS NOT NULL AND [IsDeleted] = 0");

        b.HasIndex(x => new { x.StoreId, x.InvoiceHeadId, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.SourceType, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.ProductVariantId, x.IsDeleted });
    }
}
