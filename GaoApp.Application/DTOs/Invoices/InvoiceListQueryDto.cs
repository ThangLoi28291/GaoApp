using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceListQueryDto
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public int? OrderId { get; set; }

    public string? Keyword { get; set; }

    /// <summary>
    /// Bộ lọc hiển thị:
    /// Tất cả, có số lượng, rỗng, đã khóa, chưa khóa...
    /// </summary>
    public InvoiceListDisplayMode DisplayMode { get; set; } = InvoiceListDisplayMode.All;

    /// <summary>
    /// Kiểu sắp xếp danh sách.
    /// Mặc định ưu tiên OrderId mới nhất lên trước.
    /// </summary>
    public InvoiceListSortMode SortMode { get; set; } = InvoiceListSortMode.OrderIdDesc;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}