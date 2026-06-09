// GaoApp.Application/Interfaces/Repositories/AdminMenus/IAdminMenuRepository.cs
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.AdminMenus;

public interface IAdminMenuRepository
{
    Task<List<AdminMenuItem>> GetAllForStoreAsync(int storeId, CancellationToken ct = default);
    Task<AdminMenuItem?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);
    Task AddAsync(AdminMenuItem entity, CancellationToken ct = default);
    Task<bool> HasChildrenAsync(int id, CancellationToken ct = default);
}