using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Security.Roles;
using GaoApp.Application.DTOs.Security.UserInStores;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Security;

public class RoleAdminService : IRoleAdminService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IUserInStoreRepository _userInStoreRepository;
    private readonly IAppUnitOfWork _uow;

    public RoleAdminService(
        IRoleRepository roleRepository,
        IUserInStoreRepository userInStoreRepository,
        IAppUnitOfWork uow)
    {
        _roleRepository = roleRepository;
        _userInStoreRepository = userInStoreRepository;
        _uow = uow;
    }

    public async Task<RoleIndexVm> GetPagedAsync(int storeId, RoleIndexQueryDto query, CancellationToken ct = default)
    {
        var (items, total) = await _roleRepository.GetPagedAsync(
            storeId,
            query.Keyword,
            query.IsActive,
            query.Page,
            query.PageSize,
            ct);

        var roleIds = items.Select(x => x.Id).ToList();

        var permissionCounts = roleIds.Count == 0
            ? new Dictionary<int, int>()
            : await _roleRepository.GetPermissionCountsAsync(storeId, roleIds, ct);

        var userCounts = roleIds.Count == 0
            ? new Dictionary<int, int>()
            : await _roleRepository.GetUserCountsAsync(storeId, roleIds, ct);

        var dtoItems = items.Select(x => new RoleListItemDto
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            IsActive = !x.IsDeleted,
            IsSystemRole = x.IsSystemRole,
            PermissionCount = permissionCounts.TryGetValue(x.Id, out var p) ? p : 0,
            UserCount = userCounts.TryGetValue(x.Id, out var u) ? u : 0,
            CreatedAtUtc = x.CreatedAtUtc,
            UpdatedAtUtc = x.UpdatedAtUtc
        }).ToList();

        return new RoleIndexVm
        {
            Query = query,
            Roles = new PagedResult<RoleListItemDto>(query.Page, query.PageSize, total, dtoItems),
            TotalActiveRoles = items.Count(x => !x.IsDeleted),
            TotalInactiveRoles = items.Count(x => x.IsDeleted)
        };
    }

    public async Task<RoleEditDto?> GetByIdAsync(int storeId, int roleId, CancellationToken ct = default)
    {
        var role = await _roleRepository.GetByIdAsync(storeId, roleId, ct);
        if (role == null) return null;

        var permissionCounts = await _roleRepository.GetPermissionCountsAsync(storeId, new[] { roleId }, ct);
        var userCounts = await _roleRepository.GetUserCountsAsync(storeId, new[] { roleId }, ct);

        return new RoleEditDto
        {
            Id = role.Id,
            Code = role.Code,
            Name = role.Name,
            IsActive = !role.IsDeleted,
            IsSystemRole = role.IsSystemRole,
            PermissionCount = permissionCounts.TryGetValue(roleId, out var p) ? p : 0,
            UserCount = userCounts.TryGetValue(roleId, out var u) ? u : 0,
            CreatedAtUtc = role.CreatedAtUtc,
            UpdatedAtUtc = role.UpdatedAtUtc
        };
    }

    public async Task<int> CreateAsync(int storeId, int? actorUserId, RoleCreateRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var code = request.Code.Trim();

        if (await _roleRepository.ExistsNameAsync(storeId, name, null, ct))
            throw new InvalidOperationException("Tên vai trò đã tồn tại trong cửa hàng hiện tại.");

        if (await _roleRepository.ExistsCodeAsync(storeId, code, null, ct))
            throw new InvalidOperationException("Mã vai trò đã tồn tại trong cửa hàng hiện tại.");

        var entity = new Role
        {
            StoreId = storeId,
            Name = name,
            Code = code,
            IsSystemRole = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actorUserId
        };

        await _roleRepository.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return entity.Id;
    }

    public async Task<bool> UpdateAsync(int storeId, int? actorUserId, RoleUpdateRequest request, CancellationToken ct = default)
    {
        var role = await _roleRepository.GetByIdAsync(storeId, request.Id, ct);
        if (role == null) return false;

        var name = request.Name.Trim();
        var code = request.Code.Trim();

        if (await _roleRepository.ExistsNameAsync(storeId, name, request.Id, ct))
            throw new InvalidOperationException("Tên vai trò đã tồn tại trong cửa hàng hiện tại.");

        if (await _roleRepository.ExistsCodeAsync(storeId, code, request.Id, ct))
            throw new InvalidOperationException("Mã vai trò đã tồn tại trong cửa hàng hiện tại.");

        if (role.IsSystemRole && !string.Equals(role.Code, code, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Không được đổi mã của vai trò hệ thống.");

        role.Name = name;
        role.Code = code;
        role.UpdatedAtUtc = DateTime.UtcNow;
        role.UpdatedBy = actorUserId;

        await _roleRepository.UpdateAsync(role, ct);
        await _uow.SaveChangesAsync(ct);

        return true;
    }

    public async Task<(bool Success, string? ErrorMessage)> DeleteAsync(int storeId, int roleId, int? actorUserId, CancellationToken ct = default)
    {
        var role = await _roleRepository.GetByIdAsync(storeId, roleId, ct);
        if (role == null)
            return (false, "Không tìm thấy vai trò.");

        if (role.IsSystemRole)
            return (false, "Không thể xóa vai trò hệ thống.");

        var userCount = await _userInStoreRepository.CountByRoleIdAsync(storeId, roleId, ct);
        if (userCount > 0)
            return (false, $"Không thể xóa vai trò vì đang có {userCount} người dùng sử dụng.");

        var ok = await _roleRepository.SoftDeleteAsync(storeId, roleId, actorUserId, ct);
        if (!ok)
            return (false, "Xóa vai trò không thành công.");

        await _uow.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<List<RoleLookupItemDto>> GetLookupAsync(int storeId, bool onlyActive = true, CancellationToken ct = default)
    {
        var roles = await _roleRepository.GetLookupAsync(storeId, onlyActive, ct);

        return roles.Select(x => new RoleLookupItemDto
        {
            RoleId = x.Id,
            RoleName = x.Name,
            RoleCode = x.Code,
            IsActive = !x.IsDeleted
        }).ToList();
    }
}