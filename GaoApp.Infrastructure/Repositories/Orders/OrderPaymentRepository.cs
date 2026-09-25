using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Orders;

public sealed class OrderPaymentRepository : IOrderPaymentRepository
{
    private readonly AppDbContext _db;
    public OrderPaymentRepository(AppDbContext db) => _db = db;

    public async Task LockOrderAsync(int orderId, Guid requestId, CancellationToken ct = default)
    {
        var storeId = _db.CurrentStoreId ?? throw new InvalidOperationException("Payment requires a store.");
        if (_db.Database.CurrentTransaction is null) throw new InvalidOperationException("Payment lock requires a transaction.");
        var resource = $"pos-collection:{storeId}:{requestId:N}";
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @result < 0 THROW 51000, 'Payment request is busy.', 1;", ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 51000)
        {
            throw new GaoApp.Application.Common.Exceptions.ConflictAppException("Lần thu đang được xử lý. Vui lòng gửi lại cùng yêu cầu.");
        }
        // An update lock serializes manual collections for this order across Web instances.
        await _db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {orderId} AND [StoreId] = {storeId}")
            .AsNoTracking().Select(x => x.Id).FirstOrDefaultAsync(ct);
    }

    public Task<OrderPayment?> GetByClientRequestIdAsync(Guid requestId, CancellationToken ct = default)
    {
        var storeId = _db.CurrentStoreId ?? throw new InvalidOperationException("Payment requires a store.");
        // Include cancelled entries so retrying a removed collection cannot recreate it.
        return _db.OrderPayments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == storeId && x.ClientRequestId == requestId, ct);
    }

    public Task AddAsync(OrderPayment payment, CancellationToken ct = default)
        => _db.OrderPayments.AddAsync(payment, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public Task<Order?> GetDraftOrderForPaymentAsync(int orderId, CancellationToken ct = default)
        => _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.Status == OrderStatus.Draft, ct);

    public Task<OrderPayment?> GetDraftPaymentAsync(int paymentId, CancellationToken ct = default)
        => _db.OrderPayments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.Order.Status == OrderStatus.Draft, ct);
}
