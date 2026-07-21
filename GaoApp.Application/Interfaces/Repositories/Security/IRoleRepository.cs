using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Security;

public interface IRoleRepository
{
    Task<(List<Role> Items, int Total)> GetPagedAsync(
        int storeId,
        string? keyword,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<Role?> GetByIdAsync(int storeId, int roleId, CancellationToken ct = default);

    Task<bool> ExistsNameAsync(int storeId, string name, int? ignoreId = null, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(int storeId, string code, int? ignoreId = null, CancellationToken ct = default);

    Task AddAsync(Role entity, CancellationToken ct = default);

    Task UpdateAsync(Role entity, CancellationToken ct = default);

    Task<bool> SoftDeleteAsync(int storeId, int roleId, int? actorUserId, CancellationToken ct = default);

    Task<List<Role>> GetLookupAsync(int storeId, bool onlyActive = true, CancellationToken ct = default);

    Task<Dictionary<int, int>> GetPermissionCountsAsync(
        int storeId,
        IEnumerable<int> roleIds,
        CancellationToken ct = default);

    Task<Dictionary<int, int>> GetUserCountsAsync(
        int storeId,
        IEnumerable<int> roleIds,
        CancellationToken ct = default);
}