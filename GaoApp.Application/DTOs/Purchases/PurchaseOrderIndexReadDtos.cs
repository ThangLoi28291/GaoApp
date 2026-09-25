using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Purchases;

public static class PurchaseOrderIndexStates
{
    public const string Open = "open";
    public const string All = "all";
    public const string NeedsAction = "needs-action";
    public const string InProgress = "in-progress";
    public const string Completed = "completed";
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Returned = "returned";
    public const string Rejected = "rejected";
    public const string Approved = "approved";
    public const string Sent = "sent";
    public const string Receiving = "receiving";
    public const string FullyReceived = "fully-received";
    public const string ShortClosed = "short-closed";
    public const string Cancelled = "cancelled";
}

public static class PurchaseOrderIndexSources
{
    public const string All = "all";
    public const string Request = "request";
    public const string Direct = "direct";
}

public sealed class PurchaseOrderIndexQueryRequest
{
    public string? Keyword { get; set; }
    public int? LegalEntityId { get; set; }
    public int? SupplierId { get; set; }
    public int? WarehouseId { get; set; }
    public string? Source { get; set; }
    public string? State { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class PurchaseOrderIndexPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling(
        (double)TotalItems / Math.Max(1, PageSize));
    public bool CanViewCost { get; set; }
    public PurchaseOrderIndexSummaryDto Summary { get; set; } = new();
    public List<PurchaseOrderIndexItemDto> Items { get; set; } = new();
}

public sealed class PurchaseOrderIndexSummaryDto
{
    public int TotalItems { get; set; }
    public int NeedsActionItems { get; set; }
    public int InProgressItems { get; set; }
    public int CompletedItems { get; set; }
}

public sealed class PurchaseOrderIndexItemDto
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? SourcePurchaseRequestId { get; set; }
    public string? SourcePurchaseRequestNumber { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierCode { get; set; }
    public string LegalEntityName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public PurchaseOrderStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusHint { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public int EligibleLineCount { get; set; }
    public int ResolvedLineCount { get; set; }
    public int ReceivingLineCount { get; set; }
    public bool HasShortClosedLine { get; set; }
    public int ProgressPercent => EligibleLineCount <= 0
        ? 0
        : Math.Min(100, (int)Math.Round(
            ResolvedLineCount * 100d / EligibleLineCount));
    public decimal? TotalAfterVat { get; set; }
}

public sealed class PurchaseOrderIndexFilterOptionsDto
{
    public List<PurchaseOrderIndexOptionDto> LegalEntities { get; set; } = new();
    public List<PurchaseOrderIndexOptionDto> Suppliers { get; set; } = new();
    public List<PurchaseOrderIndexOptionDto> Warehouses { get; set; } = new();
}

public sealed class PurchaseOrderIndexOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public int? LegalEntityId { get; set; }
}

public sealed class PurchaseOrderIndexQuickViewDto
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? SourcePurchaseRequestNumber { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierCode { get; set; }
    public string LegalEntityName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public PurchaseOrderStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusHint { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? OutsideRequestReason { get; set; }
    public int LineCount { get; set; }
    public int EligibleLineCount { get; set; }
    public int ResolvedLineCount { get; set; }
    public int ReceivingLineCount { get; set; }
    public int ProgressPercent => EligibleLineCount <= 0
        ? 0
        : Math.Min(100, (int)Math.Round(
            ResolvedLineCount * 100d / EligibleLineCount));
    public bool CanViewCost { get; set; }
    public decimal? SubtotalBeforeVat { get; set; }
    public decimal? VatTotal { get; set; }
    public decimal? TotalAfterVat { get; set; }
    public List<PurchaseOrderIndexLineDto> Lines { get; set; } = new();
}

public sealed class PurchaseOrderIndexLineDto
{
    public int LineNo { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? ImageUrl { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal ShortClosedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public bool IsResolved { get; set; }
    public decimal? UnitPriceBeforeVat { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? LineTotalAfterVat { get; set; }
}
