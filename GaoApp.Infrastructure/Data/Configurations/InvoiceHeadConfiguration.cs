using GaoApp.Domain.Constants;
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

        // =====================================================
        // UNIQUE HÓA ĐƠN GỐC THEO ORDER / LEGAL ENTITY
        // =====================================================
        // Legacy: một Order chỉ có một InvoiceHead gốc không gắn LegalEntity.
        b.HasIndex(x => new { x.StoreId, x.OrderId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NULL");

        // Multi LegalEntity: một Order có tối đa một InvoiceHead gốc cho mỗi HKD.
        b.HasIndex(x => new { x.StoreId, x.OrderId, x.LegalEntityId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NOT NULL");

        // InvoiceNumber nội bộ hoặc số hóa đơn sau khi phát hành.
        b.Property(x => x.InvoiceNumber)
            .HasMaxLength(50);

        b.Property(x => x.InvoiceDate)
            .IsRequired();
        // =====================================================
        // THÔNG TIN NGƯỜI MUA XUẤT HÓA ĐƠN
        // =====================================================

        b.Property(x => x.BuyerType)
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(InvoiceBuyerTypes.NoInvoice);

        b.Property(x => x.BuyerName)
            .HasMaxLength(300);

        b.Property(x => x.BuyerLegalName)
            .HasMaxLength(500);

        b.Property(x => x.BuyerTaxCode)
            .HasMaxLength(50);

        b.Property(x => x.BuyerAddress)
            .HasMaxLength(1200);

        b.Property(x => x.BuyerEmail)
            .HasMaxLength(2000);

        b.Property(x => x.BuyerPhone)
            .HasMaxLength(30);

        b.HasIndex(x => new { x.StoreId, x.BuyerTaxCode, x.IsDeleted });

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

        // TransactionUuid phải unique trong 1 store nếu có giá trị.
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
        b.Property(x => x.OfficialPdfFileName)
    .HasMaxLength(260);

        b.Property(x => x.OfficialZipXmlFileName)
            .HasMaxLength(260);

        b.Property(x => x.LastEmailTo)
            .HasMaxLength(500);

        b.Property(x => x.LastEmailErrorMessage)
            .HasMaxLength(1000);
        // =====================================================
        // PHASE 16 - HÓA ĐƠN THAY THẾ / ĐIỀU CHỈNH
        // =====================================================

        b.Property(x => x.CorrectionType)
            .HasConversion<byte?>();

        b.Property(x => x.OriginalInvoiceNo)
            .HasMaxLength(50);

        b.Property(x => x.AdjustedNote)
            .HasMaxLength(255);

        b.Property(x => x.AdditionalReferenceDesc)
            .HasMaxLength(255);

        // Hóa đơn thay thế / điều chỉnh trỏ về hóa đơn gốc.
        b.HasOne(x => x.OriginalInvoiceHead)
            .WithMany(x => x.CorrectionInvoices)
            .HasForeignKey(x => x.OriginalInvoiceHeadId)
            .OnDelete(DeleteBehavior.Restrict);

        // Index hỗ trợ tìm tất cả hóa đơn xử lý sai sót của 1 hóa đơn gốc.
        b.HasIndex(x => new { x.StoreId, x.OriginalInvoiceHeadId, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.CorrectionType, x.IsDeleted });

        // =====================================================
        // INDEX PHỤC VỤ TRA CỨU
        // =====================================================

        b.HasIndex(x => new { x.StoreId, x.ProviderStatus, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.ProviderInvoiceNo, x.IsDeleted });

        b.HasIndex(x => new { x.StoreId, x.InvoiceDate, x.IsDeleted });

        // Hai composite FK khóa cứng không cho InvoiceHead tham chiếu HKD/cấu hình
        // hóa đơn thuộc store khác.
        b.HasOne(x => x.LegalEntity)
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.LegalEntityId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.InvoiceProviderSetting)
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.InvoiceProviderSettingId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.StoreId, x.LegalEntityId, x.InvoiceDate, x.IsDeleted });
        b.HasIndex(x => new { x.StoreId, x.InvoiceProviderSettingId, x.IsDeleted });

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
