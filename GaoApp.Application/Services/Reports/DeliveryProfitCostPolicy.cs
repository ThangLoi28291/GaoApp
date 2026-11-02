using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Reports;

// Read-only adapter for settled delivery sales. The handover owns the price/cost slices;
// the report must neither export stock again nor fabricate an Order-reference valuation.
public sealed class DeliveryProfitCostPolicy(ProfitSourceSnapshot source)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ILookup<int, DeliveryJournalEntry> settlements = source.DeliveryJournals
        .Where(x => x.Kind == DeliveryJournalKind.Settlement && x.SaleOrderId.HasValue).ToLookup(x => x.SaleOrderId!.Value);
    private readonly ILookup<int, OrderLine> lines = source.Lines.Where(x => !x.IsDeleted).ToLookup(x => x.OrderId);
    private readonly ILookup<int, OrderLegalEntityAllocation> maps = source.LegalAllocations.ToLookup(x => x.OrderLineId);
    private readonly ILookup<int, DeliveryRevision> revisions = source.DeliveryRevisions.ToLookup(x => x.DeliveryOrderId);
    private readonly ILookup<int, DeliveryDispatchCostFragment> fragments = source.DeliveryFragments.ToLookup(x => x.Id);
    private readonly ILookup<int, DeliveryJournalEntry> journals = source.DeliveryJournals.ToLookup(x => x.Id);
    private readonly ILookup<int, InventoryValuationEntry> entries = source.Entries.ToLookup(x => x.Id);
    private readonly ILookup<int, InventoryValuationEntry> children = source.Entries
        .SelectMany(x => new[] { x.RevaluationOfEntryId, x.SourceValuationEntryId }.OfType<int>().Distinct()
            .Select(id => (Id: id, Entry: x))).ToLookup(x => x.Id, x => x.Entry);

    public bool Contains(int orderId) => settlements.Contains(orderId);

    private sealed class InvalidProof(string message, ProfitQuality quality = ProfitQuality.DataIntegrityConflict) : Exception(message)
    { public ProfitQuality Quality { get; } = quality; }
    private static void Require([DoesNotReturnIf(false)] bool valid, string message)
    {
        if (!valid) throw new InvalidProof(message);
    }
    private static decimal Number(string? value)
    {
        Require(decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var result), "Số lượng hoặc thành tiền trong chứng cứ giao hàng không hợp lệ.");
        return result;
    }

    public ProfitReportAggregationPolicy.CostResult Evaluate(Order order,
        Dictionary<int, ProfitReportAggregationPolicy.CostResult>? lineResults, List<ProfitCostIssueDto>? issues,
        CancellationToken ct)
    {
        var outcomes = new List<ProfitReportAggregationPolicy.CostResult>();
        void Issue(OrderLine? line, string code, string message, ProfitQuality quality = ProfitQuality.DataIntegrityConflict)
            => issues?.Add(new(order.Id, order.OrderNumber ?? order.Id.ToString(), line?.Id, line?.ItemName, quality, code, message));
        try
        {
            var settlementRows = settlements[order.Id].ToArray();
            Require(settlementRows.Length == 1, "Đơn giao có nhiều chứng cứ chốt thanh toán trùng nhau.");
            var settlement = settlementRows[0];
            var delivery = source.Deliveries.SingleOrDefault(x => x.Id == settlement.DeliveryOrderId);
            Require(delivery is not null && !delivery.IsDeleted && delivery.StoreId == source.StoreId &&
                delivery.State == DeliveryState.Settled && !settlement.IsDeleted && settlement.StoreId == source.StoreId,
                "Thiếu hồ sơ giao hàng đã chốt hợp lệ của cửa hàng.");
            var records = revisions[settlement.DeliveryOrderId].Where(x => x.Action == "counter-handover").ToArray();
            Require(records.Length == 1 && !records[0].IsDeleted && records[0].StoreId == source.StoreId,
                "Thiếu hoặc trùng chứng cứ bàn giao hàng thực nhận.");
            var record = records[0];
            Require(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.SnapshotJson))) == record.SnapshotHash,
                "Nội dung chứng cứ bàn giao không khớp mã kiểm tra toàn vẹn.");
            var saved = JsonSerializer.Deserialize<DeliveryHandoverSnapshot>(record.SnapshotJson, Json);
            Require(saved is not null && saved.Settlement is not null && saved.SaleLines is not null && saved.Lines is not null,
                "Chứng cứ bàn giao thiếu dữ liệu hàng thực giao.");
            var proof = saved!;
            Require(proof.Schema == "delivery-handover-v1" && proof.StoreId == source.StoreId &&
                proof.DeliveryOrderId == delivery!.Id && proof.SourceOrderId == delivery.SourceCartId &&
                proof.SourceWarehouseId == delivery.SourceWarehouseId && proof.SourceLegalEntityId == delivery.SourceLegalEntityId &&
                proof.OrderRevision == record.Revision && proof.OrderRevision <= delivery.Revision && proof.OrderVersion == record.AggregateVersion &&
                proof.ActorUserId == record.ActorUserId && proof.ClientRequestId != Guid.Empty && proof.ReportSource == "AtStore" &&
                proof.Settlement.DeliveryOrderId == delivery.Id && proof.Settlement.SaleOrderId == order.Id &&
                proof.Settlement.SourceWarehouseId == delivery.SourceWarehouseId && proof.Settlement.SourceLegalEntityId == delivery.SourceLegalEntityId &&
                proof.Settlement.State == "Settled" && settlement.SourceWarehouseId == delivery.SourceWarehouseId &&
                settlement.SourceLegalEntityId == delivery.SourceLegalEntityId && settlement.PostingKey == "handover:" + proof.ClientRequestId.ToString("N"),
                "Liên kết chứng cứ bàn giao, kho nguồn hoặc hộ kinh doanh không khớp đơn bán.");
            Require(order.StoreId == source.StoreId && !order.IsDeleted && order.Status == OrderStatus.Completed &&
                order.POSShiftId == proof.Settlement.ShiftId && order.CustomerId == delivery.CustomerId &&
                order.CompletedAtUtc == proof.Settlement.RecordedAtUtc.UtcDateTime && order.GrandTotal == settlement.MoneyAmount &&
                order.Subtotal - order.DiscountTotal == settlement.MoneyAmount && Number(proof.Settlement.PayableText) == settlement.MoneyAmount &&
                order.PaidTotal == Number(proof.Settlement.ReceivedCashText) + Number(proof.Settlement.ConfirmedTransferText) &&
                order.BalanceDue == Number(proof.Settlement.CustomerDebtText) && proof.SaleLines.Sum(x => x.Net) == settlement.MoneyAmount &&
                proof.SaleLines.Select(x => x.DeliveryLineId).SequenceEqual(proof.Lines.Select(x => x.LineId)),
                "Tiền hàng, thanh toán, thời điểm chốt hoặc số lượng dòng hàng không khớp chứng cứ giao hàng.");
            Require(!source.Returns.Any(x => x.OrderId == order.Id && !x.IsDeleted && x.Status == SalesReturnStatus.Completed) &&
                !source.Entries.Any(x => x.ReferenceType == InventoryReferenceType.Order && x.ReferenceId == order.Id.ToString(CultureInfo.InvariantCulture)),
                "Đơn giao có lịch sử hoàn/hủy POS hoặc xuất bán lần hai cần đối chiếu riêng.");
            var sold = proof.SaleLines.Where(x => x.DeliveredBaseQuantity > 0).ToArray();
            Require(sold.Length == lines[order.Id].Count() && sold.Select(x => x.DeliveryLineId).Distinct().Count() == sold.Length &&
                source.LegalAllocations.Where(x => x.OrderId == order.Id).Sum(x => x.NetAmount) == settlement.MoneyAmount,
                "Dòng bán hoặc phân bổ hộ kinh doanh không khớp hàng thực giao.");
            var used = new HashSet<int>();
            foreach (var line in lines[order.Id])
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var allocation = maps[line.Id].ToArray();
                    Require(allocation.Length == 1 && !allocation[0].IsDeleted && allocation[0].StoreId == source.StoreId &&
                        allocation[0].OrderId == order.Id && allocation[0].ProductVariantId == line.VariantId &&
                        allocation[0].WarehouseId == delivery.SourceWarehouseId && allocation[0].LegalEntityId == delivery.SourceLegalEntityId &&
                        allocation[0].BaseQuantity == line.BaseQuantity && allocation[0].InventoryTransactionId.HasValue,
                        "Phân bổ dòng bán không khớp nguồn xuất giao.");
                    var matches = sold.Where(x => x.VariantId == line.VariantId && x.DeliveredBaseQuantity == line.BaseQuantity &&
                        x.Slices is not null && x.Slices.Count > 0 && x.Slices.All(y => y.InventoryTransactionId == allocation[0].InventoryTransactionId)).ToArray();
                    Require(matches.Length == 1 && used.Add(matches[0].DeliveryLineId), "Dòng bán không khớp duy nhất một dòng hàng thực giao.");
                    var priced = matches[0];
                    Require(line.StoreId == source.StoreId && line.BaseQuantity > 0 && allocation[0].NetAmount == priced.Net &&
                        priced.Net == priced.Gross - priced.Discount && priced.RecordedCost >= 0 &&
                        line.LineCostTotal == Math.Round(priced.RecordedCost, 2, MidpointRounding.AwayFromZero) &&
                        priced.Slices.Select(x => x.DispatchFragmentId).Distinct().Count() == priced.Slices.Count,
                        "Giá vốn hoặc tiền dòng bán không khớp các lát giá vốn đã bàn giao.");
                    decimal cost = 0, exposure = 0, quantity = 0;
                    var provisional = false;
                    foreach (var slice in priced.Slices)
                    {
                        var f = fragments[slice.DispatchFragmentId].SingleOrDefault();
                        var entry = entries[slice.ValuationEntryId].SingleOrDefault();
                        Require(f is not null && entry is not null && !f.IsDeleted && !entry.IsDeleted &&
                            f.StoreId == source.StoreId && entry.StoreId == source.StoreId && f.DeliveryOrderId == delivery.Id &&
                            f.DeliveryOrderLineId == priced.DeliveryLineId && slice.DeliveryLineId == priced.DeliveryLineId &&
                            f.VariantId == line.VariantId && f.SourceWarehouseId == delivery.SourceWarehouseId && f.SourceLegalEntityId == delivery.SourceLegalEntityId &&
                            f.InventoryTransactionId == slice.InventoryTransactionId && f.InventoryValuationEntryId == slice.ValuationEntryId &&
                            entry.InventoryTransactionId == f.InventoryTransactionId && entry.WarehouseId == delivery.SourceWarehouseId && entry.ProductVariantId == line.VariantId &&
                            entry.ReferenceType == InventoryReferenceType.Delivery && entry.ReferenceId == delivery.Id.ToString(CultureInfo.InvariantCulture) &&
                            entry.ReferenceLineId == priced.DeliveryLineId && entry.EntryType == InventoryValuationEntryType.Outbound &&
                            entry.Quantity == -f.BaseQuantity && entry.Amount == -f.CostAmount && entry.UnitCost == f.UnitCost && f.BaseQuantity > 0,
                            "Lát giá vốn không khớp bút toán xuất giao, kho hoặc sản phẩm gốc.");
                        var root = entry!; var fragment = f!;
                        var tx = root.InventoryTransaction; var wh = root.Warehouse; var variant = root.ProductVariant;
                        var journal = journals[fragment.DeliveryJournalEntryId].SingleOrDefault();
                        Require(tx is not null && !tx.IsDeleted && tx.StoreId == source.StoreId && tx.WarehouseId == root.WarehouseId &&
                            tx.ProductVariantId == root.ProductVariantId && tx.ReferenceType == InventoryReferenceType.Delivery && tx.ReferenceId == root.ReferenceId &&
                            tx.ReferenceLineId == root.ReferenceLineId && tx.TransactionType == InventoryTransactionType.DeliveryDispatchOut &&
                            wh is not null && !wh.IsDeleted && wh.StoreId == source.StoreId && wh.LegalEntityId == delivery.SourceLegalEntityId &&
                            variant is not null && !variant.IsDeleted && variant.StoreId == source.StoreId &&
                            source.LegalEntities.Any(x => x.Id == delivery.SourceLegalEntityId && x.StoreId == source.StoreId && !x.IsDeleted) &&
                            journal is not null && !journal.IsDeleted && journal.StoreId == source.StoreId && journal.Kind == DeliveryJournalKind.Dispatch &&
                            journal.DeliveryOrderId == delivery.Id && journal.DeliveryOrderLineId == priced.DeliveryLineId && journal.VariantId == line.VariantId &&
                            journal.SourceWarehouseId == delivery.SourceWarehouseId && journal.SourceLegalEntityId == delivery.SourceLegalEntityId &&
                            journal.InventoryTransactionId == tx.Id && tx.QuantityChange == -journal.BaseQuantity && tx.TotalCost == -journal.CostAmount &&
                            source.DeliveryFragments.Where(x => x.DeliveryJournalEntryId == journal.Id).Sum(x => x.BaseQuantity) == journal.BaseQuantity &&
                            source.DeliveryFragments.Where(x => x.DeliveryJournalEntryId == journal.Id).Sum(x => x.CostAmount) == journal.CostAmount,
                            "Giao dịch kho hoặc phiếu xuất giao không khớp nguồn giá vốn.");
                        if (children[root.Id].Any(x => x.EntryType == InventoryValuationEntryType.Revaluation))
                            throw new InvalidProof("Nguồn xuất giao đã được điều chỉnh sau bàn giao; cần đối chiếu giá vốn giao hàng mới nhất.", ProfitQuality.Unavailable);
                        var start = Number(slice.StartBaseQuantityText); var end = Number(slice.EndBaseQuantityText); var amount = Number(slice.CostAmountText);
                        Require(start >= 0 && end > start && end <= fragment.BaseQuantity && amount >= 0 &&
                            slice.LayerId == (fragment.InventoryCostLayerId ?? 0) && slice.BudgetDebitId == (fragment.InventoryCostBudgetDebitId ?? 0),
                            "Khoảng số lượng hoặc chứng từ nguồn của lát giá vốn không hợp lệ.");
                        var sourceAllocation = root.CostLayerAllocations.SingleOrDefault(x => x.Id == fragment.InventoryCostLayerAllocationId);
                        Require(sourceAllocation is not null && !sourceAllocation.IsDeleted && sourceAllocation.StoreId == source.StoreId &&
                            sourceAllocation.InventoryValuationEntryId == root.Id && sourceAllocation.ReverseOfAllocationId is null &&
                            sourceAllocation.Quantity == fragment.BaseQuantity && sourceAllocation.Amount == fragment.CostAmount && sourceAllocation.UnitCost == fragment.UnitCost,
                            "Phân bổ giá vốn gốc không khớp phiếu xuất giao.");
                        if (slice.BudgetDebitId > 0)
                        {
                            var debit = root.CostBudgetDebits.SingleOrDefault(x => x.Id == slice.BudgetDebitId); var layer = debit?.Layer;
                            Require(debit is not null && !debit.IsDeleted && !debit.IsProvisionalResolution && debit.StoreId == source.StoreId &&
                                debit.WarehouseId == root.WarehouseId && debit.SourceLegalEntityId == delivery.SourceLegalEntityId && debit.ProductVariantId == line.VariantId &&
                                debit.InventoryValuationEntryId == root.Id && debit.InventoryTransactionId == tx!.Id && debit.InventoryCostLayerAllocationId == sourceAllocation!.Id &&
                                debit.InventoryCostLayerId == slice.LayerId && debit.EndQuantity - debit.StartQuantity == fragment.BaseQuantity &&
                                debit.Amount == fragment.CostAmount && debit.UnitCost == fragment.UnitCost && layer is not null && !layer.IsDeleted &&
                                layer.StoreId == source.StoreId && layer.WarehouseId == root.WarehouseId && layer.ProductVariantId == line.VariantId && layer.CostBudgetPolicy == 1 &&
                                layer.BudgetAnchorQuantity == debit.AnchorQuantity && layer.BudgetAnchorAmount == debit.AnchorAmount &&
                                sourceAllocation.InventoryCostLayerId == layer.Id && root.InventoryCostLayerId == layer.Id &&
                                debit.Amount == InventoryCostBudgetPolicy.Slice(debit.AnchorQuantity, debit.AnchorAmount, debit.StartQuantity, fragment.BaseQuantity) &&
                                amount == InventoryCostBudgetPolicy.Slice(debit.AnchorQuantity, debit.AnchorAmount, debit.StartQuantity + start, end - start),
                                "Chứng từ nguồn ngân sách không khớp số tiền của lát giá vốn thực giao.");
                        }
                        else Require(root.IsProvisional && sourceAllocation!.IsProvisional && slice.LayerId == 0 &&
                            sourceAllocation.InventoryCostLayerId is null && amount == InventoryCostBudgetPolicy.Slice(fragment.BaseQuantity, fragment.CostAmount, start, end - start),
                            "Lát giá vốn tạm tính không khớp nguồn xuất giao còn thiếu tồn.");
                        Require(slice.Quality is "Confirmed" or "Provisional" or "Unknown", "Trạng thái giá vốn thực giao không được hỗ trợ.");
                        if (slice.Quality == "Unknown") throw new InvalidProof("Nguồn giá vốn thực giao chưa có đơn giá để tính.", ProfitQuality.Unavailable);
                        Require(slice.Quality != "Confirmed" || !root.IsProvisional && root.CostFinalizedAtUtc.HasValue && slice.BudgetDebitId > 0,
                            "Giá vốn thực giao được đánh dấu xác định nhưng chứng từ nguồn còn tạm tính.");
                        var temporary = slice.Quality != "Confirmed"; provisional |= temporary;
                        cost += amount; if (temporary) exposure += amount; quantity += end - start;
                    }
                    Require(quantity == line.BaseQuantity && cost == priced.RecordedCost &&
                        priced.CostQuality == (provisional ? "Provisional" : "Confirmed"), "Tổng giá vốn hoặc số lượng các lát không khớp hàng thực giao.");
                    var current = new ProfitReportAggregationPolicy.CostResult(cost, exposure,
                        provisional ? ProfitQuality.Provisional : ProfitQuality.Finalized, provisional ? 1 : 0);
                    outcomes.Add(current); if (lineResults is not null) lineResults[line.Id] = current;
                }
                catch (InvalidProof ex)
                {
                    Issue(line, "DeliverySourceMismatch", ex.Message, ex.Quality);
                    var unknown = new ProfitReportAggregationPolicy.CostResult(null, null, ex.Quality, null);
                    outcomes.Add(unknown); if (lineResults is not null) lineResults[line.Id] = unknown;
                }
            }
            var summary = ProfitReportAggregationPolicy.Summarize(0, outcomes, true);
            return new(summary.Cogs.Value, summary.ProvisionalCogs.Value, summary.Cogs.Quality, summary.AffectedLines);
        }
        catch (Exception ex) when (ex is InvalidProof or JsonException or ArgumentException or InvalidOperationException or BusinessRuleException)
        {
            Issue(null, "DeliveryHandoverInvalid", ex is InvalidProof ? ex.Message : "Chứng cứ giá vốn bàn giao thiếu hoặc không đọc được.");
            return new(null, null, ProfitQuality.DataIntegrityConflict, null);
        }
    }
}
