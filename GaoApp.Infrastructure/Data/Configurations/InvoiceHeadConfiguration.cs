using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InvoiceHeadConfiguration : IEntityTypeConfiguration<InvoiceHead>
{
    public void Configure(EntityTypeBuilder<InvoiceHead> b)
    {
        b.ToTable("InvoiceHeads");

        b.HasKey(x => x.Id);

        // StoreId
        b.Property(x => x.StoreId)
            .IsRequired();

        // OrderId
        b.Property(x => x.OrderId)
            .IsRequired();

        // Một Order chỉ nên có 1 InvoiceHead active.
        // Dùng filter IsDeleted để sau này nếu soft delete vẫn không bị kẹt unique.
        b.HasIndex(x => new { x.StoreId, x.OrderId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // InvoiceNumber nội bộ hoặc số hóa đơn sau khi phát hành.
        b.Property(x => x.InvoiceNumber)
            .HasMaxLength(50);

        b.Property(x => x.InvoiceDate)
            .IsRequired();

        b.Property(x => x.BuyerName)
            .HasMaxLength(250);

        b.Property(x => x.BuyerTaxCode)
            .HasMaxLength(50);

        b.Property(x => x.BuyerAddress)
            .HasMaxLength(500);

        // Tiền
        b.Property(x => x.TotalQuantity)
            .HasPrecision(18, 3);

        b.Property(x => x.SubTotal)
            .HasPrecision(18, 2);

        b.Property(x => x.VatAmount)
            .HasPrecision(18, 2);

        b.Property(x => x.GrandTotal)
            .HasPrecision(18, 2);

        b.Property(x => x.Note)
            .HasMaxLength(500);

        // Khóa hóa đơn
        b.Property(x => x.IsLocked)
            .IsRequired()
            .HasDefaultValue(false);

        b.Property(x => x.LockReason)
            .HasMaxLength(500);

        // =====================================================
        // PHẦN TÍCH HỢP HÓA ĐƠN ĐIỆN TỬ
        // =====================================================

        // Enum -> byte để DB gọn và ổn định.
        b.Property(x => x.ProviderStatus)
            .HasConversion<byte>()
            .IsRequired()
            .HasDefaultValue(InvoiceProviderStatus.LocalDraft);

        b.Property(x => x.TransactionUuid)
            .HasMaxLength(36);

        // transactionUuid phải unique trong 1 store nếu có giá trị.
        // Đây là mã chống tạo trùng hóa đơn khi gọi Viettel.
        b.HasIndex(x => new { x.StoreId, x.TransactionUuid })
            .IsUnique()
            .HasFilter("[TransactionUuid] IS NOT NULL AND [IsDeleted] = 0");

        b.Property(x => x.ProviderCode)
            .HasMaxLength(50);

        b.Property(x => x.SupplierTaxCode)
            .HasMaxLength(20);

        b.Property(x => x.InvoiceType)
            .HasMaxLength(20);

        b.Property(x => x.TemplateCode)
            .HasMaxLength(20);

        b.Property(x => x.InvoiceSeries)
            .HasMaxLength(25);

        b.Property(x => x.ProviderInvoiceNo)
            .HasMaxLength(35);

        b.Property(x => x.ProviderTransactionId)
            .HasMaxLength(100);

        b.Property(x => x.ReservationCode)
            .HasMaxLength(100);

        b.Property(x => x.CodeOfTax)
            .HasMaxLength(200);

        b.Property(x => x.LastErrorCode)
            .HasMaxLength(100);

        b.Property(x => x.LastErrorMessage)
            .HasMaxLength(1000);

        b.Property(x => x.PdfFilePath)
            .HasMaxLength(500);

        b.Property(x => x.ZipFilePath)
            .HasMaxLength(500);

        b.HasIndex(x => new { x.StoreId, x.ProviderStatus, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.ProviderInvoiceNo, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.InvoiceDate, x.IsDeleted });

        // FK InvoiceHead -> Order
        b.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Giải thích:
        // Restrict để không bao giờ xóa Order kéo theo xóa InvoiceHead.
        // Hóa đơn là dữ liệu kế toán/đối soát, phải giữ độc lập.
    }
}