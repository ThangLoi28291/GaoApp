using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceHeadDto
{

    public int Id { get; set; }
    public int StoreId { get; set; }

    public int? OrderId { get; set; }
    public long? LegacySourceId { get; set; }
    public long? LegacyOrderCategoryId { get; set; }
    public string? LegacyMergeId { get; set; }
    public bool LegacyReadOnly { get; set; }

    public string? OrderNumber { get; set; }

    public int? LegalEntityId { get; set; }

    public string? LegalEntityCode { get; set; }

    public string? LegalEntityName { get; set; }

    public int? InvoiceProviderSettingId { get; set; }

    public string? InvoiceNumber { get; set; }

    public DateTime InvoiceDate { get; set; }

    public string? BuyerName { get; set; }

    public string? BuyerTaxCode { get; set; }

    public string? BuyerAddress { get; set; }
    public string BuyerType { get; set; } = "NoInvoice";

    public string? BuyerLegalName { get; set; }

    public string? BuyerEmail { get; set; }

    public string? BuyerPhone { get; set; }

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
    public InvoiceFileDownloadStatus OfficialPdfStatus { get; set; }

    public DateTime? OfficialPdfDownloadedAtUtc { get; set; }

    public string? OfficialPdfFileName { get; set; }

    public InvoiceFileDownloadStatus OfficialZipXmlStatus { get; set; }

    public DateTime? OfficialZipXmlDownloadedAtUtc { get; set; }

    public string? OfficialZipXmlFileName { get; set; }

    public InvoiceEmailSendStatus EmailStatus { get; set; }

    public DateTime? EmailSentAtUtc { get; set; }

    public string? LastEmailTo { get; set; }

    public int EmailSendCount { get; set; }

    public string? LastEmailErrorMessage { get; set; }
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
