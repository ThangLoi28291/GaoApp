using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Customers;
using GaoApp.Application.Interfaces.Repositories.Customers;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Customers;

public sealed class CustomerManagementRepository : ICustomerManagementRepository
{
    private const string AccentInsensitiveSearchCollation = "Latin1_General_100_CI_AI";
    private readonly AppDbContext _db;

    public CustomerManagementRepository(AppDbContext db) => _db = db;

    public async Task<PagedResult<CustomerListItemDto>> GetPageAsync(
        int storeId,
        CustomerManagementQueryRequest request,
        CancellationToken ct = default)
    {
        var query = ApplyDebt(
            ApplyStatus(
                ApplyPriceTier(
                    ApplySearch(BuildReadQuery(storeId), request.SearchString),
                    request.PriceTier),
                request.Status),
            request.HaveDebt);

        var totalItems = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)totalItems / request.PageSize));
        var page = Math.Min(request.Page, totalPages);
        var items = await query
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Code)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(item => new CustomerListItemDto
            {
                Id = item.Id,
                Name = item.Name,
                Code = item.Code,
                Phone = item.Phone,
                Email = item.Email,
                TaxCode = item.TaxCode,
                PriceTier = item.PriceTier,
                HaveDebt = item.HaveDebt,
                IsActive = item.IsActive,
                CreatedAtUtc = item.CreatedAtUtc
            })
            .ToListAsync(ct);

        return new PagedResult<CustomerListItemDto>(page, request.PageSize, totalItems, items);
    }

    public async Task<CustomerManagementSummaryDto> GetSummaryAsync(
        int storeId,
        CustomerManagementQueryRequest request,
        CancellationToken ct = default)
    {
        // Bốn KPI là tổng quan chính xác của toàn bộ Customer trong Store hiện tại;
        // bộ lọc chỉ thu hẹp danh sách kết quả phía dưới.
        _ = request;
        var query = BuildReadQuery(storeId);

        return await query
            .GroupBy(_ => 1)
            .Select(group => new CustomerManagementSummaryDto
            {
                TotalCustomers = group.Count(),
                ActiveCustomers = group.Count(item => item.IsActive),
                InactiveCustomers = group.Count(item => !item.IsActive),
                DebtEnabledCustomers = group.Count(item => item.HaveDebt)
            })
            .FirstOrDefaultAsync(ct)
            ?? new CustomerManagementSummaryDto();
    }

    public Task<CustomerQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
        => _db.Set<Customer>()
            .AsNoTracking()
            .Where(item => item.StoreId == storeId && item.Id == id)
            .Select(item => new CustomerQuickViewDto
            {
                Id = item.Id,
                Name = item.Name,
                Code = item.Code,
                Phone = item.Phone,
                Email = item.Email,
                TaxCode = item.TaxCode,
                Address = item.Address,
                Note = item.Note,
                PriceTier = item.PriceTier,
                CustomerGroup = item.CustomerGroup,
                HaveDebt = item.HaveDebt,
                IsActive = item.IsActive,
                IsImportedFromOldSystem = item.IsImportedFromOldSystem,
                CreatedAtUtc = item.CreatedAtUtc
            })
            .FirstOrDefaultAsync(ct);

    public Task<Customer?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
        => _db.Set<Customer>()
            .FirstOrDefaultAsync(item => item.StoreId == storeId && item.Id == id, ct);

    public Task<bool> ExistsCodeAsync(
        int storeId,
        string code,
        int? excludeId,
        CancellationToken ct = default)
        => _db.Set<Customer>().AnyAsync(item =>
            item.StoreId == storeId
            && item.Code == code
            && (!excludeId.HasValue || item.Id != excludeId.Value), ct);

    public Task<bool> ExistsPhoneAsync(
        int storeId,
        string phone,
        int? excludeId,
        CancellationToken ct = default)
        => _db.Set<Customer>().AnyAsync(item =>
            item.StoreId == storeId
            && item.Phone == phone
            && (!excludeId.HasValue || item.Id != excludeId.Value), ct);

    public Task AddAsync(Customer customer, CancellationToken ct = default)
        => _db.Set<Customer>().AddAsync(customer, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    private IQueryable<Customer> BuildReadQuery(int storeId)
        => _db.Set<Customer>()
            .AsNoTracking()
            .Where(item => item.StoreId == storeId);

    private IQueryable<Customer> ApplySearch(IQueryable<Customer> query, string? searchString)
    {
        if (string.IsNullOrWhiteSpace(searchString)) return query;
        var search = searchString.Trim();

        if (_db.Database.IsRelational())
        {
            var accentInsensitiveSearch = search.Replace('Đ', 'D').Replace('đ', 'd');
            return query.Where(item =>
                EF.Functions.Collate(item.Name.Replace("Đ", "D").Replace("đ", "d"), AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || (item.Code != null && EF.Functions.Collate(item.Code.Replace("Đ", "D").Replace("đ", "d"), AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch))
                || (item.Phone != null && EF.Functions.Collate(item.Phone, AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch))
                || (item.Email != null && EF.Functions.Collate(item.Email, AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch))
                || (item.TaxCode != null && EF.Functions.Collate(item.TaxCode, AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)));
        }

        return query.Where(item =>
            item.Name.Contains(search)
            || (item.Code != null && item.Code.Contains(search))
            || (item.Phone != null && item.Phone.Contains(search))
            || (item.Email != null && item.Email.Contains(search))
            || (item.TaxCode != null && item.TaxCode.Contains(search)));
    }

    private static IQueryable<Customer> ApplyPriceTier(IQueryable<Customer> query, string? priceTier)
        => string.IsNullOrWhiteSpace(priceTier)
            ? query
            : query.Where(item => item.PriceTier == priceTier);

    private static IQueryable<Customer> ApplyStatus(IQueryable<Customer> query, bool? status)
        => status.HasValue ? query.Where(item => item.IsActive == status.Value) : query;

    private static IQueryable<Customer> ApplyDebt(IQueryable<Customer> query, bool? haveDebt)
        => haveDebt.HasValue ? query.Where(item => item.HaveDebt == haveDebt.Value) : query;
}
