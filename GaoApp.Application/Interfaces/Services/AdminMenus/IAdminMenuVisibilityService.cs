using GaoApp.Application.DTOs.AdminMenus;

namespace GaoApp.Application.Interfaces.Services.AdminMenus;

public interface IAdminMenuVisibilityService
{
    Task<List<MenuVisibilitySubject>> GetSubjectsAsync(int storeId, CancellationToken ct = default);
    Task<MenuVisibilityWorkspace> GetAsync(int storeId, string type, int id, CancellationToken ct = default);
    Task<MenuVisibilityWorkspace> SaveAsync(int storeId, SaveMenuVisibilityRequest request, CancellationToken ct = default);
    Task<HashSet<int>> GetHiddenIdsAsync(int storeId, int userId, CancellationToken ct = default);
}
