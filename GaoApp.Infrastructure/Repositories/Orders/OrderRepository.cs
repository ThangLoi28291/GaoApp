using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace GaoApp.Infrastructure.Repositories.Orders;

public sealed class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;

    public OrderRepository(AppDbContext db) => _db = db;

    public Task AddAsync(Order order, CancellationToken ct = default)
        => _db.Orders.AddAsync(order, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public async Task<Order?> GetDraftAsync(int orderId, CancellationToken ct = default)
    {
        return await _db.Orders
            .Include(x => x.Customer)
            .Include(x => x.InventoryIssue)

            .Include(x => x.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant!)
                    .ThenInclude(v => v.PrimaryProductImage!)
                        .ThenInclude(pi => pi.MediaAsset)

            .Include(x => x.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant!)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.ProductImages.Where(pi => !pi.IsDeleted))
                            .ThenInclude(pi => pi.MediaAsset)

            // NEW:
            // Load bảng quy đổi đơn vị để POS hiển thị bảng giá:
            // Cái / Lốc / Thùng, giá lẻ, giá sỉ.
            .Include(x => x.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant!)
                    .ThenInclude(v => v.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
                        .ThenInclude(c => c.Unit)

            .Include(x => x.Payments.Where(p => !p.IsDeleted))

            .Include(x => x.RewardVouchers.Where(v => !v.IsDeleted))
                .ThenInclude(v => v.Voucher)

            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == orderId && !x.IsDeleted, ct);
    }

    public Task<OrderLine?> GetDraftLineAsync(int lineId, CancellationToken ct = default)
        => _db.OrderLines
            .Include(l => l.Order)
            .FirstOrDefaultAsync(l => l.Id == lineId && l.Order.Status == OrderStatus.Draft, ct);
    public Task<Order?> GetByIdAsync(int orderId, CancellationToken ct = default)
      => _db.Orders
          .Include(o => o.Customer)
          .Include(o => o.InventoryIssue)

          .Include(o => o.Lines.Where(l => !l.IsDeleted))
              .ThenInclude(l => l.Variant!)
                  .ThenInclude(v => v.PrimaryProductImage!)
                      .ThenInclude(pi => pi.MediaAsset)

          .Include(o => o.Lines.Where(l => !l.IsDeleted))
              .ThenInclude(l => l.Variant!)
                  .ThenInclude(v => v.Product)
                      .ThenInclude(p => p.ProductImages.Where(pi => !pi.IsDeleted))
                          .ThenInclude(pi => pi.MediaAsset)

          // NEW: bảng giá đơn vị cho popup POS
          .Include(o => o.Lines.Where(l => !l.IsDeleted))
              .ThenInclude(l => l.Variant!)
                  .ThenInclude(v => v.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
                      .ThenInclude(c => c.Unit)

          .Include(o => o.Payments.Where(p => !p.IsDeleted))

          .Include(o => o.RewardVouchers.Where(v => !v.IsDeleted))
              .ThenInclude(v => v.Voucher)

          .AsSplitQuery()
          .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, ct);
    public async Task<(List<Order> Items, int Total)> QueryOrdersAsync(
      DateTime? fromUtc,
      DateTime? toUtcExclusive,
      OrderStatus? status,
      string? keyword,
      int page,
      int pageSize,
      CancellationToken ct = default)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var q = _db.Orders
      .AsNoTracking()
      .Where(o => !o.IsDeleted);

        // Ẩn đơn nháp rỗng khỏi màn danh sách đơn.
        // Draft rỗng vẫn giữ trong POS để làm giỏ hiện tại,
        // nhưng không đưa vào lịch sử đơn.
        q = q.Where(o =>
            o.Status != OrderStatus.Draft
            || o.GrandTotal > 0
            || o.PaidTotal > 0
            || !string.IsNullOrWhiteSpace(o.Note)
            || o.Lines.Any(l => !l.IsDeleted)
            || o.Payments.Any(p => !p.IsDeleted));

        // 1) Lọc theo trạng thái
        if (status.HasValue)
            q = q.Where(o => o.Status == status.Value);

        // 2) Lọc theo ngày (chuẩn POS)
        // - Completed: dùng CompletedAtUtc (vì thời điểm chốt)
        // - Draft: dùng CreatedAtUtc
        // - Cancelled: tùy bạn muốn dùng CreatedAtUtc hay CompletedAtUtc (thường CancelledAtUtc riêng),
        //   hiện bạn chưa có CancelledAtUtc => dùng CreatedAtUtc để lọc tạm.
        if (fromUtc.HasValue || toUtcExclusive.HasValue)
        {
            q = q.Where(o =>
                (o.Status == OrderStatus.Completed &&
                    o.CompletedAtUtc.HasValue &&
                    (!fromUtc.HasValue || o.CompletedAtUtc.Value >= fromUtc.Value) &&
                    (!toUtcExclusive.HasValue || o.CompletedAtUtc.Value < toUtcExclusive.Value))
                ||
                (o.Status == OrderStatus.Voided &&
                    o.CompletedAtUtc.HasValue &&
                    (!fromUtc.HasValue || o.CompletedAtUtc.Value >= fromUtc.Value) &&
                    (!toUtcExclusive.HasValue || o.CompletedAtUtc.Value < toUtcExclusive.Value))
                ||
                (o.Status == OrderStatus.Refunded &&
                    o.CompletedAtUtc.HasValue &&
                    (!fromUtc.HasValue || o.CompletedAtUtc.Value >= fromUtc.Value) &&
                    (!toUtcExclusive.HasValue || o.CompletedAtUtc.Value < toUtcExclusive.Value))
                ||
                (o.Status == OrderStatus.Draft &&
                    (!fromUtc.HasValue || o.CreatedAtUtc >= fromUtc.Value) &&
                    (!toUtcExclusive.HasValue || o.CreatedAtUtc < toUtcExclusive.Value))
                ||
                (o.Status == OrderStatus.Cancelled &&
                    (!fromUtc.HasValue || o.CreatedAtUtc >= fromUtc.Value) &&
                    (!toUtcExclusive.HasValue || o.CreatedAtUtc < toUtcExclusive.Value))
                ||
                (o.Status == OrderStatus.OnHold &&
                    (!fromUtc.HasValue || o.CreatedAtUtc >= fromUtc.Value) &&
                    (!toUtcExclusive.HasValue || o.CreatedAtUtc < toUtcExclusive.Value))
            );
        }

        // 3) Keyword: OrderNumber / Note
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(o =>
                (o.OrderNumber != null && o.OrderNumber.Contains(k)) ||
                (o.Note != null && o.Note.Contains(k)));
        }

        // 4) Tổng số bản ghi
        var total = await q.CountAsync(ct);

        // 5) Sort: Completed ưu tiên theo CompletedAtUtc desc (nếu có), còn lại theo Id desc
        q = q.OrderByDescending(o => o.CompletedAtUtc ?? o.CreatedAtUtc)
             .ThenByDescending(o => o.Id);

        // 6) Paging
        var items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
    public async Task<bool> ExistsDraftByShiftAsync(int shiftId, CancellationToken ct = default)
    {
        return await _db.Orders
            .AnyAsync(x => x.POSShiftId == shiftId && x.Status == OrderStatus.Draft, ct);
    }

    public async Task<int> CountDraftByShiftAsync(int shiftId, CancellationToken ct = default)
    {
        return await _db.Orders
            .CountAsync(x => x.POSShiftId == shiftId && x.Status == OrderStatus.Draft, ct);
    }
    public async Task<List<Order>> GetByShiftIdAsync(int shiftId, CancellationToken ct = default)
    {
        return await _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Payments.Where(p => !p.IsDeleted))
            .Include(o => o.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant)
            .AsSplitQuery()
            .Where(o => o.POSShiftId == shiftId && !o.IsDeleted)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(ct);
    }
    public async Task<List<Order>> GetHeldOrdersByShiftAsync(int shiftId, CancellationToken ct = default)
    {
        return await _db.Orders
            .Include(x => x.Customer)
            .Include(x => x.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant)
            .Where(x => x.POSShiftId == shiftId && x.Status == OrderStatus.OnHold && !x.IsDeleted)
            .OrderByDescending(x => x.HeldAtUtc)
            .ToListAsync(ct);
    }
    public async Task<List<Order>> GetDraftOrdersByShiftAsync(int shiftId, CancellationToken ct = default)
    {
        return await _db.Orders
            .Include(x => x.Customer)

            .Include(o => o.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant!)
                    .ThenInclude(v => v.PrimaryProductImage!)
                        .ThenInclude(pi => pi.MediaAsset)

            .Include(o => o.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant!)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.ProductImages.Where(pi => !pi.IsDeleted))
                            .ThenInclude(pi => pi.MediaAsset)

            // NEW: bảng giá đơn vị cho POS screen
            .Include(o => o.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant!)
                    .ThenInclude(v => v.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
                        .ThenInclude(c => c.Unit)

            .Include(x => x.Payments.Where(p => !p.IsDeleted))

            .Include(x => x.RewardVouchers.Where(v => !v.IsDeleted))
                .ThenInclude(v => v.Voucher)

            .AsSplitQuery()
            .Where(x => x.POSShiftId == shiftId && x.Status == OrderStatus.Draft && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }
    public async Task<Order?> GetByIdWithDetailsAsync(int orderId, CancellationToken ct = default)
    {
        return await _db.Orders
            .Include(x => x.Customer)
            .Include(x => x.InventoryIssue)
            .Include(x => x.POSShift)
            .Include(x => x.Store)

            .Include(x => x.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant!)
                    .ThenInclude(v => v.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
                        .ThenInclude(c => c.Unit)

            .Include(x => x.Payments.Where(p => !p.IsDeleted))

            .Include(x => x.RewardVouchers)
                .ThenInclude(x => x.Voucher)

            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == orderId && !x.IsDeleted, ct);
    }
    public async Task<Order?> GetCompletedOrderForVoidAsync(int orderId, CancellationToken ct = default)
    {
        return await _db.Orders
            .Include(x => x.Customer)
            .Include(x => x.POSShift)
            .Include(x => x.Store)

            .Include(x => x.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant!)
                    .ThenInclude(v => v.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
                        .ThenInclude(c => c.Unit)

            .Include(x => x.Payments.Where(p => !p.IsDeleted))

            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == orderId && !x.IsDeleted, ct);
    }

    public async Task<Order?> GetCompletedOrderForRefundAsync(int orderId, CancellationToken ct = default)
    {
        return await _db.Orders
            .Include(x => x.Customer)
            .Include(x => x.POSShift)
            .Include(x => x.Store)

            .Include(x => x.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant!)
                    .ThenInclude(v => v.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
                        .ThenInclude(c => c.Unit)

            .Include(x => x.Payments.Where(p => !p.IsDeleted))

            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == orderId && !x.IsDeleted, ct);
    }
    public async Task<Order?> GetDraftForFinalizeAsync(int orderId, CancellationToken ct = default)
    {
        return await _db.Orders
            .Include(x => x.Customer)
            .Include(x => x.POSShift)
            .Include(x => x.InventoryIssue)
            .Include(x => x.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant)
            .Include(x => x.Payments.Where(p => !p.IsDeleted))
            .Include(x => x.RewardVouchers.Where(v => !v.IsDeleted))
                .ThenInclude(v => v.Voucher)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == orderId && !x.IsDeleted, ct);
    }
    public void Update(Order order)
    {
        _db.Orders.Update(order);
    }
    public async Task<List<Order>> GetHeldOrdersByStoreAsync(int storeId, CancellationToken ct = default)
    {
        return await _db.Orders
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.InventoryIssue)
            .Include(x => x.Lines.Where(l => !l.IsDeleted))
                .ThenInclude(l => l.Variant)
            .Include(x => x.Payments.Where(p => !p.IsDeleted))
            .Include(x => x.POSShift)
                .ThenInclude(s => s.Terminal)
            .AsSplitQuery()
            .Where(x =>
                x.StoreId == storeId &&
                x.Status == OrderStatus.OnHold &&
                !x.IsDeleted)
            .OrderByDescending(x => x.HeldAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
    }
    public async Task ReplaceRewardVouchersAsync(
    int orderId,
    List<OrderRewardVoucher> vouchers,
    CancellationToken ct = default)
    {
        var oldApplied = await _db.OrderRewardVouchers
            .Where(x => x.OrderId == orderId)
            .ToListAsync(ct);

        if (oldApplied.Any())
        {
            _db.OrderRewardVouchers.RemoveRange(oldApplied);
        }

        if (vouchers.Any())
        {
            await _db.OrderRewardVouchers.AddRangeAsync(vouchers, ct);
        }
    }

    public async Task ClearRewardVouchersAsync(
        int orderId,
        CancellationToken ct = default)
    {
        var oldApplied = await _db.OrderRewardVouchers
            .Where(x => x.OrderId == orderId)
            .ToListAsync(ct);

        if (oldApplied.Any())
        {
            _db.OrderRewardVouchers.RemoveRange(oldApplied);
        }
    }
}
