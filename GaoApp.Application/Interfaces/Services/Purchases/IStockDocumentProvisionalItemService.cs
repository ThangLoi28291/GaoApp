using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Application.Interfaces.Services.Purchases;

public interface IStockDocumentProvisionalItemService
{
    Task<ProvisionalReceivingStateDto> GetAsync(int stockDocumentId, CancellationToken ct = default);
    Task<ProvisionalReceivingStateDto> CaptureAsync(int stockDocumentId, CaptureProvisionalItemRequest request, CancellationToken ct = default);
    Task<ProvisionalReceivingStateDto> IncrementAsync(int stockDocumentId, int itemId, IncrementProvisionalItemRequest request, CancellationToken ct = default);
    Task<ProvisionalReceivingStateDto> EditAsync(int stockDocumentId, int itemId, EditProvisionalItemRequest request, CancellationToken ct = default);
    Task<ProvisionalReceivingStateDto> RemoveAsync(int stockDocumentId, int itemId, RemoveProvisionalItemRequest request, bool managerRemoval, CancellationToken ct = default);
    Task<ProvisionalReceivingStateDto> UndoAsync(int stockDocumentId, ProvisionalReceivingMutationRequest request, CancellationToken ct = default);
    Task<ProvisionalReceivingStateDto> LinkExistingAsync(int stockDocumentId, int itemId, ResolveProvisionalItemRequest request, bool canCreateBarcode, CancellationToken ct = default);
    Task<ProvisionalReceivingStateDto> QuickCreateAndResolveAsync(int stockDocumentId, int itemId, QuickCreateAndResolveProvisionalItemRequest request, bool canCreateUnit, bool canCreateBarcode, CancellationToken ct = default);
}
