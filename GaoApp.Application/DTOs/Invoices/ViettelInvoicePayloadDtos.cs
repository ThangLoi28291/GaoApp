using GaoApp.Domain.Enums;
using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Invoices;

public class ViettelInvoicePayloadResultDto
{
    public int InvoiceHeadId { get; set; }
    public int StoreId { get; set; }

    public int? LegalEntityId { get; set; }

    public string? LegalEntityCode { get; set; }

    public string? LegalEntityName { get; set; }

    public int? InvoiceProviderSettingId { get; set; }

    public int? OrderId { get; set; }

    public string? OrderNumber { get; set; }

    public string SupplierTaxCode { get; set; } = string.Empty;

    public string TemplateCode { get; set; } = string.Empty;

    public string InvoiceSeries { get; set; } = string.Empty;

    public string TransactionUuid { get; set; } = string.Empty;

    public decimal TotalAmountWithoutTax { get; set; }

    public decimal TotalTaxAmount { get; set; }

    public decimal TotalAmountWithTax { get; set; }

    public List<string> Warnings { get; set; } = new();
    public int ProviderStatus { get; set; }

    public string ProviderStatusName { get; set; } = string.Empty;

    public string ProviderStatusBadgeClass { get; set; } = "bg-secondary";

    public string? ProviderInvoiceNo { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTime? IssuedAtUtc { get; set; }

    public DateTime? LastSyncedAtUtc { get; set; }

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

    public bool IsIssued { get; set; }

    public bool HasOfficialPdf { get; set; }

    public bool HasOfficialZip { get; set; }

    public bool CanPreviewDraft { get; set; }

    public bool CanIssue { get; set; }

    public bool CanSyncByUuid { get; set; }

    public bool CanDownloadOfficialFiles { get; set; }

    public bool CanViewSavedPdf { get; set; }

    public bool CanDownloadSavedZip { get; set; }

    public bool RequiresUuidSyncBeforeIssue { get; set; }

    public string? IssueBlockReason { get; set; }
    public string? BuyerEmail { get; set; }

    public bool CanSendEmail { get; set; }
    public ViettelInvoicePayloadDto Payload { get; set; } = new();


    public string Json { get; set; } = string.Empty;
    public List<InvoiceIntegrationLogItemDto> Logs { get; set; } = new();
}

public class ViettelInvoicePayloadDto
{
    [JsonPropertyName("generalInvoiceInfo")]
    public ViettelGeneralInvoiceInfoDto GeneralInvoiceInfo { get; set; } = new();

    [JsonPropertyName("buyerInfo")]
    public ViettelBuyerInfoDto BuyerInfo { get; set; } = new();

    [JsonPropertyName("sellerInfo")]
    public object SellerInfo { get; set; } = new { };

    [JsonPropertyName("payments")]
    public List<ViettelPaymentDto> Payments { get; set; } = new();

    [JsonPropertyName("itemInfo")]
    public List<ViettelItemInfoDto> ItemInfo { get; set; } = new();

    [JsonPropertyName("metadata")]
    public List<ViettelMetadataDto> Metadata { get; set; } = new();

    [JsonPropertyName("summarizeInfo")]
    public ViettelSummarizeInfoDto SummarizeInfo { get; set; } = new();

    [JsonPropertyName("taxBreakdowns")]
    public List<ViettelTaxBreakdownDto> TaxBreakdowns { get; set; } = new();
}

public class ViettelGeneralInvoiceInfoDto
{
    [JsonPropertyName("invoiceType")]
    public string InvoiceType { get; set; } = "1";

    [JsonPropertyName("templateCode")]
    public string TemplateCode { get; set; } = string.Empty;

    [JsonPropertyName("invoiceSeries")]
    public string InvoiceSeries { get; set; } = string.Empty;

    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = "VND";

    [JsonPropertyName("adjustmentType")]
    public string AdjustmentType { get; set; } = "1";

    [JsonPropertyName("paymentStatus")]
    public bool PaymentStatus { get; set; } = true;

    [JsonPropertyName("cusGetInvoiceRight")]
    public bool CusGetInvoiceRight { get; set; } = true;

    [JsonPropertyName("transactionUuid")]
    public string TransactionUuid { get; set; } = string.Empty;

    [JsonPropertyName("invoiceIssuedDate")]
    public long? InvoiceIssuedDate { get; set; }

    [JsonPropertyName("invoiceNote")]
    public string? InvoiceNote { get; set; }

    [JsonPropertyName("validation")]
    public int? Validation { get; set; }
    [JsonPropertyName("adjustedNote")]
    public string? AdjustedNote { get; set; }

    [JsonPropertyName("adjustmentInvoiceType")]
    public string? AdjustmentInvoiceType { get; set; }

