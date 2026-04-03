using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Security;

public interface IUserInStoreRepository
{
    // =========================
    // METHOD CŨ - CurrentStorePermissionService đang dùng
    // =========================
    Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
        int storeId,
        int userId,
        CancellationToken ct = default);
    Task<(List<UserInStore> Items, int Total)> GetPagedAsync(
        int storeId,
        string? keyword,
        int? roleId,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<UserInStore?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);

    Task<bool> ExistsAsync(int storeId, int userId, CancellationToken ct = default);

    Task AddAsync(UserInStore entity, CancellationToken ct = default);

    Task UpdateAsync(UserInStore entity, CancellationToken ct = default);

    Task<bool> SoftDeleteAsync(int storeId, int id, int? actorUserId, CancellationToken ct = default);

    Task<int> CountByRoleIdAsync(int storeId, int roleId, CancellationToken ct = default);

    Task<Dictionary<int, int>> CountByRoleIdsAsync(
        int storeId,
        IEnumerable<int> roleIds,
        CancellationToken ct = default);
}