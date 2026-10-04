using GaoApp.Application.DTOs.POSShifts;

namespace GaoApp.Application.Interfaces.Services.POSShifts;

public interface IPOSShiftReconciliationService
{
    Task<POSShiftReconciliationDto> GetAsync(int? shiftId, CancellationToken ct = default);
}
