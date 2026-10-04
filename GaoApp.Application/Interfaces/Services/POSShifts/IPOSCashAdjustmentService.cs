using GaoApp.Application.Common;
using GaoApp.Application.DTOs.POSShifts;

namespace GaoApp.Application.Interfaces.Services.POSShifts;

public interface IPOSCashAdjustmentService
{
    Task<Dictionary<int, int>> PendingAsync(int[] transactionIds, CancellationToken ct);
    Task<PagedResult<CashAdjustmentTransactionDto>> TransactionsAsync(int page, string? keyword, int? transactionId, int? shiftId, CancellationToken ct);
    Task<PagedResult<CashAdjustmentItemDto>> ListAsync(int page, string? status, int? shiftId, CancellationToken ct);
    Task<CashAdjustmentDetailDto> DetailAsync(int id, CancellationToken ct);
    Task<int> CreateAsync(int transactionId, CreateCashAdjustmentRequest request, CancellationToken ct);
    Task DecideAsync(int id, string action, CashAdjustmentDecision request, CancellationToken ct);
    Task ReconcileAsync(int shiftId, CashAdjustmentDecision request, CancellationToken ct);
}
