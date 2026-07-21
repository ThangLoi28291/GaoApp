using GaoApp.Application.DTOs.LegalEntities;

namespace GaoApp.Application.Interfaces.Services.LegalEntities;

public interface ILegalEntityReconciliationService
{
    Task<LegalEntityOrderBreakdownDto?> GetOrderBreakdownAsync(int orderId, CancellationToken ct = default);

    Task<LegalEntityReconciliationReportDto> GetReportAsync(
        LegalEntityReconciliationQueryDto query,
        CancellationToken ct = default);

    Task<LegalEntityReconciliationReportDto> GetExportAsync(
        LegalEntityReconciliationQueryDto query,
        CancellationToken ct = default);
}
