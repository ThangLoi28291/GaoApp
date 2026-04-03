using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

/// <summary>
/// Repository thao tác dữ liệu reservation tồn kho.
/// 
/// Nhiệm vụ:
/// - lưu reservation active
/// - truy vấn reservation active theo chứng từ nguồn
/// - kiểm tra reservation trùng
/// - hỗ trợ flow reserve / release / consume
/// </summary>
public class InventoryReservationRepository : IInventoryReservationRepository
{
    private readonly AppDbContext _context;

    public InventoryReservationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(InventoryReservation entity, CancellationToken ct = default)
    {
        await _context.InventoryReservations.AddAsync(entity, ct);
    }

    public async Task AddRangeAsync(IEnumerable<InventoryReservation> entities, CancellationToken ct = default)
    {
        await _context.InventoryReservations.AddRangeAsync(entities, ct);
    }

    /// <summary>
    /// Lấy tất cả reservation active theo chứng từ nguồn.
    /// Ví dụ:
    /// - toàn bộ reservation active của 1 order
    /// </summary>
    public async Task<List<InventoryReservation>> GetActiveByReferenceAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        CancellationToken ct = default)
    {
        referenceId = (referenceId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(referenceId))
            return new List<InventoryReservation>();

        return await _context.InventoryReservations
            .Where(x => x.ReferenceType == referenceType)
            .Where(x => x.ReferenceId == referenceId)
            .Where(x => x.Status == InventoryReservationStatus.Active)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Kiểm tra đã tồn tại reservation active cho đúng line/chứng từ/kho/variant hay chưa.
    /// Dùng để chống reserve trùng khi giữ đơn nhiều lần hoặc retry.
    /// </summary>
    public async Task<bool> ExistsActiveAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        int? referenceLineId,
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default)
    {
        referenceId = (referenceId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(referenceId))
            return false;

        return await _context.InventoryReservations
            .AnyAsync(x =>
                x.ReferenceType == referenceType &&
                x.ReferenceId == referenceId &&
                x.ReferenceLineId == referenceLineId &&
                x.WarehouseId == warehouseId &&
                x.ProductVariantId == productVariantId &&
                x.Status == InventoryReservationStatus.Active,
                ct);
    }

    /// <summary>
    /// Kiểm tra chứng từ nguồn hiện còn reservation active hay không.
    /// Ví dụ:
    /// - order hiện còn reservation active để consume khi finalize
    /// </summary>
    public async Task<bool> HasActiveByReferenceAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        CancellationToken ct = default)
    {
        referenceId = (referenceId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(referenceId))
            return false;

        return await _context.InventoryReservations
            .AnyAsync(x =>
                x.ReferenceType == referenceType &&
                x.ReferenceId == referenceId &&
                x.Status == InventoryReservationStatus.Active,
                ct);
    }

    
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}