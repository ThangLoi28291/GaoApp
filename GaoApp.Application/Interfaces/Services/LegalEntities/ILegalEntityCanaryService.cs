using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.LegalEntities;

namespace GaoApp.Application.Interfaces.Services.LegalEntities;

public interface ILegalEntityCanaryService
{
    Task<Result<LegalEntityCanaryStatusDto>> GetStatusAsync(CancellationToken ct = default);
    Task<Result<LegalEntityCanaryStateChangeDto>> SetStateAsync(
        SetMultiLegalEntityEnabledRequest request,
        CancellationToken ct = default);
}
