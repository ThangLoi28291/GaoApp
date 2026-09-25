using GaoApp.Domain.Common;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

    [Table("InvoiceHeads")]
public class InvoiceHead : BaseStoreEntity
{
    /// <summary>
    /// Hóa đơn tổng hợp do hàng đợi phát hành tự động tạo. Đây không phải draft POS
    /// mới và luôn có liên kết nguồn trong AutoInvoiceOperationSources.
    /// </summary>
    public bool IsAutoInvoiceGroup { get; set; }

    /// <summary>
    /// Chủ thể pháp lý phát hành hóa đơn. Null chỉ dành cho dữ liệu/luồng legacy
    /// khi store chưa bật Multi LegalEntity.
    /// </summary>
    public int? LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    /// <summary>
    /// Cấu hình nhà cung cấp được chốt tại thời điểm tạo InvoiceHead.
    /// Không suy đoán lại theo cấu hình active mới nhất khi phát hành/sync/tải file.
    /// </summary>
    public int? InvoiceProviderSettingId { get; set; }
    public InvoiceProviderSetting? InvoiceProviderSetting { get; set; }

    /// <summary>
    /// POS Order gốc tạo hóa đơn bán ra. Null chỉ cho bản ghi GaoStore chỉ tra cứu
    /// chưa ánh xạ được Order (ràng buộc CK_InvoiceHeads_LegacyOrder).
    /// Một Order legacy sinh một InvoiceHead; đơn Multi LegalEntity sinh một
    /// InvoiceHead cho mỗi LegalEntity có allocation.
    /// </summary>
    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    // GaoStore provenance is independent of the optional GaoApp Order relationship.
    public long? LegacySourceId { get; set; }
    public long? LegacyOrderCategoryId { get; set; }
    public string? LegacyMergeId { get; set; }
    public string? LegacySnapshotJson { get; set; }
    public byte[]? LegacyImportedHash { get; set; }
    public bool LegacyReadOnly { get; set; }

    [StringLength(50)]
    public string? InvoiceNumber { get; set; }

    public DateTime InvoiceDate { get; set; } = DateTime.Now;

    /// <summary>
    /// Loại người mua:
    /// - NoInvoice: không lấy hóa đơn
    /// - Individual: cá nhân
    /// - Business: doanh nghiệp/tổ chức/hộ kinh doanh
    /// </summary>
    [StringLength(30)]
    public string BuyerType { get; set; } =InvoiceBuyerTypes.NoInvoice;

    /// <summary>
    /// Tên người mua cá nhân hoặc người liên hệ.
    /// Với cá nhân: đây là tên khách hàng.
    /// Với doanh nghiệp: có thể là người liên hệ, có thể để trống.
    /// </summary>
    [StringLength(300)]
    public string? BuyerName { get; set; }

    /// <summary>
    /// Tên đơn vị/công ty/hộ kinh doanh.
    /// Dùng khi BuyerType = Business.
    /// Khi có BuyerTaxCode thì Viettel bắt buộc buyerLegalName.
    /// </summary>
    [StringLength(500)]
    public string? BuyerLegalName { get; set; }

    /// <summary>
    /// MST / mã định danh người mua.
    /// Không ép 10/13 số vì có MST cá nhân, CCCD/mã định danh, MST nước ngoài, mã chi nhánh.
    /// </summary>
    [StringLength(50)]
    public string? BuyerTaxCode { get; set; }

    /// <summary>
    /// Địa chỉ xuất hóa đơn.
    /// </summary>
    [StringLength(1200)]
    public string? BuyerAddress { get; set; }

    [StringLength(2000)]
    public string? BuyerEmail { get; set; }

    [StringLength(30)]
    public string? BuyerPhone { get; set; }

    public decimal TotalQuantity { get; set; }

    public decimal SubTotal { get; set; }

    public decimal VatAmount { get; set; }

