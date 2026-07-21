// GaoApp.Application/Interfaces/Services/AdminMenus/IAdminMenuService.cs
using GaoApp.Application.DTOs.AdminMenus;

namespace GaoApp.Application.Interfaces.Services.AdminMenus;

public interface IAdminMenuService
{
    Task<List<AdminMenuItemDto>> GetForRenderAsync(int storeId, int userId, CancellationToken ct = default);
    Task<List<AdminMenuItemDto>> GetForAdminAsync(int storeId, CancellationToken ct = default);
    Task<AdminMenuItemDto?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);
    Task<int> CreateAsync(int storeId, int? actorUserId, SaveAdminMenuItemRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(int storeId, int? actorUserId, SaveAdminMenuItemRequest request, CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> DeleteAsync(int storeId, int id, int? actorUserId, CancellationToken ct = default);
    Task<AdminMenuFormOptionsDto> GetFormOptionsAsync(
    int storeId,
    int? excludeMenuId = null,
    CancellationToken ct = default);
    Task<List<int>> GetAssignedRoleIdsAsync(
        int storeId,
        string? permissionCode,
        CancellationToken ct = default);
}
