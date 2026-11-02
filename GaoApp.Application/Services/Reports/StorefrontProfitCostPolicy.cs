using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;

namespace GaoApp.Application.Services.Reports;

// Native source adapter. Actual Delivery11 roots are never relabelled as POS issues.
public sealed class StorefrontProfitCostPolicy(ProfitSourceSnapshot source)
{
    public bool Contains(int orderId) => source.StorefrontSales.Any(x => x.SaleOrderId == orderId);
    public ProfitReportAggregationPolicy.CostResult Evaluate(Order order,
        Dictionary<int, ProfitReportAggregationPolicy.CostResult>? lineResults, List<ProfitCostIssueDto>? issues, CancellationToken ct)
    {
        var results = new List<ProfitReportAggregationPolicy.CostResult>();
        try
        {
            void Require(bool valid) { if (!valid) throw new InvalidOperationException("Nguồn bán website/đơn giao/phân bổ giá vốn không khớp."); }
            var binding = source.StorefrontSales.Single(x => x.SaleOrderId == order.Id);
            var parent = source.StorefrontOrders.Single(x => x.Id == binding.StorefrontOrderId);
            var links = source.StorefrontSettlements.Where(x => x.StorefrontOrderId == parent.Id).ToList();
            var origins = source.StorefrontSources.Where(x => x.StorefrontOrderId == parent.Id).ToList();
            Require(!binding.IsDeleted && binding.StoreId == source.StoreId && binding.SettlingShiftId == order.POSShiftId && !parent.IsDeleted &&
                parent.StoreId == source.StoreId && parent.UseMultiLegalEntitySnapshot == order.UseMultiLegalEntity && parent.GoodsAmount == order.GrandTotal &&
                parent.LegalEntityModeCapturedAtUtc.HasValue && parent.LegalEntityModeCapturedAtUtc == order.LegalEntityModeCapturedAtUtc &&
                order.Status == OrderStatus.Completed && links.Count > 0 && links.Count == origins.Count && links.Sum(x => x.GoodsAmount) == order.GrandTotal &&
                links.All(x => x.StoreId == source.StoreId && !x.IsDeleted && x.SaleOrderId == order.Id && x.SettlingShiftId == order.POSShiftId));
            var lines = source.Lines.Where(x => x.OrderId == order.Id && !x.IsDeleted).ToList();
            Require(lines.Count > 0 && lines.Select(x => x.StorefrontOrderLineId).Distinct().Count() == lines.Count);
            foreach (var line in lines)
            {
                ct.ThrowIfCancellationRequested();
                var web = source.StorefrontLines.Single(x => x.Id == line.StorefrontOrderLineId && x.StorefrontOrderId == parent.Id);
                var maps = source.LegalAllocations.Where(x => x.OrderId == order.Id && x.OrderLineId == line.Id && !x.IsDeleted).ToList();
                Require(web.StoreId == source.StoreId && !web.IsDeleted && line.StoreId == source.StoreId && line.StorefrontOrderId == parent.Id &&
                    line.Quantity == web.Quantity && line.Multiplier == web.BaseMultiplier && line.BaseQuantity == web.BaseQuantity && line.VariantId == web.VariantId &&
                    maps.Count > 0 && maps.Sum(x => x.BaseQuantity) == web.BaseQuantity && maps.Sum(x => x.NetAmount) == web.NetAmount);
                decimal cost = 0, exposure = 0; var quality = ProfitQuality.Finalized;
                foreach (var map in maps)
                {
                    var origin = origins.Single(x => x.SourceWarehouseId == map.WarehouseId && x.SourceLegalEntityId == map.LegalEntityId);
                    Require(links.Any(x => x.FulfillmentSourceId == origin.Id));
                    var physical = source.StorefrontLineSources.Single(x => x.StorefrontOrderId == parent.Id && x.OrderLineId == web.Id && x.FulfillmentSourceId == origin.Id);
                    var child = source.Deliveries.Single(x => x.Id == origin.DeliveryOrderId);
                    var quote = source.StorefrontDeliveryLines.Single(x => x.DeliveryOrderId == child.Id && x.StorefrontOrderLineId == web.Id);
                    Require(physical.StoreId == source.StoreId && !physical.IsDeleted && physical.InventoryTransactionId == map.InventoryTransactionId && physical.BaseQuantity == map.BaseQuantity &&
                        child.SourceKind == DeliverySourceKind.Storefront && child.StorefrontOrderId == parent.Id && child.StorefrontFulfillmentSourceId == origin.Id &&
                        child.SourceWarehouseId == map.WarehouseId && child.SourceLegalEntityId == map.LegalEntityId && quote.StorefrontBaseQuantity == map.BaseQuantity &&
                        (map.InvoiceUnitKind == OrderAllocationInvoiceUnitKind.NativeBaseUnit ? map.Quantity == map.BaseQuantity && map.InvoiceUnitId == quote.BaseUnitId :
                        map.Quantity == map.BaseQuantity / web.BaseMultiplier && map.InvoiceUnitId == web.SellingUnitId));
                    var roots = source.Entries.Where(x => x.InventoryTransactionId == map.InventoryTransactionId && x.EntryType == InventoryValuationEntryType.Outbound).ToList();
                    Require(roots.Count > 0 && roots.Sum(x => -x.Quantity) == map.BaseQuantity && roots.All(x => x.ReferenceType == InventoryReferenceType.Delivery &&
                        x.ReferenceId == child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) && x.ReferenceLineId == quote.Id && x.WarehouseId == map.WarehouseId && x.ProductVariantId == web.VariantId));
                    foreach (var root in roots)
                    {
                        var journal = source.DeliveryJournals.Single(x => x.Kind == DeliveryJournalKind.Dispatch && x.InventoryTransactionId == root.InventoryTransactionId && x.DeliveryOrderLineId == quote.Id);
                        Require(journal.SourceWarehouseId == map.WarehouseId && journal.SourceLegalEntityId == map.LegalEntityId && journal.BaseQuantity == map.BaseQuantity &&
                            source.DeliveryFragments.Where(x => x.DeliveryJournalEntryId == journal.Id).Sum(x => x.BaseQuantity) == journal.BaseQuantity);
                        var events = source.Entries.Where(x => x.RevaluationOfEntryId == root.Id || x.SourceValuationEntryId == root.Id).ToList();
                        var result = SaleValuationCostPolicy.EvaluateDeliveryBudget(root, events.Where(x => x.EntryType == InventoryValuationEntryType.Revaluation).ToList(), events);
                        if (!result.Cost.HasValue || !result.ProvisionalExposure.HasValue) throw new InvalidOperationException(result.Reason ?? "Giá vốn nguồn giao chưa đủ chứng cứ.");
                        if (result.State is SaleValuationCostPolicy.Quality.Unavailable or SaleValuationCostPolicy.Quality.DataIntegrityConflict) throw new InvalidOperationException(result.Reason);
                        if (result.State != SaleValuationCostPolicy.Quality.Finalized) quality = ProfitQuality.Provisional;
                        cost += result.Cost.Value; exposure += result.ProvisionalExposure.Value;
                    }
                }
                var value = new ProfitReportAggregationPolicy.CostResult(cost, exposure, quality, quality == ProfitQuality.Provisional ? 1 : 0);
                results.Add(value); if (lineResults is not null) lineResults[line.Id] = value;
            }
            return new(results.Sum(x => x.Cost!.Value), results.Sum(x => x.Exposure!.Value), results.Any(x => x.Quality == ProfitQuality.Provisional) ? ProfitQuality.Provisional : ProfitQuality.Finalized,
                results.Sum(x => x.AffectedLines ?? 0));
        }
        catch (Exception ex) when (ex is InvalidOperationException or DivideByZeroException)
        {
            issues?.Add(new(order.Id, order.OrderNumber ?? order.Id.ToString(), null, null, ProfitQuality.Unavailable, "StorefrontDispatchSourceUnavailable", ex.Message));
            return new(null, null, ProfitQuality.Unavailable, null);
        }
    }
}
