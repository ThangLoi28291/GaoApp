using GaoApp.Application.Interfaces.Repositories.Users;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Users;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<User?> GetByUserNameAsync(string userName, CancellationToken ct = default)
    {
        return _context.Users
            .FirstOrDefaultAsync(x => x.UserName == userName, ct);
    }

    public Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return _context.Users
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        await _context.Users.AddAsync(user, ct);
    }

    public Task<bool> HasOtherStoreMembershipAsync(int userId, int storeId, CancellationToken ct = default)
        // Deliberate cross-tenant boolean check, never expose other store records.
        // Include disabled/deleted memberships: a store admin must not take over
        // a global account which can later be restored in another store.
        => _context.UserInStores.IgnoreQueryFilters()
            .AnyAsync(x => x.UserId == userId && x.StoreId != storeId, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
    public async Task<List<User>> SearchForSecurityAssignmentAsync(
      string? keyword,
      int maxResults = 20,
      CancellationToken ct = default)
    {
        if (_context.CurrentStoreId is not > 0) return [];
        var storeId = _context.CurrentStoreId.Value;
        var isHostAdmin = _context.CurrentUserId.HasValue && await _context.Users.AsNoTracking()
            .AnyAsync(x => x.Id == _context.CurrentUserId.Value && x.IsHostAdmin && x.IsActive && !x.IsDeleted, ct);
        var query = _context.Users.Where(x => !x.IsDeleted && !x.IsHostAdmin &&
            (isHostAdmin || x.UserInStores.Any(m => m.StoreId == storeId && !m.IsDeleted)));

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim();
            query = query.Where(x =>
                x.UserName.Contains(keyword) ||
                (x.FullName != null && x.FullName.Contains(keyword)) ||
                (x.Email != null && x.Email.Contains(keyword)));
        }

        return await query
            .OrderBy(x => x.UserName)
            .Take(Math.Clamp(maxResults, 1, 50))
            .ToListAsync(ct);
    }
    public async Task<List<User>> GetByIdsAsync(List<int> ids, CancellationToken ct = default)
    {
        if (ids == null || ids.Count == 0)
            return new List<User>();

        return await _context.Users
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct);
    }
    public Task<bool> ExistsByUserNameAsync(
    string userName,
    CancellationToken ct = default)
    {
        return _context.Users.AnyAsync(x =>
            x.UserName == userName &&
            !x.IsDeleted,
            ct);
    }

}
