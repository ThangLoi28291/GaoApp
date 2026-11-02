// GaoApp.Application/Services/AdminMenus/AdminMenuService.cs
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Repositories.AdminMenus;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Application.Common.Security;

namespace GaoApp.Application.Services.AdminMenus;

public class AdminMenuService : IAdminMenuService
{
    private readonly IAdminMenuRepository _adminMenuRepository;
    private readonly IAdminMenuPermissionRepository _menuPermissionRepository;
    private readonly ICurrentStorePermissionService _permissionService;
    private readonly IAdminMenuVisibilityService? _visibilityService;
    private readonly ISecurityAdministrationGuard? _security;

    public AdminMenuService(
        IAdminMenuRepository adminMenuRepository,
        IAdminMenuPermissionRepository menuPermissionRepository,
        ICurrentStorePermissionService permissionService,
        IAdminMenuVisibilityService? visibilityService = null,
        ISecurityAdministrationGuard? security = null)
    {
        _adminMenuRepository = adminMenuRepository;
        _menuPermissionRepository = menuPermissionRepository;
        _permissionService = permissionService;
        _visibilityService = visibilityService;
        _security = security;
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
        var current = excludeMenuId.HasValue
            ? await _adminMenuRepository.GetByIdAsync(storeId, excludeMenuId.Value, ct)
            : null;
        if (!string.IsNullOrWhiteSpace(current?.PermissionCode) &&
            !permissions.Any(x => x.Code.Equals(current.PermissionCode, StringComparison.OrdinalIgnoreCase)))
        {
            // Keep a legacy menu's condition visible so a metadata edit cannot silently clear it.
            permissions.Add(new Permission { Code = current.PermissionCode,
                Name = "Quyền đang dùng (không còn trong danh mục)" });
        }

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
                .ToList()
        };
    }

    public async Task<int> CreateAsync(
        int storeId,
        int? actorUserId,
        SaveAdminMenuItemRequest request,
        CancellationToken ct = default)
    {
        await using var security = await (_security ?? throw new InvalidOperationException("Security guard is required."))
            .BeginAsync(storeId, actorUserId, PermissionCodes.Security.Role.Permissions, ct);
        security.RequireAdministrator();
        RejectLegacyPermissionFields(request);
        if (!string.IsNullOrWhiteSpace(request.PermissionCode))
            await security.ConfirmAsync(request, true, ct);
        var now = DateTime.UtcNow;

        var permissionCode = await ResolvePermissionAsync(request.PermissionCode, null, ct);
        await ValidateParentAsync(storeId, request.ParentId, null, ct);

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

        await security.CompleteAsync("Tạo menu", nameof(AdminMenuItem), 0,
            null, Snapshot(entity), request.Reason, ct: ct);

        return entity.Id;
    }

    public async Task<bool> UpdateAsync(
        int storeId,
        int? actorUserId,
        SaveAdminMenuItemRequest request,
        CancellationToken ct = default)
    {
        await using var security = await (_security ?? throw new InvalidOperationException("Security guard is required."))
            .BeginAsync(storeId, actorUserId, PermissionCodes.Security.Role.Permissions, ct);
        security.RequireAdministrator();
        RejectLegacyPermissionFields(request);
        var entity = await _adminMenuRepository.GetByIdAsync(storeId, request.Id, ct);

        if (entity == null)
            return false;

        var before = Snapshot(entity);
        if (!string.IsNullOrWhiteSpace(request.PermissionCode) || !string.IsNullOrWhiteSpace(entity.PermissionCode))
            await security.ConfirmAsync(request, true, ct);

        var permissionCode = await ResolvePermissionAsync(request.PermissionCode, entity.PermissionCode, ct);
        await ValidateParentAsync(storeId, request.ParentId, entity.Id, ct);

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

        await security.CompleteAsync("Sửa menu", nameof(AdminMenuItem), entity.Id,
            before, Snapshot(entity), request.Reason, ct: ct);

        return true;
    }

    public async Task<(bool Success, string? ErrorMessage)> DeleteAsync(
        int storeId,
        int id,
        int? actorUserId,
        CancellationToken ct = default)
    {
        await using var security = await (_security ?? throw new InvalidOperationException("Security guard is required."))
            .BeginAsync(storeId, actorUserId, PermissionCodes.Security.Role.Permissions, ct);
        security.RequireAdministrator();
        var entity = await _adminMenuRepository.GetByIdAsync(storeId, id, ct);

        if (entity == null)
            return (false, "Không tìm thấy menu.");

        if (entity.IsSystem)
            return (false, "Không được xóa menu hệ thống.");

        var hasChildren = await _adminMenuRepository.HasChildrenAsync(storeId, id, ct);

        if (hasChildren)
            return (false, "Menu đang có menu con, vui lòng xóa menu con trước.");

        var before = Snapshot(entity);
        entity.IsDeleted = true;
        entity.IsActive = false;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorUserId;

        await security.CompleteAsync("Xóa menu", nameof(AdminMenuItem), entity.Id,
            before, Snapshot(entity), ct: ct);

        return (true, null);
    }

    private async Task<string?> ResolvePermissionAsync(
        string? requestedCode,
        string? existingCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requestedCode)) return null;
        var code = requestedCode.Trim();
        var permission = await _menuPermissionRepository.GetPermissionByCodeAsync(code, ct);
        if (permission is not null) return permission.Code;
        if (!string.IsNullOrWhiteSpace(existingCode) && code.Equals(existingCode, StringComparison.OrdinalIgnoreCase))
            return existingCode;
        throw new ValidationAppException("Quyền đã chọn không còn trong danh mục. Hãy tải lại và chọn quyền có sẵn.");
    }

    private static void RejectLegacyPermissionFields(SaveAdminMenuItemRequest request)
    {
        if (request.CreateNewPermission.HasValue || request.AssignRoleIds is not null ||
            request.NewPermissionCode is not null || request.NewPermissionName is not null || request.NewPermissionGroupName is not null)
            throw new ValidationAppException("Biểu mẫu menu đã thay đổi. Hãy tải lại trang. Cấp hoặc thu hồi quyền tại Phân quyền vai trò.");
    }

    private async Task ValidateParentAsync(int storeId, int? parentId, int? menuId, CancellationToken ct)
    {
        if (parentId is null) return;
        var menus = (await _adminMenuRepository.GetAllForStoreAsync(storeId, ct)).ToDictionary(x => x.Id);
        var visited = new HashSet<int>();
        if (menuId.HasValue) visited.Add(menuId.Value);
        while (parentId.HasValue)
        {
            if (!visited.Add(parentId.Value))
                throw new ValidationAppException("Không được chọn chính menu hoặc menu con của nó làm menu cha.");
            if (!menus.TryGetValue(parentId.Value, out var parent) || parent.StoreId != storeId)
                throw new ValidationAppException("Menu cha không tồn tại hoặc không thuộc cửa hàng hiện tại.");
            parentId = parent.ParentId;
        }
    }

    private static object Snapshot(AdminMenuItem item) => new
    {
        item.ParentId, item.Title, item.Area, item.Controller, item.Action, item.Url,
        item.Icon, item.PermissionCode, item.SortOrder, item.IsActive, item.IsDeleted
    };

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
}
