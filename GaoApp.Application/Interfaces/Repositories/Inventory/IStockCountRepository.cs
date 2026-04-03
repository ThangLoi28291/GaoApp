using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IStockCountRepository
{
    Task AddAsync(StockCountDocument entity, CancellationToken ct = default);

    Task<StockCountDocument?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<StockCountDocument?> GetDetailAsync(int id, CancellationToken ct = default);

    Task<StockCountLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default);

    Task<int> GetNextLineNoAsync(int stockCountDocumentId, CancellationToken ct = default);

    Task<bool> WarehouseExistsAsync(int warehouseId, CancellationToken ct = default);

    Task<ProductVariant?> GetVariantForStockCountAsync(int productVariantId, CancellationToken ct = default);

    Task<InventoryBalance?> GetInventoryBalanceAsync(int warehouseId, int productVariantId, CancellationToken ct = default);

    Task<ProductUnitConversion?> GetConversionAsync(int productVariantId, int unitId, CancellationToken ct = default);

    Task<ProductUnitConversion?> GetBaseConversionAsync(int productVariantId, CancellationToken ct = default);

    Task RemoveLineAsync(StockCountLine line, CancellationToken ct = default);

    Task<List<StockCountDocument>> GetListAsync(CancellationToken ct = default);

    Task<int> CountTodayAsync(DateTime localDate, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
    Task<StockCountLine?> FindExistingEditableLineAsync(
    int stockCountDocumentId,
    int productVariantId,
    int unitId,
    CancellationToken ct = default);
    Task<string?> GetLastDocumentNoByDateAsync(DateTime localDate, CancellationToken ct = default);

    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
}