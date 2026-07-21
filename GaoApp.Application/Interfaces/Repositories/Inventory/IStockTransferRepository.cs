using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

/// <summary>
/// Repository cho nghiệp vụ chuyển kho.
/// Chỉ xử lý data access.
/// </summary>
public interface IStockTransferRepository
{
    Task<(List<StockTransferDocument> Items, int Total)> GetListAsync(
        int page,
        int pageSize,
        string? keyword,
        int? fromWarehouseId,
        int? toWarehouseId,
        int? status,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct = default);

    Task AddAsync(StockTransferDocument document, CancellationToken ct = default);

    Task<StockTransferDocument?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<StockTransferDocument?> GetDetailAsync(int id, CancellationToken ct = default);

    Task<StockTransferLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default);

    Task RemoveLineAsync(StockTransferLine line, CancellationToken ct = default);

    Task<int> GetNextLineNoAsync(int documentId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    // Ẩn transaction implementation khỏi Application layer
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);

    Task<bool> WarehouseExistsAsync(int warehouseId, CancellationToken ct = default);

    Task<Warehouse?> GetWarehouseByIdAsync(int warehouseId, CancellationToken ct = default);

    Task<ProductVariant?> GetVariantForTransferAsync(int productVariantId, CancellationToken ct = default);

    Task<string?> GetLastDocumentNoByDateAsync(DateTime documentDate, CancellationToken ct = default);
}