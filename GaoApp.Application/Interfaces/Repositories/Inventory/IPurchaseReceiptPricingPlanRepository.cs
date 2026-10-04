using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IPurchaseReceiptPricingPlanRepository
{
    Task<StockDocument?> GetReceiptSnapshotAsync(int storeId, int receiptId, bool forUpdate, CancellationToken ct);
    Task<PurchaseReceiptPricingPlan?> GetPlanAsync(int storeId, int receiptId, bool forUpdate, CancellationToken ct);
    Task<IReadOnlyList<ProductUnitConversion>> GetConversionsAsync(int storeId, IReadOnlyCollection<int> variantIds, CancellationToken ct);
    Task<IReadOnlyDictionary<int, PurchaseReceiptGiftHistory>> GetGiftHistoryAsync(int storeId, IReadOnlyCollection<int> variantIds, CancellationToken ct);
    void Add(PurchaseReceiptPricingPlan plan);
}

public sealed record PurchaseReceiptGiftHistory(int ReceiptLineId, decimal BaseUnitValueBeforeVat, DateTime ConfirmedAtUtc);
