using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InvoiceBuyerProfileConfiguration : IEntityTypeConfiguration<InvoiceBuyerProfile>
{
    public void Configure(EntityTypeBuilder<InvoiceBuyerProfile> b)
    {
        b.ToTable("InvoiceBuyerProfiles");

        b.HasKey(x => x.Id);

        // StoreId
        b.Property(x => x.StoreId)
            .IsRequired();

        // =====================================================
        // THÔNG TIN PHÂN LOẠI NGƯỜI MUA
        // =====================================================

        b.Property(x => x.BuyerType)
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(InvoiceBuyerTypes.Business);

        b.Property(x => x.TaxCode)
            .HasMaxLength(50);

        b.Property(x => x.BuyerName)
            .HasMaxLength(300);

        b.Property(x => x.BuyerLegalName)
            .HasMaxLength(500);

        b.Property(x => x.BuyerAddress)
            .HasMaxLength(1200);

        b.Property(x => x.BuyerEmail)
            .HasMaxLength(2000);

        b.Property(x => x.BuyerPhone)
            .HasMaxLength(30);

        b.Property(x => x.Source)
            .HasMaxLength(50)
            .IsRequired()
            .HasDefaultValue("manual");

        b.Property(x => x.IsVerifiedByUser)
            .IsRequired()
            .HasDefaultValue(false);

        b.Property(x => x.UseCount)
            .IsRequired()
            .HasDefaultValue(0);

        b.Property(x => x.Note)
            .HasMaxLength(500);

        b.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // =====================================================
        // LIÊN KẾT CUSTOMER POS NẾU CÓ
        // =====================================================

        b.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        // =====================================================
        // INDEX TRA CỨU NHANH
        // =====================================================

        // Gõ MST/mã định danh để tìm lại hồ sơ người mua.
        b.HasIndex(x => new { x.StoreId, x.TaxCode, x.IsDeleted });

        // Lọc theo loại người mua.
        b.HasIndex(x => new { x.StoreId, x.BuyerType, x.IsDeleted });

        // Tìm hồ sơ active.
        b.HasIndex(x => new { x.StoreId, x.IsActive, x.IsDeleted });

        // Tìm theo Customer nếu người mua có liên kết khách POS.
        b.HasIndex(x => new { x.StoreId, x.CustomerId, x.IsDeleted });

        // Tìm theo lần dùng gần nhất.
        b.HasIndex(x => new { x.StoreId, x.LastUsedAtUtc, x.IsDeleted });
    }
}