using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class PurchaseReceiptPricingPlanRepository(AppDbContext db, IStockDocumentRepository receiptLocks) : IPurchaseReceiptPricingPlanRepository
{
    private string ReadLockedTable<T>()
    {
        var entity = db.Model.FindEntityType(typeof(T))!;
        var name = db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        return $"SELECT * FROM {name} WITH (HOLDLOCK)";
    }
    private void EnsureStore(int storeId, bool forUpdate = false)
    {
        if (storeId <= 0 || db.CurrentStoreId != storeId)
            throw new BusinessRuleException("Không thể truy cập kế hoạch giá ngoài cửa hàng hiện tại.");
        if (forUpdate && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Pricing-plan lock requires the existing receipt transaction.");
    }

    public async Task<StockDocument?> GetReceiptSnapshotAsync(int storeId, int receiptId, bool forUpdate, CancellationToken ct)
    {
        EnsureStore(storeId, forUpdate);
        IQueryable<StockDocument> receipts = forUpdate
            ? db.StockDocuments.FromSqlInterpolated($"SELECT * FROM [dbo].[StockDocument] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {receiptId} AND [StoreId] = {storeId} AND [IsDeleted] = 0")
            : db.StockDocuments;
        if (forUpdate)
        {
            // Protect the physical line set and catalog dependencies through commit. Reuse
            // the canonical variant lock before taking catalog read locks to preserve order.
            if (await receipts.AsNoTracking().SingleOrDefaultAsync(ct) is null) return null;
            var variants = await db.StockDocumentLines.FromSqlInterpolated($"SELECT * FROM [dbo].[StockDocumentLine] WITH (HOLDLOCK) WHERE [StockDocumentId] = {receiptId}")
                .AsNoTracking().Where(x => !x.IsDeleted).Select(x => x.ProductVariantId).Distinct().ToArrayAsync(ct);
            if (!await receiptLocks.LockPurchasePriceHistoryVariantsAsync(storeId, variants.OrderBy(x => x).ToArray(), ct))
                throw new GaoApp.Application.DTOs.Inventory.PurchaseReceiptPricingConflictException("Không thể khóa dữ liệu định giá. Vui lòng tải lại và thử lại.");
            var products = await db.ProductVariants.FromSqlRaw(ReadLockedTable<ProductVariant>())
                .AsNoTracking().Where(x => x.StoreId == storeId && variants.Contains(x.Id)).Select(x => x.ProductId).Distinct().ToArrayAsync(ct);
            await db.Products.FromSqlRaw(ReadLockedTable<Product>()).AsNoTracking()
                .Where(x => x.StoreId == storeId && products.Contains(x.Id)).ToArrayAsync(ct);
        }
        return await receipts.AsNoTracking().Where(x => x.StoreId == storeId && x.Id == receiptId && !x.IsDeleted)
            .Include(x => x.Lines.Where(line => !line.IsDeleted)).ThenInclude(x => x.ProductVariant).ThenInclude(x => x.Product)
            .Include(x => x.Lines.Where(line => !line.IsDeleted)).ThenInclude(x => x.ProductVariant).ThenInclude(x => x.PrimaryProductImage).ThenInclude(x => x!.MediaAsset)
            .AsSplitQuery().SingleOrDefaultAsync(ct);
    }

    public async Task<PurchaseReceiptPricingPlan?> GetPlanAsync(int storeId, int receiptId, bool forUpdate, CancellationToken ct)
    {
        EnsureStore(storeId, forUpdate);
        IQueryable<PurchaseReceiptPricingPlan> plans = forUpdate
            ? db.PurchaseReceiptPricingPlans.FromSqlInterpolated($"SELECT * FROM [dbo].[PurchaseReceiptPricingPlan] WITH (UPDLOCK, HOLDLOCK) WHERE [StockDocumentId] = {receiptId} AND [StoreId] = {storeId} AND [IsDeleted] = 0")
            : db.PurchaseReceiptPricingPlans.AsNoTracking();
        return await plans.Where(x => x.StoreId == storeId && x.StockDocumentId == receiptId && !x.IsDeleted)
            .Include(x => x.Lines.Where(line => !line.IsDeleted))
            .Include(x => x.BillLines.Where(line => !line.IsDeleted))
            .Include(x => x.Rules.Where(rule => !rule.IsDeleted)).ThenInclude(x => x.Sources.Where(source => !source.IsDeleted))
            .Include(x => x.GiftValuations.Where(value => !value.IsDeleted)).AsSplitQuery().SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ProductUnitConversion>> GetConversionsAsync(int storeId, IReadOnlyCollection<int> variantIds, CancellationToken ct)
    {
        EnsureStore(storeId);
        IQueryable<ProductUnitConversion> conversions = db.Database.CurrentTransaction is null ? db.ProductUnitConversions
            : db.ProductUnitConversions.FromSqlRaw(ReadLockedTable<ProductUnitConversion>());
        if (db.Database.CurrentTransaction is not null)
        {
            var units = await conversions.AsNoTracking().Where(x => x.StoreId == storeId && variantIds.Contains(x.ProductVariantId)).Select(x => x.UnitId).Distinct().ToArrayAsync(ct);
            await db.Units.FromSqlRaw(ReadLockedTable<Unit>()).AsNoTracking()
                .Where(x => x.StoreId == storeId && units.Contains(x.Id)).ToArrayAsync(ct);
        }
        return await conversions.AsNoTracking().Where(x => x.StoreId == storeId && variantIds.Contains(x.ProductVariantId) &&
                !x.IsDeleted && x.IsActive && x.Factor > 0 && !x.Unit.IsDeleted && x.Unit.IsActive && x.Unit.StoreId == storeId &&
                !x.ProductVariant.IsDeleted && x.ProductVariant.StoreId == storeId && !x.ProductVariant.Product.IsDeleted && x.ProductVariant.Product.StoreId == storeId)
            .Include(x => x.Unit).OrderBy(x => x.ProductVariantId).ThenBy(x => x.UnitId).ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<int, PurchaseReceiptGiftHistory>> GetGiftHistoryAsync(int storeId, IReadOnlyCollection<int> variantIds, CancellationToken ct)
    {
        EnsureStore(storeId);
        var rows = await db.StockDocumentLines.AsNoTracking().Where(x => variantIds.Contains(x.ProductVariantId) && !x.IsDeleted &&
                !x.StockDocument.IsDeleted && x.StockDocument.StoreId == storeId && x.StockDocument.Type == StockDocumentType.Receipt &&
                x.StockDocument.Status == StockDocumentStatus.Confirmed && x.BaseQuantity > 0 && x.Quantity > 0 && x.Factor > 0 &&
                x.LineTotal - x.VatAmount > 0 && !x.ProductVariant.IsDeleted && x.ProductVariant.StoreId == storeId)
            .Select(x => new { x.ProductVariantId, x.Id, x.StockDocumentId,
                BasePrice = (x.LineTotal - x.VatAmount) / x.BaseQuantity,
                ConfirmedAt = x.StockDocument.ConfirmedAtUtc ?? x.StockDocument.ApprovedAtUtc ?? x.StockDocument.DocumentDate })
            .ToListAsync(ct);
        return rows.GroupBy(x => x.ProductVariantId).ToDictionary(x => x.Key, x =>
        {
            var latest = x.OrderByDescending(v => v.ConfirmedAt).ThenByDescending(v => v.StockDocumentId).ThenByDescending(v => v.Id).First();
            return new PurchaseReceiptGiftHistory(latest.Id, latest.BasePrice, latest.ConfirmedAt);
        });
    }

    public void Add(PurchaseReceiptPricingPlan plan)
    {
        EnsureStore(plan.StoreId);
        db.PurchaseReceiptPricingPlans.Add(plan);
    }
}
