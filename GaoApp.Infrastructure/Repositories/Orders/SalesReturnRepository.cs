using GaoApp.Application.DTOs.Returns;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Orders;

/// <summary>
/// Repository đọc/ghi SalesReturn phục vụ:
/// - trả hàng / hoàn tiền
/// - thống kê hậu mãi
/// - nền dữ liệu cho SalesReturnService mức 2
///
/// Lưu ý:
/// - Repository này chỉ lo query/persist
/// - Không chứa business rule costing
/// - Không mirror valuation ở đây
/// </summary>
public sealed class SalesReturnRepository : ISalesReturnRepository
{
    private readonly AppDbContext _db;

    public SalesReturnRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(SalesReturn entity, CancellationToken ct = default)
        => _db.SalesReturns.AddAsync(entity, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public Task<SalesReturn?> GetByIdAsync(int id, CancellationToken ct = default)
        => _db.SalesReturns.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    public Task<SalesReturn?> GetByIdWithDetailsAsync(int id, CancellationToken ct = default)
        => _db.SalesReturns
            .Include(x => x.Order)
            .Include(x => x.Lines.Where(l => !l.IsDeleted))
            .Include(x => x.Payments.Where(p => !p.IsDeleted))
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    public Task<List<SalesReturn>> GetByOrderIdAsync(int orderId, CancellationToken ct = default)
        => _db.SalesReturns
            .AsNoTracking()
            .Include(x => x.Lines.Where(l => !l.IsDeleted))
            .Include(x => x.Payments.Where(p => !p.IsDeleted))
            .Where(x => x.OrderId == orderId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

    /// <summary>
    /// Tổng số lượng đã trả theo đơn vị bán của 1 order line.
    /// Chỉ tính các phiếu completed, chưa bị xóa mềm.
    /// </summary>
    public async Task<decimal> GetReturnedQuantityByOrderLineAsync(int orderLineId, CancellationToken ct = default)
    {
        return await _db.SalesReturnLines
            .Where(x =>
                x.OrderLineId == orderLineId &&
                !x.IsDeleted &&
                !x.SalesReturn.IsDeleted &&
                x.SalesReturn.Status == SalesReturnStatus.Completed)
            .SumAsync(x => (decimal?)x.ReturnQuantity, ct) ?? 0m;
    }

    /// <summary>
    /// Tổng số lượng đã trả theo đơn vị gốc (base quantity) của 1 order line.
    /// Đây là số quan trọng cho mức 2 vì inventory movement + valuation đều bám base qty.
    /// </summary>
    public async Task<decimal> GetReturnedBaseQuantityByOrderLineAsync(int orderLineId, CancellationToken ct = default)
    {
        return await _db.SalesReturnLines
            .Where(x =>
                x.OrderLineId == orderLineId &&
                !x.IsDeleted &&
                !x.SalesReturn.IsDeleted &&
                x.SalesReturn.Status == SalesReturnStatus.Completed)
            .SumAsync(x => (decimal?)x.ReturnBaseQuantity, ct) ?? 0m;
    }

    /// <summary>
    /// Tổng tiền đã refund của toàn bộ sales return completed thuộc 1 order.
    /// Dùng để chặn refund vượt tổng giá trị nghiệp vụ cho phép.
    /// </summary>
    public async Task<decimal> GetRefundedTotalByOrderAsync(int orderId, CancellationToken ct = default)
    {
        var money = await _db.SalesReturnPayments
            .Where(x =>
                !x.IsDeleted &&
                !x.SalesReturn.IsDeleted &&
                x.SalesReturn.OrderId == orderId &&
                x.SalesReturn.Status == SalesReturnStatus.Completed)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        return money + (await _db.SalesReturns.Where(x => x.OrderId == orderId && x.Status == SalesReturnStatus.Completed)
            .SumAsync(x => (decimal?)x.DepositRestoredTotal, ct) ?? 0m);
    }

    /// <summary>
    /// Kiểm tra order line đã từng có phiếu return completed hay chưa.
    /// Hữu ích cho audit / badge / tối ưu flow service.
    /// </summary>
    public Task<bool> HasCompletedReturnByOrderLineAsync(int orderLineId, CancellationToken ct = default)
    {
        return _db.SalesReturnLines.AnyAsync(x =>
            x.OrderLineId == orderLineId &&
            !x.IsDeleted &&
            !x.SalesReturn.IsDeleted &&
            x.SalesReturn.Status == SalesReturnStatus.Completed, ct);
    }

    public Task<bool> HasCompletedReturnByOrderAsync(int orderId, CancellationToken ct = default)
    {
        return _db.SalesReturns.AnyAsync(x =>
            x.OrderId == orderId &&
            !x.IsDeleted &&
            x.Status == SalesReturnStatus.Completed, ct);
    }

    /// <summary>
    /// Lấy các return line completed của 1 order line.
    /// Dùng cho audit và phân tích partial return trước đó.
    /// </summary>
    public Task<List<SalesReturnLine>> GetCompletedReturnLinesByOrderLineAsync(
        int orderLineId,
        CancellationToken ct = default)
    {
        return _db.SalesReturnLines
            .AsNoTracking()
            .Include(x => x.SalesReturn)
            .Where(x =>
                x.OrderLineId == orderLineId &&
                !x.IsDeleted &&
                !x.SalesReturn.IsDeleted &&
                x.SalesReturn.Status == SalesReturnStatus.Completed)
            .OrderBy(x => x.SalesReturn.CompletedAtUtc ?? x.SalesReturn.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Tổng base qty đã trả và có nhập kho lại.
    ///
    /// Rule:
    /// - chỉ tính phiếu completed
    /// - chỉ tính line có Action = Restock
    /// </summary>
    public async Task<decimal> GetRestockedBaseQuantityByOrderLineAsync(
        int orderLineId,
        CancellationToken ct = default)
    {
        return await _db.SalesReturnLines
            .Where(x =>
                x.OrderLineId == orderLineId &&
                !x.IsDeleted &&
                x.Action == SalesReturnLineAction.Restock &&
                !x.SalesReturn.IsDeleted &&
                x.SalesReturn.Status == SalesReturnStatus.Completed)
            .SumAsync(x => (decimal?)x.ReturnBaseQuantity, ct) ?? 0m;
    }

    /// <summary>
    /// Tổng base qty đã trả nhưng không nhập kho lại.
    ///
    /// Rule:
    /// - chỉ tính phiếu completed
    /// - chỉ tính line có Action = NoRestock
    /// </summary>
    public async Task<decimal> GetNonRestockedBaseQuantityByOrderLineAsync(
        int orderLineId,
        CancellationToken ct = default)
    {
        return await _db.SalesReturnLines
            .Where(x =>
                x.OrderLineId == orderLineId &&
                !x.IsDeleted &&
                x.Action == SalesReturnLineAction.NoRestock &&
                !x.SalesReturn.IsDeleted &&
                x.SalesReturn.Status == SalesReturnStatus.Completed)
            .SumAsync(x => (decimal?)x.ReturnBaseQuantity, ct) ?? 0m;
    }

    /// <summary>
    /// Tổng hợp hậu mãi cho nhiều order cùng lúc để tránh N+1 query.
    /// Chỉ lấy phiếu completed, chưa bị xóa mềm.
    /// </summary>
    public async Task<List<OrderAfterSaleSummaryDto>> GetAfterSaleSummaryByOrderIdsAsync(
        IReadOnlyCollection<int> orderIds,
        CancellationToken ct = default)
    {
        if (orderIds == null || orderIds.Count == 0)
            return new List<OrderAfterSaleSummaryDto>();

        return await _db.SalesReturns
            .AsNoTracking()
            .Where(x =>
                orderIds.Contains(x.OrderId) &&
                !x.IsDeleted &&
                x.Status == SalesReturnStatus.Completed)
            .GroupBy(x => x.OrderId)
            .Select(g => new OrderAfterSaleSummaryDto
            {
                OrderId = g.Key,
                RefundedTotal = g.Sum(x => x.RefundTotal),
                ReturnCount = g.Count()
            })
            .ToListAsync(ct);
    }
}
