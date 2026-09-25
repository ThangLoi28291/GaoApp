using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Purchases;

public static class PurchaseRequestIndexScopes
{
    public const string Store = "store";
    public const string Mine = "mine";
}

public static class PurchaseRequestIndexStates
{
    public const string All = "all";
    public const string Working = "working";
    public const string OrderReady = "order-ready";
    public const string Converted = "converted";
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Returned = "returned";
    public const string Rejected = "rejected";
    public const string Approved = "approved";
    public const string Partial = "partial";
    public const string Cancelled = "cancelled";
}

public sealed class PurchaseRequestIndexQueryRequest
{
    public string? Scope { get; set; }
    public string? Keyword { get; set; }
    public int? RequesterUserId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? State { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class PurchaseRequestIndexPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling(
        (double)TotalItems / Math.Max(1, PageSize));
    public PurchaseRequestIndexSummaryDto Summary { get; set; } = new();
    public List<PurchaseRequestIndexItemDto> Items { get; set; } = new();
}

public sealed class PurchaseRequestIndexSummaryDto
{
    public int TotalItems { get; set; }
    public int WorkingItems { get; set; }
    public int OrderReadyItems { get; set; }
    public int ConvertedItems { get; set; }
}

public sealed class PurchaseRequestIndexItemDto
{
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public DateTime? NeedByDate { get; set; }
    public PurchaseRequestStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusHint { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public int EligibleLineCount { get; set; }
    public int CompletedLineCount { get; set; }
    public int ProgressPercent => EligibleLineCount <= 0
        ? 0
        : Math.Min(100, (int)Math.Round(
            CompletedLineCount * 100d / EligibleLineCount));
}

public sealed class PurchaseRequestIndexRequesterOptionDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class PurchaseRequestIndexQuickViewDto
{
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public DateTime? NeedByDate { get; set; }
    public string RequestedByName { get; set; } = string.Empty;
    public PurchaseRequestStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusHint { get; set; } = string.Empty;
    public string? Note { get; set; }
    public int LineCount { get; set; }
    public int EligibleLineCount { get; set; }
    public int CompletedLineCount { get; set; }
    public int ProgressPercent => EligibleLineCount <= 0
        ? 0
        : Math.Min(100, (int)Math.Round(
            CompletedLineCount * 100d / EligibleLineCount));
    public List<PurchaseRequestIndexLineDto> Lines { get; set; } = new();
}

public sealed class PurchaseRequestIndexLineDto
{
    public int LineNo { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? ImageUrl { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public decimal? ApprovedQuantity { get; set; }
    public decimal ConvertedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal CurrentStockQuantity { get; set; }
    public decimal IncomingQuantity { get; set; }
}
