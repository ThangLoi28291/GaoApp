using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Security;

public class PermissionRepository : IPermissionRepository
{
    private readonly AppDbContext _db;

    public PermissionRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<List<Permission>> GetAllAsync(CancellationToken ct = default)
    {
        return _db.Permissions
            .AsNoTracking()
            .OrderBy(x => x.GroupName)
            .ThenBy(x => x.Code)
            .ToListAsync(ct);
    }

    public Task<List<Permission>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToList();

        return _db.Permissions
            .Where(x => idList.Contains(x.Id))
            .ToListAsync(ct);
    }

    public Task<int> CountByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToList();

        return _db.Permissions
            .CountAsync(x => idList.Contains(x.Id), ct);
    }
}