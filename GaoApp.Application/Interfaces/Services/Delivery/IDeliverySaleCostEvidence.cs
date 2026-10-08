using GaoApp.Application.DTOs.Delivery;

namespace GaoApp.Application.Interfaces.Services.Delivery;

public interface IDeliverySaleCostEvidence
{
    Task<IReadOnlyList<DeliverySaleLinePlan>> ReadDeliveredSlicesAsync(int deliveryOrderId,
        IReadOnlyList<DeliverySaleLinePlan> pricedLines, CancellationToken ct = default);
    Task EnsureGenericSaleReturnSupportedAsync(int saleOrderId, CancellationToken ct = default);
}
