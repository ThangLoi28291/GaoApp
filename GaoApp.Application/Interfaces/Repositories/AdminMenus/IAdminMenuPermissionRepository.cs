using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.AdminMenus;

/// <summary>Read-only permission catalog for menu visibility conditions.</summary>
public interface IAdminMenuPermissionRepository
{
    Task<List<Permission>> GetPermissionsAsync(CancellationToken ct = default);
    Task<Permission?> GetPermissionByCodeAsync(string code, CancellationToken ct = default);
}
