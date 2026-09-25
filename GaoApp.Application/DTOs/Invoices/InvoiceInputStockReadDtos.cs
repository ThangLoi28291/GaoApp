namespace GaoApp.Application.DTOs.Invoices;

public sealed class InvoiceInputStockQuery
{
    public string View { get; set; } = "balance";
    public int? WarehouseId { get; set; }
    public int? ProductVariantId { get; set; }
    public string? Keyword { get; set; }
    public string? Kind { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class InvoiceInputStockMovement
{
    public string Key { get; set; } = "";
    public int WarehouseId { get; set; }
    public int? LegalEntityId { get; set; }
    public int ProductVariantId { get; set; }
    public string ProductName { get; set; } = "";
    public string Code { get; set; } = "";
    public string BaseUnit { get; set; } = "";
    public string WarehouseName { get; set; } = "";
    public string LegalEntityName { get; set; } = "";
    public DateTime DateUtc { get; set; }
    public string Kind { get; set; } = "";
    public decimal Change { get; set; }
    public bool IsOpening { get; set; }
    public decimal Held { get; set; }
    public decimal Before { get; set; }
    public decimal After { get; set; }
    public int? InvoiceHeadId { get; set; }
    public int? StockDocumentId { get; set; }
    public string SourceCode { get; set; } = "";
    public string? LegacySourceKey { get; set; }
    public string? LegacyOrderId { get; set; }
    public string? LegacyInvoiceNumber { get; set; }
    public string? LegacyInvoiceSymbol { get; set; }
    public string? OperationLabel { get; set; }
    public string? XmlNumber { get; set; }
    public string Note { get; set; } = "";
}

public sealed class InvoiceInputStockBalance
{
    public int WarehouseId { get; set; }
    public int ProductVariantId { get; set; }
    public string ProductName { get; set; } = "";
    public string Code { get; set; } = "";
    public string BaseUnit { get; set; } = "";
    public string WarehouseName { get; set; } = "";
    public string LegalEntityName { get; set; } = "";
    public decimal Opening { get; set; }
    public decimal Received { get; set; }
    public decimal Issued { get; set; }
    public decimal Held { get; set; }
    public decimal Remaining => Opening + Received - Issued;
    public decimal Available => Remaining - Held;
}

public sealed class InvoiceInputStockPage
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((decimal)TotalItems / PageSize));
    public int ProductCount { get; set; }
    public int IncreaseCount { get; set; }
    public int DecreaseCount { get; set; }
    public int HoldCount { get; set; }
    public int NegativeCount { get; set; }
    public IReadOnlyList<InvoiceInputStockBalance> Balances { get; set; } = [];
    public IReadOnlyList<InvoiceInputStockMovement> Movements { get; set; } = [];
    public IReadOnlyList<InvoiceInputStockWarehouse> Warehouses { get; set; } = [];
}

public sealed record InvoiceInputStockWarehouse(int Id, string Name);
