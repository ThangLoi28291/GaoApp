namespace GaoApp.Application.DTOs.Inventory.InputInvoices;

public sealed class InputInvoicePickerBrowseRequest
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Search { get; set; }
}

public sealed class InputInvoicePickerContextDto
{
    public int StockDocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string ReceiptStatus { get; set; } = string.Empty;
    public int SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierTaxCode { get; set; } = string.Empty;
    public bool IsConfirmed { get; set; }
    public int ReceiptOwnerLegalEntityId { get; set; }
    public string ReceiptOwnerLegalEntityName { get; set; } = string.Empty;
    public int? CurrentInputInvoiceHeadId { get; set; }
    public bool IsRelinkAvailable { get; set; }
}

public sealed class InputInvoicePickerBrowseResultDto
{
    public InputInvoicePickerContextDto Context { get; set; } = new();
    public List<InputInvoicePickerCandidateDto> Candidates { get; set; } = [];
}

public sealed class InputInvoicePickerCandidateDto
{
    public string DocumentKey { get; set; } = string.Empty;
    public DateTime? InvoiceDate { get; set; }
    public string? InvoiceTemplateCode { get; set; }
    public string? InvoiceSeries { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? SellerTaxCode { get; set; }
    public string? SellerName { get; set; }
    public string? BuyerTaxCode { get; set; }
    public string BuyerOwnerResolutionStatus { get; set; } = "NotEvaluated";
    public int? ResolvedBuyerLegalEntityId { get; set; }
    public string? ResolvedBuyerLegalEntityName { get; set; }
    public bool BuyerOwnerMatchesReceipt { get; set; }
    public decimal TotalPaymentAmount { get; set; }
    public int DetailCount { get; set; }
    public bool HasPdf { get; set; }
    public bool HasXml { get; set; }
    public bool XmlValid { get; set; }
    public bool FolderTaxCodeMatches { get; set; }
    public bool SelectionAllowed { get; set; }
    public string? SelectionBlockReasonCode { get; set; }
    public string? SelectionBlockMessage { get; set; }
    public bool LinkedCurrentReceipt { get; set; }
    public int LinkedOtherReceiptCount { get; set; }
    public bool RequiresRelinkReason { get; set; }
}

public sealed class InputInvoicePdfPreviewDto
{
    public byte[] Content { get; set; } = [];
    public string FileName { get; set; } = "invoice.pdf";
}

public sealed class InputInvoiceXmlPreviewDto
{
    public string? InvoiceTemplateCode { get; set; }
    public string? InvoiceSeries { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public string? TaxAuthorityCode { get; set; }
    public string? SellerName { get; set; }
    public string? SellerTaxCode { get; set; }
    public string? SellerAddress { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerTaxCode { get; set; }
    public string? BuyerAddress { get; set; }
    public string BuyerOwnerResolutionStatus { get; set; } = "NotEvaluated";
    public int? ResolvedBuyerLegalEntityId { get; set; }
    public string? ResolvedBuyerLegalEntityName { get; set; }
    public bool BuyerOwnerMatchesReceipt { get; set; }
    public decimal TotalBeforeTax { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public decimal TotalPaymentAmount { get; set; }
    public List<InputInvoiceXmlPreviewLineDto> Lines { get; set; } = [];
}

public sealed class InputInvoiceXmlPreviewLineDto
{
    public int LineNo { get; set; }
    public string? SupplierItemCode { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }
    public string? VatRate { get; set; }
    public decimal VatAmount { get; set; }
}

public sealed class SelectInputInvoiceDocumentRequest
{
    public string DocumentKey { get; set; } = string.Empty;
}

public sealed class InputInvoicePickerSelectionResultDto
{
    public int InputInvoiceHeadId { get; set; }
    public int StockDocumentId { get; set; }
    public string? InvoiceSeries { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal TotalPaymentAmount { get; set; }
    public bool IsExistingInvoice { get; set; }
    public bool WasAlreadyLinked { get; set; }
}

public sealed record InputInvoiceBusinessIdentityDto(
    string NormalizedSellerTaxCode,
    string NormalizedInvoiceSeries,
    string NormalizedInvoiceNumber,
    DateTime InvoiceIdentityDate);
