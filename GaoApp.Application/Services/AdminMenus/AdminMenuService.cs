// GaoApp.Application/Services/AdminMenus/AdminMenuService.cs
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.AdminMenus;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.AdminMenus;

public class AdminMenuService : IAdminMenuService
{
    private readonly IAdminMenuRepository _adminMenuRepository;
    private readonly IAdminMenuPermissionRepository _menuPermissionRepository;
    private readonly IAppUnitOfWork _uow;
    private readonly ICurrentStorePermissionService _permissionService;
    private readonly IAdminMenuVisibilityService? _visibilityService;

    public AdminMenuService(
        IAdminMenuRepository adminMenuRepository,
        IAdminMenuPermissionRepository menuPermissionRepository,
        IAppUnitOfWork uow,
        ICurrentStorePermissionService permissionService,
        IAdminMenuVisibilityService? visibilityService = null)
    {
        _adminMenuRepository = adminMenuRepository;
        _menuPermissionRepository = menuPermissionRepository;
        _uow = uow;
        _permissionService = permissionService;
        _visibilityService = visibilityService;
    }

    public async Task<List<AdminMenuItemDto>> GetForRenderAsync(
        int storeId,
        int userId,
        CancellationToken ct = default)
    {
        var permissions = await _permissionService.GetPermissionsAsync(storeId, userId, ct);
        var items = await _adminMenuRepository.GetAllForStoreAsync(storeId, ct);

        var visible = items
            .Where(x => x.IsActive)
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .Where(x =>
                string.IsNullOrWhiteSpace(x.PermissionCode)
                || permissions.Contains(x.PermissionCode, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var hidden = _visibilityService == null ? new HashSet<int>() : await _visibilityService.GetHiddenIdsAsync(storeId, userId, ct);
        var visibleIds = MenuVisibilityRules.VisibleIds(visible.Select(x => new MenuVisibilityNode(
            x.Id, x.ParentId, x.Title, x.Icon ?? "", true, null,
            string.IsNullOrWhiteSpace(x.Controller) && string.IsNullOrWhiteSpace(x.Url))), hidden);
        visible.RemoveAll(x => !visibleIds.Contains(x.Id));

        return BuildTree(visible);
    }

    public async Task<List<AdminMenuItemDto>> GetForAdminAsync(
        int storeId,
        CancellationToken ct = default)
    {
        var items = await _adminMenuRepository.GetAllForStoreAsync(storeId, ct);
        return BuildTree(items);
    }

    public async Task<AdminMenuItemDto?> GetByIdAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        var item = await _adminMenuRepository.GetByIdAsync(storeId, id, ct);
        return item == null ? null : ToDto(item);
    }

    public async Task<AdminMenuFormOptionsDto> GetFormOptionsAsync(
        int storeId,
        int? excludeMenuId = null,
        CancellationToken ct = default)
    {
        var menus = await GetForAdminAsync(storeId, ct);
        var permissions = await _menuPermissionRepository.GetPermissionsAsync(ct);
        var roles = await _menuPermissionRepository.GetRolesAsync(storeId, ct);

        if (excludeMenuId.HasValue)
            menus = RemoveMenuFromTree(menus, excludeMenuId.Value);

        return new AdminMenuFormOptionsDto
        {
            ParentMenus = menus,

            Permissions = permissions
                .OrderBy(x => x.GroupName)
                .ThenBy(x => x.Code)
                .Select(x => new AdminMenuPermissionOptionDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    GroupName = x.GroupName
                })
                .ToList(),

            Roles = roles
                .OrderBy(x => x.Name)
                .Select(x => new AdminMenuRoleOptionDto
                {
                    RoleId = x.Id,
                    RoleCode = x.Code,
                    RoleName = x.Name
                })
                .ToList()
        };
    }

    public async Task<int> CreateAsync(
        int storeId,
        int? actorUserId,
        SaveAdminMenuItemRequest request,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var permissionResult = await ResolvePermissionAsync(request, ct);
        var permissionCode = permissionResult.Code;

        var entity = new AdminMenuItem
        {
            StoreId = storeId,
            ParentId = request.ParentId,
            Title = request.Title.Trim(),
            Area = string.IsNullOrWhiteSpace(request.Area) ? "Admin" : request.Area.Trim(),
            Controller = request.Controller?.Trim(),
            Action = string.IsNullOrWhiteSpace(request.Action) ? "Index" : request.Action.Trim(),
            Url = request.Url?.Trim(),
            Icon = request.Icon?.Trim(),
            PermissionCode = permissionCode,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            IsSystem = false,
            CreatedAtUtc = now,
            CreatedBy = actorUserId
        };

        await _adminMenuRepository.AddAsync(entity, ct);

        await SyncPermissionRolesAsync(
            storeId,
            permissionResult.Permission,
            request.AssignRoleIds,
            ct);

        await _uow.SaveChangesAsync(ct);

        return entity.Id;
    }

