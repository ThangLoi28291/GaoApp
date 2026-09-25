using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Orders;

public interface IOrderFinalizeGuard
{
    Task ValidateAsync(Order order, CancellationToken ct);
}
