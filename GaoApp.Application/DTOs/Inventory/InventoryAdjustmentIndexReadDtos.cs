using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public static class InventoryAdjustmentIndexStates
{
    public const string All = "all";
    public const string Open = "open";
    public const string Working = "working";
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Rejected = "rejected";
    public const string Approved = "approved";
    public const string Cancelled = "cancelled";
}

public sealed class InventoryAdjustmentIndexQueryRequest
{
    public string? Keyword { get; set; }
    public int? WarehouseId { get; set; }
    public InventoryTransactionType? AdjustmentType { get; set; }
    public InventoryAdjustmentReasonType? ReasonType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? State { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class InventoryAdjustmentIndexPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling(
        (double)TotalItems / Math.Max(1, PageSize));
    public InventoryAdjustmentIndexSummaryDto Summary { get; set; } = new();
    public List<InventoryAdjustmentIndexItemDto> Items { get; set; } = new();
}

public sealed class InventoryAdjustmentIndexSummaryDto
{
    public int TotalItems { get; set; }
    public int WorkingItems { get; set; }
    public int PendingItems { get; set; }
    public int ApprovedItems { get; set; }
}

public sealed class InventoryAdjustmentIndexItemDto
{
    public int DocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public InventoryTransactionType AdjustmentType { get; set; }
    public string AdjustmentTypeLabel { get; set; } = string.Empty;
    public InventoryAdjustmentReasonType ReasonType { get; set; }
    public string ReasonTypeLabel { get; set; } = string.Empty;
    public InventoryAdjustmentDocumentStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public int TotalLines { get; set; }
    public string? Note { get; set; }
}

public sealed class InventoryAdjustmentIndexWarehouseOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class InventoryAdjustmentIndexQuickViewDto
{
    public int DocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public InventoryTransactionType AdjustmentType { get; set; }
    public string AdjustmentTypeLabel { get; set; } = string.Empty;
    public InventoryAdjustmentReasonType ReasonType { get; set; }
    public string ReasonTypeLabel { get; set; } = string.Empty;
    public InventoryAdjustmentDocumentStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? ApprovalNote { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public int TotalLines { get; set; }
    public List<InventoryAdjustmentIndexLineDto> Lines { get; set; } = new();
}

public sealed class InventoryAdjustmentIndexLineDto
{
    public string ProductName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
}
