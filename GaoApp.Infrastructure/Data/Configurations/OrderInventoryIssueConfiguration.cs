using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class OrderInventoryIssueConfiguration : IEntityTypeConfiguration<OrderInventoryIssue>
{
    public void Configure(EntityTypeBuilder<OrderInventoryIssue> builder)
    {
        builder.ToTable("OrderInventoryIssues");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.Severity)
            .IsRequired();

        builder.Property(x => x.OpenedAtUtc)
            .IsRequired();

        builder.Property(x => x.DueAtUtc)
            .IsRequired();

        builder.Property(x => x.ReasonType)
            .IsRequired();

        builder.Property(x => x.InternalNote)
            .HasMaxLength(1000);

        /// <summary>
        /// Cờ tính sẵn để UI/dashboard/query nhanh.
        /// </summary>
        builder.Property(x => x.IsOverdue)
            .IsRequired();

        /// <summary>
        /// Thời điểm bắt đầu overdue.
        /// Null nếu case chưa overdue.
        /// </summary>
        builder.Property(x => x.OverdueSinceUtc)
            .IsRequired(false);

        /// <summary>
        /// Mốc chống spam notification overdue.
        /// Null nếu chưa từng gửi cảnh báo overdue.
        /// </summary>
        builder.Property(x => x.LastOverdueNotifiedAtUtc)
            .IsRequired(false);

        builder.Property(x => x.LastAutoResolvedAtUtc)
            .IsRequired(false);

        builder.Property(x => x.AutoResolvedLineCount)
            .IsRequired();

        // =====================================================
        // Relationships
        // =====================================================

        builder.HasOne(x => x.Order)
            .WithOne(x => x.InventoryIssue)
            .HasForeignKey<OrderInventoryIssue>(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ApprovedByUser)
            .WithMany(x => x.ApprovedInventoryIssues)
            .HasForeignKey(x => x.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RejectedByUser)
            .WithMany(x => x.RejectedInventoryIssues)
            .HasForeignKey(x => x.RejectedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // =====================================================
        // Indexes
        // =====================================================

        /// <summary>
        /// Mỗi order chỉ có tối đa 1 case active (chưa soft delete).
        /// </summary>
        builder.HasIndex(x => x.OrderId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        /// <summary>
        /// Code case duy nhất trong phạm vi store.
        /// </summary>
        builder.HasIndex(x => new { x.StoreId, x.Code })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        /// <summary>
        /// Query list theo trạng thái.
        /// </summary>
        builder.HasIndex(x => new { x.StoreId, x.Status, x.IsDeleted });

        /// <summary>
        /// Hữu ích cho job/service quét case đến hạn hoặc quá hạn.
        /// Có thể dùng để lấy các case open rồi so với thời điểm hiện tại.
        /// </summary>
        builder.HasIndex(x => new { x.StoreId, x.DueAtUtc, x.Status, x.IsDeleted });

        /// <summary>
        /// Hữu ích cho màn hình list/dashboard lọc overdue nhanh.
        /// </summary>
        builder.HasIndex(x => new { x.StoreId, x.IsOverdue, x.Status, x.IsDeleted });

        /// <summary>
        /// Hữu ích khi sort case cũ nhất / mở lâu nhất.
        /// </summary>
        builder.HasIndex(x => new { x.StoreId, x.OpenedAtUtc, x.IsDeleted });

        /// <summary>
        /// Hữu ích khi cần hiển thị các case overdue lâu nhất trước.
        /// </summary>
        builder.HasIndex(x => new { x.StoreId, x.IsOverdue, x.OverdueSinceUtc, x.IsDeleted });

        /// <summary>
        /// Hữu ích cho job tạo notification overdue mà không spam lại.
        /// Ví dụ:
        /// - case đã overdue
        /// - LastOverdueNotifiedAtUtc null hoặc quá cũ
        /// </summary>
        builder.HasIndex(x => new { x.StoreId, x.IsOverdue, x.LastOverdueNotifiedAtUtc, x.IsDeleted });
    }
}