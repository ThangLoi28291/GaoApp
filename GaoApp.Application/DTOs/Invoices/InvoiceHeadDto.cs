using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceHeadDto
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string? InvoiceNumber { get; set; }

    public DateTime InvoiceDate { get; set; }

    public string? BuyerName { get; set; }

    public string? BuyerTaxCode { get; set; }

    public string? BuyerAddress { get; set; }

    public decimal TotalQuantity { get; set; }

    public decimal SubTotal { get; set; }

    public decimal VatAmount { get; set; }

    public decimal GrandTotal { get; set; }

    public string? Note { get; set; }
    public bool IsLocked { get; set; }

    public DateTime? LockedAtUtc { get; set; }

    public int? LockedByUserId { get; set; }

    public string? LockReason { get; set; }
    public string? TransactionUuid { get; set; }

    public string? ProviderCode { get; set; }

    public string? SupplierTaxCode { get; set; }

    public string? InvoiceType { get; set; }

    public string? TemplateCode { get; set; }

    public string? InvoiceSeries { get; set; }

    public List<InvoiceDetailDto> Details { get; set; } = new();
    public InvoiceProviderStatus ProviderStatus { get; set; } = InvoiceProviderStatus.LocalDraft;

    public string? ProviderInvoiceNo { get; set; }

    public string? ProviderTransactionId { get; set; }

    public string? ReservationCode { get; set; }

    public string? CodeOfTax { get; set; }

    public DateTime? IssuedAtUtc { get; set; }

    public DateTime? LastSyncedAtUtc { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public string? PdfFilePath { get; set; }

    public string? ZipFilePath { get; set; }
    public int? OriginalInvoiceHeadId { get; set; }

    public string? OriginalInvoiceNo { get; set; }

    public DateTime? OriginalInvoiceIssuedAtUtc { get; set; }

    public InvoiceCorrectionType? CorrectionType { get; set; }

    public string? AdjustedNote { get; set; }

    public string? AdditionalReferenceDesc { get; set; }

    public DateTime? AdditionalReferenceDateUtc { get; set; }


    public bool IsCorrectionInvoice =>
    OriginalInvoiceHeadId.HasValue || CorrectionType.HasValue;

    public bool IsReplacementInvoice =>
        CorrectionType == InvoiceCorrectionType.Replacement;

    public bool IsAdjustmentAmountInvoice =>
        CorrectionType == InvoiceCorrectionType.AdjustmentAmount;

    public bool IsAdjustmentInfoInvoice =>
        CorrectionType == InvoiceCorrectionType.AdjustmentInfo;

    public string CorrectionTypeName
    {
        get
        {
            return CorrectionType switch
            {
                InvoiceCorrectionType.Replacement => "Hóa đơn thay thế",
                InvoiceCorrectionType.AdjustmentAmount => "Hóa đơn điều chỉnh tiền",
                InvoiceCorrectionType.AdjustmentInfo => "Hóa đơn điều chỉnh thông tin",
                _ => string.Empty
            };
        }
    }
}