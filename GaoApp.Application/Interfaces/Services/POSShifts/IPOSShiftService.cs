using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Common;
namespace GaoApp.Application.Interfaces.Services.POSShifts;

public interface IPOSShiftService
{
    Task<POSShiftDto?> GetCurrentOpenAsync(CancellationToken ct = default);
    Task<POSShiftDto> OpenAsync(OpenShiftRequest req, CancellationToken ct = default);
    Task<POSShiftDto> CloseAsync(CloseShiftRequest req, CancellationToken ct = default);
    Task<POSShiftCashTransactionDto> AddCashTransactionAsync(CreatePosShiftCashTransactionRequest dto, CancellationToken ct = default);
    Task<List<POSShiftCashTransactionDto>> GetCashTransactionsAsync(CancellationToken ct = default);
    Task<POSShiftSummaryDto> GetSummaryAsync(int? shiftId = null, CancellationToken ct = default);
    Task<PagedResult<POSShiftHistoryItemDto>> QueryHistoryAsync(
        QueryPOSShiftHistoryRequest req,
        CancellationToken ct = default);
}