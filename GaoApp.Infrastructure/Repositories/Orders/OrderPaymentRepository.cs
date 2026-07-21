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