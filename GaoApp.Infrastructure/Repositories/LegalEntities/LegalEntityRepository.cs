using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.LegalEntities;

public sealed class LegalEntityRepository : ILegalEntityRepository
{
    private readonly AppDbContext _db;

    public LegalEntityRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<List<LegalEntity>> GetAllAsync(CancellationToken ct = default)
        => _db.LegalEntities
            .AsNoTracking()
            .Include(x => x.DefaultWarehouse)
            .Include(x => x.InvoiceProviderSetting)
            .Include(x => x.Warehouses)
            .OrderBy(x => x.SalePriority)
            .ThenBy(x => x.Code)
            .ToListAsync(ct);

    public Task<LegalEntity?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
        => _db.LegalEntities
            .Include(x => x.DefaultWarehouse)
            .Include(x => x.InvoiceProviderSetting)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<LegalEntity?> GetDefaultForPurchaseAsync(
        CancellationToken ct = default)
        => _db.LegalEntities
            .Where(x => x.IsActive && x.IsDefaultForPurchase)
            .OrderBy(x => x.SalePriority)
            .FirstOrDefaultAsync(ct);

    public Task<LegalEntity?> GetFirstActiveByPriorityAsync(
        CancellationToken ct = default)
        => _db.LegalEntities
            .Where(x => x.IsActive)
            .OrderBy(x => x.SalePriority)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

    public Task<bool> ExistsCodeAsync(
        string code,
        int? excludeId = null,
        CancellationToken ct = default)
        => _db.LegalEntities.AnyAsync(x =>
            x.Code == code &&
            (!excludeId.HasValue || x.Id != excludeId.Value),
            ct);

    public Task<bool> ExistsSalePriorityAsync(
        int salePriority,
        int? excludeId = null,
        CancellationToken ct = default)
        => _db.LegalEntities.AnyAsync(x =>
            x.IsActive &&
            x.SalePriority == salePriority &&
            (!excludeId.HasValue || x.Id != excludeId.Value),
            ct);

    public Task<bool> ExistsTaxCodeAsync(
        string taxCode,
        int? excludeId = null,
        CancellationToken ct = default)
        => _db.LegalEntities.AnyAsync(x =>
            x.TaxCode == taxCode &&
            (!excludeId.HasValue || x.Id != excludeId.Value),
            ct);

    public Task<bool> ExistsDefaultWarehouseAssignmentAsync(
        int warehouseId,
        int? excludeId = null,
        CancellationToken ct = default)
        => _db.LegalEntities.AnyAsync(x =>
            x.DefaultWarehouseId == warehouseId &&
            (!excludeId.HasValue || x.Id != excludeId.Value),
            ct);

    public Task<bool> ExistsInvoiceSettingAssignmentAsync(
        int invoiceProviderSettingId,
        int? excludeId = null,
        CancellationToken ct = default)
        => _db.LegalEntities.AnyAsync(x =>
            x.InvoiceProviderSettingId == invoiceProviderSettingId &&
            (!excludeId.HasValue || x.Id != excludeId.Value),
            ct);

    public Task<bool> HasActiveWarehousesAsync(
        int legalEntityId,
        CancellationToken ct = default)
        => _db.Warehouses.AnyAsync(x =>
            x.LegalEntityId == legalEntityId && x.IsActive,
            ct);

    public Task<Store?> GetStoreAsync(
        int storeId,
        CancellationToken ct = default)
        => _db.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == storeId, ct);

    public Task<Store?> GetStoreForUpdateAsync(
        int storeId,
        CancellationToken ct = default)
        => _db.Stores
            .FirstOrDefaultAsync(x => x.Id == storeId, ct);

    public async Task ClearDefaultForPurchaseAsync(
        int? exceptLegalEntityId = null,
        CancellationToken ct = default)
    {
        var query = _db.LegalEntities
            .Where(x => x.IsDefaultForPurchase);

        if (exceptLegalEntityId.HasValue)
        {
            query = query.Where(x => x.Id != exceptLegalEntityId.Value);
        }

        var entities = await query.ToListAsync(ct);
        foreach (var entity in entities)
        {
            entity.IsDefaultForPurchase = false;
        }
    }

    public Task AddAsync(LegalEntity entity, CancellationToken ct = default)
        => _db.LegalEntities.AddAsync(entity, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
