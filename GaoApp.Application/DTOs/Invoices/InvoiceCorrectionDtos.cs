using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceCorrectionCreateInfoDto
{
    public int OriginalInvoiceHeadId { get; set; }

    public string? OriginalInvoiceNo { get; set; }

    public string? ProviderInvoiceNo { get; set; }

    public DateTime InvoiceDate { get; set; }

    public DateTime? IssuedAtUtc { get; set; }

    public string? BuyerName { get; set; }

    public string? BuyerTaxCode { get; set; }

    public decimal GrandTotal { get; set; }

    public InvoiceCorrectionType Type { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public string DefaultAgreementDocumentNo { get; set; } = string.Empty;

    public DateTime DefaultAgreementDate { get; set; } = DateTime.Today;
}

public class CreateInvoiceCorrectionRequestDto
{
    public int OriginalInvoiceHeadId { get; set; }

    public InvoiceCorrectionType Type { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string AgreementDocumentNo { get; set; } = string.Empty;

    public DateTime AgreementDate { get; set; } = DateTime.Today;

    public string? Note { get; set; }
}

public class CreateInvoiceCorrectionResultDto
{
    public int CorrectionCaseId { get; set; }

    public int OriginalInvoiceHeadId { get; set; }

    public int NewInvoiceHeadId { get; set; }

    public InvoiceCorrectionType Type { get; set; }

    public string TypeName { get; set; } = string.Empty;
}