using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public static class StockTransferIndexStates
{
    public const string All = "all";
    public const string Open = "open";
    public const string Working = "working";
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Rejected = "rejected";
    public const string Confirmed = "confirmed";
}

public sealed class StockTransferIndexQueryRequest
{
    public string? Keyword { get; set; }
    public int? FromWarehouseId { get; set; }
    public int? ToWarehouseId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? State { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class StockTransferIndexPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling(
        (double)TotalItems / Math.Max(1, PageSize));
    public StockTransferIndexSummaryDto Summary { get; set; } = new();
    public List<StockTransferIndexItemDto> Items { get; set; } = new();
}

public sealed class StockTransferIndexSummaryDto
{
    public int TotalItems { get; set; }
    public int WorkingItems { get; set; }
    public int PendingItems { get; set; }
    public int ConfirmedItems { get; set; }
}

public sealed class StockTransferIndexItemDto
{
    public int DocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public string FromWarehouseName { get; set; } = string.Empty;
    public string ToWarehouseName { get; set; } = string.Empty;
    public StockTransferDocumentStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public int TotalLines { get; set; }
    public decimal TotalQuantity { get; set; }
    public string? Note { get; set; }
}

public sealed class StockTransferIndexQuickViewDto
{
    public int DocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public string FromWarehouseName { get; set; } = string.Empty;
    public string ToWarehouseName { get; set; } = string.Empty;
    public StockTransferDocumentStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public int TotalLines { get; set; }
    public decimal TotalQuantity { get; set; }
    public List<StockTransferIndexLineDto> Lines { get; set; } = new();
}

public sealed class StockTransferIndexLineDto
{
    public int LineNo { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? ImageUrl { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
}
