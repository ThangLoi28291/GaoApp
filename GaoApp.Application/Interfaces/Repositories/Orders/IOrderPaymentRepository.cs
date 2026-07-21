using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Orders;

public interface IOrderPaymentRepository
{
    Task<Order?> GetDraftOrderForPaymentAsync(int orderId, CancellationToken ct = default);
    Task<OrderPayment?> GetDraftPaymentAsync(int paymentId, CancellationToken ct = default);

    Task AddAsync(OrderPayment payment, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}