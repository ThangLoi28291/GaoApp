using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.LegalEntities;

namespace GaoApp.Application.Interfaces.Services.LegalEntities;

public interface ILegalEntityService
{
    Task<Result<List<LegalEntityDto>>> GetAllAsync(CancellationToken ct = default);

    Task<Result<List<LegalEntityOptionDto>>> GetActiveOptionsAsync(
        CancellationToken ct = default);

    Task<Result<LegalEntityManagementDto>> GetManagementAsync(
        CancellationToken ct = default);

    Task<Result<LegalEntityActivationPreflightDto>> GetActivationPreflightAsync(
        CancellationToken ct = default);

    Task<Result<bool>> SetMultiLegalEntityEnabledAsync(
        bool isEnabled,
        CancellationToken ct = default);

    Task<Result<int>> CreateAsync(
        CreateLegalEntityRequest request,
        CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(
        UpdateLegalEntityRequest request,
        CancellationToken ct = default);

    Task<Result<bool>> SetActiveAsync(
        int legalEntityId,
        bool isActive,
        CancellationToken ct = default);

    Task<Result<bool>> SetDefaultWarehouseAsync(
        int legalEntityId,
        int warehouseId,
        CancellationToken ct = default);
}
