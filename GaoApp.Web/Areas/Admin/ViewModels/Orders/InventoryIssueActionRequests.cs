using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Areas.Admin.ViewModels.Orders;

public sealed class LinkReceiptRequest
{
    [Range(1, int.MaxValue)]
    public int IssueId { get; set; }

    [Range(1, int.MaxValue)]
    public int IssueLineId { get; set; }

    [Range(1, int.MaxValue)]
    public int ReceiptId { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}

public sealed class LinkAdjustmentRequest
{
    [Range(1, int.MaxValue)]
    public int IssueId { get; set; }

    [Range(1, int.MaxValue)]
    public int IssueLineId { get; set; }

    [Range(1, int.MaxValue)]
    public int AdjustmentId { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}

public sealed class ReopenIssueRequest
{
    [Range(1, int.MaxValue)]
    public int IssueId { get; set; }

    public int? IssueLineId { get; set; }

    [Required, StringLength(1000)]
    public string Note { get; set; } = string.Empty;
}

public sealed class EscalateIssueRequest
{
    [Range(1, int.MaxValue)]
    public int IssueId { get; set; }

    public int? IssueLineId { get; set; }

    [Required, StringLength(1000)]
    public string Note { get; set; } = string.Empty;
}