    public async Task<bool> UpdateAsync(
        int storeId,
        int? actorUserId,
        SaveAdminMenuItemRequest request,
        CancellationToken ct = default)
    {
        var entity = await _adminMenuRepository.GetByIdAsync(storeId, request.Id, ct);

        if (entity == null)
            return false;

        if (request.ParentId == request.Id)
            throw new InvalidOperationException("Không được chọn chính menu hiện tại làm menu cha.");

        var permissionResult = await ResolvePermissionAsync(request, ct);
        var permissionCode = permissionResult.Code;

        entity.ParentId = request.ParentId;
        entity.Title = request.Title.Trim();
        entity.Area = string.IsNullOrWhiteSpace(request.Area) ? "Admin" : request.Area.Trim();
        entity.Controller = request.Controller?.Trim();
        entity.Action = string.IsNullOrWhiteSpace(request.Action) ? "Index" : request.Action.Trim();
        entity.Url = request.Url?.Trim();
        entity.Icon = request.Icon?.Trim();
        entity.PermissionCode = permissionCode;
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorUserId;

        await SyncPermissionRolesAsync(
            storeId,
            permissionResult.Permission,
            request.AssignRoleIds,
            ct);

        await _uow.SaveChangesAsync(ct);

        return true;
    }

    public async Task<(bool Success, string? ErrorMessage)> DeleteAsync(
        int storeId,
        int id,
        int? actorUserId,
        CancellationToken ct = default)
    {
        var entity = await _adminMenuRepository.GetByIdAsync(storeId, id, ct);

        if (entity == null)
            return (false, "Không tìm thấy menu.");

        if (entity.IsSystem)
            return (false, "Không được xóa menu hệ thống.");

        var hasChildren = await _adminMenuRepository.HasChildrenAsync(storeId, id, ct);

        if (hasChildren)
            return (false, "Menu đang có menu con, vui lòng xóa menu con trước.");

        entity.IsDeleted = true;
        entity.IsActive = false;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorUserId;

        await _uow.SaveChangesAsync(ct);

        return (true, null);
    }

    private async Task<(string? Code, Permission? Permission)> ResolvePermissionAsync(
        SaveAdminMenuItemRequest request,
        CancellationToken ct)
    {
        if (!request.CreateNewPermission)
        {
            var selectedCode = string.IsNullOrWhiteSpace(request.PermissionCode)
                ? null
                : request.PermissionCode.Trim();

            if (selectedCode == null)
                return (null, null);

            var selectedPermission = await _menuPermissionRepository
                .GetPermissionByCodeAsync(selectedCode, ct);

            return (selectedCode, selectedPermission);
        }

        var code = request.NewPermissionCode?.Trim();

        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Vui lòng nhập mã quyền mới.");

        var existed = await _menuPermissionRepository.GetPermissionByCodeAsync(code, ct);

        if (existed != null)
            return (existed.Code, existed);

        var permission = new Permission
        {
            Code = code,
            Name = string.IsNullOrWhiteSpace(request.NewPermissionName)
                ? code
                : request.NewPermissionName.Trim(),
            GroupName = string.IsNullOrWhiteSpace(request.NewPermissionGroupName)
                ? "Menu"
                : request.NewPermissionGroupName.Trim()
        };

        await _menuPermissionRepository.AddPermissionAsync(permission, ct);

        return (permission.Code, permission);
    }

    private async Task SyncPermissionRolesAsync(
        int storeId,
        Permission? permission,
        List<int> roleIds,
        CancellationToken ct)
    {
        if (permission == null)
            return;

        await _menuPermissionRepository.SyncPermissionRolesAsync(
            storeId,
            permission,
            roleIds ?? new List<int>(),
            ct);
    }

    private static List<AdminMenuItemDto> BuildTree(List<AdminMenuItem> items)
    {
        var dict = items
            .OrderBy(x => x.SortOrder)
            .ToDictionary(x => x.Id, ToDto);

        var roots = new List<AdminMenuItemDto>();

        foreach (var item in items.OrderBy(x => x.SortOrder))
        {
            var dto = dict[item.Id];

            if (item.ParentId.HasValue && dict.TryGetValue(item.ParentId.Value, out var parent))
                parent.Children.Add(dto);
            else
                roots.Add(dto);
        }

        return roots;
    }

    private static AdminMenuItemDto ToDto(AdminMenuItem x)
    {
        return new AdminMenuItemDto
        {
            Id = x.Id,
            ParentId = x.ParentId,
            Title = x.Title,
            Area = x.Area,
            Controller = x.Controller,
            Action = x.Action,
            Url = x.Url,
            Icon = x.Icon,
            PermissionCode = x.PermissionCode,
            SortOrder = x.SortOrder,
            IsActive = x.IsActive,
            IsSystem = x.IsSystem
        };
    }

    private static List<AdminMenuItemDto> RemoveMenuFromTree(
        List<AdminMenuItemDto> menus,
        int excludeMenuId)
    {
        return menus
            .Where(x => x.Id != excludeMenuId)
            .Select(x =>
            {
                x.Children = RemoveMenuFromTree(x.Children, excludeMenuId);
                return x;
            })
            .ToList();
    }
    public async Task<List<int>> GetAssignedRoleIdsAsync(
        int storeId,
        string? permissionCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
            return new List<int>();

        return await _menuPermissionRepository
            .GetRoleIdsByPermissionCodeAsync(storeId, permissionCode, ct);
    }
}
