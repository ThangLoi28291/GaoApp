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

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
    public async Task<List<User>> SearchForSecurityAssignmentAsync(
      string? keyword,
      int maxResults = 20,
      CancellationToken ct = default)
    {
        var query = _context.Users.Where(x => !x.IsDeleted);

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
            .Take(maxResults)
            .ToListAsync(ct);
    }
}