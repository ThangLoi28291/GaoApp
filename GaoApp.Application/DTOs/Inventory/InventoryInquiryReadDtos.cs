using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Inventory;

public static class InventoryInquiryStates
{
    public const string Positive = "positive";
    public const string Zero = "zero";
    public const string Negative = "negative";
}

public sealed class InventoryInquiryQueryRequest
{
    public int? WarehouseId { get; set; }
    public string? Keyword { get; set; }
    public string? State { get; set; }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class InventoryInquiryPageDto
{
    public bool CanViewCost { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling(
        (double)TotalItems / Math.Max(1, PageSize));
    public InventoryInquirySummaryDto Summary { get; set; } = new();
    public List<InventoryInquiryListItemDto> Items { get; set; } = new();
}

public sealed class InventoryInquirySummaryDto
{
    public int TotalItems { get; set; }
    public int NegativeItems { get; set; }
    public int PositiveItems { get; set; }
    public int ZeroItems { get; set; }
}

public sealed class InventoryInquiryListItemDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InventoryInquiryCostDto? Cost { get; set; }
    public int WarehouseId { get; set; }
    public int ProductVariantId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? ImageUrl { get; set; }
    public string? Barcode { get; set; }
    public decimal OnHandQty { get; set; }
    public decimal ReservedQty { get; set; }
    public decimal AvailableQty { get; set; }
    public string State { get; set; } = InventoryInquiryStates.Zero;
}

public sealed class InventoryInquiryQuickViewDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InventoryInquiryCostDto? Cost { get; set; }
    public int WarehouseId { get; set; }
    public int ProductVariantId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? ImageUrl { get; set; }
    public string? Barcode { get; set; }
    public string BaseUnitName { get; set; } = string.Empty;
    public decimal OnHandQty { get; set; }
    public decimal ReservedQty { get; set; }
    public decimal AvailableQty { get; set; }
    public string State { get; set; } = InventoryInquiryStates.Zero;
    public List<InventoryInquiryUnitDto> Units { get; set; } = new();
}

public sealed class InventoryInquiryUnitDto
{
    public string UnitName { get; set; } = string.Empty;
    public decimal Factor { get; set; }
    public string? Barcode { get; set; }
    public decimal SellPrice { get; set; }
    public bool IsBaseUnit { get; set; }
    public bool IsDefaultForSale { get; set; }
}

public sealed class InventoryInquiryCostDto
{
    public int WarehouseId { get; set; }
    public int ProductVariantId { get; set; }
    public string BaseUnitName { get; set; } = string.Empty;
    public decimal? NextFifoUnitCost { get; set; }
    public decimal? NextFifoRemainingQuantity { get; set; }
    public bool NextFifoIsProvisional { get; set; }
    public decimal? AverageUnitCost { get; set; }
    public decimal? InventoryValue { get; set; }
    public decimal? LastInboundUnitCost { get; set; }
    public DateTime? LastInboundAtUtc { get; set; }
    public bool HasProvisionalCost { get; set; }
}
