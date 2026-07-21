using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("Orders");
        b.HasKey(x => x.Id);

        // Concurrency
        b.Property(x => x.RowVersion).IsRowVersion();

        // StoreId
        b.Property(x => x.StoreId).IsRequired();

        // OrderNumber
        b.Property(x => x.OrderNumber).HasMaxLength(30);
        b.HasIndex(x => new { x.StoreId, x.OrderNumber })
         .IsUnique()
         .HasFilter("[OrderNumber] IS NOT NULL"); // tránh unique null

        // Enum -> byte
        b.Property(x => x.Status).HasConversion<byte>().IsRequired();
        b.Property(x => x.PaymentStatus).HasConversion<byte>().IsRequired();

        // Tiền: decimal precision
        b.Property(x => x.Subtotal).HasPrecision(18, 2);
        b.Property(x => x.DiscountTotal).HasPrecision(18, 2);
        b.Property(x => x.GrandTotal).HasPrecision(18, 2);
        b.Property(x => x.PaidTotal).HasPrecision(18, 2);
        b.Property(x => x.BalanceDue).HasPrecision(18, 2);
        b.Property(x => x.ChangeDue).HasPrecision(18, 2);
        b.Property(x => x.VoucherDiscountTotal).HasPrecision(18, 2);

        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.ComboDiscountTotal)
    .HasPrecision(18, 2);

        b.Property(x => x.ComboPromotionName)
            .HasMaxLength(200);

        b.Property(x => x.ComboPromotionNote)
            .HasMaxLength(500);

        // ✅ BẮT BUỘC thuộc 1 ca POS
        b.Property(x => x.POSShiftId).IsRequired();

        b.Property(x => x.HoldNote)
    .HasMaxLength(500);

        b.Property(x => x.HoldCode)
            .HasMaxLength(50);
        b.HasOne(x => x.Customer)
    .WithMany()
    .HasForeignKey(x => x.CustomerId)
    .OnDelete(DeleteBehavior.NoAction);
        // FK Order -> POSShift
        b.HasOne(x => x.POSShift)
         .WithMany() // hiện POSShift chưa khai báo ICollection<Order>, nên để WithMany() trống
         .HasForeignKey(x => x.POSShiftId)
         .OnDelete(DeleteBehavior.Restrict);
        // Giải thích: Restrict để tránh xóa ca -> xóa luôn Orders (rất nguy hiểm).
        // Vì POSShift là dữ liệu đối soát, thường không được cascade delete.

        // Index phục vụ query theo ca
        b.HasIndex(x => new { x.StoreId, x.POSShiftId, x.Status });
        b.HasIndex(x => new { x.StoreId, x.CompletedAtUtc });

        b.Property(x => x.LegalEntityCount)
            .IsRequired()
            .HasDefaultValue(0);

        b.Property(x => x.HasMultipleLegalEntities)
            .IsRequired()
            .HasDefaultValue(false);

        b.Property(x => x.LegalEntityAllocatedAtUtc);
        b.Property(x => x.UseMultiLegalEntity)
            .IsRequired()
            .HasDefaultValue(false);
        b.Property(x => x.LegalEntityModeCapturedAtUtc);
        b.Property(x => x.LegalEntityActivationAtUtcSnapshot);

        b.HasIndex(x => new
        {
            x.StoreId,
            x.HasMultipleLegalEntities,
            x.LegalEntityAllocatedAtUtc,
            x.IsDeleted
        });

        b.HasIndex(x => new
        {
            x.StoreId,
            x.UseMultiLegalEntity,
            x.Status,
            x.LegalEntityModeCapturedAtUtc,
            x.IsDeleted
        });

        b.Property(x => x.HasInventoryIssue)
    .IsRequired()
    .HasDefaultValue(false);

        b.Property(x => x.InventoryResolutionStatus)
            .IsRequired()
            .HasDefaultValue(InventoryResolutionStatus.None);

        b.Property(x => x.InventoryIssueOpenedAtUtc);

        b.Property(x => x.InventoryIssueApprovedAtUtc);

        b.HasIndex(x => new { x.StoreId, x.HasInventoryIssue, x.InventoryResolutionStatus, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.InventoryIssueOpenedAtUtc, x.IsDeleted });
    }
}
