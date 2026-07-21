using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POS;

public sealed class OrderListQueryDto
{
    public DateTime? FromDate { get; set; }  // ngày bắt đầu (local)
    public DateTime? ToDate { get; set; }    // ngày kết thúc (local)

    public OrderStatus? Status { get; set; } // null = tất cả
    public string? Keyword { get; set; }     // orderNumber, note...

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}