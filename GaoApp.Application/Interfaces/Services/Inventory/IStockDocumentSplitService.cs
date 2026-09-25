using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IStockDocumentSplitService
{
    Task<PurchaseReceiptSplitWorkspaceDto> GetWorkspaceAsync(
        int sourceReceiptId,
        CancellationToken ct = default);

    Task<PurchaseReceiptSplitResultDto> SplitAsync(
        int sourceReceiptId,
        PurchaseReceiptSplitRequest request,
        CancellationToken ct = default);
}
