using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Products;

public interface IProductImageRepository
{
    Task<List<ProductImage>> GetByProductIdAsync(
         int productId,
         int storeId,
         CancellationToken ct = default);

    // Command
    Task AddAsync(ProductImage entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<ProductImage> entities, CancellationToken ct = default);

    void Update(ProductImage entity);
    void Remove(ProductImage entity);
    void RemoveRange(IEnumerable<ProductImage> entities);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<int> GetNextSortOrderAsync(int storeId, int productId, CancellationToken ct = default);
    Task UnsetPrimaryAsync(int storeId, int productId, CancellationToken ct = default);
     Task<bool> SetPrimaryByIdAsync(int storeId, int productId, int id, CancellationToken ct = default);

    // ✅ BẮT BUỘC – dùng để fallback khi xóa + đổi primary
    Task<int?> GetFirstActiveIdAsync(int storeId, int productId, CancellationToken ct = default);
    Task MoveToFirstAsync(int storeId, int productId, int imageId, CancellationToken ct = default);
    void ClearTracking(int storeId, int productId);

}
