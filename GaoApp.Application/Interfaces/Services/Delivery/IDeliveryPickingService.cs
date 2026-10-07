using GaoApp.Application.DTOs.Delivery;

namespace GaoApp.Application.Interfaces.Services.Delivery;

public interface IDeliveryPickingService
{
    Task<DeliveryPickingDetailDto> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<DeliveryPickingReplacementOptionDto>> ReplacementOptionsAsync(int id, string? query, int take = 25, CancellationToken ct = default);
    Task<DeliveryPickingCommandAck> ClaimAsync(int id, DeliveryPickingEnvelope request, CancellationToken ct = default);
    Task<DeliveryPickingCommandAck> ReportAsync(int id, DeliveryPickingReportRequest request, CancellationToken ct = default);
    Task<DeliveryPickingCommandAck> SubmitAsync(int id, DeliveryPickingEnvelope request, CancellationToken ct = default);
    Task<DeliveryPickingCommandAck> PlanAsync(int id, DeliveryPickingPlanRequest request, CancellationToken ct = default);
    Task<DeliveryPickingCommandAck> ApproveAsync(int id, DeliveryPickingApproveRequest request, CancellationToken ct = default);
    Task<DeliveryPickingCommandAck> ReopenAsync(int id, DeliveryPickingReopenRequest request, CancellationToken ct = default);
    Task<DeliveryPickingCommandAck> ReassignAsync(int id, DeliveryPickingReassignRequest request, CancellationToken ct = default);
}
