using GaoApp.Application.DTOs.Purchases;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Purchases;

public interface IReceiptIntakeService
{
    Task<IReadOnlyList<ReceiptIntakeRecentDto>> GetRecentAsync(int documentId, CancellationToken ct);
    Task<byte[]?> GetPhotoAsync(int documentId, int itemId, CancellationToken ct);
    Task<ProvisionalReceivingStateDto> UpdateQuantityAsync(int documentId, int itemId,
        UpdateReceiptIntakeQuantityRequest request, CancellationToken ct);
    Task<ProvisionalReceivingStateDto> RecordKnownAsync(int documentId, RecordKnownReceiptItemRequest request, CancellationToken ct);
    Task<ProvisionalReceivingStateDto> CaptureIntakeAsync(int documentId, CaptureReceiptIntakeRequest request,
        ReceiptIntakePermissions permissions, CancellationToken ct);
    Task<ProvisionalReceivingStateDto> ReviewIntakeAsync(int documentId, int itemId,
        ReviewReceiptIntakeRequest request, ReceiptIntakePermissions permissions, CancellationToken ct);
}

/// <summary>Uses the receipt transaction; never commits or changes stock itself.</summary>
public interface IReceiptIntakeCatalog
{
    Task LockAsync(int storeId, CancellationToken ct);
    Task<ProductUnitConversion> PrepareKnownAsync(StockDocument document, RecordKnownReceiptItemRequest request, CancellationToken ct);
    Task ValidateAsync(int storeId, CaptureReceiptIntakeRequest request, CancellationToken ct);
    Task<ProductUnitConversion> ResolveAsync(StockDocument document, StockDocumentProvisionalItem item,
        int? categoryId, ReceiptIntakePermissions permissions, CancellationToken ct);
}
