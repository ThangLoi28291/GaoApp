using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class ReceiptInvoiceBackfillQuery
{
    public DateTime LinkedFromDate { get; set; } = new(2026, 9, 26);
    public DateTime? LinkedToDate { get; set; }
    [Range(1, 1000000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 50;
}

public sealed class ReceiptInvoiceBackfillRequest : ReceiptInvoiceBackfillQuery
{
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
    [Required, MinLength(1), MaxLength(50)] public List<ReceiptInvoiceBackfillItem> Items { get; set; } = [];
}

public sealed class ReceiptInvoiceBackfillItem
{
    [Range(1, int.MaxValue)] public int ReceiptId { get; set; }
    [Required, RegularExpression("^[A-F0-9]{64}$")] public string SnapshotHash { get; set; } = string.Empty;
}

public sealed class ReceiptInvoiceBackfillRow
{
    public int ReceiptId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public PurchaseReceiptSource ReceiptSource { get; set; }
    public int MapId { get; set; }
    public string XmlNumber { get; set; } = string.Empty;
    public DateTime LinkedAtUtc { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int PostedTransactionCount { get; set; }
    public int ProductCount { get; set; }
    public int ProductCountNeedingConfirmation { get; set; }
    public string Status { get; set; } = "Blocked";
    public string Message { get; set; } = string.Empty;
    public string SnapshotHash { get; set; } = string.Empty;
}

public sealed record ReceiptInvoiceBackfillPage(int Page, int PageSize, int TotalItems,
    IReadOnlyList<ReceiptInvoiceBackfillRow> Rows)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((decimal)TotalItems / PageSize));
    public int ReadyCount => Rows.Count(x => x.Status == "Ready");
    public int CompleteCount => Rows.Count(x => x.Status == "Complete");
    public int BlockedCount => Rows.Count(x => x.Status == "Blocked");
}

public sealed record ReceiptInvoiceBackfillResult(int ReceiptId, string Status, string Message);
