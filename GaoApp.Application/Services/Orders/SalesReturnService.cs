using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Returns;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Text.Json;

namespace GaoApp.Application.Services.Orders;

/// <summary>
/// Service xử lý nghiệp vụ trả hàng / hoàn tiền sau bán.
///
/// NGUYÊN TẮC NGHIỆP VỤ:
/// 1. Không sửa lịch sử order bán gốc.
/// 2. Mỗi lần return/refund tạo một chứng từ SalesReturn riêng.
/// 3. Return có thể nhập kho hoặc không.
/// 4. Refund tiền ghi vào ca POS đang mở hiện tại theo Store + Terminal hiện tại,
///    KHÔNG bắt buộc theo ca gốc của order.
/// 5. Order gốc chỉ dùng để đối chiếu số đã bán / đã thu / đã hoàn.
/// 6. Ở mức 2, validate quantity phải bám base quantity để không lệch FIFO layer.
/// </summary>
public sealed class SalesReturnService : ISalesReturnService
{
    private sealed class LegacyRestockPlan
    {
        public required SalesReturnLine Line { get; init; }
        public required IReadOnlyList<ReturnCostAllocationDto> Allocations { get; init; }
        public required IReadOnlyList<ReturnableValuationFragmentDto> Fragments { get; init; }
    }

    private readonly ICustomerDepositService? _deposits;
    private readonly ICustomerReceivableService? _receivables;
    private readonly IUnitOfWork _uow;
    private readonly IOrderRepository _orders;
    private readonly ISalesReturnRepository _salesReturns;
    private readonly IPOSShiftRepository _shifts;
    private readonly IPOSAuditLogRepository _posAuditLogs;
    private readonly IWarehouseRepository _warehouses;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly IInventoryMovementFactory _inventoryMovementFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;
    private readonly IReturnableValuationFragmentService _returnableValuationFragmentService;
    private readonly IReturnCostAllocator _returnCostAllocator;
    private readonly ICustomerRewardLedgerRepository _rewardLedgers;
    private readonly IOrderRewardCalculator _orderRewardCalculator;
    private readonly IOrderLegalEntityReversalService _legalEntityReversalService;
    private readonly IDraftInvoiceReturnSyncService _draftInvoiceReturnSyncService;
    public SalesReturnService(
        IUnitOfWork uow,
        IOrderRepository orders,
        ISalesReturnRepository salesReturns,
        IPOSShiftRepository shifts,
        IPOSAuditLogRepository posAuditLogs,
        IWarehouseRepository warehouses,
        IInventoryMovementService inventoryMovementService,
        IInventoryMovementFactory inventoryMovementFactory,
        IAuditLogService auditLogService,
        ICurrentStore currentStore,
        ICurrentUser currentUser,
        IReturnableValuationFragmentService returnableValuationFragmentService,
        IReturnCostAllocator returnCostAllocator,
        ICustomerRewardLedgerRepository rewardLedgers,
        IOrderRewardCalculator orderRewardCalculator,
        IOrderLegalEntityReversalService legalEntityReversalService,
        IDraftInvoiceReturnSyncService draftInvoiceReturnSyncService,
        ICustomerReceivableService? receivables = null, ICustomerDepositService? deposits = null)
    {
        _deposits = deposits;
        _receivables = receivables;
        _uow = uow;
        _orders = orders;
        _salesReturns = salesReturns;
        _shifts = shifts;
        _posAuditLogs = posAuditLogs;
        _warehouses = warehouses;
        _inventoryMovementService = inventoryMovementService;
        _inventoryMovementFactory = inventoryMovementFactory;
        _auditLogService = auditLogService;
        _currentStore = currentStore;
        _currentUser = currentUser;
        _returnableValuationFragmentService = returnableValuationFragmentService;
        _returnCostAllocator = returnCostAllocator;
        _rewardLedgers = rewardLedgers;
        _orderRewardCalculator = orderRewardCalculator;
        _legalEntityReversalService = legalEntityReversalService;
        _draftInvoiceReturnSyncService = draftInvoiceReturnSyncService;
    }

