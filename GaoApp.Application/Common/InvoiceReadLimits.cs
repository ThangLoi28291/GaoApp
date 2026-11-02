using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Common;

/// <summary>Interactive invoice reads, including service/repository callers outside MVC.</summary>
public static class InvoiceReadLimits
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxKeywordLength = 200;
    public const string InvalidQueryMessage = "Tham số tra cứu hóa đơn không hợp lệ. Vui lòng kiểm tra trang, ngày và bộ lọc.";

    public static (int Page, int PageSize, int Offset) NormalizePage(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            throw new ValidationAppException("Số trang quá lớn. Vui lòng thu hẹp bộ lọc hóa đơn.");
        return (page, pageSize, (int)offset);
    }

    public static void ValidateDates(DateTime? from, DateTime? to)
    {
        LogReadLimits.ValidateDateRange(from?.Date, to?.Date);
        // All three screens share dates that also support UTC log boundaries.
        // The first day underflows at UTC+7; the last has no exclusive next day.
        if (from?.Date == DateTime.MinValue.Date || to?.Date == DateTime.MinValue.Date ||
            from?.Date == DateTime.MaxValue.Date || to?.Date == DateTime.MaxValue.Date)
            throw new ValidationAppException("Khoảng ngày tra cứu hóa đơn không hợp lệ.");
    }

    public static string? NormalizeKeyword(string? value)
    {
        LogReadLimits.ValidateText(value, MaxKeywordLength, "Từ khóa");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static void ValidateEnum<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ValidationAppException(InvalidQueryMessage);
    }
}
