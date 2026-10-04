using System.Text.Json;
using GaoApp.Application.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Products;

public sealed partial class ReceiptSellingPriceService
{
    private sealed record ReviewSnapshot(string CatalogVersion, decimal BaseCost, bool HasChanges,
        string Basis, Dictionary<int, decimal> RetailTargets, Dictionary<int, decimal> WholesaleTargets);
    private static string ReviewKey(int documentId, int lineId) => $"{documentId}:{lineId}";
    private static ReviewSnapshot? ReadReview(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var review = JsonSerializer.Deserialize<ReviewSnapshot>(json);
            return review?.RetailTargets != null && review.WholesaleTargets != null &&
                !string.IsNullOrEmpty(review.CatalogVersion) ? review : null;
        }
        catch (JsonException) { return null; }
    }

    public async Task<ReceiptPriceReviewsDto> GetReviewsAsync(int documentId, CancellationToken ct)
    {
        var document = await DocumentAsync(documentId, ct);
        var lines = await db.StockDocumentLines.AsNoTracking()
            .Where(x => x.StockDocumentId == documentId && !x.IsDeleted).ToListAsync(ct);
        var variantIds = lines.Select(x => x.ProductVariantId).Distinct().ToArray();
        var variants = await db.ProductVariants.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Where(x => variantIds.Contains(x.Id) && x.StoreId == StoreId && !x.IsDeleted &&
                x.Product.StoreId == StoreId && !x.Product.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
        var units = await db.ProductUnitConversions.AsNoTracking().Include(x => x.Unit)
            .Where(x => variantIds.Contains(x.ProductVariantId) && x.StoreId == StoreId && !x.IsDeleted &&
                x.IsActive && x.Factor > 0 && x.Unit.StoreId == StoreId && !x.Unit.IsDeleted)
            .OrderBy(x => x.Factor).ThenBy(x => x.Id).ToListAsync(ct);
        var unitGroups = units.ToLookup(x => x.ProductVariantId);
        var keys = lines.Select(x => ReviewKey(documentId, x.Id)).ToArray();
        var reviews = await db.AuditLogs.AsNoTracking().Where(x => x.StoreId == StoreId &&
                x.EntityName == ReviewEntity && keys.Contains(x.EntityId!))
            .GroupBy(x => x.EntityId).Select(g => g.OrderByDescending(x => x.Id).First()).ToListAsync(ct);
        var reviewMap = reviews.ToDictionary(x => x.EntityId!);
        var result = new List<ReceiptPriceReviewLineDto>();
        foreach (var line in lines)
        {
            reviewMap.TryGetValue(ReviewKey(documentId, line.Id), out var audit);
            var review = ReadReview(audit?.NewValuesJson);
            if (!variants.TryGetValue(line.ProductVariantId, out var variant))
            {
                result.Add(new(line.Id, review?.HasChanges == true, false, true, false,
                    audit?.CreatedAtUtc, audit?.ActorUserName, review?.BaseCost, null));
                continue;
            }
            var conversions = unitGroups[variant.Id].ToList();
            var prices = Prices(variant, conversions);
            var factor = line.Factor;
            var price = line.UnitPriceBeforeVat > 0 ? line.UnitPriceBeforeVat : variant.CostPrice * factor;
            var cost = factor > 0 ? (price * (1 + (document.HasVat && document.IncludeVatInInventoryCost ? line.TaxRate / 100 : 0)) +
                (document.HasFreight && document.CapitalizeFreightInInventoryCost && line.Quantity > 0 ? line.FreightAllocation / line.Quantity : 0)) / factor : 0;
            var currentCatalog = review != null && review.CatalogVersion == CatalogVersion(variant, conversions);
            var reviewed = currentCatalog && Math.Abs(cost - review!.BaseCost) < 0.0002m;
            var belowCost = cost > 0 && prices.Any(x => x.Price < cost * x.Factor || (x.WholesalePrice ?? x.Price) < cost * x.Factor);
            var belowTarget = cost > 0 && prices.Any(x =>
                Margin(x.Price, cost * x.Factor, review?.Basis) < (review?.RetailTargets.GetValueOrDefault(x.Id, 10) ?? 10) ||
                Margin(x.WholesalePrice ?? x.Price, cost * x.Factor, review?.Basis) < (review?.WholesaleTargets.GetValueOrDefault(x.Id, 10) ?? 10));
            result.Add(new(line.Id, review?.HasChanges == true, reviewed,
                !reviewed && (review != null || belowTarget || cost <= 0), belowCost,
                audit?.CreatedAtUtc, audit?.ActorUserName, review?.BaseCost, cost) { CatalogCurrent = currentCatalog });
        }
        return new(document.ReceiptSource, result);
    }

    private static decimal Margin(decimal price, decimal cost, string? basis) =>
        price <= 0 || cost <= 0 ? -100 : (price - cost) / (basis == "markup" ? cost : price) * 100;
}
