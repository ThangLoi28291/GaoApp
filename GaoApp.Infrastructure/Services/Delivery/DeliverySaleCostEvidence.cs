using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Delivery;

public sealed class DeliverySaleCostEvidence(AppDbContext db) : IDeliverySaleCostEvidence
{
    private int Store => db.CurrentStoreId ?? throw new BusinessRuleException("Cần chọn cửa hàng.");
    private static string Text(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static void Proof([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition) throw new DeliveryFoundationException(409, "DELIVERED_SOURCE_INVALID", "Chứng cứ xuất/nhận trả không khớp nguồn thực giao.");
    }

    public async Task<IReadOnlyList<DeliverySaleLinePlan>> ReadDeliveredSlicesAsync(int deliveryOrderId,
        IReadOnlyList<DeliverySaleLinePlan> pricedLines, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Delivery cost evidence requires the locked transaction.");
        var order = await db.DeliveryOrders.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.Id == deliveryOrderId, ct);
        var fragments = await db.DeliveryDispatchCostFragments.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == deliveryOrderId).OrderBy(x => x.Id).ToListAsync(ct);
        var returned = await db.DeliveryReturnedCostFragments.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == deliveryOrderId).ToListAsync(ct);
        var result = new List<DeliverySaleLinePlan>();
        foreach (var line in pricedLines)
        {
            var remaining = line.DeliveredBaseQuantity; var slices = new List<DeliverySaleCostSlice>(); var amount = 0m;
            foreach (var fragment in fragments.Where(x => x.DeliveryOrderLineId == line.DeliveryLineId))
            {
                Proof(fragment.VariantId == line.VariantId && fragment.SourceWarehouseId == order.SourceWarehouseId && fragment.SourceLegalEntityId == order.SourceLegalEntityId && fragment.BaseQuantity > 0);
                var journal = await db.DeliveryJournalEntries.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == fragment.DeliveryJournalEntryId, ct);
                var transaction = await db.InventoryTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == fragment.InventoryTransactionId, ct);
                Proof(journal is not null && journal.Kind == DeliveryJournalKind.Dispatch && journal.DeliveryOrderId == order.Id && journal.DeliveryOrderLineId == line.DeliveryLineId &&
                    journal.VariantId == line.VariantId && journal.SourceWarehouseId == order.SourceWarehouseId && journal.SourceLegalEntityId == order.SourceLegalEntityId &&
                    journal.InventoryTransactionId == fragment.InventoryTransactionId && transaction is not null && transaction.WarehouseId == order.SourceWarehouseId &&
                    transaction.ProductVariantId == line.VariantId && transaction.ReferenceType == InventoryReferenceType.Delivery && transaction.ReferenceId == deliveryOrderId.ToString(CultureInfo.InvariantCulture) &&
                    transaction.ReferenceLineId == line.DeliveryLineId && transaction.QuantityChange == -journal.BaseQuantity && transaction.TotalCost == -journal.CostAmount);
                Proof(fragments.Where(x => x.DeliveryJournalEntryId == journal!.Id).Sum(x => x.BaseQuantity) == journal!.BaseQuantity &&
                    fragments.Where(x => x.DeliveryJournalEntryId == journal.Id).Sum(x => x.CostAmount) == journal.CostAmount);
                var physicalReturns = returned.Where(x => x.DispatchCostFragmentId == fragment.Id).OrderBy(x => x.StartQuantity).ToArray();
                var accepted = physicalReturns.Sum(x => x.BaseQuantity);
                var cursor = fragment.BaseQuantity - accepted;
                Proof(cursor >= 0);
                foreach (var receipt in physicalReturns)
                {
                    Proof(receipt.DeliveryOrderLineId == line.DeliveryLineId && receipt.VariantId == line.VariantId &&
                        receipt.SourceWarehouseId == order.SourceWarehouseId && receipt.SourceLegalEntityId == order.SourceLegalEntityId &&
                        receipt.StartQuantity == cursor && receipt.EndQuantity - receipt.StartQuantity == receipt.BaseQuantity);
                    var receivedJournal = await db.DeliveryJournalEntries.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == receipt.DeliveryJournalEntryId, ct);
                    var receivedValuation = await db.InventoryValuationEntries.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == receipt.InventoryValuationEntryId, ct);
                    Proof(receivedJournal is not null && receivedJournal.Kind == DeliveryJournalKind.Return && receivedJournal.DeliveryOrderId == order.Id &&
                        receivedJournal.DeliveryOrderLineId == line.DeliveryLineId && receivedJournal.InventoryTransactionId == receipt.InventoryTransactionId &&
                        receivedJournal.BaseQuantity == receipt.BaseQuantity && receivedJournal.CostAmount == receipt.CostAmount && receivedValuation is not null &&
                        receivedValuation.SourceValuationEntryId == fragment.InventoryValuationEntryId && receivedValuation.EntryType == InventoryValuationEntryType.Inbound &&
                        receivedValuation.InventoryTransactionId == receipt.InventoryTransactionId && receivedValuation.WarehouseId == order.SourceWarehouseId &&
                        receivedValuation.ProductVariantId == line.VariantId && receivedValuation.Quantity == receipt.BaseQuantity &&
                        receivedValuation.Amount == receipt.CostAmount && receivedValuation.UnitCost == receipt.UnitCost);
                    cursor = receipt.EndQuantity;
                }
                Proof(cursor == fragment.BaseQuantity);
                var quantity = Math.Min(remaining, fragment.BaseQuantity - accepted);
                if (quantity == 0) continue;
                var valuation = await db.InventoryValuationEntries.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == fragment.InventoryValuationEntryId, ct);
                Proof(valuation is not null && valuation.InventoryTransactionId == fragment.InventoryTransactionId && valuation.WarehouseId == order.SourceWarehouseId &&
                    valuation.ProductVariantId == line.VariantId && valuation.ReferenceType == InventoryReferenceType.Delivery && valuation.ReferenceId == deliveryOrderId.ToString(CultureInfo.InvariantCulture) &&
                    valuation.ReferenceLineId == line.DeliveryLineId && valuation.EntryType == InventoryValuationEntryType.Outbound && valuation.Quantity == -fragment.BaseQuantity && valuation.Amount == -fragment.CostAmount && valuation.UnitCost == fragment.UnitCost);
                var allocation = await db.Set<InventoryCostLayerAllocation>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == fragment.InventoryCostLayerAllocationId, ct);
                Proof(allocation is not null && allocation.InventoryValuationEntryId == valuation!.Id && allocation.ReverseOfAllocationId is null &&
                    allocation.Quantity == fragment.BaseQuantity && allocation.Amount == fragment.CostAmount && allocation.UnitCost == fragment.UnitCost);
                var debitId = Convert.ToInt32(fragment.InventoryCostBudgetDebitId);
                var layerId = Convert.ToInt32(fragment.InventoryCostLayerId);
                decimal piece; string quality;
                if (debitId > 0)
                {
                    var debit = await db.Set<InventoryCostBudgetDebit>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == debitId, ct);
                    Proof(debit is not null && !debit.IsProvisionalResolution && debit.WarehouseId == order.SourceWarehouseId && debit.SourceLegalEntityId == order.SourceLegalEntityId &&
                        debit.ProductVariantId == line.VariantId && debit.InventoryTransactionId == fragment.InventoryTransactionId && debit.InventoryValuationEntryId == valuation!.Id &&
                        debit.InventoryCostLayerId == layerId && debit.InventoryCostLayerAllocationId == allocation!.Id &&
                        debit.EndQuantity - debit.StartQuantity == fragment.BaseQuantity && debit.Amount == fragment.CostAmount && debit.UnitCost == fragment.UnitCost);
                    var layer = await db.Set<InventoryCostLayer>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == layerId, ct);
                    Proof(layer is not null && layer.WarehouseId == order.SourceWarehouseId && layer.ProductVariantId == line.VariantId && layer.CostBudgetPolicy == 1 &&
                        layer.BudgetAnchorQuantity == debit!.AnchorQuantity && layer.BudgetAnchorAmount == debit.AnchorAmount &&
                        allocation!.InventoryCostLayerId == layerId && !allocation.IsProvisional && valuation!.InventoryCostLayerId == layerId &&
                        debit.Amount == InventoryCostBudgetPolicy.Slice(debit.AnchorQuantity, debit.AnchorAmount, debit.StartQuantity, fragment.BaseQuantity));
                    var inbound = await db.InventoryValuationEntries.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == layer!.InventoryValuationEntryId, ct);
                    Proof(inbound is not null && inbound.InventoryTransactionId == layer!.InventoryTransactionId && inbound.WarehouseId == order.SourceWarehouseId &&
                        inbound.ProductVariantId == line.VariantId && inbound.EntryType == InventoryValuationEntryType.Inbound && inbound.Quantity == debit!.AnchorQuantity &&
                        inbound.Amount == debit.AnchorAmount && inbound.UnitCost == layer.UnitCost && layer.UnitCost == fragment.UnitCost);
                    foreach (var physical in physicalReturns)
                        Proof(physical.UnitCost == fragment.UnitCost && physical.CostAmount == InventoryCostBudgetPolicy.Slice(debit!.AnchorQuantity, debit.AnchorAmount,
                            debit.StartQuantity + physical.StartQuantity, physical.BaseQuantity));
                    piece = InventoryCostBudgetPolicy.Slice(debit!.AnchorQuantity, debit.AnchorAmount, debit.StartQuantity, quantity);
                    quality = valuation!.IsProvisional || valuation.CostFinalizedAtUtc is null || inbound!.IsProvisional || inbound.CostFinalizedAtUtc is null ? "Provisional" : "Confirmed";
                }
                else
                {
                    // Valid unresolved negative source: retain its recorded amount, never invent a final FIFO layer.
                    Proof(valuation!.IsProvisional && layerId == 0 && allocation!.IsProvisional && allocation.InventoryCostLayerId is null);
                    piece = InventoryCostBudgetPolicy.Slice(fragment.BaseQuantity, fragment.CostAmount, 0, quantity);
                    quality = valuation.CostSourceType == InventoryCostSourceType.Unknown ? "Unknown" : "Provisional";
                }
                if (await db.InventoryValuationEntries.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.RevaluationOfEntryId == valuation!.Id, ct))
                    quality = quality == "Unknown" ? "Unknown" : "Provisional"; // Captured estimate; correction propagation is a future adapter.
                slices.Add(new(line.DeliveryLineId, fragment.Id, fragment.InventoryTransactionId, valuation!.Id, debitId, layerId,
                    "0", Text(quantity), Text(piece), quality));
                amount += piece; remaining -= quantity;
            }
            Proof(remaining == 0 && (line.DeliveredBaseQuantity == 0 || slices.Count > 0));
            result.Add(line with { RecordedCost = amount, CostQuality = slices.Any(x => x.Quality == "Unknown") ? "Unknown" :
                slices.Any(x => x.Quality != "Confirmed") ? "Provisional" : "Confirmed", Slices = slices });
        }
        return result;
    }

    public async Task EnsureGenericSaleReturnSupportedAsync(int saleOrderId, CancellationToken ct = default)
    {
        if (await db.DeliveryJournalEntries.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Kind == DeliveryJournalKind.Settlement && x.SaleOrderId == saleOrderId, ct))
            throw new BusinessRuleException("DELIVERY_SALE_RETURN_UNSUPPORTED: Đơn thực giao cần luồng hoàn theo nguồn giao; chưa hỗ trợ trả/hoàn/hủy POS thông thường.");
    }
}
