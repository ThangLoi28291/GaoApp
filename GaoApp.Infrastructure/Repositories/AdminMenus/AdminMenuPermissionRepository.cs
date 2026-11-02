using GaoApp.Application.Interfaces.Repositories.AdminMenus;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.AdminMenus;

public class AdminMenuPermissionRepository(AppDbContext db) : IAdminMenuPermissionRepository
{
    public Task<List<Permission>> GetPermissionsAsync(CancellationToken ct = default)
        => db.Permissions.AsNoTracking().OrderBy(x => x.GroupName).ThenBy(x => x.Code).ToListAsync(ct);

    public Task<Permission?> GetPermissionByCodeAsync(string code, CancellationToken ct = default)
        => db.Permissions.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);
}
