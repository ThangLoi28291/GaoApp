using GaoApp.Application.Common;
using GaoApp.Application.DTOs.POSShiftHandoverSlips;

namespace GaoApp.Application.Interfaces.Services.POSShiftHandoverSlips;

public interface IPOSShiftHandoverSlipService
{
    Task<POSShiftHandoverSlipDto> CreateAsync(
        CreatePOSShiftHandoverSlipRequest request,
        CancellationToken ct = default);

    Task<POSShiftHandoverSlipDto?> GetByIdAsync(
        int id,
        CancellationToken ct = default);

    Task<POSShiftHandoverSlipDto?> GetByBarcodeAsync(
        string barcodeValue,
        CancellationToken ct = default);

    Task<PagedResult<POSShiftHandoverSlipDto>> QueryAsync(
        QueryPOSShiftHandoverSlipRequest request,
        CancellationToken ct = default);

    Task<POSShiftHandoverSlipDto> MarkPrintedAsync(
        int id,
        CancellationToken ct = default);

    Task<POSShiftHandoverSlipDto> CancelAsync(
        int id,
        CancelPOSShiftHandoverSlipRequest request,
        CancellationToken ct = default);
}