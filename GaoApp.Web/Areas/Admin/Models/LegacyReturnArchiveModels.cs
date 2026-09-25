namespace GaoApp.Web.Areas.Admin.Models;

public sealed class LegacyReturnArchiveRow
{
    public long LegacyOrderId { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public long? LegacyCustomerId { get; set; }
    public string? CustomerName { get; set; }
    public long? LegacyUserId { get; set; }
    public string? EmployeeName { get; set; }
    public decimal? SourceTotal { get; set; }
    public bool? SourcePaymentFlag { get; set; }
}

public sealed class LegacyReturnArchiveDetail
{
    public long LegacyOrderId { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public long? LegacyCustomerId { get; set; }
    public string? CustomerName { get; set; }
    public long? LegacyUserId { get; set; }
    public string? EmployeeName { get; set; }
    public decimal? SourceTotal { get; set; }
    public bool? SourcePaymentFlag { get; set; }
    public string HeaderJson { get; set; } = "{}";
    public string DetailsJson { get; set; } = "[]";
}

public sealed record LegacyReturnArchivePage(IReadOnlyList<LegacyReturnArchiveRow> Items, long Total, int Page, string? Search)
{
    public const int PageSize = 50;
}
