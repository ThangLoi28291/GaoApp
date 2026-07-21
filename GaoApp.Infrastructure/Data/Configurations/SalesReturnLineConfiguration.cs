using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

/// <summary>
/// Cấu hình EF cho SalesReturnLine.
/// 
/// Mục tiêu:
/// - chuẩn hóa các cột snapshot của dòng trả hàng
/// - bổ sung nền tảng cost snapshot để đảo COGS / revaluation về sau
/// - tối ưu index cho truy vấn theo phiếu trả hàng và theo dòng bán gốc
/// </summary>
public class SalesReturnLineConfiguration : IEntityTypeConfiguration<SalesReturnLine>
{
    public void Configure(EntityTypeBuilder<SalesReturnLine> builder)
    {
        builder.ToTable("SalesReturnLines");

        builder.HasKey(x => x.Id);

        // =========================================================
        // Snapshot text fields
        // =========================================================
        builder.Property(x => x.ItemName)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(x => x.UnitName)
            .HasMaxLength(100);

        builder.Property(x => x.Reason)
            .HasMaxLength(500);

        // =========================================================
        // Quantity fields
        // =========================================================
        // Hệ thống hiện tại của bạn đang dùng quantity chủ yếu decimal(18,3),
        // nên giữ đồng nhất để migration gọn và không phá phần cũ.
        builder.Property(x => x.ReturnQuantity)
            .HasColumnType("decimal(18,3)");

        builder.Property(x => x.ReturnBaseQuantity)
            .HasColumnType("decimal(18,3)");

        // =========================================================
        // Refund amount fields
        // =========================================================
        // Đây là tiền hoàn trả nghiệp vụ bán hàng,
        // nên giữ theo chuẩn tiền hiện tại của order/refund: decimal(18,2).
        builder.Property(x => x.RefundUnitAmount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0);

        builder.Property(x => x.RefundLineTotal)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0);

        // =========================================================
        // Cost snapshot foundation
        // =========================================================
        // UnitCostSnapshot:
        // giá vốn snapshot của dòng trả hàng, thường lấy từ OrderLine gốc.
        builder.Property(x => x.UnitCostSnapshot)
            .HasColumnType("decimal(18,6)")
            .HasDefaultValue(0);

        // LineCostTotal:
        // tổng cost của phần hàng trả.
        builder.Property(x => x.LineCostTotal)
            .HasColumnType("decimal(18,4)")
            .HasDefaultValue(0);

        // IsProvisionalCost:
        // đánh dấu cost này là tạm để sau revaluation xử lý tiếp.
        builder.Property(x => x.IsProvisionalCost)
            .HasDefaultValue(false);

        // =========================================================
        // Enum fields
        // =========================================================
        builder.Property(x => x.Action)
    .ValueGeneratedNever();

        // =========================================================
        // Indexes
        // =========================================================

        // Truy vấn các dòng theo phiếu trả hàng
        builder.HasIndex(x => new { x.StoreId, x.SalesReturnId });

        // Truy nhanh theo dòng bán gốc để tính số lượng đã trả / chưa trả
        builder.HasIndex(x => new { x.StoreId, x.OrderLineId });

        // Truy nhanh theo variant để tổng hợp / audit / revaluation
        builder.HasIndex(x => new { x.StoreId, x.VariantId });

        // Nếu muốn chặt hơn cho nghiệp vụ cùng 1 phiếu + cùng 1 dòng bán gốc
        // thì có thể mở index dưới đây. Hiện tại để non-unique cho an toàn nghiệp vụ.
        // builder.HasIndex(x => new { x.SalesReturnId, x.OrderLineId });

        // =========================================================
        // Relationships
        // =========================================================
        builder.HasOne(x => x.SalesReturn)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.SalesReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.OrderLine)
            .WithMany()
            .HasForeignKey(x => x.OrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nếu sau này entity có navigation Product / Variant riêng thì mới cấu hình thêm.
        // Hiện tại entity chỉ có ProductId / VariantId dạng scalar nên chưa cần.
    }
}