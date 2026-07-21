namespace GaoApp.Application.Interfaces.Services.Security;

public interface ICurrentStorePermissionService
{
    Task<bool> HasPermissionAsync(int storeId, int userId, string permissionCode, CancellationToken ct = default);
    Task<List<string>> GetPermissionsAsync(int storeId, int userId, CancellationToken ct = default);
}