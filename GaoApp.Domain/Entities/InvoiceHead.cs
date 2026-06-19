using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

[Table("InvoiceHeads")]
public class InvoiceHead : BaseStoreEntity
{
    /// <summary>
    /// POS Order gốc tạo hóa đơn bán ra.
    /// Một Order có thể sinh một InvoiceHead.
    /// </summary>
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;

    [StringLength(50)]
    public string? InvoiceNumber { get; set; }

    public DateTime InvoiceDate { get; set; } = DateTime.Now;

    [StringLength(250)]
    public string? BuyerName { get; set; }

    [StringLength(50)]
    public string? BuyerTaxCode { get; set; }

    [StringLength(500)]
    public string? BuyerAddress { get; set; }

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