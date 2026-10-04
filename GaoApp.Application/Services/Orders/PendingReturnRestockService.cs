using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Returns;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Orders;

public sealed class PendingReturnRestockService(
    IUnitOfWork uow, ISalesReturnRestockRepository pending, ISalesReturnRepository returns,
    IReturnableValuationFragmentService fragments, IInventoryMovementService movements,
    IInventoryMovementFactory factory, ICurrentStore store, ICurrentUser user,
    IPOSAuditLogRepository posAudit, IAuditLogService audit) : IPendingReturnRestockService
{
    private IPendingRestockCostEvidence CostEvidence => fragments as IPendingRestockCostEvidence
        ?? throw new BusinessRuleException("Chưa đọc được giá vốn để hoàn tất nhập kho.");

    public async Task<List<PendingReturnRestockDto>> GetPendingAsync(CancellationToken ct = default)
    {
        var rows = await pending.GetPendingAsync(null, ct);
        var result = new List<PendingReturnRestockDto>();
        foreach (var group in rows.GroupBy(x => x.SalesReturnLine.SalesReturnId))
        {
            var header = group.First().SalesReturnLine.SalesReturn;
            var dto = new PendingReturnRestockDto {ReturnId = header.Id, OrderId = header.OrderId,
                ReturnNumber = header.ReturnNumber, OrderNumber = header.Order.OrderNumber ?? $"#{header.OrderId}",
                ReceivedAtUtc = header.CreatedAtUtc, RefundedAmount = header.RefundTotal + header.DepositRestoredTotal};
            foreach (var itemGroup in group.GroupBy(x => (x.SalesReturnLineId, x.SourceValuationEntry.WarehouseId)))
            {
                var row = itemGroup.First();
                var reasons = new List<string>();
                foreach (var fragment in itemGroup)
                {
                    try { await ReadCostAsync(fragment, ct); }
                    catch (SaleRestockCostException ex) { reasons.Add(ex.SafeMessage + " " + ex.ActionHint); dto.CanComplete = false; }
                    catch (BusinessRuleException ex) { reasons.Add(ex.SafeMessage); dto.CanComplete = false; }
                }
                dto.Items.Add(new PendingReturnRestockItemDto {ItemName = row.SalesReturnLine.ItemName,
                    WarehouseName = row.SourceValuationEntry.Warehouse?.Name ?? "Kho xuất bán", BaseQuantity = itemGroup.Sum(x => x.BaseQuantity),
                    BlockReason = reasons.Count == 0 ? null : string.Join(" ", reasons.Distinct())});
            }
            result.Add(dto);
        }
        return result;
    }

    private async Task<ReturnableValuationFragmentDto> ReadCostAsync(SalesReturnRestockFragment row, CancellationToken ct)
    {
        var line = row.SalesReturnLine;
        if (row.StoreId != store.StoreId || line.StoreId != store.StoreId || line.SalesReturn.StoreId != store.StoreId ||
            row.SourceValuationEntry.StoreId != store.StoreId || line.Action != SalesReturnLineAction.PendingRestock)
            throw new BusinessRuleException("Nguồn hàng chờ nhập kho không khớp cửa hàng hoặc dòng hàng.");
        var source = await CostEvidence.GetReservedSourceAsync(line.SalesReturn.OrderId, line.OrderLineId, row.SourceValuationEntryId, ct);
        if (source.ProductVariantId != line.VariantId || row.BaseQuantity <= 0 || source.RemainingQuantityAbs < row.BaseQuantity)
            throw new BusinessRuleException("Số lượng hoặc nguồn hàng chờ nhập kho không còn khớp. Quản lý cần đối soát.");
        if (row.AllocationReversalId is not null && (row.AllocationReversal is not { } reversal ||
            reversal.StoreId != row.StoreId || reversal.SalesReturnLineId != line.Id || reversal.SourceValuationEntryId != source.SourceValuationEntryId ||
            reversal.BaseQuantity != row.BaseQuantity || reversal.InventoryTransactionId != null || reversal.ReversalType != OrderLegalEntityReversalType.ReturnPendingRestock))
            throw new BusinessRuleException("Phân bổ hàng chờ nhập kho không khớp chứng từ gốc.");
        return source;
    }

    public async Task CompleteAsync(int returnId, CancellationToken ct = default)
    {
        var userId = user.UserId ?? throw new BusinessRuleException("Phiên đăng nhập không hợp lệ.");
        await uow.BeginTransactionAsync(ct);
        try
        {
            var reference = await returns.GetByIdAsync(returnId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu trả hàng.");
            await pending.LockOrderAsync(reference.OrderId, ct);
            var header = await returns.GetByIdWithDetailsAsync(returnId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu trả hàng.");
            var rows = await pending.GetPendingAsync(returnId, ct);
            if (header.Status != SalesReturnStatus.Completed || header.StoreId != store.StoreId)
                throw new BusinessRuleException("Phiếu trả hàng không đủ điều kiện nhập kho.");
            if (rows.Count == 0)
            {
                if (!await pending.HasFragmentsAsync(returnId, ct))
                    throw new BusinessRuleException("Phiếu này không có hàng chờ nhập kho.");
                if (header.Lines.Any(x => x.Action == SalesReturnLineAction.PendingRestock))
                    throw new BusinessRuleException("Thiếu nguồn hàng chờ nhập kho. Quản lý cần đối soát.");
                // A repeated completion is a no-op; never post another refund or receipt.
                await uow.CommitTransactionAsync(ct);
                return;
            }
            await movements.PreLockBalancesAsync(rows.Select(x => new InventoryPostingLockKey(store.StoreId,
                x.SourceValuationEntry.WarehouseId, x.SourceValuationEntry.ProductVariantId)).Distinct().ToList(), ct);
            var plans = new List<(SalesReturnRestockFragment Row, ReturnableValuationFragmentDto Source)>();
            foreach (var row in rows) plans.Add((row, await ReadCostAsync(row, ct)));
            foreach (var group in plans.GroupBy(x => x.Source.SourceValuationEntryId))
                if (group.Sum(x => x.Row.BaseQuantity) > group.First().Source.RemainingQuantityAbs)
                    throw new BusinessRuleException("Số lượng nhập lại kho vượt nguồn hàng gốc.");
            var now = DateTime.UtcNow;
            foreach (var (row, source) in plans)
            {
                var line = row.SalesReturnLine;
                var request = factory.CreateSaleRefund(source.WarehouseId, source.ProductVariantId, header.Id, line.Id,
                    row.BaseQuantity, source.UnitCost, header.Reason, now,
                    $"PENDING-RET:R{line.Id}:F{row.Id}:S{source.SourceValuationEntryId}", source.SourceValuationEntryId, source.ReferenceSubKey);
                var movement = await movements.CreateAsync(request, ct);
                if (!movement.IsCreated || movement.InventoryTransactionId is null)
                    throw new BusinessRuleException("Phiếu nhập đã thay đổi. Tải lại danh sách để đối chiếu.");
                row.InventoryTransactionId = movement.InventoryTransactionId;
                row.CompletedAtUtc = now;
                row.CompletedByUserId = userId;
                if (row.AllocationReversal is { } reversal)
                {
                    reversal.InventoryTransactionId = movement.InventoryTransactionId;
                    reversal.ReversalType = OrderLegalEntityReversalType.ReturnRestock;
                }
            }
            foreach (var group in plans.GroupBy(x => x.Row.SalesReturnLineId))
            {
                var line = group.First().Row.SalesReturnLine;
                if (group.Sum(x => x.Row.BaseQuantity) != line.ReturnBaseQuantity)
                    throw new BusinessRuleException("Thiếu số lượng hàng chờ nhập kho của dòng trả.");
                line.LineCostTotal = group.Sum(x => x.Row.BaseQuantity * x.Source.UnitCost);
                line.UnitCostSnapshot = Math.Round(line.LineCostTotal / line.ReturnBaseQuantity, 6, MidpointRounding.AwayFromZero);
                line.IsProvisionalCost = false;
                line.Action = SalesReturnLineAction.Restock;
            }
            await uow.SaveChangesAsync(ct);
            await posAudit.AddAsync(new POSAuditLog {StoreId = store.StoreId, OrderId = header.OrderId, UserId = userId,
                Action = "RETURN_RESTOCK_COMPLETED", Note = "Hoàn tất nhập kho hàng trả; không phát sinh thêm tiền hoàn.",
                MetadataJson = JsonSerializer.Serialize(new {returnId, FragmentIds = rows.Select(x => x.Id), BaseQuantity = rows.Sum(x => x.BaseQuantity)})}, ct);
            await audit.WriteAsync(new WriteAuditLogRequest {Module = AuditModuleType.Orders, ActionType = AuditActionType.ReturnComplete,
                EntityName = nameof(SalesReturn), EntityId = header.Id.ToString(), EntityDisplay = header.ReturnNumber,
                Summary = "Hoàn tất nhập kho hàng trả đang chờ xác định giá vốn", IsSuccess = true}, ct);
            await uow.SaveChangesAsync(ct);
            await uow.CommitTransactionAsync(ct);
        }
        catch (SaleRestockCostException ex)
        {
            await uow.RollbackTransactionAsync(CancellationToken.None);
            throw PosAppException.Business(ex.ErrorCode, ex.SafeMessage, ex.ActionHint, new {returnId, ex.OrderId, ex.OrderLineId});
        }
        catch
        {
            await uow.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }
}
