using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Đầu hóa đơn điện tử đầu vào lấy từ XML.
/// Đây là dữ liệu nguồn để map với phiếu nhập kho.
/// </summary>
[Table("InputInvoiceHead")]
public class InputInvoiceHead : BaseStoreEntity, IAuditTrackedEntity
{
    /// <summary>
    /// Mẫu số hóa đơn.
    /// Ví dụ: 1, 2, 6 hoặc C26T...
    /// </summary>
    [StringLength(50)]
    public string? InvoiceTemplateCode { get; set; }

    /// <summary>
    /// Ký hiệu hóa đơn.
    /// </summary>
    [StringLength(50)]
    public string? InvoiceSeries { get; set; }

    /// <summary>
    /// Số hóa đơn.
    /// </summary>
    [StringLength(50)]
    public string? InvoiceNumber { get; set; }

    /// <summary>
    /// Ngày lập hóa đơn.
    /// </summary>
    public DateTime? InvoiceDate { get; set; }

    /// <summary>
    /// Mã của cơ quan thuế nếu XML có.
    /// </summary>
    [StringLength(100)]
    public string? TaxAuthorityCode { get; set; }

    [StringLength(50)]
    public string? SellerTaxCode { get; set; }

    /// <summary>
    /// Seller tax code normalized for durable invoice identity matching.
    /// Null is reserved for legacy rows that predate identity enforcement.
    /// </summary>
    [StringLength(50)]
    public string? NormalizedSellerTaxCode { get; set; }

    /// <summary>
    /// Invoice series normalized for durable invoice identity matching.
    /// </summary>
    [StringLength(50)]
    public string? NormalizedInvoiceSeries { get; set; }

    /// <summary>
    /// Invoice number normalized for durable invoice identity matching.
    /// </summary>
    [StringLength(50)]
    public string? NormalizedInvoiceNumber { get; set; }

    /// <summary>
    /// Date-only component used by the durable business identity.
    /// </summary>
    [Column(TypeName = "date")]
    public DateTime? InvoiceIdentityDate { get; set; }

    [StringLength(300)]
    public string? SellerName { get; set; }

    [StringLength(500)]
    public string? SellerAddress { get; set; }

    [StringLength(50)]
    public string? BuyerTaxCode { get; set; }

    /// <summary>Durable normalized evidence derived only from BuyerTaxCode.</summary>
    [StringLength(50)]
    public string? NormalizedBuyerTaxCode { get; private set; }

    public InputInvoiceBuyerOwnerResolutionStatus BuyerOwnerResolutionStatus { get; set; }
        = InputInvoiceBuyerOwnerResolutionStatus.NotEvaluated;

    public int? ResolvedBuyerLegalEntityId { get; set; }
    public LegalEntity? ResolvedBuyerLegalEntity { get; set; }

    public DateTime? BuyerOwnerResolutionUpdatedAtUtc { get; set; }

    [StringLength(300)]
    public string? BuyerName { get; set; }

    [StringLength(500)]
    public string? BuyerAddress { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalBeforeTax { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalTaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPaymentAmount { get; set; }

    /// <summary>
    /// Tên file XML gốc user upload.
    /// </summary>
    [StringLength(260)]
    public string? OriginalFileName { get; set; }

    /// <summary>
    /// Đường dẫn file XML nếu sau này lưu file vật lý.
    /// </summary>
    [StringLength(500)]
    public string? XmlFilePath { get; set; }

    /// <summary>
    /// Hash XML để chống upload trùng.
    /// </summary>
    [StringLength(128)]
    public string? XmlHash { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }

    /// <summary>
    /// Canonical historical Supplier for this invoice identity. It is not
    /// re-resolved when Supplier master data later changes.
    /// </summary>
    public int? ResolvedSupplierId { get; set; }
    public Supplier? ResolvedSupplier { get; set; }

    public InputInvoiceSupplierResolutionStatus SupplierResolutionStatus { get; set; }
        = InputInvoiceSupplierResolutionStatus.NotEvaluated;

    public DateTime? SupplierResolutionUpdatedAtUtc { get; set; }

    public ICollection<InputInvoiceDetail> Details { get; set; } = new List<InputInvoiceDetail>();

    public ICollection<StockDocumentInputInvoiceMap> StockDocumentMaps { get; set; } = new List<StockDocumentInputInvoiceMap>();

    public ICollection<InputInvoiceSupplierResolutionEvent> SupplierResolutionEvents { get; set; }
        = new List<InputInvoiceSupplierResolutionEvent>();
}
