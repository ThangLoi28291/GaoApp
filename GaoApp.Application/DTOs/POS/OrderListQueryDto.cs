using GaoApp.Domain.Enums;

using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.POS;

public sealed class OrderListQueryDto
{
    public DateTime? FromDate { get; set; }  // ngày bắt đầu theo giờ Việt Nam (UTC+7)
    public DateTime? ToDate { get; set; }    // ngày kết thúc theo giờ Việt Nam (UTC+7)

    public OrderStatus? Status { get; set; } // null = tất cả
    public string? Keyword { get; set; }     // orderNumber, note...

    [StringLength(320)] public string? Employee { get; set; }
    [StringLength(200)] public string? Customer { get; set; }
    [StringLength(320)] public string? Terminal { get; set; }
    [Range(1, int.MaxValue)] public int? EmployeeId { get; set; }
    [Range(1, int.MaxValue)] public int? TerminalId { get; set; }
    [EnumDataType(typeof(OrderSettlementFilter))]
    public OrderSettlementFilter? Settlement { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public enum OrderSettlementFilter
{
    Cash, BankTransfer, Credit, OutstandingCredit, Mixed, Card, EWallet, Other
}

public sealed record OrderFilterOptionDto(int Id, string Name, string Code);
