using GaoApp.Application.Interfaces.Repositories.Auth;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using GaoApp.Infrastructure.Data;

namespace GaoApp.Infrastructure.Repositories.Auth;

public class AuthUserRepository : IAuthUserRepository
{
    private readonly AppDbContext _db;

    public AuthUserRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByUserNameAsync(string userName, CancellationToken ct = default)
    {
        return await _db.Users
            .Include(x => x.UserInStores)
            .FirstOrDefaultAsync(x => x.UserName == userName && !x.IsDeleted, ct);
    }

    public async Task<UserInStore?> GetUserInStoreAsync(int userId, int storeId, CancellationToken ct = default)
    {
        return await _db.UserInStores
            .Include(x => x.Role)
            .FirstOrDefaultAsync(x =>
                x.UserId == userId &&
                x.StoreId == storeId &&
                x.IsActive &&
                !x.IsDeleted, ct);
    }
}