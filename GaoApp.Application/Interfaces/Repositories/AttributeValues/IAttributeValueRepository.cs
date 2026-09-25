using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.AttributeValues;

public interface IAttributeValueRepository
{
    Task<(IReadOnlyList<AttributeValue> Items, int TotalItems)> GetPagedAsync(
   int storeId,
   int? attributeId,
   bool? status,
   string? search,
   int page,
   int pageSize,
   CancellationToken ct = default);

    Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default);

    Task<AttributeValue?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(int storeId, int attributeId, string code, int? excludeId, CancellationToken ct = default);
    Task<bool> ExistsNameAsync(int storeId, int attributeId, string name, int? excludeId, CancellationToken ct = default);

    Task AddAsync(AttributeValue entity, CancellationToken ct = default);

    void Remove(AttributeValue entity);
    Task SaveChangesAsync(CancellationToken ct = default);
  

    Task<bool> IsUsedAsync(
        int storeId,
        int id,
        CancellationToken ct = default);
}
