using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public class InventoryAdjustmentDocumentNumberRepository
    : IInventoryAdjustmentDocumentNumberRepository
{
    private readonly AppDbContext _db;

    public InventoryAdjustmentDocumentNumberRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<int> CountTodayAsync(
        string prefix,
        CancellationToken ct = default)
    {
        return _db.InventoryAdjustmentDocuments
            .AsNoTracking()
            .CountAsync(x => x.DocumentNo.StartsWith(prefix), ct);
    }
}