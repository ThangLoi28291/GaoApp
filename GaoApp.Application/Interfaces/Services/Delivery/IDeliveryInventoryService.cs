using GaoApp.Application.DTOs.Delivery;

namespace GaoApp.Application.Interfaces.Services.Delivery;

public interface IDeliveryInventoryService
{
    Task<DeliveryInventorySummaryDto> GetAsync(int id, CancellationToken ct = default);
    // Requires the caller's aggregate lock and ambient transaction; derives current persisted picking facts.
    Task ReconcilePickedReservationsInTransactionAsync(int id, CancellationToken ct = default);
    Task<DeliveryInventoryCommandAck> CancelBeforeHandoverAsync(int id, DeliveryInventoryCancelRequest request, CancellationToken ct = default);
    Task<DeliveryInventoryPostingAck> DispatchInTransactionAsync(int id, DeliveryInventoryDispatchRequest request, CancellationToken ct = default);
    Task<DeliveryInventoryPostingAck> ReceiveReturnsInTransactionAsync(int id, DeliveryInventoryReceiveRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DeliveryInventoryResidualCostDto>> GetResidualCostsInTransactionAsync(int id,
        IReadOnlyList<DeliveryInventoryCostSelection> selections, CancellationToken ct = default);
}
