using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

/// <summary>
/// Repository thao tác với FIFO cost layer.
/// </summary>
public class InventoryCostLayerRepository : IInventoryCostLayerRepository
{
    private readonly AppDbContext _db;

    public InventoryCostLayerRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Thêm 1 cost layer mới.
    /// </summary>
    public Task AddAsync(InventoryCostLayer entity, CancellationToken ct = default)
        => _db.InventoryCostLayers.AddAsync(entity, ct).AsTask();

    /// <summary>
    /// Thêm nhiều cost layer.
    /// </summary>
    public Task AddRangeAsync(IEnumerable<InventoryCostLayer> entities, CancellationToken ct = default)
        => _db.InventoryCostLayers.AddRangeAsync(entities, ct);

    /// <summary>
    /// Lấy layer theo id.
    /// </summary>
    public Task<InventoryCostLayer?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return _db.InventoryCostLayers
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    /// <summary>
    /// Lấy các layer còn qty để consume theo FIFO.
    /// </summary>
    public Task<List<InventoryCostLayer>> GetOpenLayersAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default)
    {
        return _db.InventoryCostLayers
            .Where(x =>
                x.WarehouseId == warehouseId &&
                x.ProductVariantId == productVariantId &&
                x.RemainingQuantity > 0)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Lấy các layer còn qty để update/consume.
    ///
    /// Hiện tại EF Core không tự cung cấp row lock portable giữa các DB provider.
    /// Ở giai đoạn này tạm thời vẫn dùng query thường nhưng trả về tracked entities
    /// để service xử lý trong cùng transaction.
    ///
    /// Sau này nếu cần locking mạnh hơn cho SQL Server,
    /// có thể nâng cấp sang raw SQL với UPDLOCK/ROWLOCK/HOLDLOCK.
    /// </summary>
    public Task<List<InventoryCostLayer>> GetOpenLayersForUpdateAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default)
    {
        return _db.InventoryCostLayers
            .Where(x =>
                x.WarehouseId == warehouseId &&
                x.ProductVariantId == productVariantId &&
                x.RemainingQuantity > 0)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Lưu thay đổi.
    /// </summary>
    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}