using GaoApp.Application.Common;
using GaoApp.Application.DTOs.POSShiftHandoverSlips;

namespace GaoApp.Application.Interfaces.Services.POSShiftHandoverSlips;

public interface IPOSShiftHandoverSlipService
{
    Task<POSShiftHandoverAssignmentsDto> GetAssignmentsAsync(CancellationToken ct = default);

    Task<POSShiftHandoverSlipDto> CreateAsync(
        CreatePOSShiftHandoverSlipRequest request,
        CancellationToken ct = default);

    Task<POSShiftHandoverSlipDto> UpdateAsync(
        int id, UpdatePOSShiftHandoverSlipRequest request, CancellationToken ct = default);

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
        CancellationToken ct = default,
        string? barcodeValue = null);

    Task<POSShiftHandoverSlipDto> CancelAsync(
        int id,
        CancelPOSShiftHandoverSlipRequest request,
        CancellationToken ct = default);
}