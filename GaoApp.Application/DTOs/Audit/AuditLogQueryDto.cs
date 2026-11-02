namespace GaoApp.Application.DTOs.Audit;

using GaoApp.Application.Common;

/// <summary>
/// Bộ lọc tra cứu audit log.
/// </summary>
public class AuditLogQueryDto
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }

    public int? ActorUserId { get; set; }
    public int? Module { get; set; }
    public int? ActionType { get; set; }

    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? Keyword { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>Normalize paging and return a checked SQL offset before any read.</summary>
    public int NormalizeAndValidate()
    {
        var paging = LogReadLimits.NormalizePage(Page, PageSize);
        LogReadLimits.ValidateText(Keyword, LogReadLimits.MaxKeywordLength, "Từ khóa");
        LogReadLimits.ValidateText(EntityName, 150, "Tên đối tượng");
        LogReadLimits.ValidateText(EntityId, 100, "Mã đối tượng");
        LogReadLimits.ValidateDateRange(FromUtc, ToUtc);
        Page = paging.Page;
        PageSize = paging.PageSize;
        return paging.Offset;
    }
}
