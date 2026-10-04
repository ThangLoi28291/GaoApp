using GaoApp.Application.DTOs.Security.Permissions;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Application.Common.Security;

namespace GaoApp.Application.Services.Security;

public class RolePermissionAdminService : IRolePermissionAdminService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IRolePermissionRepository _rolePermissionRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserInStoreRepository _userInStoreRepository;
    private readonly IAppUnitOfWork _uow;

    public RolePermissionAdminService(
        IRoleRepository roleRepository,
        IRolePermissionRepository rolePermissionRepository,
        IPermissionRepository permissionRepository,
        IUserInStoreRepository userInStoreRepository,
        IAppUnitOfWork uow)
    {
        _roleRepository = roleRepository;
        _rolePermissionRepository = rolePermissionRepository;
        _permissionRepository = permissionRepository;
        _userInStoreRepository = userInStoreRepository;
        _uow = uow;
    }

    public async Task<RolePermissionMatrixDto?> GetMatrixAsync(int storeId, int roleId, CancellationToken ct = default)
    {
        var role = await _roleRepository.GetByIdAsync(storeId, roleId, ct);
        if (role == null) return null;

        var allPermissions = await _permissionRepository.GetAllAsync(ct);
        var selectedIds = await _rolePermissionRepository.GetPermissionIdsByRoleIdAsync(roleId, ct);
        var userCount = await _userInStoreRepository.CountByRoleIdAsync(storeId, roleId, ct);

        return new RolePermissionMatrixDto
        {
            RoleId = role.Id,
            RoleName = role.Name,
            RoleCode = role.Code,
            RoleIsActive = !role.IsDeleted,
            UserCount = userCount,
            SelectedPermissionCount = selectedIds.Count,
            TotalPermissionCount = allPermissions.Count,
            Groups = BuildGroups(allPermissions, selectedIds)
        };
    }

    public async Task<(bool Success, string? ErrorMessage)> SaveAsync(
        int storeId,
        int? actorUserId,
        SaveRolePermissionsRequest request,
        CancellationToken ct = default)
    {
        var role = await _roleRepository.GetByIdAsync(storeId, request.RoleId, ct);
        if (role == null)
            return (false, "Không tìm thấy vai trò.");

        var selectedIds = request.SelectedPermissionIds.Distinct().ToList();

        if (selectedIds.Count > 0)
        {
            var validCount = await _permissionRepository.CountByIdsAsync(selectedIds, ct);
            if (validCount != selectedIds.Count)
                return (false, "Danh sách quyền gửi lên không hợp lệ.");
        }

        var currentIds = await _rolePermissionRepository.GetPermissionIdsByRoleIdAsync(request.RoleId, ct);

        var toAdd = selectedIds.Except(currentIds).ToList();
        var toRemove = currentIds.Except(selectedIds).ToList();

        if (toRemove.Count > 0)
            await _rolePermissionRepository.RemoveByRoleIdAndPermissionIdsAsync(request.RoleId, toRemove, ct);

        if (toAdd.Count > 0)
        {
            var entities = toAdd.Select(permissionId => new RolePermission
            {
                RoleId = request.RoleId,
                PermissionId = permissionId
            });

            await _rolePermissionRepository.AddRangeAsync(entities, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return (true, null);
    }

    private static List<RolePermissionGroupDto> BuildGroups(
        List<Permission> allPermissions,
        List<int> selectedIds)
    {
        var result = new List<RolePermissionGroupDto>();

        var groupQuery = allPermissions
            .GroupBy(x => string.IsNullOrWhiteSpace(x.GroupName) ? "Other" : x.GroupName)
            .OrderBy(g => g.Key);

        foreach (var group in groupQuery)
        {
            var rows = new List<RolePermissionRowDto>();

            var entityGroups = group
                .GroupBy(p => GetModulePart(p.Code) + "." + GetEntityPart(p.Code))
                .OrderBy(g => g.Key);

            foreach (var entityGroup in entityGroups)
            {
                var row = new RolePermissionRowDto
                {
                    Module = GetModulePart(entityGroup.First().Code),
                    Entity = GetEntityPart(entityGroup.First().Code),
                    DisplayName = PermissionDisplayNames.Feature(GetModulePart(entityGroup.First().Code), GetEntityPart(entityGroup.First().Code))
                };

                foreach (var permission in entityGroup.OrderBy(x => x.Code))
                {
                    row.Cells.Add(new RolePermissionCellDto
                    {
                        PermissionId = permission.Id,
                        PermissionCode = permission.Code,
                        Action = GetActionPart(permission.Code),
                        ActionDisplayName = PermissionDisplayNames.Permission(permission.Code, permission.Name),
                        IsAvailable = true,
                        IsChecked = selectedIds.Contains(permission.Id)
                    });
                }

                rows.Add(row);
            }

            result.Add(new RolePermissionGroupDto
            {
                GroupName = PermissionDisplayNames.Group(group.Key),
                SortOrder = 0,
                SelectedCount = group.Count(x => selectedIds.Contains(x.Id)),
                TotalCount = group.Count(),
                Rows = rows
            });
        }

        return result;
    }

    private static string GetModulePart(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "other";
        var parts = code.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 1 ? parts[0] : "other";
    }

    private static string GetEntityPart(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "other";
        var parts = code.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : code;
    }

    private static string GetActionPart(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "view";
        var parts = code.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[2] : "view";
    }

}
