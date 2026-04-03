using GaoApp.Application.Interfaces.Repositories.Customers;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Customers;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _db;

    public CustomerRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Customer?> GetActiveByIdAsync(int customerId, CancellationToken ct = default)
    {
        return await _db.Customers
            .FirstOrDefaultAsync(x => x.Id == customerId && x.IsActive && !x.IsDeleted, ct);
    }

    public async Task<Customer?> GetByPhoneAsync(string phone, CancellationToken ct = default)
    {
        phone = (phone ?? "").Trim();

        if (string.IsNullOrWhiteSpace(phone))
            return null;

        return await _db.Customers
            .FirstOrDefaultAsync(x =>
                x.Phone == phone &&
                !x.IsDeleted,
                ct);
    }

    public async Task<List<Customer>> SearchActiveAsync(string keyword, int take = 20, CancellationToken ct = default)
    {
        keyword = (keyword ?? "").Trim();

        if (take <= 0) take = 20;
        if (take > 50) take = 50;

        var query = _db.Customers
            .AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                x.Name.Contains(keyword) ||
                (x.Phone != null && x.Phone.Contains(keyword)) ||
                (x.Address != null && x.Address.Contains(keyword)));
        }

        return await query
            .OrderBy(x => x.Name)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Customer customer, CancellationToken ct = default)
    {
        await _db.Customers.AddAsync(customer, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
}