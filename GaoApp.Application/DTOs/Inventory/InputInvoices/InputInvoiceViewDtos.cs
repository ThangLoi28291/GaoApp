namespace GaoApp.Application.DTOs.Inventory.InputInvoices;

public sealed class InputInvoiceHeadDto
{
    public int Id { get; set; }

    public string? InvoiceTemplateCode { get; set; }
    public string? InvoiceSeries { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }

    public string? SellerTaxCode { get; set; }
    public string? SellerName { get; set; }
    public string? BuyerTaxCode { get; set; }
    public string? BuyerOwnerResolutionStatus { get; set; }
    public int? ResolvedBuyerLegalEntityId { get; set; }
    public string? ResolvedBuyerLegalEntityName { get; set; }
    public int? ReceiptOwnerLegalEntityId { get; set; }
    public string? ReceiptOwnerLegalEntityName { get; set; }
    public bool BuyerOwnerMatchesReceipt { get; set; }
    public string? OwnerWarningReasonCode { get; set; }
    public string? OwnerWarningMessage { get; set; }

    public decimal TotalBeforeTax { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public decimal TotalPaymentAmount { get; set; }

    public int DetailCount { get; set; }

    public List<InputInvoiceDetailDto> Details { get; set; } = new();
}

public sealed class InputInvoiceDetailDto
{
    public int Id { get; set; }
    public int LineNo { get; set; }
    public string? SupplierItemCode { get; set; }

    public string ItemName { get; set; } = "";
    public string? UnitName { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }

    public string? VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public InputInvoiceItemCatalogResolutionDto? ItemCatalogMapping { get; set; }
}
