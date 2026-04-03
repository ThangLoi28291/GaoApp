using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Security.UserInStores;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Application.Interfaces.Repositories.Users;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Security;

public class UserInStoreAdminService : IUserInStoreAdminService
{
    private readonly IUserInStoreRepository _userInStoreRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAppUnitOfWork _uow;

    public UserInStoreAdminService(
        IUserInStoreRepository userInStoreRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        IAppUnitOfWork uow)
    {
        _userInStoreRepository = userInStoreRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _uow = uow;
    }

    public async Task<UserInStoreIndexVm> GetPagedAsync(int storeId, UserInStoreIndexQueryDto query, CancellationToken ct = default)
    {
        var (items, total) = await _userInStoreRepository.GetPagedAsync(
            storeId,
            query.Keyword,
            query.RoleId,
            query.IsActive,
            query.Page,
            query.PageSize,
            ct);

        var roles = await _roleRepository.GetLookupAsync(storeId, false, ct);

        var dtoItems = items.Select(x => new UserInStoreListItemDto
        {
            Id = x.Id,
            UserId = x.UserId,
            Username = x.User?.UserName ?? string.Empty,
            FullName = x.User?.FullName ?? string.Empty,
            Email = x.User?.Email,
            RoleId = x.RoleId,
            RoleName = x.Role?.Name ?? string.Empty,
            RoleCode = x.Role?.Code ?? string.Empty,
            IsActive = x.IsActive,
            CreatedAtUtc = x.CreatedAtUtc,
            UpdatedAtUtc = x.UpdatedAtUtc
        }).ToList();

        return new UserInStoreIndexVm
        {
            Query = query,
            Items = new PagedResult<UserInStoreListItemDto>(query.Page, query.PageSize, total, dtoItems),
            Roles = roles.Select(x => new RoleLookupItemDto
            {
                RoleId = x.Id,
                RoleName = x.Name,
                RoleCode = x.Code,
                IsActive = !x.IsDeleted
            }).ToList(),
            TotalActiveUsers = items.Count(x => x.IsActive),
            TotalInactiveUsers = items.Count(x => !x.IsActive)
        };
    }

    public async Task<UserInStoreEditDto?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
    {
        var entity = await _userInStoreRepository.GetByIdAsync(storeId, id, ct);
        if (entity == null) return null;

        var roles = await _roleRepository.GetLookupAsync(storeId, true, ct);

        return new UserInStoreEditDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Username = entity.User?.UserName ?? string.Empty,
            FullName = entity.User?.FullName ?? string.Empty,
            Email = entity.User?.Email,
            RoleId = entity.RoleId,
            IsActive = entity.IsActive,
            AvailableRoles = roles.Select(x => new RoleLookupItemDto
            {
                RoleId = x.Id,
                RoleName = x.Name,
                RoleCode = x.Code,
                IsActive = !x.IsDeleted
            }).ToList()
        };
    }

    public async Task<(bool Success, string? ErrorMessage, int? Id)> CreateAsync(
        int storeId,
        int? actorUserId,
        CreateUserInStoreRequest request,
        CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, ct);
        if (user == null)
            return (false, "Người dùng không tồn tại.", null);

        if (!user.IsActive || user.IsDeleted)
            return (false, "Người dùng đang bị khóa ở mức hệ thống.", null);

        var role = await _roleRepository.GetByIdAsync(storeId, request.RoleId, ct);
        if (role == null || role.IsDeleted)
            return (false, "Vai trò không thuộc cửa hàng hiện tại.", null);

        var exists = await _userInStoreRepository.ExistsAsync(storeId, request.UserId, ct);
        if (exists)
            return (false, "Người dùng đã được gán vào cửa hàng này.", null);

        var entity = new UserInStore
        {
            StoreId = storeId,
            UserId = request.UserId,
            RoleId = request.RoleId,
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actorUserId
        };

        await _userInStoreRepository.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return (true, null, entity.Id);
    }

    public async Task<(bool Success, string? ErrorMessage)> UpdateAsync(
        int storeId,
        int? actorUserId,
        UpdateUserInStoreRequest request,
        CancellationToken ct = default)
    {
        var entity = await _userInStoreRepository.GetByIdAsync(storeId, request.Id, ct);
        if (entity == null)
            return (false, "Không tìm thấy bản ghi gán người dùng.");

        var role = await _roleRepository.GetByIdAsync(storeId, request.RoleId, ct);
        if (role == null || role.IsDeleted)
            return (false, "Vai trò không thuộc cửa hàng hiện tại.");

        entity.RoleId = request.RoleId;
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorUserId;

        await _userInStoreRepository.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return (true, null);
    }

    public async Task<(bool Success, string? ErrorMessage)> DeleteAsync(
        int storeId,
        int id,
        int? actorUserId,
        CancellationToken ct = default)
    {
        var ok = await _userInStoreRepository.SoftDeleteAsync(storeId, id, actorUserId, ct);
        if (!ok)
            return (false, "Xóa bản ghi gán người dùng không thành công.");

        await _uow.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<List<UserLookupItemDto>> SearchUsersAsync(string? keyword, int maxResults = 20, CancellationToken ct = default)
    {
        var users = await _userRepository.SearchForSecurityAssignmentAsync(keyword, maxResults, ct);

        return users.Select(x => new UserLookupItemDto
        {
            UserId = x.Id,
            Username = x.UserName ?? string.Empty,
            FullName = x.FullName ?? string.Empty,
            Email = x.Email
        }).ToList();
    }

    public async Task<List<RoleLookupItemDto>> GetRoleLookupAsync(int storeId, bool onlyActive = true, CancellationToken ct = default)
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