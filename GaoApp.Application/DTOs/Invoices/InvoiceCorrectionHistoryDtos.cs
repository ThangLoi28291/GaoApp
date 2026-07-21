using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceCorrectionHistoryDto
{
    public int CurrentInvoiceHeadId { get; set; }

    public int OriginalInvoiceHeadId { get; set; }

    public string? OriginalInvoiceNo { get; set; }

    public bool IsCurrentOriginal { get; set; }

    public List<InvoiceCorrectionHistoryItemDto> Items { get; set; } = new();

    public bool HasItems => Items.Count > 0;
}

public class InvoiceCorrectionHistoryItemDto
{
    public int CorrectionCaseId { get; set; }

    public int OriginalInvoiceHeadId { get; set; }

    public int? NewInvoiceHeadId { get; set; }

    public string? NewInvoiceNo { get; set; }

    public string? NewProviderInvoiceNo { get; set; }

    public InvoiceCorrectionType Type { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public string TypeBadgeClass { get; set; } = "bg-secondary";

    public InvoiceCorrectionStatus Status { get; set; }

    public string StatusName { get; set; } = string.Empty;

    public string StatusBadgeClass { get; set; } = "bg-secondary";

    public InvoiceProviderStatus? ProviderStatus { get; set; }

    public string? ProviderStatusName { get; set; }

    public string? Reason { get; set; }

    public string? AgreementDocumentNo { get; set; }

    public DateTime? AgreementDateUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? IssuedAtUtc { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public bool IsIssuedProviderInvoice =>
        !string.IsNullOrWhiteSpace(NewProviderInvoiceNo) ||
        ProviderStatus is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
}