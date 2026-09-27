namespace GaoApp.Application.DTOs.Invoices;

public sealed class InvoiceBuyerSelfServiceLinkDto
{
    public int OrderId { get; set; }

    /// <summary>
    /// Plaintext token chỉ trả về tại lúc tạo để encode vào QR.
    /// Database chỉ lưu SHA-256 hash.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public bool IsExpired { get; set; }
}

public sealed class InvoiceBuyerSelfServiceViewDto
{
    public int OrderId { get; set; }

    public string? OrderNumber { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public decimal GrandTotal { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public bool IsExpired { get; set; }

    public bool CanEdit { get; set; }

    public string BuyerType { get; set; } = "Individual";

    public string? BuyerName { get; set; }

    public string? BuyerLegalName { get; set; }

    public string? BuyerTaxCode { get; set; }

    public string? BuyerCitizenId { get; set; }

    public string? BuyerAddress { get; set; }

    public string? BuyerEmail { get; set; }

    public string? BuyerPhone { get; set; }

    public DateTime? LastSubmittedAtUtc { get; set; }
}

public sealed class SubmitInvoiceBuyerSelfServiceRequest
{
    public string BuyerType { get; set; } = "Individual";

    public string? BuyerName { get; set; }

    public string? BuyerLegalName { get; set; }

    public string? BuyerTaxCode { get; set; }

    public string? BuyerCitizenId { get; set; }

    public string? BuyerAddress { get; set; }

    public string? BuyerEmail { get; set; }

    public string? BuyerPhone { get; set; }
}