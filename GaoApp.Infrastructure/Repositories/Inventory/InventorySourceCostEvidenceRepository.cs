using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class InventorySourceCostEvidenceRepository(AppDbContext db) : IInventorySourceCostEvidenceRepository
{
    private static BusinessRuleException Bad(string detail) => new("COST_NOT_READY: " + detail);
    private int Store => db.CurrentStoreId ?? throw Bad("Cần cửa hàng hiện tại cho nguồn giá vốn.");
    private void Ambient()
    {
        if (!db.Database.IsSqlServer() || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Typed source posting requires an ambient SQL Server transaction.");
    }
    public async Task EnsurePromotedAsync(InventoryBalance balance, CancellationToken ct)
    {
        Ambient();
        var rows = await new InventoryReservationRepository(db).GetActiveForBalanceAsync(balance.StoreId, balance.WarehouseId, balance.ProductVariantId, ct);
        var sum = rows.Aggregate(0m, (value, row) => checked(value + row.ReservedQty));
        if (sum != balance.ReservedQty || sum < 0) throw Bad("Tổng giữ hàng không khớp số dư; cần đối chiếu riêng.");
        InventoryPostingPrecisionPolicy.Quantity(balance.OnHandQty); InventoryPostingPrecisionPolicy.Quantity(sum);
        InventoryPostingPrecisionPolicy.Amount(balance.InventoryValue);
        if (balance.QuantityPrecisionVersion is not (0 or 1)) throw Bad("Phiên bản tồn kho không hợp lệ.");
        balance.QuantityPrecisionVersion = 1;
    }
    public async Task PromoteLayerAsync(InventoryCostLayer layer, CancellationToken ct)
    {
        Ambient();
        if (layer.CostBudgetPolicy == 1)
        {
            if (layer.BudgetAnchorQuantity is not decimal q || layer.BudgetAnchorAmount is not decimal amount ||
                layer.RemainingCostAmount != amount - InventoryCostBudgetPolicy.Cumulative(q, amount, q - layer.RemainingQuantity))
                throw Bad("Ngân sách layer không khớp.");
            var proofs = await db.InventoryCostBudgetDebits.AsNoTracking().Where(x => x.StoreId == layer.StoreId && x.InventoryCostLayerId == layer.Id)
                .OrderBy(x => x.StartQuantity).ThenBy(x => x.Id).ToListAsync(ct);
            decimal cursor = 0, debited = 0;
            foreach (var proof in proofs)
            {
                if (proof.AnchorQuantity != q || proof.AnchorAmount != amount || proof.StartQuantity != cursor || proof.EndQuantity <= cursor ||
                    proof.Amount != InventoryCostBudgetPolicy.Slice(q, amount, proof.StartQuantity, proof.EndQuantity - proof.StartQuantity))
                    throw Bad("Khoảng ngân sách layer thiếu/trùng hoặc bị thay đổi.");
                cursor = proof.EndQuantity; debited += proof.Amount;
            }
            if (cursor != q - layer.RemainingQuantity || debited != amount - layer.RemainingCostAmount) throw Bad("Chứng cứ tiêu thụ layer chưa đầy đủ.");
            return;
        }
        var source = await db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.StoreId == layer.StoreId && x.Id == layer.InventoryValuationEntryId, ct);
        var tx = await db.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.StoreId == layer.StoreId && x.Id == layer.InventoryTransactionId, ct);
        var warehouse = await db.Warehouses.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == layer.StoreId && x.Id == layer.WarehouseId, ct);
        if (source is null || tx is null || warehouse is null || source.IsDeleted || tx.IsDeleted || warehouse.IsDeleted ||
            layer.IsDeleted || layer.IsProvisionalSource || source.IsProvisional || tx.IsProvisionalCost || source.CostSourceType == InventoryCostSourceType.Unknown ||
            (source.EntryType != InventoryValuationEntryType.Inbound &&
                (source.EntryType != InventoryValuationEntryType.Opening || tx.TransactionType != InventoryTransactionType.OpeningBalance)) ||
            source.CostFinalizedAtUtc is null || tx.CostFinalizedAtUtc is null ||
            source.InventoryTransactionId != tx.Id || source.WarehouseId != layer.WarehouseId || source.ProductVariantId != layer.ProductVariantId ||
            tx.WarehouseId != layer.WarehouseId || tx.ProductVariantId != layer.ProductVariantId ||
            source.Quantity != layer.OriginalQuantity || source.UnitCost != layer.UnitCost || source.Amount < 0)
            throw Bad("Nguồn nhập FIFO thiếu, tạm tính hoặc không khớp.");
        // Initial import names the same positive product event LEGACY-OPENING in
        // its valuation and LEGACY-POS in the transaction/layer (Migration.sql).
        var legacyOpeningAlias = source.EntryType == InventoryValuationEntryType.Opening &&
            tx.TransactionType == InventoryTransactionType.OpeningBalance && source.ReferenceType == InventoryReferenceType.None &&
            source.ReferenceId == "LEGACY-PRODUCT" && source.ReferenceLineId is > 0 && source.InventoryCostLayerId == layer.Id &&
            source.CostSourceType == InventoryCostSourceType.Manual && tx.CostSourceType == InventoryCostSourceType.Manual &&
            tx.PostingPrecisionPolicy == 0 && source.ReferenceSubKey == "LEGACY-OPENING" &&
            layer.ReferenceSubKey == "LEGACY-POS" && tx.ReferenceSubKey == "LEGACY-POS";
        if (source.ReferenceType != layer.ReferenceType || source.ReferenceId != layer.ReferenceId || source.ReferenceLineId != layer.ReferenceLineId ||
            (source.ReferenceSubKey != layer.ReferenceSubKey && !legacyOpeningAlias) || source.ReferenceType != tx.ReferenceType || source.ReferenceId != tx.ReferenceId || source.ReferenceLineId != tx.ReferenceLineId)
            throw Bad("Định danh gốc của nguồn nhập FIFO không khớp.");
        InventoryPostingPrecisionPolicy.Cost(layer.UnitCost);
        var siblings = await db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == layer.StoreId && x.InventoryTransactionId == tx.Id).ToListAsync(ct);
        if (siblings.Any(x => x.IsDeleted || x.WarehouseId != layer.WarehouseId || x.ProductVariantId != layer.ProductVariantId) ||
            siblings.Sum(x => x.Quantity) != tx.QuantityChange || siblings.Sum(x => x.Amount) != tx.TotalCost ||
            tx.QuantityChange <= 0 || tx.AfterQty != tx.BeforeQty + tx.QuantityChange ||
            (tx.PostingPrecisionPolicy == 0 && (source.Amount != decimal.Round(source.Quantity * source.UnitCost, 4, MidpointRounding.AwayFromZero) ||
                tx.AfterInventoryValue != tx.BeforeInventoryValue + tx.TotalCost ||
                siblings.Count(x => x.EntryType == source.EntryType) == 1 && tx.UnitCostSnapshot != source.UnitCost)))
            throw Bad("Lượng/giá trị bút toán nguồn không khớp chứng cứ nhập.");
        var allocations = await db.InventoryCostLayerAllocations.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.InventoryValuationEntry).Where(x => x.StoreId == layer.StoreId &&
                (x.InventoryCostLayerId == layer.Id || x.ResolvedByInventoryCostLayerId == layer.Id)).ToListAsync(ct);
        var actualAllocations = allocations.Where(x => !x.IsProvisional).ToArray();
        if (actualAllocations.Any(x => x.IsDeleted || x.ReverseOfAllocationId is not null || x.Quantity <= 0 || x.Amount < 0 ||
            x.InventoryValuationEntry.IsDeleted || x.InventoryValuationEntry.StoreId != layer.StoreId ||
            x.InventoryValuationEntry.WarehouseId != layer.WarehouseId || x.InventoryValuationEntry.ProductVariantId != layer.ProductVariantId ||
            x.InventoryValuationEntry.EntryType != InventoryValuationEntryType.Outbound ||
            x.InventoryValuationEntry.Quantity != -x.Quantity || x.InventoryValuationEntry.Amount != -x.Amount))
            throw Bad("Lịch sử tiêu thụ layer chưa đủ chứng cứ.");
        var resolved = allocations.Where(x => x.IsProvisional).ToArray();
        if (resolved.Any(x => x.IsDeleted || x.InventoryCostLayerId != layer.Id || x.ResolvedByInventoryCostLayerId != layer.Id ||
            x.ResolvedQuantity <= 0 || x.ResolvedQuantity > x.Quantity || x.ResolvedAmount < 0 ||
            x.InventoryValuationEntry.IsDeleted || !x.InventoryValuationEntry.IsProvisional ||
            x.InventoryValuationEntry.StoreId != layer.StoreId || x.InventoryValuationEntry.Quantity != -x.Quantity ||
            x.InventoryValuationEntry.WarehouseId != layer.WarehouseId || x.InventoryValuationEntry.ProductVariantId != layer.ProductVariantId ||
            x.ResolvedAmount != decimal.Round(x.ResolvedQuantity * layer.UnitCost, 4, MidpointRounding.AwayFromZero)) ||
            resolved.Sum(x => x.ResolvedQuantity) != layer.ResolvedProvisionalQty)
            throw Bad("Lịch sử bù âm nhiều nguồn/chưa đủ khoảng giá vốn; cần đối chiếu riêng.");
        var resolvedSourceIds = resolved.Select(x => x.InventoryValuationEntryId).ToArray();
        var resolutions = await db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.StoreId == layer.StoreId && x.RevaluationOfEntryId != null && resolvedSourceIds.Contains(x.RevaluationOfEntryId.Value)).ToListAsync(ct);
        if (resolutions.Any(x => x.IsDeleted || x.InventoryCostLayerId != layer.Id || x.SourceValuationEntryId != x.RevaluationOfEntryId ||
            x.InventoryTransactionId != layer.InventoryTransactionId || x.EntryType != InventoryValuationEntryType.Revaluation ||
            x.Quantity != 0 || x.WarehouseId != layer.WarehouseId || x.ProductVariantId != layer.ProductVariantId ||
            x.IsProvisional || x.CostFinalizedAtUtc is null || x.UnitCost != layer.UnitCost ||
            x.CostSourceType != InventoryCostSourceType.Manual))
            throw Bad("Chuỗi bù âm lịch sử không có nguồn duy nhất đã xác định.");
        foreach (var allocation in resolved)
        {
            var sourceEntry = allocation.InventoryValuationEntry;
            var adjustments = resolutions.Where(x => x.RevaluationOfEntryId == sourceEntry.Id).ToArray();
            if (adjustments.Any(x => x.ReferenceType != sourceEntry.ReferenceType || x.ReferenceId != sourceEntry.ReferenceId ||
                x.ReferenceLineId != sourceEntry.ReferenceLineId || x.ReferenceSubKey != sourceEntry.ReferenceSubKey ||
                x.SourceReferenceSubKey != sourceEntry.ReferenceSubKey))
                throw Bad("Định danh gốc của chuỗi bù âm lịch sử không khớp.");
            var adjustment = adjustments.Sum(x => x.Amount);
            var provisionalAmount = InventoryCostBudgetPolicy.Slice(allocation.Quantity, allocation.Amount, 0, allocation.ResolvedQuantity);
            if (adjustment != allocation.ResolvedAmount - provisionalAmount) throw Bad("Giá trị bù âm lịch sử không khớp.");
        }
        var quantity = actualAllocations.Sum(x => x.Quantity) + resolved.Sum(x => x.ResolvedQuantity);
        var consumed = actualAllocations.Sum(x => x.Amount) + resolved.Sum(x => x.ResolvedAmount);
        if (layer.RemainingQuantity <= 0 || layer.OriginalQuantity - quantity != layer.RemainingQuantity || source.Amount < consumed)
            throw Bad("Số lượng/giá trị còn lại của layer không khớp.");
        layer.CostBudgetPolicy = 1;
        layer.BudgetAnchorQuantity = InventoryPostingPrecisionPolicy.Quantity(layer.RemainingQuantity);
        layer.BudgetAnchorAmount = InventoryPostingPrecisionPolicy.Amount(source.Amount - consumed);
        layer.RemainingCostAmount = layer.BudgetAnchorAmount;
    }
    public async Task RejectUnsupportedMirrorAsync(CreateInventoryMovementRequest request, int storeId, CancellationToken ct)
    {
        if (request.SourceValuationEntryId is not int id) return;
        var protectedSource = await db.InventoryCostBudgetDebits.AsNoTracking().AnyAsync(x =>
            x.StoreId == storeId && x.InventoryValuationEntryId == id, ct);
        if (protectedSource) throw new BusinessRuleException("SOURCE_BUDGET_REVERSAL_UNSUPPORTED: đảo nguồn giá vốn mới cần adapter nguồn; chưa thay đổi kho/tiền.");
    }
    private async Task<DeliveryOrder> Delivery(int id, bool active, CancellationToken ct)
    {
        Ambient();
        var order = await db.DeliveryOrders.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == id, ct) ?? throw Bad("Đơn giao không thuộc cửa hàng.");
        if (!await db.Warehouses.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceWarehouseId &&
            x.LegalEntityId == order.SourceLegalEntityId && (!active || x.IsActive), ct) ||
            !await db.LegalEntities.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceLegalEntityId && (!active || x.IsActive), ct))
            throw Bad("Kho/chủ thể nguồn không khớp.");
        return order;
    }
    public async Task<InventoryDeliveryPostingProof> GetDeliveryDispatchProofAsync(InventoryDeliveryDispatchPosting request, CancellationToken ct)
    {
        var order = await Delivery(request.DeliveryOrderId, true, ct);
        var work = await db.DeliveryPickingWorks.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.DeliveryOrderId == order.Id, ct);
        var line = await db.DeliveryPickingLines.AsNoTracking().Include(x => x.Quote).SingleOrDefaultAsync(x => x.StoreId == Store &&
            x.DeliveryOrderId == order.Id && x.DeliveryOrderLineId == request.LineId && x.IsActive, ct);
        if (order.State != DeliveryState.ReadyForHandover || work is null || work.ApprovedRevision != request.ApprovedRevision ||
            work.ApprovedTotal is null || line?.ApprovedQuantity is not > 0 || line.ReportedQuantity is null || line.ReportedQuantity < line.ApprovedQuantity)
            throw Bad("Thiếu phiên bản duyệt hoặc kết quả soạn thực tế.");
        var quantity = DeliveryPickingPolicy.ToBase(line.ApprovedQuantity.Value, line.Quote.BaseMultiplier);
        var rows = await db.InventoryReservations.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == order.Id &&
            x.DeliveryOrderLineId == request.LineId && x.Status == InventoryReservationStatus.Active).ToListAsync(ct);
        var hold = rows.SingleOrDefault();
        if (hold is null || hold.WarehouseId != order.SourceWarehouseId || hold.ProductVariantId != line.Quote.VariantId ||
            hold.SourceLegalEntityId != order.SourceLegalEntityId || hold.ReservedQty != quantity ||
            hold.ReportedByUserId != line.ReporterUserId || hold.ReportedAtUtc != line.ReportedAtUtc)
            throw Bad("Giữ hàng không khớp nguồn/số lượng/generation đã duyệt.");
        return new(Store, order.SourceWarehouseId, line.Quote.VariantId, order.SourceLegalEntityId, quantity, hold.Id, [], line.DeliveryOrderLineId);
    }
    public async Task<InventoryDeliveryPostingProof> GetDeliveryReturnProofAsync(InventoryDeliveryReturnPosting request, CancellationToken ct)
    {
        var order = await Delivery(request.DeliveryOrderId, false, ct);
        var fragment = await db.DeliveryDispatchCostFragments.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store &&
            x.DeliveryOrderId == order.Id && x.Id == request.DispatchFragmentId, ct) ?? throw Bad("Mảnh xuất nguồn không tồn tại.");
        var debit = await db.InventoryCostBudgetDebits.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == fragment.InventoryCostBudgetDebitId, ct);
        if (debit is null || debit.IsProvisionalResolution || debit.InventoryValuationEntryId != fragment.InventoryValuationEntryId ||
            debit.InventoryTransactionId != fragment.InventoryTransactionId || debit.InventoryCostLayerId != fragment.InventoryCostLayerId ||
            debit.InventoryCostLayerAllocationId != fragment.InventoryCostLayerAllocationId || debit.SourceLegalEntityId != order.SourceLegalEntityId ||
            debit.Amount != fragment.CostAmount || debit.EndQuantity - debit.StartQuantity != fragment.BaseQuantity ||
            request.Quantity <= 0 || request.StartQuantity < 0 || request.StartQuantity + request.Quantity > fragment.BaseQuantity)
            throw Bad("Mảnh xuất/đoạn giá vốn nhận trả không khớp.");
        var overlaps = await db.DeliveryReturnedCostFragments.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.DispatchCostFragmentId == fragment.Id &&
            x.StartQuantity < request.StartQuantity + request.Quantity && x.EndQuantity > request.StartQuantity, ct);
        if (overlaps) throw new BusinessRuleException("RETURN_BUDGET_EXCEEDED: đoạn hàng đã nhận lại.");
        var amount = InventoryCostBudgetPolicy.Slice(debit.AnchorQuantity, debit.AnchorAmount,
            debit.StartQuantity + request.StartQuantity, request.Quantity);
        var source = await db.InventoryValuationEntries.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.Id == fragment.InventoryValuationEntryId, ct);
        return new(Store, order.SourceWarehouseId, fragment.VariantId, order.SourceLegalEntityId, request.Quantity, null,
            [new(source.Id, source.ReferenceSubKey, debit.Id, request.Quantity, fragment.UnitCost, amount)], fragment.DeliveryOrderLineId);
    }
    public async Task<InventoryTransferPostingProof> GetTransferProofAsync(InventorySourcePreservingTransfer request, CancellationToken ct)
    {
        Ambient();
        var line = await db.StockTransferLines.AsNoTracking().Include(x => x.StockTransferDocument)
            .SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == request.LineId && x.StockTransferDocumentId == request.DocumentId, ct)
            ?? throw Bad("Dòng chuyển kho không thuộc cửa hàng.");
        var doc = line.StockTransferDocument;
        if (doc.StoreId != Store || doc.Status is not (StockTransferDocumentStatus.PendingApproval or StockTransferDocumentStatus.Confirmed) ||
            line.BaseQuantity <= 0 || doc.FromWarehouseId == doc.ToWarehouseId ||
            !await db.Warehouses.AnyAsync(x => x.StoreId == Store && x.Id == doc.FromWarehouseId, ct) ||
            !await db.Warehouses.AnyAsync(x => x.StoreId == Store && x.Id == doc.ToWarehouseId, ct)) throw Bad("Nguồn chuyển kho không hợp lệ.");
        return new(Store, doc.Id, line.Id, doc.FromWarehouseId, doc.ToWarehouseId, line.ProductVariantId, line.BaseQuantity, doc.DocumentNo);
    }
    public async Task<IReadOnlyList<InventorySourceCostMirrorPart>> GetPostingPartsAsync(int storeId, int transactionId, CancellationToken ct)
    {
        var rows = await db.InventoryCostBudgetDebits.AsNoTracking().Include(x => x.ValuationEntry)
            .Where(x => x.StoreId == storeId && x.InventoryTransactionId == transactionId && !x.IsProvisionalResolution)
            .OrderBy(x => x.Id).ToListAsync(ct);
        return rows.Select(x => new InventorySourceCostMirrorPart(x.InventoryValuationEntryId, x.ValuationEntry.ReferenceSubKey,
            x.Id, x.EndQuantity - x.StartQuantity, x.UnitCost, x.Amount)).ToArray();
    }
    public Task AddDebitAsync(InventoryCostBudgetDebit debit, CancellationToken ct) => db.InventoryCostBudgetDebits.AddAsync(debit, ct).AsTask();
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
