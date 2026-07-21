using GaoApp.Application.DTOs.LegalEntities;

namespace GaoApp.Application.Interfaces.Repositories.LegalEntities;

public interface ILegalEntityReconciliationRepository
{
    Task<LegalEntityOrderBreakdownDto?> GetOrderBreakdownAsync(int storeId, int orderId, CancellationToken ct = default);

    Task<LegalEntityReconciliationReportDto> QueryAsync(
        int storeId,
        LegalEntityReconciliationQueryDto query,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        CancellationToken ct = default);
}
