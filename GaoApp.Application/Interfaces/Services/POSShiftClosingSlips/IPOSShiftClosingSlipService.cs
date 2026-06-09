using GaoApp.Application.DTOs.POSShiftClosingSlips;

namespace GaoApp.Application.Interfaces.Services.POSShiftClosingSlips;

public interface IPOSShiftClosingSlipService
{
    Task<POSShiftClosingSlipDto> CreateFromClosedShiftAsync(
        int shiftId,
        CancellationToken ct = default);

    Task<POSShiftClosingSlipDto?> GetByIdAsync(
        int id,
        CancellationToken ct = default);

    Task<POSShiftClosingSlipDto?> GetByShiftIdAsync(
        int shiftId,
        CancellationToken ct = default);

    Task<POSShiftClosingSlipDto> MarkPrintedAsync(
        int id,
        CancellationToken ct = default);
}