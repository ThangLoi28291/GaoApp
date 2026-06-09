using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.POSShiftClosingSlips;

public interface IPOSShiftClosingSlipRepository
{
    Task AddAsync(POSShiftClosingSlip slip, CancellationToken ct = default);

    Task<POSShiftClosingSlip?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);

    Task<POSShiftClosingSlip?> GetByShiftIdAsync(int storeId, int shiftId, CancellationToken ct = default);

    Task<bool> ExistsSlipCodeAsync(int storeId, string slipCode, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}