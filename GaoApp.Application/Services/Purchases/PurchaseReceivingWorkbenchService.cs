using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

public sealed class PurchaseReceivingWorkbenchService : IPurchaseReceivingWorkbenchService
{
    public const int HeartbeatSeconds = 30;
    public const int LeaseSeconds = 120;

    private readonly IPurchaseReceivingWorkbenchRepository _repository;
    private readonly IStockDocumentRepository _stockDocuments;
    private readonly IDocumentNumberSequenceRepository _numbers;
    private readonly IBarcodeLookupService _barcodes;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;

    public PurchaseReceivingWorkbenchService(
        IPurchaseReceivingWorkbenchRepository repository,
        IStockDocumentRepository stockDocuments,
        IDocumentNumberSequenceRepository numbers,
        IBarcodeLookupService barcodes,
        ITenantContext tenant,
        ICurrentUser user)
    {
        _repository = repository;
        _stockDocuments = stockDocuments;
        _numbers = numbers;
        _barcodes = barcodes;
        _tenant = tenant;
        _user = user;
    }

    public async Task<PurchaseReceivingWorkbenchDto> StartOrResumeAsync(
        int purchaseOrderId, CancellationToken ct = default)
    {
        var storeId = RequireStore();
        var userId = RequireUser();
        await _repository.BeginTransactionAsync(ct);
        try
        {
            var order = await _repository.LockAndGetPurchaseOrderAsync(storeId, purchaseOrderId, ct)
                ?? throw new BusinessRuleException("Đơn đặt hàng không tồn tại.");
            EnsureReceivable(order);
            var existing = await _repository.GetEditableForPurchaseOrderAsync(storeId, order.Id, ct);
            if (existing is not null)
            {
                if (existing.ReceivingSessionState != ReceivingSessionState.Active)
                    throw new BusinessRuleException(
                        $"[EDITABLE_RECEIPT_EXISTS] Đơn đặt hàng đã có phiếu {existing.DocumentNo} đang chỉnh sửa.");
                Guid? resumedToken = null;
                var hasLiveLease = existing.ReceivingLeaseToken.HasValue &&
                    existing.ReceivingLeaseExpiresAtUtc > DateTime.UtcNow;
                if (!hasLiveLease || existing.ReceivingOwnerUserId == userId)
                {
                    Acquire(existing, userId, allowExpiredTakeover: true);
                    resumedToken = existing.ReceivingLeaseToken;
                    PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
                        existing, PurchaseReceiptAuditEventType.ReceivingSessionResumed);
                    await _stockDocuments.SaveChangesAsync(ct);
                }
                await _repository.CommitTransactionAsync(ct);
                return await GetOwnedAsync(existing.Id, resumedToken, ct);
            }

            var now = DateTime.UtcNow;
            var next = await _numbers.GetNextNumberAsync(
                storeId, DocumentNumberSequenceType.StockReceipt, now, ct);
            var document = new StockDocument
            {
                StoreId = storeId,
                DocumentNo = $"NK-{now:yyyyMMdd}-{next:D4}",
                DocumentTitle = $"Nhận hàng {order.OrderNumber}",
                Type = StockDocumentType.Receipt,
                Status = StockDocumentStatus.Draft,
                ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
                PurchaseOrderId = order.Id,
                PurchaseOrder = order,
                WarehouseId = order.ExpectedWarehouseId,
                SupplierId = order.SupplierId,
                DocumentDate = now,
                ReceivingSessionState = ReceivingSessionState.Active,
                ReceivingOwnerUserId = userId,
                ReceivingLeaseToken = Guid.NewGuid(),
                ReceivingLeaseExpiresAtUtc = now.AddSeconds(LeaseSeconds),
                ReceivingLastSavedAtUtc = now,
                ReceivingRevision = 1
            };
            await _repository.AddDocumentAsync(document, ct);
            PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
                document, PurchaseReceiptAuditEventType.ReceivingSessionStarted);
            await _stockDocuments.SaveChangesAsync(ct);
            await _repository.CommitTransactionAsync(ct);
            return await GetOwnedAsync(document.Id, document.ReceivingLeaseToken, ct);
        }
        catch
        {
            await _repository.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<PurchaseReceivingWorkbenchDto> GetAsync(
        int stockDocumentId, CancellationToken ct = default)
    {
        var storeId = RequireStore();
        var document = await _repository.GetDocumentAsync(storeId, stockDocumentId, tracking: false, ct)
            ?? throw new BusinessRuleException("Phiếu nhận hàng không tồn tại.");
        return await MapAsync(document, includeLease: false, ct);
    }

    public Task<PurchaseReceivingWorkbenchDto> AcquireAsync(
        int stockDocumentId, PurchaseReceivingLeaseRequest request, CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureRowVersion(document, request.RowVersion);
            Acquire(document, RequireUser(), allowExpiredTakeover: true);
            PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
                document, PurchaseReceiptAuditEventType.ReceivingSessionResumed);
            await _stockDocuments.SaveChangesAsync(ct);
        }, includeLease: true, ct);

    public Task<PurchaseReceivingWorkbenchDto> HeartbeatAsync(
        int stockDocumentId, PurchaseReceivingLeaseRequest request, CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureRowVersion(document, request.RowVersion);
            EnsureLease(document, request.LeaseToken);
            document.ReceivingLeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(LeaseSeconds);
            await _stockDocuments.SaveChangesAsync(ct);
        }, includeLease: true, ct);

    public async Task<PurchaseReceivingWorkbenchDto> ScanAsync(
        int stockDocumentId, PurchaseReceivingScanRequest request, CancellationToken ct = default)
    {
        var result = await _barcodes.FindAsync(request.Barcode.Trim(), ct);
        if (result is null || !result.Found || !result.ProductUnitConversionId.HasValue)
            throw new BusinessRuleException("Không tìm thấy barcode đơn vị đang hoạt động.");
        return await AddAsync(stockDocumentId, new PurchaseReceivingAddRequest
        {
            CommandId = request.CommandId,
            ProductUnitConversionId = result.ProductUnitConversionId.Value,
            Quantity = 1m,
            LeaseToken = request.LeaseToken,
            RowVersion = request.RowVersion
        }, bulk: false, ct);
    }

    public Task<PurchaseReceivingWorkbenchDto> AddAsync(
        int stockDocumentId, PurchaseReceivingAddRequest request, bool bulk,
        CancellationToken ct = default)
        => MutateAsync(stockDocumentId, request.CommandId, request.LeaseToken,
            request.RowVersion, async document =>
            {
                var quantity = Normalize(request.Quantity);
                var conversion = await _repository.GetConversionAsync(
                    document.StoreId, request.ProductUnitConversionId, ct)
                    ?? throw new BusinessRuleException("Đơn vị nhận không hợp lệ hoặc đã ngừng hoạt động.");
                var order = document.PurchaseOrder
                    ?? throw new BusinessRuleException("Phiếu nhận không còn liên kết đơn đặt hàng.");
                var candidates = order.Lines.Where(x => !x.IsDeleted &&
                    x.ProductVariantId == conversion.ProductVariantId).ToArray();
                var poLine = ResolvePurchaseOrderLine(
                    candidates, conversion.Id, request.PurchaseOrderLineId);
                var allocation = poLine is null
                    ? ReceiptAllocationKind.OutsidePo
                    : ReceiptAllocationKind.PurchaseOrder;
                var line = document.Lines.FirstOrDefault(x => !x.IsDeleted &&
                    x.ProductUnitConversionId == conversion.Id &&
                    x.ReceiptAllocationKind == allocation &&
                    (allocation == ReceiptAllocationKind.OutsidePo
                        ? x.ProductVariantId == conversion.ProductVariantId
                        : x.PurchaseOrderLineId == poLine!.Id));
                var before = line?.Quantity ?? 0m;
                var beforeDeleted = line?.IsDeleted ?? true;
                var actionType = line is null
                    ? (bulk ? PurchaseReceivingActionType.Bulk : PurchaseReceivingActionType.Add)
                    : PurchaseReceivingActionType.Accumulate;
                if (line is null)
                {
                    var nextLineNo = await _stockDocuments.GetNextLineNoAsync(document.Id, ct);
                    line = CreateLine(document, conversion, poLine, quantity, nextLineNo);
                    document.Lines.Add(line);
                    await _stockDocuments.SaveChangesAsync(ct);
                }
                else
                {
                    line.Quantity = Normalize(checked(line.Quantity + quantity));
                    ApplyQuantity(line);
                    ResetOutsideDecision(line);
                }
                return new PendingAction(actionType, line, before, line.Quantity, beforeDeleted, false);
            }, ct);

    public Task<PurchaseReceivingWorkbenchDto> EditAsync(
        int stockDocumentId, int lineId, PurchaseReceivingEditRequest request,
        CancellationToken ct = default)
        => MutateAsync(stockDocumentId, request.CommandId, request.LeaseToken,
            request.RowVersion, document =>
            {
                var line = RequireLine(document, lineId);
                var before = line.Quantity;
                line.Quantity = Normalize(request.Quantity);
                ApplyQuantity(line);
                ResetOutsideDecision(line);
                return Task.FromResult(new PendingAction(
                    PurchaseReceivingActionType.Edit, line, before, line.Quantity, false, false));
            }, ct);

    public Task<PurchaseReceivingWorkbenchDto> RemoveAsync(
        int stockDocumentId, int lineId, PurchaseReceivingCommandRequest request,
        CancellationToken ct = default)
        => MutateAsync(stockDocumentId, request.CommandId, request.LeaseToken,
            request.RowVersion, document =>
            {
                var line = RequireLine(document, lineId);
                line.IsDeleted = true;
                line.DeletedAtUtc = DateTime.UtcNow;
                line.DeletedBy = RequireUser();
                return Task.FromResult(new PendingAction(
                    PurchaseReceivingActionType.Remove, line, line.Quantity, line.Quantity, false, true));
            }, ct);

    public Task<PurchaseReceivingWorkbenchDto> UndoAsync(
        int stockDocumentId, PurchaseReceivingCommandRequest request,
        CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureCommand(request.CommandId);
            EnsureMutableSession(document, request.LeaseToken);
            if (await _repository.GetCommandAsync(document.StoreId, document.Id, request.CommandId, ct) is not null)
                return;
            EnsureRowVersion(document, request.RowVersion);
            var target = await _repository.GetLatestUndoableAsync(
                document.StoreId, document.Id, document.ReceivingRevision, ct)
                ?? throw new BusinessRuleException("Không có thao tác gần nhất để hoàn tác.");
            var line = document.Lines.FirstOrDefault(x => x.Id == target.StockDocumentLineId)
                ?? throw new BusinessRuleException("Không thể hoàn tác vì thành phần nhận không còn tồn tại.");
            if (line.Quantity != target.AfterQuantity || line.IsDeleted != target.AfterIsDeleted)
                throw new BusinessRuleException("Dữ liệu đã thay đổi; không thể hoàn tác thao tác cũ.");
            var beforeQuantity = line.Quantity;
            var beforeDeleted = line.IsDeleted;
            line.Quantity = target.BeforeQuantity;
            line.IsDeleted = target.BeforeIsDeleted;
            line.DeletedAtUtc = line.IsDeleted ? DateTime.UtcNow : null;
            line.DeletedBy = line.IsDeleted ? RequireUser() : null;
            if (!line.IsDeleted) ApplyQuantity(line);
            ResetOutsideDecision(line);
            ValidatePurchaseOrderAggregates(document);
            Touch(document);
            await _repository.AddActionAsync(CreateAction(
                document, line, request.CommandId, PurchaseReceivingActionType.Undo,
                beforeQuantity, line.Quantity, beforeDeleted, line.IsDeleted, target.Id), ct);
            PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
                document, PurchaseReceiptAuditEventType.ReceivingMutationUndone);
            await _stockDocuments.SaveChangesAsync(ct);
        }, includeLease: true, ct);

    public Task<PurchaseReceivingWorkbenchDto> DecideOutsideAsync(
        int stockDocumentId, int lineId, PurchaseReceivingOutsideDecisionRequest request,
        CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureRowVersion(document, request.RowVersion);
            if (document.Status != StockDocumentStatus.PendingApproval ||
                document.ReceivingSessionState != ReceivingSessionState.Frozen)
                throw new BusinessRuleException("Chỉ phiếu đang chờ duyệt mới xử lý được hàng ngoài đơn.");
            var line = RequireLine(document, lineId);
            if (line.ReceiptAllocationKind != ReceiptAllocationKind.OutsidePo ||
                line.OutsidePoDecisionStatus != OutsidePoDecisionStatus.Pending)
                throw new BusinessRuleException("Dòng này không còn ở trạng thái chờ xử lý ngoài đơn.");
            if (request.Note?.Length > 1000)
                throw new BusinessRuleException("Ghi chú không được vượt quá 1.000 ký tự.");
            line.OutsidePoDecisionStatus = request.Accept
                ? OutsidePoDecisionStatus.Accepted
                : OutsidePoDecisionStatus.Rejected;
            line.OutsidePoDecisionAtUtc = DateTime.UtcNow;
            line.OutsidePoDecisionByUserId = RequireUser();
            line.OutsidePoDecisionNote = request.Note?.Trim();
            if (!request.Accept)
            {
                line.IsDeleted = true;
                line.DeletedAtUtc = DateTime.UtcNow;
                line.DeletedBy = RequireUser();
            }
            Touch(document);
            PurchaseReceiptAuditEvidence.MarkWorkflowEvent(document,
                request.Accept ? PurchaseReceiptAuditEventType.OutsidePoAccepted :
                    PurchaseReceiptAuditEventType.OutsidePoRejected,
                reason: request.Note);
            await _stockDocuments.SaveChangesAsync(ct);
        }, includeLease: false, ct);

    public Task<PurchaseReceivingWorkbenchDto> FinishAsync(
        int stockDocumentId, PurchaseReceivingLeaseRequest request,
        CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureMutable(document, request.LeaseToken, request.RowVersion);
            var active = document.Lines.Where(x => !x.IsDeleted).ToArray();
            var unresolvedProvisional = document.ProvisionalItems.Any(x =>
                !x.IsDeleted && x.Status == StockDocumentProvisionalItemStatus.Unresolved);
            if (active.Length == 0 && !unresolvedProvisional)
                throw new BusinessRuleException("Phiếu chưa có hàng đã nhận.");
            if (!unresolvedProvisional &&
                !active.Any(x => x.ReceiptAllocationKind == ReceiptAllocationKind.PurchaseOrder))
                throw new BusinessRuleException("Phiếu phải có ít nhất một mặt hàng thuộc đơn đặt hàng.");
            foreach (var group in active.Where(x =>
                         x.ReceiptAllocationKind == ReceiptAllocationKind.PurchaseOrder)
                     .GroupBy(x => x.PurchaseOrderLineId!.Value))
            {
                foreach (var line in group)
                {
                    line.ShortageDisposition = PurchaseShortageDisposition.WaitForBackorder;
                    line.ShortageReason = null;
                }
            }
            ValidatePurchaseOrderAggregates(document);
            document.Status = StockDocumentStatus.PendingApproval;
            document.SubmittedAtUtc = DateTime.UtcNow;
            document.SubmittedByUserId = RequireUser();
            document.ReceivingSessionState = ReceivingSessionState.Frozen;
            document.ReceivingLeaseToken = null;
            document.ReceivingLeaseExpiresAtUtc = null;
            document.ReceivingLastSavedAtUtc = DateTime.UtcNow;
            PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
                document, PurchaseReceiptAuditEventType.ReceivingFinished);
            await _stockDocuments.SaveChangesAsync(ct);
        }, includeLease: false, ct);

    private async Task<PurchaseReceivingWorkbenchDto> MutateAsync(
        int stockDocumentId, Guid commandId, Guid? leaseToken, string? rowVersion,
        Func<StockDocument, Task<PendingAction>> mutation, CancellationToken ct)
        => await WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureCommand(commandId);
            EnsureMutableSession(document, leaseToken);
            if (await _repository.GetCommandAsync(document.StoreId, document.Id, commandId, ct) is not null)
                return;
            EnsureRowVersion(document, rowVersion);
            var pending = await mutation(document);
            ValidatePurchaseOrderAggregates(document);
            Touch(document);
            await _repository.AddActionAsync(CreateAction(
                document, pending.Line, commandId, pending.ActionType,
                pending.BeforeQuantity, pending.AfterQuantity,
                pending.BeforeDeleted, pending.AfterDeleted), ct);
            PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
                document, PurchaseReceiptAuditEventType.ReceivingMutationApplied);
            await _stockDocuments.SaveChangesAsync(ct);
        }, includeLease: true, ct);

    private async Task<PurchaseReceivingWorkbenchDto> WithLockedDocumentAsync(
        int stockDocumentId, Func<StockDocument, Task> operation,
        bool includeLease, CancellationToken ct)
    {
        var storeId = RequireStore();
        await _repository.BeginTransactionAsync(ct);
        try
        {
            var document = await _repository.LockAndGetDocumentAsync(storeId, stockDocumentId, ct)
                ?? throw new BusinessRuleException("Phiếu nhận hàng không tồn tại.");
            await operation(document);
            var lease = includeLease ? document.ReceivingLeaseToken : null;
            await _repository.CommitTransactionAsync(ct);
            return await GetOwnedAsync(document.Id, lease, ct);
        }
        catch
        {
            await _repository.RollbackTransactionAsync(ct);
            throw;
        }
    }

    private async Task<PurchaseReceivingWorkbenchDto> GetOwnedAsync(
        int id, Guid? leaseToken, CancellationToken ct)
    {
        var document = await _repository.GetDocumentAsync(RequireStore(), id, tracking: false, ct)
            ?? throw new BusinessRuleException("Phiếu nhận hàng không tồn tại.");
        var dto = await MapAsync(document, includeLease: leaseToken.HasValue, ct);
        dto.LeaseToken = leaseToken;
        return dto;
    }

    private async Task<PurchaseReceivingWorkbenchDto> MapAsync(
        StockDocument document, bool includeLease, CancellationToken ct)
    {
        var order = document.PurchaseOrder
            ?? throw new BusinessRuleException("Phiếu nhận không còn liên kết đơn đặt hàng.");
        var active = document.Lines.Where(x => !x.IsDeleted).OrderByDescending(x => x.Id).ToArray();
        var latest = await _repository.GetLatestUndoableAsync(
            document.StoreId, document.Id, document.ReceivingRevision, ct);
        return new PurchaseReceivingWorkbenchDto
        {
            StockDocumentId = document.Id,
            PurchaseOrderId = order.Id,
            PurchaseOrderNumber = order.OrderNumber,
            ReceiptNumber = document.DocumentNo,
            Status = document.Status,
            SessionState = document.ReceivingSessionState,
            OwnerUserId = document.ReceivingOwnerUserId,
            LeaseToken = includeLease ? document.ReceivingLeaseToken : null,
            LeaseExpiresAtUtc = document.ReceivingLeaseExpiresAtUtc,
            LastSavedAtUtc = document.ReceivingLastSavedAtUtc,
            Revision = document.ReceivingRevision,
            RowVersion = Convert.ToBase64String(document.RowVersion ?? []),
            HasPendingOutsidePo = active.Any(x =>
                x.ReceiptAllocationKind == ReceiptAllocationKind.OutsidePo &&
                x.OutsidePoDecisionStatus == OutsidePoDecisionStatus.Pending),
            CanUndo = latest is not null,
            ProvisionalItems = document.ProvisionalItems.Where(x => !x.IsDeleted)
                .OrderByDescending(x => x.Id)
                .Select(x => new StockDocumentProvisionalItemDto
                {
                    Id = x.Id,
                    Name = x.NameSnapshot,
                    RawBarcode = x.RawBarcodeSnapshot,
                    UnitId = x.UnitId,
                    UnitName = x.UnitNameSnapshot,
                    Quantity = x.Quantity,
                    Note = x.Note,
                    Status = x.Status,
                    ResolvedStockDocumentLineId = x.ResolvedStockDocumentLineId,
                    RawBarcodeRemembered = x.RawBarcodeRemembered,
                    RowVersion = Convert.ToBase64String(x.RowVersion ?? [])
                }).ToList(),
            UnresolvedProvisionalCount = document.ProvisionalItems.Count(x =>
                !x.IsDeleted && x.Status == StockDocumentProvisionalItemStatus.Unresolved),
            Components = active.Select(x => new PurchaseReceivingComponentDto
            {
                Id = x.Id,
                ProductVariantId = x.ProductVariantId,
                ProductUnitConversionId = x.ProductUnitConversionId ?? 0,
                PurchaseOrderLineId = x.PurchaseOrderLineId,
                ProductName = x.ProductNameSnapshot,
                UnitName = x.UnitNameSnapshot ?? string.Empty,
                BaseUnitName = x.ProductVariant.Product.BaseUnit.Name,
                IsBaseUnit = x.UnitId == x.ProductVariant.Product.BaseUnitId,
                Quantity = x.Quantity,
                Factor = x.Factor,
                BaseQuantity = x.BaseQuantity,
                AllocationKind = x.ReceiptAllocationKind,
                OutsideStatus = x.OutsidePoDecisionStatus
            }).ToList(),
            Progress = order.Lines.Where(x => !x.IsDeleted && x.ProductVariantId.HasValue)
                .OrderBy(x => x.LineNo)
                .Select(x =>
                {
                    var current = active.Where(l => l.PurchaseOrderLineId == x.Id)
                        .Sum(l => l.BaseQuantity);
                    var aggregate = PurchaseReceivingAggregatePolicy.Calculate(
                        x.OrderedQuantity, x.ConversionFactor, x.ReceivedQuantity,
                        x.ShortClosedQuantity, 0m,
                        active.Where(l => l.PurchaseOrderLineId == x.Id)
                            .Select(l => new PurchaseReceivingComponentQuantity(
                                l.Quantity, l.Factor,
                                x.UnitId.HasValue && l.UnitId == x.UnitId)));
                    return new PurchaseReceivingProgressDto
                    {
                        PurchaseOrderLineId = x.Id,
                        ProductVariantId = x.ProductVariantId!.Value,
                        ProductUnitConversionId = x.ProductUnitConversionId,
                        ProductName = x.ProductNameSnapshot,
                        OrderedUnitName = x.UnitNameSnapshot,
                        BaseUnitName = x.ProductVariant!.Product.BaseUnit.Name,
                        OrderedQuantity = x.OrderedQuantity,
                        OrderedBaseQuantity = aggregate.OrderedBaseQuantity,
                        ConfirmedQuantity = x.ReceivedQuantity,
                        ConfirmedBaseQuantity = aggregate.ConfirmedBaseQuantity,
                        CurrentReceiptBaseQuantity = current,
                        RemainingBaseQuantity = aggregate.RemainingBaseQuantity,
                        ProjectedOverdeliveryBaseQuantity = aggregate.ProjectedOverdeliveryAfterReceipt
                    };
                }).ToList()
        };
    }

    private static PurchaseOrderLine? ResolvePurchaseOrderLine(
        IReadOnlyCollection<PurchaseOrderLine> candidates,
        int receivedConversionId,
        int? requestedPurchaseOrderLineId)
    {
        if (requestedPurchaseOrderLineId.HasValue)
        {
            var requested = candidates.SingleOrDefault(x => x.Id == requestedPurchaseOrderLineId.Value)
                ?? throw new BusinessRuleException(
                    "Dòng phân bổ không thuộc sản phẩm đang nhận trong đơn đặt hàng.");
            return requested;
        }

        var exactUnitMatches = candidates
            .Where(x => x.ProductUnitConversionId == receivedConversionId)
            .ToArray();
        if (exactUnitMatches.Length == 1) return exactUnitMatches[0];
        if (exactUnitMatches.Length > 1)
            throw new BusinessRuleException(
                "Đơn đặt hàng có nhiều dòng trùng cùng sản phẩm và đơn vị. Vui lòng kiểm tra lại đơn.");

        // Một dòng PO duy nhất có thể nhận bằng đơn vị quy đổi khác của cùng sản phẩm.
        // Khi có nhiều dòng, giao diện phải gửi dòng đích rõ ràng; nếu không hàng được
        // giữ là Ngoài PO để quản lý quyết định, tránh phân bổ sai.
        return candidates.Count == 1 ? candidates.Single() : null;
    }

    private static StockDocumentLine CreateLine(
        StockDocument document, ProductUnitConversion conversion,
        PurchaseOrderLine? poLine, decimal quantity, int lineNo)
    {
        var product = conversion.ProductVariant;
        var line = new StockDocumentLine
        {
            StockDocument = document,
            StockDocumentId = document.Id,
            LineNo = lineNo,
            ProductVariantId = product.Id,
            ProductVariant = product,
            PurchaseOrderLineId = poLine?.Id,
            PurchaseOrderLine = poLine,
            ReceiptAllocationKind = poLine is null
                ? ReceiptAllocationKind.OutsidePo : ReceiptAllocationKind.PurchaseOrder,
            OutsidePoDecisionStatus = poLine is null
                ? OutsidePoDecisionStatus.Pending : OutsidePoDecisionStatus.NotApplicable,
            ProductUnitConversionId = conversion.Id,
            ProductUnitConversion = conversion,
            UnitId = conversion.UnitId,
            Unit = conversion.Unit,
            UnitNameSnapshot = conversion.Unit.Name,
            Factor = conversion.Factor,
            Quantity = quantity,
            ProductNameSnapshot = string.IsNullOrWhiteSpace(product.ProductVariantName)
                ? product.Product.Name : product.ProductVariantName,
            SkuSnapshot = product.Sku,
            ShortageDisposition = poLine is null
                ? PurchaseShortageDisposition.None : PurchaseShortageDisposition.WaitForBackorder
        };
        ApplyQuantity(line);
        return line;
    }

    private static void ApplyQuantity(StockDocumentLine line)
    {
        line.BaseQuantity = PurchaseReceiptQuantityConversionPolicy.ToCanonical(
            line.Quantity, line.Factor);
        line.LineTotal = PurchasePricingPolicy.RoundMoney(line.Quantity * line.UnitCost);
    }

    private static void ResetOutsideDecision(StockDocumentLine line)
    {
        if (line.ReceiptAllocationKind != ReceiptAllocationKind.OutsidePo ||
            line.OutsidePoDecisionStatus != OutsidePoDecisionStatus.Accepted) return;
        line.OutsidePoDecisionStatus = OutsidePoDecisionStatus.Pending;
        line.OutsidePoDecisionAtUtc = null;
        line.OutsidePoDecisionByUserId = null;
        line.OutsidePoDecisionNote = null;
    }

    private static void ValidatePurchaseOrderAggregates(StockDocument document)
    {
        var order = document.PurchaseOrder
            ?? throw new BusinessRuleException("Phiếu nhận không còn liên kết đơn đặt hàng.");
        foreach (var group in document.Lines.Where(x =>
                     !x.IsDeleted &&
                     x.ReceiptAllocationKind == ReceiptAllocationKind.PurchaseOrder)
                 .GroupBy(x => x.PurchaseOrderLineId))
        {
            var purchaseOrderLine = order.Lines.SingleOrDefault(x =>
                !x.IsDeleted && x.Id == group.Key);
            if (purchaseOrderLine is null)
                throw new BusinessRuleException("Dòng nhận hàng không thuộc đơn đặt hàng hiện tại.");
            try
            {
                _ = PurchaseReceivingAggregatePolicy.Calculate(
                    purchaseOrderLine.OrderedQuantity,
                    purchaseOrderLine.ConversionFactor,
                    purchaseOrderLine.ReceivedQuantity,
                    purchaseOrderLine.ShortClosedQuantity,
                    0m,
                    group.Select(x => new PurchaseReceivingComponentQuantity(
                        x.Quantity,
                        x.Factor,
                        purchaseOrderLine.UnitId.HasValue && x.UnitId == purchaseOrderLine.UnitId)));
            }
            catch (PurchaseReceiptQuantityException ex)
            {
                throw new BusinessRuleException(ex.Message);
            }
        }
    }

    private static PurchaseReceivingAction CreateAction(
        StockDocument document, StockDocumentLine line, Guid commandId,
        PurchaseReceivingActionType type, decimal before, decimal after,
        bool beforeDeleted, bool afterDeleted, long? undoOf = null)
        => new()
        {
            StoreId = document.StoreId,
            StockDocumentId = document.Id,
            ReceivingRevision = document.ReceivingRevision,
            CommandId = commandId,
            ActionType = type,
            StockDocumentLineId = line.Id,
            ProductVariantId = line.ProductVariantId,
            ProductUnitConversionId = line.ProductUnitConversionId!.Value,
            PurchaseOrderLineId = line.PurchaseOrderLineId,
            ReceiptAllocationKind = line.ReceiptAllocationKind,
            BeforeQuantity = before,
            AfterQuantity = after,
            BeforeIsDeleted = beforeDeleted,
            AfterIsDeleted = afterDeleted,
            ActorUserId = document.ReceivingOwnerUserId ?? 0,
            OccurredAtUtc = DateTime.UtcNow,
            UndoOfActionId = undoOf
        };

    private static StockDocumentLine RequireLine(StockDocument document, int lineId)
        => document.Lines.FirstOrDefault(x => x.Id == lineId && !x.IsDeleted)
            ?? throw new BusinessRuleException("Thành phần nhận không tồn tại hoặc đã bị xóa.");

    private static decimal Normalize(decimal value)
    {
        try { return PurchaseReceiptQuantityConversionPolicy.NormalizeReceiptQuantity(value); }
        catch (PurchaseReceiptQuantityException ex) { throw new BusinessRuleException(ex.Message); }
    }

    private void EnsureMutable(StockDocument document, Guid? token, string? rowVersion)
    {
        EnsureRowVersion(document, rowVersion);
        EnsureMutableSession(document, token);
    }

    private void EnsureMutableSession(StockDocument document, Guid? token)
    {
        EnsureLease(document, token);
        if (document.Status is not (StockDocumentStatus.Draft or StockDocumentStatus.Rejected) ||
            document.ReceivingSessionState != ReceivingSessionState.Active)
            throw new BusinessRuleException("Phiếu không còn ở trạng thái nhận hàng có thể chỉnh sửa.");
    }

    private void EnsureLease(StockDocument document, Guid? token)
    {
        if (document.ReceivingOwnerUserId != RequireUser() || !token.HasValue ||
            document.ReceivingLeaseToken != token ||
            !document.ReceivingLeaseExpiresAtUtc.HasValue ||
            document.ReceivingLeaseExpiresAtUtc <= DateTime.UtcNow)
            throw new BusinessRuleException(
                "Quyền nhận hàng đã hết hạn hoặc đang thuộc một phiên khác. Vui lòng nhận lại quyền chỉnh sửa.");
    }

    private static void Acquire(
        StockDocument document, int userId, bool allowExpiredTakeover)
    {
        if (document.ReceivingSessionState != ReceivingSessionState.Active)
            throw new BusinessRuleException("Phiếu không còn ở phiên nhận hàng đang hoạt động.");
        var now = DateTime.UtcNow;
        var active = document.ReceivingLeaseToken.HasValue &&
            document.ReceivingLeaseExpiresAtUtc > now;
        var sameEmployee = active && document.ReceivingOwnerUserId == userId;
        if (active && !sameEmployee)
            throw new BusinessRuleException("Phiếu đang được nhận hàng ở một tab hoặc tài khoản khác.");
        if (!active && !allowExpiredTakeover)
            throw new BusinessRuleException("Phiên nhận hàng đã hết hạn.");
        document.ReceivingOwnerUserId = userId;
        document.ReceivingLeaseToken = sameEmployee
            ? document.ReceivingLeaseToken
            : Guid.NewGuid();
        document.ReceivingLeaseExpiresAtUtc = now.AddSeconds(LeaseSeconds);
        document.ReceivingLastSavedAtUtc ??= now;
    }

    private static void EnsureRowVersion(StockDocument document, string? posted)
    {
        if (string.IsNullOrWhiteSpace(posted))
            throw new BusinessRuleException("Thiếu phiên bản dữ liệu. Vui lòng tải lại phiếu.");
        byte[] expected;
        try { expected = Convert.FromBase64String(posted); }
        catch (FormatException) { throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ."); }
        if (!document.RowVersion.SequenceEqual(expected))
            throw new BusinessRuleException("Phiếu đã được cập nhật ở nơi khác. Vui lòng tải lại.");
    }

    private static void EnsureCommand(Guid commandId)
    {
        if (commandId == Guid.Empty)
            throw new BusinessRuleException("Thiếu mã lệnh idempotency.");
    }

    private static void Touch(StockDocument document)
    {
        document.ReceivingLastSavedAtUtc = DateTime.UtcNow;
        document.ReceivingLeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(LeaseSeconds);
    }

    private static void EnsureReceivable(PurchaseOrder order)
    {
        if (order.Status is not (PurchaseOrderStatus.Approved or
            PurchaseOrderStatus.SentToSupplier or PurchaseOrderStatus.PartiallyReceived))
            throw new BusinessRuleException("Đơn đặt hàng không còn ở trạng thái cho phép nhận hàng.");
        var catalogLines = order.Lines
            .Where(x => !x.IsDeleted && x.ProductVariantId.HasValue)
            .ToArray();
        if (catalogLines.Length != catalogLines
                .Select(x => new { x.ProductVariantId, x.ProductUnitConversionId })
                .Distinct().Count())
            throw new BusinessRuleException(
                "Đơn đặt hàng có nhiều dòng trùng cùng sản phẩm và đơn vị.");
        if (order.ExpectedWarehouse.LegalEntityId != order.LegalEntityId)
            throw new BusinessRuleException("HKD của kho nhận không khớp đơn đặt hàng.");
    }

    private int RequireStore()
        => _tenant.StoreId is > 0 and var id ? id :
            throw new InvalidOperationException("Current store context is unavailable.");

    private int RequireUser()
        => _user.UserId is > 0 and var id ? id :
            throw new BusinessRuleException("Không xác định được người đang nhận hàng.");

    private sealed record PendingAction(
        PurchaseReceivingActionType ActionType,
        StockDocumentLine Line,
        decimal BeforeQuantity,
        decimal AfterQuantity,
        bool BeforeDeleted,
        bool AfterDeleted);
}
