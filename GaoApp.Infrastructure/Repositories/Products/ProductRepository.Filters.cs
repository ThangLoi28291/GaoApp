using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Products;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

public sealed partial class ProductRepository
{
    // Include inactive lookup entries so historical products remain discoverable; never include deleted or other-store entries.
    public async Task<List<ProductFilterOptionDto>> FilterOptionsAsync(int storeId, string kind, string? term, int? selectedId, CancellationToken ct = default)
    {
        term = term?.Trim() ?? "";
        if (term.Length > 100) throw new ValidationAppException("Từ khóa gợi ý tối đa 100 ký tự.");
        var q = kind switch
        {
            "supplier" => _db.Set<Supplier>().AsNoTracking().Where(x => x.StoreId == storeId)
                .Select(x => new { x.Id, x.Name, x.Code, x.IsActive }),
            "brand" => _db.Set<Brand>().AsNoTracking().Where(x => x.StoreId == storeId)
                .Select(x => new { x.Id, x.Name, x.Code, x.IsActive }),
            "unit" => _db.Set<Unit>().AsNoTracking().Where(x => x.StoreId == storeId)
                .Select(x => new { x.Id, x.Name, x.Code, x.IsActive }),
            _ => throw new ValidationAppException("Loại bộ lọc không hợp lệ.")
        };
        if (selectedId.HasValue) q = q.Where(x => x.Id == selectedId.Value);
        else if (term.Length > 0)
        {
            var normalized = term.Replace('Đ', 'D').Replace('đ', 'd');
            q = _db.Database.IsRelational()
                ? q.Where(x => EF.Functions.Collate(x.Name.Replace("Đ", "D").Replace("đ", "d"), AccentInsensitiveSearchCollation).Contains(normalized) ||
                    EF.Functions.Collate(x.Code, AccentInsensitiveSearchCollation).Contains(normalized))
                : q.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
        }
        var rows = await q.OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ThenBy(x => x.Id).Take(20).ToListAsync(ct);
        return rows.Select(x => new ProductFilterOptionDto(x.Id, x.Name, x.Code, x.IsActive)).ToList();
    }
}
