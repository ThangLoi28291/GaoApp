using GaoApp.Application.DTOs.Orders.LegalEntityAllocation;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Orders;

public interface IOrderLegalEntityFinalizeService
{
    /// <summary>
    /// Captures the LegalEntity mode once when an order is created. A later kill
    /// switch only affects new orders and cannot change an in-flight cart cohort.
    /// </summary>
    Task CaptureModeAsync(
        Order order,
        CancellationToken ct = default);

    /// <summary>
    /// Nếu feature tắt, trả FeatureDisabled và không tạo side effect.
    /// Nếu feature bật, caller bắt buộc đã mở transaction finalize.
    /// </summary>
    Task<OrderLegalEntityFinalizeResult> ApplyIfEnabledAsync(
        Order order,
        CancellationToken ct = default);

    Task<bool> HasPersistedAllocationAsync(
        int orderId,
        CancellationToken ct = default);
}
