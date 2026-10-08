using GaoApp.Application.DTOs.Delivery;

namespace GaoApp.Application.Interfaces.Services.Delivery;

public interface IDeliveryTripService
{
    Task<DeliveryHandoverWorkspaceDto> GetHandoverAsync(int id, CancellationToken ct = default);
    Task<DeliveryHandoverPreviewDto> PreviewHandoverAsync(int id, int orderId,
        DeliveryHandoverPreviewRequest request, CancellationToken ct = default);
    Task<DeliveryHandoverAck> ConfirmHandoverAsync(int id, int orderId,
        DeliveryHandoverRequest request, CancellationToken ct = default);
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
    Task<DeliveryTripCommandAck> RecordOutcomeAsync(int id, int orderId, DeliveryOutcomeRequest request,
        CancellationToken ct = default);
    Task<DeliveryTripCommandAck> RecordReturnToStoreAsync(int id, DeliveryTripReturnRequest request,
        CancellationToken ct = default);
}
