using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Orders;

public sealed class DebtCollectionQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? CustomerId { get; set; }
    public string? Search { get; set; }
    public PaymentMethod? Method { get; set; }
    public int? UserId { get; set; }
    public int? ShiftId { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class DebtCollectionReportDto
{
    public DebtCollectionQuery Query { get; set; } = new();
    public int PageSize { get; set; } = 50;
    public int TotalReceipts { get; set; }
    public int CustomerCount { get; set; }
    public decimal CashTotal { get; set; }
    public decimal BankTotal { get; set; }
    public decimal Total => CashTotal + BankTotal;
    public string? CustomerName { get; set; }
    public List<DebtCollectorDto> Collectors { get; set; } = [];
    public List<DebtCollectionRowDto> Rows { get; set; } = [];
}

public sealed record DebtCollectorDto(int Id, string Name);
public sealed record DebtCollectionRowDto(int Id, DateTime At, int CustomerId, string CustomerName,
    string? Phone, decimal Amount, PaymentMethod Method, string? Bank, string? Reference,
    string? Note, int? UserId, string? UserName, int ShiftId);
public sealed record DebtReceiptAllocationDto(int OrderId, string? OrderNumber, decimal Amount);
public sealed record DebtReceiptDetailDto(string StoreName, DebtCollectionRowDto Receipt, List<DebtReceiptAllocationDto> Allocations);
