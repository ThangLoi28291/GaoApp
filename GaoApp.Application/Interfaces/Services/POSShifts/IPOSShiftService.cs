using GaoApp.Application.Common;
using GaoApp.Application.DTOs.POSShiftDashboards;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Domain.Enums;
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
    Task<POSShiftOwnershipInfoDto?> GetCurrentOwnershipInfoAsync(CancellationToken ct = default);

    Task<POSShiftDto> TakeOverAsync(
        TakeOverPOSShiftRequest request,
        CancellationToken ct = default);

    Task<POSShiftDto> ForceCloseAsync(
        ForceClosePOSShiftRequest request,
        CancellationToken ct = default);
    Task<POSShiftPrintDto> GetPrintAsync(
    int shiftId,
    POSShiftCashDenominationEntryType entryType,
    CancellationToken ct = default);
    /// <summary>
    /// Dashboard quản lý ca POS.
    /// Dùng cho quản lý xem tổng hợp nhiều ca.
    /// </summary>
    Task<POSShiftManagerDashboardDto> GetManagerDashboardAsync(
        POSShiftManagerDashboardQueryDto query,
        CancellationToken ct = default);
}