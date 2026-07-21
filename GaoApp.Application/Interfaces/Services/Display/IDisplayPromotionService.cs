using GaoApp.Application.DTOs.Display;

namespace GaoApp.Application.Interfaces.Services.Display;

public interface IDisplayPromotionService
{
    Task<List<DisplayPromotionDto>> GetActiveForCustomerDisplayAsync(
        CancellationToken ct = default);

    Task<List<DisplayPromotionDto>> GetListAsync(
        CancellationToken ct = default);

    Task<DisplayPromotionDto?> GetByIdAsync(
        int id,
        CancellationToken ct = default);

    Task<int> CreateAsync(
        UpsertDisplayPromotionDto dto,
        CancellationToken ct = default);

    Task UpdateAsync(
        int id,
        UpsertDisplayPromotionDto dto,
        CancellationToken ct = default);

    Task DeleteAsync(
        int id,
        CancellationToken ct = default);
    Task SetActiveAsync(int id, bool isActive, CancellationToken ct = default);
}