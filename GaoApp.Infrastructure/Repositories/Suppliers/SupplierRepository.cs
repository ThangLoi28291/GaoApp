using GaoApp.Application.Interfaces.Repositories.Suppliers;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Helpers;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace GaoApp.Infrastructure.Repositories.Suppliers;

public sealed class SupplierRepository : ISupplierRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;
    public SupplierRepository(AppDbContext db) => _db = db;

    public Task<(IReadOnlyList<Supplier> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default)
        => GetPagedAsync(storeId, search, status: null, page, pageSize, ct);

    public async Task<(IReadOnlyList<Supplier> Items, int TotalItems)> GetPagedAsync(
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 500) pageSize = 500;

        var q = _db.Set<Supplier>()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            if (_db.Database.IsRelational())
            {
                var accentInsensitiveSearch = search
                    .Replace('Đ', 'D')
                    .Replace('đ', 'd');

                q = q.Where(x =>
                    EF.Functions.Collate(
                        x.Code,
                        AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch) ||
                    EF.Functions.Collate(
                        x.Name
                            .Replace("Đ", "D")
                            .Replace("đ", "d"),
                        AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch) ||
                    (x.Phone != null &&
                     EF.Functions.Collate(
                         x.Phone,
                         AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)) ||
                    (x.TaxCode != null &&
                     EF.Functions.Collate(
                         x.TaxCode,
                         AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)));
            }
            else
            {
                q = q.Where(x =>
                    x.Code.Contains(search) ||
                    x.Name.Contains(search) ||
                    (x.Phone != null && x.Phone.Contains(search)) ||
                    (x.TaxCode != null && x.TaxCode.Contains(search)));
            }
        }

        if (status.HasValue)
        {
            q = q.Where(x => x.IsActive == status.Value);
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default)
    {
        var q = _db.Set<Supplier>()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        var totalItems = await q.CountAsync(ct);
        var activeItems = await q.CountAsync(x => x.IsActive, ct);

        return (
            totalItems,
            activeItems,
            totalItems - activeItems);
    }

    public Task<Supplier?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
        => _db.Set<Supplier>().FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id, ct);

    public Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default)
        => _db.Set<Supplier>().AnyAsync(x =>
            x.StoreId == storeId &&
            x.Code == code &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default)
        => _db.Set<Supplier>().AnyAsync(x =>
            x.StoreId == storeId &&
            x.Name == name &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public async Task<IReadOnlyList<Supplier>> GetActiveByNormalizedTaxCodeAsync(
        int storeId,
        string normalizedTaxCode,
        CancellationToken ct = default)
    {
        if (_db.Database.IsRelational())
        {
            return await _db.Suppliers
                .AsNoTracking()
                .Where(x => x.StoreId == storeId &&
                            !x.IsDeleted &&
                            x.IsActive &&
                            x.NormalizedTaxCode == normalizedTaxCode)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);
        }

        var eligible = await _db.Suppliers
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        return eligible
            .Where(x => string.Equals(
                TaxCodeIdentityNormalizer.Normalize(x.TaxCode),
                normalizedTaxCode,
                StringComparison.Ordinal))
            .ToList();
    }

    public Task<Supplier?> GetHistoricalByIdAsync(
        int storeId,
        int supplierId,
        CancellationToken ct = default)
        => _db.Suppliers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId && x.Id == supplierId,
                ct);

    public Task AddAsync(Supplier entity, CancellationToken ct = default)
        => _db.Set<Supplier>().AddAsync(entity, ct).AsTask();

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(
                "Dữ liệu đã được người khác thay đổi. Vui lòng tải lại và thử lại.");
        }
    }

    public async Task<bool> SaveChangesWithActiveTaxCodeGuardAsync(
        int storeId,
        string normalizedTaxCode,
        int? excludeSupplierId,
        CancellationToken ct = default)
    {
        if (storeId <= 0 || string.IsNullOrWhiteSpace(normalizedTaxCode))
            throw new ArgumentException("A valid Store and normalized tax code are required.");

        if (!_db.Database.IsRelational())
        {
            var rows = await _db.Suppliers
                .AsNoTracking()
                .Where(x => x.StoreId == storeId &&
                            !x.IsDeleted &&
                            x.IsActive &&
                            (!excludeSupplierId.HasValue || x.Id != excludeSupplierId.Value))
                .ToListAsync(ct);
            if (rows.Any(x => string.Equals(
                    TaxCodeIdentityNormalizer.Normalize(x.TaxCode),
                    normalizedTaxCode,
                    StringComparison.Ordinal)))
            {
                return false;
            }

            await SaveChangesAsync(ct);
            return true;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        try
        {
            var excludedId = excludeSupplierId ?? 0;
            var conflict = await _db.Suppliers
                .FromSqlInterpolated($$"""
                    SELECT TOP (1) *
                    FROM [dbo].[Suppliers] WITH
                        (UPDLOCK, HOLDLOCK, INDEX([IX_Suppliers_StoreId_NormalizedTaxCode_State]))
                    WHERE [StoreId] = {{storeId}}
                      AND [NormalizedTaxCode] = {{normalizedTaxCode}}
                      AND [IsDeleted] = 0
                      AND [IsActive] = 1
                      AND [Id] <> {{excludedId}}
                    ORDER BY [Id]
                    """)
                .AsNoTracking()
                .AnyAsync(ct);
            if (conflict)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            await SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
