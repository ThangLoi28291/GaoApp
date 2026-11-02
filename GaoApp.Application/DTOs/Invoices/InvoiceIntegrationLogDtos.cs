using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;

public class InvoiceIntegrationLogQueryDto
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public int? InvoiceHeadId { get; set; }

    public int? OrderId { get; set; }

    public string? Keyword { get; set; }

    public InvoiceIntegrationActionType? ActionType { get; set; }

    public bool? IsSuccess { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public int NormalizeAndValidate()
    {
        var paging = LogReadLimits.NormalizePage(Page, PageSize);
        LogReadLimits.ValidateText(Keyword, LogReadLimits.MaxKeywordLength, "Từ khóa");
        LogReadLimits.ValidateDateRange(FromDate?.Date, ToDate?.Date);
        // These local dates cannot be converted to the UTC half-open interval:
        // year 1 starts before UTC's minimum; the last date has no next day.
        if (FromDate?.Date == DateTime.MinValue.Date || ToDate?.Date == DateTime.MinValue.Date ||
            ToDate?.Date == DateTime.MaxValue.Date)
            throw new ValidationAppException("Khoảng ngày tra cứu log không hợp lệ.");
        Page = paging.Page;
        PageSize = paging.PageSize;
        return paging.Offset;
    }
}

public class InvoiceIntegrationLogListItemDto
{
    public int Id { get; set; }

    public int InvoiceHeadId { get; set; }

    public int? OrderId { get; set; }

    public string? InvoiceNumber { get; set; }

    public string? ProviderInvoiceNo { get; set; }

    public InvoiceIntegrationActionType ActionType { get; set; }

    public string ActionName { get; set; } = string.Empty;

    public bool IsSuccess { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? RequestUrl { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public long? DurationMs { get; set; }
}

public class InvoiceIntegrationLogDetailDto
{
    public int Id { get; set; }

    public int InvoiceHeadId { get; set; }

    public int? OrderId { get; set; }

    public string? InvoiceNumber { get; set; }

    public string? ProviderInvoiceNo { get; set; }

    public InvoiceIntegrationActionType ActionType { get; set; }

    public string ActionName { get; set; } = string.Empty;

    public bool IsSuccess { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? RequestUrl { get; set; }

    public string? RequestBody { get; set; }

    public string? ResponseBody { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public long? DurationMs { get; set; }
}
