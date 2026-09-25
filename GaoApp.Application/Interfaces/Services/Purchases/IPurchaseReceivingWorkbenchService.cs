using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Application.Interfaces.Services.Purchases;

public interface IPurchaseReceivingWorkbenchService
{
    Task<PurchaseReceivingWorkbenchDto> StartOrResumeAsync(int purchaseOrderId, CancellationToken ct = default);
    Task<PurchaseReceivingWorkbenchDto> GetAsync(int stockDocumentId, CancellationToken ct = default);
    Task<PurchaseReceivingWorkbenchDto> AcquireAsync(int stockDocumentId, PurchaseReceivingLeaseRequest request, CancellationToken ct = default);
    Task<PurchaseReceivingWorkbenchDto> HeartbeatAsync(int stockDocumentId, PurchaseReceivingLeaseRequest request, CancellationToken ct = default);
    Task<PurchaseReceivingWorkbenchDto> ScanAsync(int stockDocumentId, PurchaseReceivingScanRequest request, CancellationToken ct = default);
    Task<PurchaseReceivingWorkbenchDto> AddAsync(int stockDocumentId, PurchaseReceivingAddRequest request, bool bulk, CancellationToken ct = default);
    Task<PurchaseReceivingWorkbenchDto> EditAsync(int stockDocumentId, int lineId, PurchaseReceivingEditRequest request, CancellationToken ct = default);
    Task<PurchaseReceivingWorkbenchDto> RemoveAsync(int stockDocumentId, int lineId, PurchaseReceivingCommandRequest request, CancellationToken ct = default);
    Task<PurchaseReceivingWorkbenchDto> UndoAsync(int stockDocumentId, PurchaseReceivingCommandRequest request, CancellationToken ct = default);
    Task<PurchaseReceivingWorkbenchDto> DecideOutsideAsync(int stockDocumentId, int lineId, PurchaseReceivingOutsideDecisionRequest request, CancellationToken ct = default);
    Task<PurchaseReceivingWorkbenchDto> FinishAsync(int stockDocumentId, PurchaseReceivingLeaseRequest request, CancellationToken ct = default);
}
