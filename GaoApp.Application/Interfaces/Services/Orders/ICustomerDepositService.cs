using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
namespace GaoApp.Application.Interfaces.Services.Orders;

public interface ICustomerDepositService
{
    Task<DepositPageDto> GetAsync(int? customerId, CancellationToken ct, string? search = null, int page = 1, string? status = null);
    Task<DepositReceiptDto> GetReceiptAsync(int depositId, CancellationToken ct);
    Task<List<DepositBalanceDto>> GetAvailableAsync(int customerId, CancellationToken ct);
    Task<int> ReceiveAsync(ReceiveDepositRequest request, CancellationToken ct);
    Task<DepositQrPreviewDto> CreateReceiveQrAsync(CreateDepositQrRequest request, CancellationToken ct);
    Task<int> RefundAsync(RefundDepositRequest request, CancellationToken ct);
    Task SelectAsync(int orderId, SelectDepositRequest request, CancellationToken ct);
    Task ConsumeAsync(Order order, CancellationToken ct);
    Task RestoreReturnAsync(Order order, SalesReturn salesReturn, CancellationToken ct);
    Task<decimal> GetReturnableAsync(Order order, CancellationToken ct);
    Task RestoreVoidAsync(Order order, CancellationToken ct);
}
public sealed record ReceiveDepositRequest(Guid ClientRequestId, int CustomerId, decimal Amount, PaymentMethod Method,
    string Purpose, DateTime? ExpectedDeliveryDate, string? Reference, string? Note);
public sealed record CreateDepositQrRequest(Guid ClientRequestId, int CustomerId, decimal Amount);
public sealed record DepositQrPreviewDto(string BankName, string AccountNumber, string AccountName, decimal Amount,
    string Content, string QrDataUrl);
public sealed record RefundDepositRequest(Guid ClientRequestId, int DepositId, decimal Amount, PaymentMethod Method, string? Reference, string Note);
public sealed record SelectDepositRequest(int ExpectedCustomerId, int? DepositId, decimal Amount);
public sealed record DepositBalanceDto(int Id, string Purpose, decimal Balance, DateTime? ExpectedDeliveryDate);
public sealed record DepositJournalDto(int Id, int DepositId, DateTime At, string Kind, decimal Amount, PaymentMethod? Method,
    string? Bank, string? Reference, string? Note, int? OrderId, int ShiftId, int? Employee);
public sealed record DepositListItemDto(int Id, int CustomerId, string CustomerName, string? Phone,
    DateTime At, string Purpose, DateTime? ExpectedDeliveryDate, decimal Received, decimal Used, decimal Refunded, decimal Balance);
public sealed class DepositListDto
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; } = 25;
    public int TotalCount { get; set; }
    public int TotalPages => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
    public int ActiveCount { get; set; }
    public decimal Balance { get; set; }
    public decimal Received { get; set; }
    public decimal UsedAndRefunded { get; set; }
    public List<DepositListItemDto> Rows { get; set; } = new();
}
public sealed record DepositReceiptDto(int Id, string StoreName, string? StoreAddress, string? StorePhone,
    int CustomerId, string CustomerName, string? CustomerPhone, string? CustomerAddress, string Purpose,
    DateTime? ExpectedDeliveryDate, DateTime At, decimal Amount, decimal Balance, PaymentMethod? Method,
    string? Bank, string? Reference, string? Note, int ShiftId, int? EmployeeId, string? EmployeeName);
public sealed class DepositPageDto
{
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? DefaultBank { get; set; }
    public DepositListDto List { get; set; } = new();
    public List<DepositBalanceDto> Deposits { get; set; } = new();
    public List<DepositJournalDto> Journal { get; set; } = new();
}
