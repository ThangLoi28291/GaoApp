using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface INegativeInventoryLogRepository
{
    Task AddAsync(NegativeInventoryLog entity, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<List<NegativeInventoryLog>> GetAllAsync(CancellationToken ct = default);
}