using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Common;

/// <summary>Shared limits for interactive log searches, including callers outside MVC.</summary>
public static class LogReadLimits
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxKeywordLength = 200;

    public static int NormalizePageSize(int pageSize)
        => pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

    public static (int Page, int PageSize, int Offset) NormalizePage(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = NormalizePageSize(pageSize);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            throw new ValidationAppException("Số trang quá lớn. Vui lòng thu hẹp bộ lọc tra cứu log.");
        return (page, pageSize, (int)offset);
    }

    public static void ValidateText(string? value, int maxLength, string label)
    {
        if (value?.Length > maxLength)
            throw new ValidationAppException($"{label} tối đa {maxLength} ký tự.");
    }

    public static void ValidateDateRange(DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            throw new ValidationAppException("Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc.");
    }
}
