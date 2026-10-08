using GaoApp.Application.DTOs.Delivery;

namespace GaoApp.Application.Interfaces.Services.Delivery;

public interface IDeliveryTripService
{
    Task<DeliveryTripOptionsDto> GetOptionsAsync(CancellationToken ct = default);
    Task<DeliveryTripPageDto> ListAsync(int take = 30, int? beforeId = null, string? state = null,
        int? courierUserId = null, CancellationToken ct = default);
    Task<DeliveryTripDetailDto> GetAsync(int id, CancellationToken ct = default);
    Task<DeliveryTripCommandAck> CreateDraftAsync(DeliveryTripCreateDraftRequest request, CancellationToken ct = default);
    Task<DeliveryTripCommandAck> EditDraftAsync(int id, DeliveryTripEditDraftRequest request, CancellationToken ct = default);
    Task<DeliveryTripCommandAck> AbandonDraftAsync(int id, DeliveryTripAbandonRequest request, CancellationToken ct = default);
    Task<DeliveryTripCommandAck> HandoverAsync(int id, DeliveryTripHandoverRequest request, CancellationToken ct = default);
    Task<DeliveryTripCommandAck> DepartAsync(int id, DeliveryTripDepartureRequest request, CancellationToken ct = default);
    Task<DeliveryTripCommandAck> ReassignCourierAsync(int id, DeliveryTripReassignRequest request, CancellationToken ct = default);
    Task<DeliveryTripCommandAck> RequestCancellationAsync(int id, int orderId, DeliveryTripCancellationRequest request,
        CancellationToken ct = default);
}
