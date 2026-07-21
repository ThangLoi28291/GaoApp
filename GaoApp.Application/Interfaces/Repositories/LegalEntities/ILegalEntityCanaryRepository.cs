using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.LegalEntities;

public interface ILegalEntityCanaryRepository
{
    Task<Store?> GetStoreAsync(int storeId, bool forUpdate, CancellationToken ct = default);
    Task<List<LegalEntityActivationEvent>> GetRecentEventsAsync(int storeId, int take, CancellationToken ct = default);
    Task<LegalEntityCanaryOperationalMetricsDto> GetOperationalMetricsAsync(
        int storeId,
        DateTime sinceUtc,
        DateTime nowUtc,
        CancellationToken ct = default);
    Task<(int AllocationMismatchCount, int InvoiceMismatchCount)> GetReconciliationMismatchCountsAsync(
        int storeId,
        DateTime sinceUtc,
        DateTime nowUtc,
        CancellationToken ct = default);
    Task AddEventAsync(LegalEntityActivationEvent activationEvent, CancellationToken ct = default);
    /// <summary>
    /// Returns false when the Store row version changed after it was read.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default);
}
