using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IPurchaseReceiptPricingAllocationService
{
    Task<PurchaseReceiptPricingAllocationWorkspace> GetAsync(int receiptId, CancellationToken ct = default);
    Task<PurchaseReceiptPricingAllocationPreview> PreviewAsync(int receiptId, PurchaseReceiptPricingAllocationRequest request, CancellationToken ct = default);
    Task<PurchaseReceiptPricingAllocationWorkspace> SaveAsync(int receiptId, PurchaseReceiptPricingAllocationRequest request, CancellationToken ct = default);
    Task<PurchaseReceiptPricingAllocationWorkspace> ApplyAsync(int receiptId, PurchaseReceiptPricingApplyRequest request, CancellationToken ct = default);
    Task<bool> HasAppliedPlanAsync(int receiptId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<int, decimal>?> GetSavedGoodsAmountsForConfirmAsync(StockDocument receipt, bool withinConfirmTransaction, CancellationToken ct = default);
    Task MarkConfirmedWithinTransactionAsync(StockDocument receipt, CancellationToken ct = default);
}
