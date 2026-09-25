using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public static class StockCountIndexStates
{
    public const string All = "all";
    public const string Open = "open";
    public const string Working = "working";
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Rejected = "rejected";
    public const string Confirmed = "confirmed";
    public const string Cancelled = "cancelled";
}

public sealed class StockCountIndexQueryRequest
{
    public string? Keyword { get; set; }
    public int? WarehouseId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? State { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class StockCountIndexPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling(
        (double)TotalItems / Math.Max(1, PageSize));
    public StockCountIndexSummaryDto Summary { get; set; } = new();
    public List<StockCountIndexItemDto> Items { get; set; } = new();
}

public sealed class StockCountIndexSummaryDto
{
    public int TotalItems { get; set; }
    public int WorkingItems { get; set; }
    public int PendingItems { get; set; }
    public int ConfirmedItems { get; set; }
}

public sealed class StockCountIndexItemDto
{
    public int DocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string? DocumentName { get; set; }
    public DateTime DocumentDate { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public StockCountDocumentStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public int TotalLines { get; set; }
    public int DifferenceLines { get; set; }
    public string? Note { get; set; }
}

public sealed class StockCountIndexWarehouseOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class StockCountIndexQuickViewDto
{
    public int DocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string? DocumentName { get; set; }
    public DateTime DocumentDate { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public StockCountDocumentStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public int TotalLines { get; set; }
    public int DifferenceLines { get; set; }
    public int GainLines { get; set; }
    public int LossLines { get; set; }
    public List<StockCountIndexLineDto> Lines { get; set; } = new();
}

public sealed class StockCountIndexLineDto
{
    public int LineNo { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal SystemQtyBase { get; set; }
    public decimal CountedQty { get; set; }
    public decimal CountedQtyBase { get; set; }
    public decimal DifferenceQtyBase { get; set; }
}
