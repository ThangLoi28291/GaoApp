using GaoApp.Application.DTOs.AdminMenus;

namespace GaoApp.Application.Interfaces.Services.AdminMenus;

public interface IAdminMenuPinService
{
    Task<IReadOnlyList<int>> GetConfiguredIdsAsync(int storeId, int userId, CancellationToken ct = default);
    Task<AdminMenuPinWorkspace> GetWorkspaceAsync(int storeId, int userId, CancellationToken ct = default);
    Task<AdminMenuPinPreview> PreviewAsync(int storeId, int userId, string type, int id, IReadOnlyList<int> menuIds, CancellationToken ct = default);
    Task<AdminMenuPinWorkspace> SaveAsync(int storeId, int userId, SaveAdminMenuPinsRequest request, CancellationToken ct = default);
}
