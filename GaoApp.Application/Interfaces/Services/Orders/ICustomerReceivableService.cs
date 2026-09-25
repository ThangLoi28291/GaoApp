using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Orders;

public interface ICustomerReceivableService
{
    Task<decimal> GetBalanceAsync(int customerId, CancellationToken ct);

    Task LockOrderAsync(int orderId, CancellationToken ct);
    Task ValidateCreditAsync(Order order, FinalizeCreditRequest request, CancellationToken ct);
    Task PostSaleAsync(Order order, CancellationToken ct);
    Task PostReturnAsync(Order order, SalesReturn salesReturn, CancellationToken ct);
    Task VoidAsync(Order order, CancellationToken ct);
    Task<ReceivablePageDto> GetAsync(int? customerId, string? search, CancellationToken ct);
    Task<int> CollectAsync(CollectCustomerDebtRequest request, CancellationToken ct);
}

public sealed class CollectCustomerDebtRequest
{
    public Guid ClientRequestId { get; set; }
    public int CustomerId { get; set; }
    public int? OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public int? StoreBankAccountId { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
}

public sealed class ReceivablePageDto
{
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public List<ReceivableCustomerDto> Customers { get; set; } = new();
    public List<ReceivableOrderDto> Orders { get; set; } = new();
    public List<ReceivableJournalDto> Journal { get; set; } = new();
    public List<DebtReceiptDto> Receipts { get; set; } = new();
    public List<DebtBankDto> Banks { get; set; } = new();
    public List<DebtRefundDto> Refunds { get; set; } = new();
}
public sealed record ReceivableCustomerDto(int Id, string Name, string? Phone, decimal Balance, decimal Overdue);
public sealed record ReceivableOrderDto(int Id, string? Number, decimal Total, decimal Paid, decimal Balance, DateTime? DueDate);
public sealed record ReceivableJournalDto(int Id, DateTime At, string Kind, string? OrderNumber, int? ReceiptId, decimal Amount, string? Note, int? UserId);
public sealed record DebtReceiptDto(int Id, DateTime At, decimal Amount, string Method, string? Bank, string? Reference, string? Note, int? UserId, int ShiftId);
public sealed record DebtBankDto(int Id, string Label);
public sealed record DebtRefundDto(string Number, DateTime At, decimal Amount, string Method, string? Reference, int ShiftId);
