using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Brands;

public interface IBrandRepository
{
    Task<(IReadOnlyList<Brand> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default);

    Task<Brand?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default);
    Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default);

    Task AddAsync(Brand entity, CancellationToken ct = default);
    /// <summary>
    /// Đánh dấu entity là Deleted để DbContext chuyển thành soft delete.
    /// </summary>
    void Remove(Brand entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
