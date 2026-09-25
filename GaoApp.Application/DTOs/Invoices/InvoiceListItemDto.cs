namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceListItemDto
{
    public int Id { get; set; }

    public int? OrderId { get; set; }
    public long? LegacySourceId { get; set; }
    public long? LegacyOrderCategoryId { get; set; }
    public string? LegacyMergeId { get; set; }
    public bool LegacyReadOnly { get; set; }

    public int? LegalEntityId { get; set; }

    public string? LegalEntityCode { get; set; }

    public string? LegalEntityName { get; set; }

    public string? OrderNumber { get; set; }

    public string? InvoiceNumber { get; set; }

    public DateTime InvoiceDate { get; set; }

    public string? BuyerName { get; set; }

    public decimal TotalQuantity { get; set; }

    public decimal SubTotal { get; set; }

    public decimal VatAmount { get; set; }

    public decimal GrandTotal { get; set; }

    public int DetailCount { get; set; }

    public int AutoLineCount { get; set; }

    public int ManualLineCount { get; set; }

    /// <summary>
    /// Dùng để hiển thị badge "Đã khóa" / "Chưa khóa" ngoài danh sách.
    /// </summary>
    public bool IsLocked { get; set; }
}
