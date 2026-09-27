namespace GaoApp.Application.DTOs.Invoices;

public class UpdateInvoiceBuyerInfoRequest
{
    public int InvoiceHeadId { get; set; }

    /// <summary>
    /// NoInvoice / Individual / Business
    /// </summary>
    public string BuyerType { get; set; } = "NoInvoice";

    /// <summary>
    /// Cá nhân: tên khách hàng.
    /// Doanh nghiệp: người liên hệ/người mua nếu có.
    /// </summary>
    public string? BuyerName { get; set; }

    /// <summary>
    /// Doanh nghiệp: tên đơn vị/công ty/hộ kinh doanh.
    /// </summary>
    public string? BuyerLegalName { get; set; }

    /// <summary>
    /// MST / mã định danh.
    /// Không ép 10/13 số.
    /// </summary>
    public string? BuyerTaxCode { get; set; }
    public string? BuyerCitizenId { get; set; }

    // Null/omitted CitizenId preserves existing data. Clearing must be explicit.
    public bool ClearBuyerCitizenId { get; set; }

    public string? BuyerAddress { get; set; }

    public string? BuyerEmail { get; set; }

    public string? BuyerPhone { get; set; }

    /// <summary>
    /// Có lưu vào sổ hồ sơ xuất hóa đơn để dùng lần sau không.
    /// </summary>
    public bool SaveToProfile { get; set; } = true;

    /// <summary>
    /// manual / vietqr / customer / invoice-history
    /// </summary>
    public string Source { get; set; } = "manual";
}

public class InvoiceBuyerLookupDto
{
    public bool IsFound { get; set; }

    public string BuyerType { get; set; } = "Business";

    public string? BuyerName { get; set; }

    public string? BuyerLegalName { get; set; }

    public string? BuyerTaxCode { get; set; }
    public string? BuyerCitizenId { get; set; }

    public string? BuyerAddress { get; set; }

    public string? BuyerEmail { get; set; }

    public string? BuyerPhone { get; set; }

    public string Source { get; set; } = "none";
}