    public async Task<SalesReturnDto> CreateAsync(CreateSalesReturnRequest request, CancellationToken ct = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (request.Lines == null || request.Lines.Count == 0)
            throw new InvalidOperationException("Phiếu trả hàng phải có ít nhất 1 dòng.");

        if (request.OrderId <= 0)
            throw new InvalidOperationException("Order không hợp lệ.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("Lý do không được để trống.");

        var duplicateOrderLineId = request.Lines
            .GroupBy(x => x.OrderLineId)
            .FirstOrDefault(x => x.Count() > 1)?.Key;
        if (duplicateOrderLineId.HasValue)
        {
            throw new InvalidOperationException(
                $"OrderLine #{duplicateOrderLineId.Value} bị lặp trong cùng phiếu trả hàng.");
        }

        // luôn đảm bảo không null để tránh Sum/foreach lỗi ngầm
        request.Payments ??= new List<CreateSalesReturnPaymentRequest>();

        var storeId = RequireStoreId();
        var terminalId = RequireTerminalId();
        var userId = RequireUserId();

        await _uow.BeginTransactionAsync(ct);

        try
        {
            // =====================================================
            // 1. Load order gốc
            // =====================================================
            if (_receivables != null) await _receivables.LockOrderAsync(request.OrderId, ct);
            var order = await _orders.GetByIdWithDetailsAsync(request.OrderId, ct)
                ?? throw new InvalidOperationException("Không tìm thấy order.");

            if (order.Status != OrderStatus.Completed && order.Status != OrderStatus.Refunded)
                throw new InvalidOperationException("Chỉ xử lý return/refund cho đơn đã chốt.");

            if (order.Lines == null || order.Lines.Count == 0)
                throw new InvalidOperationException("Order không có dòng hàng để xử lý trả.");

            // InvoiceHead còn nháp phải đi cùng return POS; hóa đơn đã phát hành
            // chuyển sang luồng trả hàng kế toán và tuyệt đối không sửa tại đây.
            await _draftInvoiceReturnSyncService.EnsurePosReturnAllowedAsync(
                order.StoreId,
                order.Id,
                request.Lines.Select(x => x.OrderLineId).ToList(),
                ct);

            // =====================================================
            // 2. Lấy ca POS đang mở theo Store + Terminal hiện tại
            //    KHÔNG dùng order.POSShiftId để tránh bị khóa bởi ca cũ đã đóng
            // =====================================================
            var currentShift = await _shifts.GetOpenShiftAsync(storeId, terminalId, ct)
                ?? throw new InvalidOperationException("Không có ca POS đang mở tại terminal hiện tại để xử lý trả hàng / hoàn tiền.");

            var warehouse = await _warehouses.GetByIdAsync(currentShift.WarehouseId, ct)
                ?? throw new InvalidOperationException("Không tìm thấy kho của ca POS hiện tại.");

            // =====================================================
            // 3. Kiểm tra giới hạn số tiền còn được refund
            // =====================================================
            var alreadyRefunded = await _salesReturns.GetRefundedTotalByOrderAsync(order.Id, ct);
            var requestedRefund = request.Payments.Sum(x => x.Amount) + request.DepositRefundAmount;
            if (request.DepositRefundAmount < 0 || request.DepositRefundAmount != decimal.Truncate(request.DepositRefundAmount))
                throw new GaoApp.Application.Common.Exceptions.BusinessRuleException("Số hoàn vào cọc không hợp lệ.");
            if (request.DepositRefundAmount > 0 && (_deposits == null || request.DepositRefundAmount > await _deposits.GetReturnableAsync(order, ct)))
                throw new GaoApp.Application.Common.Exceptions.BusinessRuleException("Số hoàn vào cọc vượt phần cọc còn có thể hoàn.");
            var maxRefundable = order.PaidTotal;

            if (requestedRefund < 0)
                throw new InvalidOperationException("Tổng tiền hoàn không hợp lệ.");

            if (alreadyRefunded + requestedRefund > maxRefundable)
                throw new InvalidOperationException("Số tiền hoàn vượt quá số tiền đã thu còn có thể hoàn.");

            // =====================================================
            // 4. Tạo phiếu SalesReturn
            //    POSShiftId = ca đang mở hiện tại
            // =====================================================
            var entity = new SalesReturn
            {
                StoreId = order.StoreId,
                OrderId = order.Id,
                POSShiftId = currentShift.Id,
                ReturnNumber = await GenerateReturnNumberAsync(ct),
                Type = request.Type,
                Status = SalesReturnStatus.Completed,
                Reason = request.Reason.Trim(),
                Note = request.Note?.Trim(),
                CreatedByUserId = userId,
                CompletedByUserId = userId,
                CompletedAtUtc = DateTime.UtcNow
            };

            // =====================================================
            // 5. Build các dòng hàng trả
            // =====================================================
            foreach (var reqLine in request.Lines)
            {
                if (reqLine.Action == null)
                {
                    throw new InvalidOperationException(
                        $"Dòng OrderLineId={reqLine.OrderLineId} chưa chọn cách xử lý hàng trả.");
                }

                if (!Enum.IsDefined(typeof(SalesReturnLineAction), reqLine.Action.Value))
                {
                    throw new InvalidOperationException(
                        $"Action không hợp lệ ở dòng OrderLineId={reqLine.OrderLineId}.");
                }

                var orderLine = order.Lines.FirstOrDefault(x => x.Id == reqLine.OrderLineId && !x.IsDeleted)
                    ?? throw new InvalidOperationException($"Không tìm thấy dòng hàng #{reqLine.OrderLineId}.");

                if (reqLine.ReturnQuantity <= 0)
                {
                    throw new InvalidOperationException(
                        $"Số lượng trả phải > 0 ở dòng OrderLineId={reqLine.OrderLineId}.");
                }

                // =================================================
                // 5.1. Tính base qty chuẩn mức 2
                // - ưu tiên client truyền sẵn
                // - fallback theo multiplier snapshot của order line
                // =================================================
                var multiplier = orderLine.Multiplier <= 0 ? 1m : orderLine.Multiplier;

                var baseQty = reqLine.ReturnBaseQuantity.HasValue && reqLine.ReturnBaseQuantity.Value > 0
                    ? reqLine.ReturnBaseQuantity.Value
                    : reqLine.ReturnQuantity * multiplier;

                if (baseQty <= 0)
                {
                    throw new InvalidOperationException(
                        $"Base quantity không hợp lệ ở dòng OrderLineId={reqLine.OrderLineId}.");
                }

                // =================================================
                // 5.2. Validate theo qty bán để tránh input UI sai rõ ràng
                // =================================================
                var alreadyReturnedQty = await _salesReturns.GetReturnedQuantityByOrderLineAsync(orderLine.Id, ct);
                var returnableQty = orderLine.Quantity - alreadyReturnedQty;

                if (reqLine.ReturnQuantity > returnableQty)
                {
                    throw new InvalidOperationException(
                        $"Dòng #{orderLine.Id} chỉ còn được trả tối đa {returnableQty}, nhưng yêu cầu {reqLine.ReturnQuantity}.");
                }

                // =================================================
                // 5.3. Validate CHUẨN theo BASE QTY
                // Đây mới là rule quan trọng cho inventory movement
                // + FIFO valuation level 2
                // =================================================
                var alreadyReturnedBaseQty = await _salesReturns.GetReturnedBaseQuantityByOrderLineAsync(orderLine.Id, ct);
                var soldBaseQty = orderLine.Quantity * multiplier;
                var returnableBaseQty = soldBaseQty - alreadyReturnedBaseQty;

                if (baseQty > returnableBaseQty)
                {
                    throw new InvalidOperationException(
                        $"Dòng #{orderLine.Id} chỉ còn được trả tối đa {returnableBaseQty} (base qty), nhưng yêu cầu {baseQty}.");
                }

                if (reqLine.RefundUnitAmount < 0)
                {
                    throw new InvalidOperationException(
                        $"Tiền hoàn trên đơn vị không hợp lệ ở dòng OrderLineId={reqLine.OrderLineId}.");
                }

                var lineTotal = reqLine.ReturnQuantity * reqLine.RefundUnitAmount;

                entity.Lines.Add(new SalesReturnLine
                {
                    StoreId = order.StoreId,
                    OrderLineId = orderLine.Id,
                    ProductId = orderLine.ProductId,
                    VariantId = orderLine.VariantId,
                    ItemName = orderLine.ItemName,
                    UnitName = orderLine.SellingUnitName ?? orderLine.UnitName,
                    ReturnQuantity = reqLine.ReturnQuantity,
                    ReturnBaseQuantity = baseQty,
                    RefundUnitAmount = reqLine.RefundUnitAmount,
                    RefundLineTotal = lineTotal,
                    Action = reqLine.Action.Value,
                    Reason = reqLine.Reason
                });
            }

            // =====================================================
            // 6. Build các payment refund
            // =====================================================
            foreach (var reqPay in request.Payments)
            {
                if (reqPay.Amount <= 0)
                    throw new InvalidOperationException("Tiền hoàn phải > 0.");

                entity.Payments.Add(new SalesReturnPayment
                {
                    StoreId = order.StoreId,
                    Method = reqPay.Method,
                    Amount = reqPay.Amount,
                    ReferenceCode = reqPay.ReferenceCode,
                    Provider = reqPay.Provider,
                    Note = reqPay.Note,
                    PaidAtUtc = DateTime.UtcNow
                });
            }

            entity.ReturnSubtotal = entity.Lines.Sum(x => x.RefundLineTotal);
            entity.RefundTotal = entity.Payments.Sum(x => x.Amount);
            entity.DepositRestoredTotal = request.DepositRefundAmount;
            if (order.DepositAmount > 0 && entity.RefundTotal + entity.DepositRestoredTotal > Math.Max(0, entity.ReturnSubtotal - order.BalanceDue))
                throw new GaoApp.Application.Common.Exceptions.BusinessRuleException("Tiền hoàn vượt giá trị hàng trả sau khi trừ công nợ.");

            // =====================================================
            // 7. Lưu phiếu trước để có ID
            // =====================================================
            await _salesReturns.AddAsync(entity, ct);
            await _salesReturns.SaveChangesAsync(ct);
            if (_receivables != null) await _receivables.PostReturnAsync(order, entity, ct);
            if (_deposits != null) await _deposits.RestoreReturnAsync(order, entity, ct);

            // =====================================================
            // 8. Chỉ các dòng Restock mới được:
            //    - tạo inventory movement inbound
            //    - mirror valuation entry từ source fragments gốc
            //
            //    NoRestock:
            //    - vẫn có SalesReturnLine
            //    - vẫn có thể refund tiền
            //    - KHÔNG nhập kho
            //    - KHÔNG mirror valuation
            // =====================================================
            var persistedLines = entity.Lines.ToList();
            if (_legalEntityReversalService is not
                IOrderLegalEntitySalesReturnBatchService legalEntityBatchService)
            {
                throw new InvalidOperationException(
                    "Sales return LegalEntity reversal implementation does not support operation-wide batch planning.");
            }

            var legalEntityBatch =
                await legalEntityBatchService.PrepareSalesReturnBatchAsync(
                    order,
                    entity,
                    persistedLines,
                    ct);
            var legacyPlansByLineId =
                new Dictionary<int, LegacyRestockPlan>();

            // Complete every unhandled legacy Restock plan before the
            // operation-wide union is locked.
            foreach (var line in persistedLines)
            {
                if (line.Action != SalesReturnLineAction.Restock ||
                    legalEntityBatch.HandledLineIds.Contains(line.Id))
                {
                    continue;
                }

                var fragments = await _returnableValuationFragmentService
                    .GetForOrderLineAsync(
                        order.Id,
                        line.OrderLineId,
                        ct);
                if (fragments.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"OrderLine #{line.OrderLineId} không còn source valuation fragment nào để reverse.");
                }

                var allocations = _returnCostAllocator.Allocate(
                    fragments,
                    line.ReturnBaseQuantity);
                if (allocations.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"SalesReturnLine #{line.Id} không tạo được allocation cost.");
                }

                var allocatedTotalQty = allocations.Sum(x => x.Quantity);
                if (allocatedTotalQty != line.ReturnBaseQuantity)
                {
                    throw new InvalidOperationException(
                        $"SalesReturnLine #{line.Id} allocation qty không khớp. " +
                        $"Expected={line.ReturnBaseQuantity}, Actual={allocatedTotalQty}");
                }

                legacyPlansByLineId.Add(
                    line.Id,
                    new LegacyRestockPlan
                    {
                        Line = line,
                        Allocations = allocations,
                        Fragments = fragments
                    });
            }

            var balanceKeys = legalEntityBatch.LockKeys
                .Concat(legacyPlansByLineId.Values.SelectMany(x => x.Fragments.Select(fragment =>
                    new InventoryPostingLockKey(
                        order.StoreId,
                        fragment.WarehouseId,
                        fragment.ProductVariantId))))
                .Distinct()
                .ToList();
            if (balanceKeys.Count > 0)
            {
                await _inventoryMovementService.PreLockBalancesAsync(
                    balanceKeys,
                    ct);
            }

            // Apply in the persisted SalesReturnLine order after the one
            // operation-wide pre-lock.
            foreach (var line in persistedLines)
            {
                if (legalEntityBatch.HandledLineIds.Contains(line.Id))
                {
                    await legalEntityBatchService
                        .ApplyPreparedSalesReturnLineAsync(
                            legalEntityBatch,
                            line.Id,
                            ct);
                    continue;
                }

                if (!legacyPlansByLineId.TryGetValue(
                        line.Id,
                        out var legacyPlan))
                {
                    // Legacy NoRestock: persisted return line only.
                    continue;
                }

                var freshFragments = await _returnableValuationFragmentService.GetForOrderLineAsync(
                    order.Id, line.OrderLineId, ct);
                var allocations = _returnCostAllocator.Allocate(freshFragments, line.ReturnBaseQuantity);
                if (!legacyPlan.Allocations.Select(x => (x.SourceValuationEntryId, x.Quantity))
                        .SequenceEqual(allocations.Select(x => (x.SourceValuationEntryId, x.Quantity))) ||
                    freshFragments.Any(x => !balanceKeys.Contains(new InventoryPostingLockKey(
                        order.StoreId, x.WarehouseId, x.ProductVariantId))))
                    throw new InvalidOperationException("Restock source plan changed after locking; retry the operation.");
                var sourceById = freshFragments.ToDictionary(x => x.SourceValuationEntryId);
                line.LineCostTotal = allocations
                    .Sum(x => x.Quantity * x.UnitCost);
                line.UnitCostSnapshot = line.ReturnBaseQuantity > 0
                    ? Math.Round(
                        line.LineCostTotal / line.ReturnBaseQuantity,
                        6)
                    : 0m;
                line.IsProvisionalCost = allocations
                    .Any(x => x.IsProvisional);

                for (var i = 0; i < allocations.Count; i++)
                {
                    var allocation = allocations[i];
                    var sourceFragment = sourceById[allocation.SourceValuationEntryId];
                    if (sourceFragment.ProductVariantId != line.VariantId)
                        throw new InvalidOperationException("Restock source variant mismatch.");
                    var referenceSubKey =
                        $"RET:{line.Id}:ALLOC:{i + 1}:SRC:{allocation.SourceValuationEntryId}";
                    var movement = _inventoryMovementFactory.CreateSaleRefund(
                        sourceFragment.WarehouseId,
                        line.VariantId,
                        entity.Id,
                        line.Id,
                        allocation.Quantity,
                        allocation.UnitCost,
                        entity.Reason,
                        DateTime.UtcNow,
                        referenceSubKey,
                        allocation.SourceValuationEntryId,
                        allocation.SourceReferenceSubKey);

                    await _inventoryMovementService.CreateAsync(
                        movement,
                        ct);
                }
            }

            // Reversal allocation phải được persist trước để sync invoice đọc được
            // tổng đã trả tuyệt đối theo từng HKD. Tất cả vẫn nằm trong transaction ngoài.
            await _salesReturns.SaveChangesAsync(ct);
            await _draftInvoiceReturnSyncService.SyncAfterReturnAsync(
                order.Id,
                entity.Id,
                ct);

            // =====================================================
            // 9. Ghi refund vào ca hiện tại
            //    - cộng tiền theo từng payment
            //    - RefundCount chỉ tăng 1 lần nếu phiếu có hoàn tiền
            // =====================================================
            var cashRefundAmount = entity.Payments
     .Where(x => x.Method == PaymentMethod.Cash)
     .Sum(x => x.Amount);

            if (cashRefundAmount > 0)
            {
                currentShift.RecalcExpected();

                if (currentShift.ClosingCashExpected < cashRefundAmount)
                {
                    throw new InvalidOperationException(
                        $"Tiền mặt trong ca không đủ để hoàn tiền. " +
                        $"Hiện có {currentShift.ClosingCashExpected:n0}đ, cần hoàn {cashRefundAmount:n0}đ.");
                }
            }
            foreach (var payment in entity.Payments)
            {
                currentShift.AddRefundAmount(payment.Amount, payment.Method);
            }

            if (entity.Payments.Count > 0)
            {
                currentShift.IncreaseRefundCount();
            }

            // =====================================================
            // 10. Nếu sau phiếu này order đã refund đủ tiền thì set PaymentStatus
            // =====================================================
            if (entity.RefundTotal + entity.DepositRestoredTotal > 0)
            {
                var totalRefundedAfterThis = alreadyRefunded + entity.RefundTotal + entity.DepositRestoredTotal;

                if (totalRefundedAfterThis >= order.PaidTotal)
                {
                    order.PaymentStatus = PaymentStatus.Refunded;
                }
            }

            // =====================================================
            // 11. Gắn note cho order để dễ tra cứu nhanh
            // =====================================================
            var returnNote =
     $"[RETURN/REFUND - {DateTime.Now:dd/MM/yyyy HH:mm:ss}] {entity.ReturnNumber} - {entity.Reason}";

            order.Note = string.IsNullOrWhiteSpace(order.Note)
                ? returnNote
                : $"{order.Note}{Environment.NewLine}{returnNote}";

            // Trừ tích lũy theo đúng các dòng hàng trả có đủ điều kiện tích điểm.
            // Chỉ trừ những dòng trước đó thuộc nhóm được tích.
            await ApplyRewardDeductionForSalesReturnAsync(order, entity, ct);

            await _salesReturns.SaveChangesAsync(ct);

            // =====================================================
            // 12. POS audit log
            // =====================================================
            await _posAuditLogs.AddAsync(new POSAuditLog
            {
                StoreId = order.StoreId,
                Action = "RETURN_COMPLETED",
                OrderId = order.Id,
                UserId = userId,
                Note = entity.Reason,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    entity.Id,
                    entity.ReturnNumber,
                    entity.Type,
                    entity.ReturnSubtotal,
                    entity.RefundTotal,
                    CurrentShiftId = currentShift.Id,
                    CurrentShiftCode = currentShift.ShiftCode
                })
            }, ct);

            await _posAuditLogs.SaveChangesAsync(ct);

            // =====================================================
            // 13. Audit log tổng quát
            // =====================================================
            await _auditLogService.WriteAsync(new WriteAuditLogRequest
            {
                Module = AuditModuleType.Orders,
                ActionType = AuditActionType.ReturnComplete,
                EntityName = nameof(SalesReturn),
                EntityId = entity.Id.ToString(),
                EntityDisplay = entity.ReturnNumber,
                Summary = $"Hoàn/trả cho order {order.OrderNumber} - phiếu {entity.ReturnNumber}",
                NewValuesJson = JsonSerializer.Serialize(new
                {
                    entity.OrderId,
                    entity.POSShiftId,
                    entity.Type,
                    entity.Status,
                    entity.ReturnSubtotal,
                    entity.RefundTotal,
                    entity.Reason
                }),
                IsSuccess = true
            }, ct);

            await _uow.CommitTransactionAsync(ct);

            return await GetByIdAsync(entity.Id, ct)
                ?? throw new InvalidOperationException("Không đọc được phiếu vừa tạo.");
        }
        catch
        {
            await _uow.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<SalesReturnDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var entity = await _salesReturns.GetByIdWithDetailsAsync(id, ct);
        return entity == null ? null : Map(entity);
    }

    public async Task<List<SalesReturnDto>> GetByOrderIdAsync(int orderId, CancellationToken ct = default)
    {
        var items = await _salesReturns.GetByOrderIdAsync(orderId, ct);
        return items.Select(Map).ToList();
    }

    public async Task<OrderReturnEligibilityDto> GetEligibilityAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithDetailsAsync(orderId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy order.");

        var refundedTotal = await _salesReturns.GetRefundedTotalByOrderAsync(order.Id, ct);

        var result = new OrderReturnEligibilityDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            GrandTotal = order.GrandTotal,
            PaidTotal = order.PaidTotal,
            RefundedTotal = refundedTotal,
            DepositRefundable = _deposits != null ? await _deposits.GetReturnableAsync(order, ct) : 0,
            IsCreditSale = order.IsCreditSale,
            BalanceDue = order.BalanceDue,
            RefundableRemaining = order.PaidTotal - refundedTotal
        };

        foreach (var line in order.Lines.Where(x => !x.IsDeleted))
        {
            var returnedQty = await _salesReturns.GetReturnedQuantityByOrderLineAsync(line.Id, ct);
            var returnedBaseQty = await _salesReturns.GetReturnedBaseQuantityByOrderLineAsync(line.Id, ct);

            var multiplier = line.Multiplier <= 0 ? 1m : line.Multiplier;
            var soldBaseQty = line.Quantity * multiplier;

            var returnableQty = line.Quantity - returnedQty;
            var returnableBaseQty = soldBaseQty - returnedBaseQty;

            var suggestedUnitRefund = line.Quantity > 0
                ? (line.LineTotal / line.Quantity)
                : line.UnitPrice;

            result.Lines.Add(new OrderReturnEligibilityLineDto
            {
                OrderLineId = line.Id,
                VariantId = line.VariantId,
                ItemName = line.ItemName,
                UnitName = line.SellingUnitName ?? line.UnitName,
                SoldQuantity = line.Quantity,
                ReturnedQuantity = returnedQty,
                ReturnableQuantity = returnableQty < 0 ? 0 : returnableQty,
                UnitPrice = line.UnitPrice,
                LineDiscount = line.LineDiscount,
                SuggestedRefundUnitAmount = suggestedUnitRefund,
                Multiplier = multiplier

                // Nếu DTO của bạn sau này có thêm field base qty,
                // có thể gắn:
                // SoldBaseQuantity = soldBaseQty,
                // ReturnedBaseQuantity = returnedBaseQty,
                // ReturnableBaseQuantity = returnableBaseQty < 0 ? 0 : returnableBaseQty
            });
        }

        return result;
    }

    private static SalesReturnDto Map(SalesReturn entity)
    {
        return new SalesReturnDto
        {
            Id = entity.Id,
            ReturnNumber = entity.ReturnNumber,
            OrderId = entity.OrderId,
            OrderNumber = entity.Order?.OrderNumber,
            Type = entity.Type,
            Status = entity.Status,
            Reason = entity.Reason,
            Note = entity.Note,
            ReturnSubtotal = entity.ReturnSubtotal,
            RefundTotal = entity.RefundTotal,
            DepositRestoredTotal = entity.DepositRestoredTotal,
            CreatedAtUtc = entity.CreatedAtUtc,
            Lines = entity.Lines.Select(x => new SalesReturnLineDto
            {
                Id = x.Id,
                OrderLineId = x.OrderLineId,
                ItemName = x.ItemName,
                UnitName = x.UnitName,
                ReturnQuantity = x.ReturnQuantity,
                ReturnBaseQuantity = x.ReturnBaseQuantity,
                RefundUnitAmount = x.RefundUnitAmount,
                RefundLineTotal = x.RefundLineTotal,
                Action = x.Action
            }).ToList(),
            Payments = entity.Payments.Select(x => new SalesReturnPaymentDto
            {
                Id = x.Id,
                Method = x.Method.ToString(),
                Amount = x.Amount,
                ReferenceCode = x.ReferenceCode,
                Provider = x.Provider,
                PaidAtUtc = x.PaidAtUtc
            }).ToList()
        };
    }

    private int RequireStoreId()
        => _currentStore.StoreId;

    private int RequireUserId()
        => _currentUser.UserId ?? throw new InvalidOperationException("Phiên đăng nhập không hợp lệ.");

    private int RequireTerminalId()
        => _currentUser.TerminalId ?? throw new InvalidOperationException("Không xác định được terminal hiện tại.");

    /// <summary>
    /// Sinh mã phiếu return.
    /// Include a random suffix so separate returns in the same second cannot reuse the timestamp number.
    /// </summary>
    private Task<string> GenerateReturnNumberAsync(CancellationToken ct)
    {
        return Task.FromResult($"RTN-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid():N}"[..29]);
    }
    private async Task ApplyRewardDeductionForSalesReturnAsync(
    Order order,
    SalesReturn salesReturn,
    CancellationToken ct)
    {
        if (!order.CustomerId.HasValue || order.CustomerId.Value <= 0)
            return;

        if (salesReturn.Id <= 0)
            return;

        var existed = await _rewardLedgers.HasLedgerForSalesReturnAsync(
            salesReturn.Id,
            CustomerRewardLedgerType.ReturnDeducted,
            ct);

        if (existed)
            return;

        var earned = await _rewardLedgers.GetOrderLedgerAmountAsync(order.Id, CustomerRewardLedgerType.SaleEarned, ct);
        if (earned <= 0) return;
        var calculation = await _orderRewardCalculator.CalculateForReturnAsync(order.Id, ct);

        var rewardableLines = calculation.Lines
            .Where(x => x.IsRewardable && x.Quantity > 0)
            .ToDictionary(x => x.OrderLineId);

        var deductAmount = salesReturn.Lines
            .Where(x => rewardableLines.ContainsKey(x.OrderLineId))
            .Sum(x => Math.Round(rewardableLines[x.OrderLineId].RewardableAmount
                * x.ReturnQuantity / rewardableLines[x.OrderLineId].Quantity, 2, MidpointRounding.AwayFromZero));
        var alreadyDeducted = await _rewardLedgers.GetOrderLedgerAmountAsync(order.Id, CustomerRewardLedgerType.ReturnDeducted, ct);
        deductAmount = Math.Min(deductAmount, Math.Max(0, earned + alreadyDeducted));

        if (deductAmount <= 0)
            return;

        await _rewardLedgers.AddAsync(new CustomerRewardLedger
        {
            StoreId = order.StoreId,
            CustomerId = order.CustomerId.Value,
            Type = CustomerRewardLedgerType.ReturnDeducted,
            Amount = -deductAmount,
            OrderId = order.Id,
            SalesReturnId = salesReturn.Id,
            ReferenceCode = $"RETURN_{salesReturn.Id}",
            Description = $"Trừ tích lũy do trả hàng phiếu {salesReturn.ReturnNumber}: {deductAmount:N0}đ"
        }, ct);
    }
}