    public decimal GrandTotal { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
    /// <summary>
    /// Invoice đã khóa thì không cho sửa/xóa/thêm dòng.
    /// Dùng sau khi xuất hóa đơn điện tử hoặc chốt kế toán.
    /// </summary>
    public bool IsLocked { get; set; }

    public DateTime? LockedAtUtc { get; set; }

    public int? LockedByUserId { get; set; }

    [StringLength(500)]
    public string? LockReason { get; set; }
    /// <summary>
    /// Trạng thái tích hợp hóa đơn điện tử với nhà cung cấp.
    /// </summary>
    public InvoiceProviderStatus ProviderStatus { get; set; } = InvoiceProviderStatus.LocalDraft;

    /// <summary>
    /// Mã giao dịch duy nhất gửi sang Viettel.
    /// Bắt buộc để chống trùng hóa đơn và tra cứu lại khi timeout.
    /// </summary>
    [StringLength(36)]
    public string? TransactionUuid { get; set; }

    /// <summary>
    /// Nhà cung cấp hóa đơn điện tử.
    /// Giai đoạn này dùng VIETTEL.
    /// </summary>
    [StringLength(50)]
    public string? ProviderCode { get; set; }

    [StringLength(20)]
    public string? SupplierTaxCode { get; set; }

    [StringLength(20)]
    public string? InvoiceType { get; set; }

    [StringLength(20)]
    public string? TemplateCode { get; set; }

    [StringLength(25)]
    public string? InvoiceSeries { get; set; }

    /// <summary>
    /// Số hóa đơn trả về từ Viettel.
    /// Có thể giống InvoiceNumber hoặc dùng để đồng bộ InvoiceNumber.
    /// </summary>
    [StringLength(35)]
    public string? ProviderInvoiceNo { get; set; }

    /// <summary>
    /// TransactionId trả về từ Viettel.
    /// </summary>
    [StringLength(100)]
    public string? ProviderTransactionId { get; set; }

    /// <summary>
    /// Mã bí mật tra cứu hóa đơn.
    /// </summary>
    [StringLength(100)]
    public string? ReservationCode { get; set; }

    /// <summary>
    /// Mã cơ quan thuế trả về nếu là hóa đơn máy tính tiền / hóa đơn có mã.
    /// </summary>
    [StringLength(200)]
    public string? CodeOfTax { get; set; }

    public DateTime? IssuedAtUtc { get; set; }

    public DateTime? LastSyncedAtUtc { get; set; }

    [StringLength(100)]
    public string? LastErrorCode { get; set; }

    [StringLength(1000)]
    public string? LastErrorMessage { get; set; }

/// <summary>
/// Đường dẫn file PDF đã tải về.
/// </summary>
[StringLength(500)]
    public string? PdfFilePath { get; set; }

    /// <summary>
    /// Đường dẫn file ZIP/XML đã tải về.
    /// </summary>
    [StringLength(500)]
    public string? ZipFilePath { get; set; }
    // =========================================================
    // Viettel official file status
    // =========================================================

    public InvoiceFileDownloadStatus OfficialPdfStatus { get; set; } =
        InvoiceFileDownloadStatus.None;

    public DateTime? OfficialPdfDownloadedAtUtc { get; set; }

    public string? OfficialPdfFileName { get; set; }

    public InvoiceFileDownloadStatus OfficialZipXmlStatus { get; set; } =
        InvoiceFileDownloadStatus.None;

    public DateTime? OfficialZipXmlDownloadedAtUtc { get; set; }

    public string? OfficialZipXmlFileName { get; set; }

    // =========================================================
    // Viettel email status
    // =========================================================

    public InvoiceEmailSendStatus EmailStatus { get; set; } =
        InvoiceEmailSendStatus.NotSent;

    public DateTime? EmailSentAtUtc { get; set; }

    public string? LastEmailTo { get; set; }

    public int EmailSendCount { get; set; }

    public string? LastEmailErrorMessage { get; set; }
    /// <summary>
    /// Hóa đơn gốc nếu đây là hóa đơn thay thế / điều chỉnh.
    /// Null nghĩa là hóa đơn gốc bình thường.
    /// </summary>
    public int? OriginalInvoiceHeadId { get; set; }

    public InvoiceHead? OriginalInvoiceHead { get; set; }

    public ICollection<InvoiceHead> CorrectionInvoices { get; set; } = new List<InvoiceHead>();

    /// <summary>
    /// Loại hóa đơn xử lý sai sót: thay thế / điều chỉnh tiền / điều chỉnh thông tin.
    /// </summary>
    public InvoiceCorrectionType? CorrectionType { get; set; }

    /// <summary>
    /// Số hóa đơn gốc gửi sang Viettel ở originalInvoiceId.
    /// Nên lưu snapshot để sau này hóa đơn gốc có thay đổi cũng không mất dấu.
    /// </summary>
    public string? OriginalInvoiceNo { get; set; }

    /// <summary>
    /// Ngày phát hành hóa đơn gốc gửi sang Viettel ở originalInvoiceIssueDate.
    /// </summary>
    public DateTime? OriginalInvoiceIssuedAtUtc { get; set; }

    /// <summary>
    /// Lý do sai sót gửi sang Viettel ở adjustedNote.
    /// </summary>
    public string? AdjustedNote { get; set; }

    /// <summary>
    /// Thông tin văn bản thỏa thuận gửi sang Viettel ở additionalReferenceDesc.
    /// </summary>
    public string? AdditionalReferenceDesc { get; set; }

    /// <summary>
    /// Ngày văn bản thỏa thuận gửi sang Viettel ở additionalReferenceDate.
    /// </summary>
    public DateTime? AdditionalReferenceDateUtc { get; set; }

    public ICollection<InvoiceIntegrationLog> IntegrationLogs { get; set; } = new List<InvoiceIntegrationLog>();

    public ICollection<InvoiceDetail> Details { get; set; } = new List<InvoiceDetail>();
}
