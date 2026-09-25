using GaoApp.Application.DTOs.Inventory.InputInvoices;

namespace GaoApp.Application.DTOs.Inventory;

public sealed class PurchaseReceiptSplitWorkspaceDto
{
    public int SourceReceiptId { get; set; }
    public string SourceDocumentNo { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public int SupplierId { get; set; }
    public int? SourceInvoiceHeadId { get; set; }
    public string? SourceInvoiceLabel { get; set; }
    public bool IsMerchandisePaid { get; set; }
    public string? MerchandisePayeeName { get; set; }
    public decimal FreightTotal { get; set; }
    public List<PurchaseReceiptSplitWorkspaceLineDto> Lines { get; set; } = [];
    public List<PurchaseReceiptSplitSupplierOptionDto> Suppliers { get; set; } = [];
    public List<PurchaseReceiptSplitWarehouseOptionDto> Warehouses { get; set; } = [];
}

public sealed class PurchaseReceiptSplitWorkspaceLineDto
{
    public int SourceLineId { get; set; }
    public int LineNo { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? UnitName { get; set; }
    public decimal Factor { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class PurchaseReceiptSplitSupplierOptionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
}

public sealed class PurchaseReceiptSplitWarehouseOptionDto
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class PurchaseReceiptSplitRequest
{
    public string SourceRowVersion { get; set; } = string.Empty;
    public int? SourceInvoiceHeadId { get; set; }
    public List<PurchaseReceiptSplitLineSnapshotDto> SourceLines { get; set; } = [];
    public List<PurchaseReceiptSplitTargetRequest> Targets { get; set; } = [];
}

public sealed class PurchaseReceiptSplitLineSnapshotDto
{
    public int SourceLineId { get; set; }
    public decimal BaseQuantity { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class PurchaseReceiptSplitTargetRequest
{
    public int TargetIndex { get; set; }
    public int WarehouseId { get; set; }
    public int SupplierId { get; set; }
    public bool? IsMerchandisePaid { get; set; }
    public string? MerchandisePayeeName { get; set; }
    public bool PaymentStateConfirmed { get; set; }
    public int? ExistingInputInvoiceHeadId { get; set; }
    public string? InvoiceDocumentKey { get; set; }
    public List<PurchaseReceiptSplitLineAllocationDto> Allocations { get; set; } = [];
}

public sealed class PurchaseReceiptSplitLineAllocationDto
{
    public int SourceLineId { get; set; }
    public decimal BaseQuantity { get; set; }
}

public sealed class PurchaseReceiptSplitResultDto
{
    public List<PurchaseReceiptSplitResultLinkDto> Results { get; set; } = [];
}

public sealed class PurchaseReceiptSplitResultLinkDto
{
    public int TargetIndex { get; set; }
    public int StockDocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed class PurchaseReceiptSplitInvoiceBrowseDto
{
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierTaxCode { get; set; } = string.Empty;
    public List<InputInvoicePickerCandidateDto> Candidates { get; set; } = [];
}
