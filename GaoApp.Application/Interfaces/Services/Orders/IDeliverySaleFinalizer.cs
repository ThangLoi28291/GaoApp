using GaoApp.Application.DTOs.Delivery;

namespace GaoApp.Application.Interfaces.Services.Orders;

public interface IDeliverySaleFinalizer
{
    Task<DeliveryHandoverCounterDto?> GetDeliveryCounterAsync(CancellationToken ct = default);
    Task<DeliverySaleFinalizeResult> FinalizeDeliverySaleAsync(DeliverySaleFinalizePlan plan,
        CancellationToken ct = default);
}
