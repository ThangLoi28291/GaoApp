using GaoApp.Domain.Enums;
using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Inventory;

public static class InventoryLedgerIndexStates
{
    public const string Increase = "increase";
    public const string Decrease = "decrease";
    public const string Negative = "negative";
}

public sealed class InventoryLedgerIndexQueryRequest
{
    public int? WarehouseId { get; set; }
    public string? Keyword { get; set; }
    public InventoryTransactionType? TransactionType { get; set; }
    public InventoryReferenceType? ReferenceType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? ReferenceCode { get; set; }
    public string? State { get; set; }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public bool IncludeSummary { get; set; } = true;
}

public sealed class InventoryLedgerIndexPageDto
{
    public bool CanViewCost { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public bool SummaryIncluded { get; set; }
    public int TotalPages => (int)Math.Ceiling(
        (double)TotalItems / Math.Max(1, PageSize));
    public InventoryLedgerIndexSummaryDto Summary { get; set; } = new();
    public List<InventoryLedgerIndexItemDto> Items { get; set; } = new();
}

public sealed class InventoryLedgerIndexSummaryDto
{
    public int TotalItems { get; set; }
    public int IncreaseItems { get; set; }
    public int DecreaseItems { get; set; }
    public int NegativeItems { get; set; }
}

public sealed class InventoryLedgerIndexItemDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InventoryLedgerInboundCostDto? InboundCost { get; set; }
    public int TransactionId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? ImageUrl { get; set; }
    public InventoryTransactionType TransactionType { get; set; }
    public string TransactionTypeLabel { get; set; } = string.Empty;
    public InventoryReferenceType ReferenceType { get; set; }
    public string ReferenceTypeLabel { get; set; } = string.Empty;
    public string? ReferenceCode { get; set; }
    public decimal BeforeQty { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal AfterQty { get; set; }
    public bool IsNegativeAfterTransaction { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Note { get; set; }
}

public sealed class InventoryLedgerIndexQuickViewDto
{
    public InventoryLedgerIndexItemDto Item { get; set; } = new();
}

public sealed class InventoryLedgerInboundCostDto
{
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string BaseUnitName { get; set; } = string.Empty;
    public bool IsProvisional { get; set; }
    public bool IsMixedCost { get; set; }
}
