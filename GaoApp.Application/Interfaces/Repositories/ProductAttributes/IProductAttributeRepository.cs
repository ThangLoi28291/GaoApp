using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.ProductAttributes;

public interface IProductAttributeRepository
{
    Task<(IReadOnlyList<ProductAttribute> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default);

    Task<ProductAttribute?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default);
    Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default);

    Task<List<ProductAttribute>> GetAllAsync(int storeId, CancellationToken ct = default); // dùng cho dropdown AttributeValue

    Task AddAsync(ProductAttribute entity, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