    [JsonPropertyName("originalInvoiceId")]
    public string? OriginalInvoiceId { get; set; }

    [JsonPropertyName("originalInvoiceIssueDate")]
    public long? OriginalInvoiceIssueDate { get; set; }

    [JsonPropertyName("additionalReferenceDesc")]
    public string? AdditionalReferenceDesc { get; set; }

    [JsonPropertyName("additionalReferenceDate")]
    public long? AdditionalReferenceDate { get; set; }
}

public class ViettelBuyerInfoDto
{
    [JsonPropertyName("buyerNotGetInvoice")]
    public int? BuyerNotGetInvoice { get; set; }
    [JsonPropertyName("buyerName")]
    public string BuyerName { get; set; } = "Người mua không lấy hoá đơn";

    [JsonPropertyName("buyerLegalName")]
    public string? BuyerLegalName { get; set; }

    [JsonPropertyName("buyerTaxCode")]
    public string? BuyerTaxCode { get; set; }

    [JsonPropertyName("buyerAddressLine")]
    public string? BuyerAddressLine { get; set; }

    [JsonPropertyName("buyerPhoneNumber")]
    public string? BuyerPhoneNumber { get; set; }

    [JsonPropertyName("buyerEmail")]
    public string? BuyerEmail { get; set; }
}

public class ViettelPaymentDto
{
    [JsonPropertyName("paymentMethodName")]
    public string PaymentMethodName { get; set; } = "TM";
}

public class ViettelItemInfoDto
{
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    [JsonPropertyName("selection")]
    public int Selection { get; set; } = 1;

    [JsonPropertyName("itemCode")]
    public string? ItemCode { get; set; }

    [JsonPropertyName("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [JsonPropertyName("unitName")]
    public string? UnitName { get; set; }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("itemTotalAmountWithoutTax")]
    public decimal ItemTotalAmountWithoutTax { get; set; }

    [JsonPropertyName("itemTotalAmountWithTax")]
    public decimal ItemTotalAmountWithTax { get; set; }

    [JsonPropertyName("itemTotalAmountAfterDiscount")]
    public decimal ItemTotalAmountAfterDiscount { get; set; }

    [JsonPropertyName("taxPercentage")]
    public decimal TaxPercentage { get; set; }

    [JsonPropertyName("taxAmount")]
    public decimal TaxAmount { get; set; }

    [JsonPropertyName("discount")]
    public decimal Discount { get; set; } = 0;

    [JsonPropertyName("itemDiscount")]
    public decimal ItemDiscount { get; set; } = 0;

    [JsonPropertyName("itemNote")]
    public string? ItemNote { get; set; }

    [JsonPropertyName("batchNo")]
    public string? BatchNo { get; set; } = "";

    [JsonPropertyName("expDate")]
    public string? ExpDate { get; set; } = "";
    [JsonPropertyName("isIncreaseItem")]
    public bool? IsIncreaseItem { get; set; }

    [JsonPropertyName("adjustmentTaxAmount")]
    public int? AdjustmentTaxAmount { get; set; }
}

public class ViettelMetadataDto
{
    [JsonPropertyName("keyTag")]
    public string KeyTag { get; set; } = string.Empty;

    [JsonPropertyName("valueType")]
    public string ValueType { get; set; } = "text";

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

public class ViettelSummarizeInfoDto
{
    [JsonPropertyName("sumOfTotalLineAmountWithoutTax")]
    public decimal SumOfTotalLineAmountWithoutTax { get; set; }

    [JsonPropertyName("totalAmountWithoutTax")]
    public decimal TotalAmountWithoutTax { get; set; }

    [JsonPropertyName("totalTaxAmount")]
    public decimal TotalTaxAmount { get; set; }

    [JsonPropertyName("totalAmountWithTax")]
    public decimal TotalAmountWithTax { get; set; }

    [JsonPropertyName("totalAmountWithTaxInWords")]
    public string TotalAmountWithTaxInWords { get; set; } = string.Empty;

    [JsonPropertyName("discountAmount")]
    public decimal DiscountAmount { get; set; } = 0;

    [JsonPropertyName("settlementDiscountAmount")]
    public decimal SettlementDiscountAmount { get; set; } = 0;
}

public class ViettelTaxBreakdownDto
{
    [JsonPropertyName("taxPercentage")]
    public decimal TaxPercentage { get; set; }

    [JsonPropertyName("taxableAmount")]
    public decimal TaxableAmount { get; set; }

    [JsonPropertyName("taxAmount")]
    public decimal TaxAmount { get; set; }
}
public class InvoiceIntegrationLogItemDto
{
    public int Id { get; set; }

    public string ActionName { get; set; } = string.Empty;

    public bool IsSuccess { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? RequestUrl { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public long? DurationMs { get; set; }
}
