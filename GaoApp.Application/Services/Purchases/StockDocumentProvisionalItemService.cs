using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

public sealed partial class StockDocumentProvisionalItemService
    : IStockDocumentProvisionalItemService, IReceiptIntakeService
{
    private readonly IStockDocumentProvisionalItemRepository _repository;
    private readonly IAppUnitOfWork _unitOfWork;
    private readonly IProcurementCatalogService _catalog;
    private readonly IProductUnitConversionService _productUnits;
    private readonly IBarcodeLookupService _barcodes;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;
    private readonly IReceiptIntakeCatalog _intakeCatalog;

    public StockDocumentProvisionalItemService(
        IStockDocumentProvisionalItemRepository repository,
        IAppUnitOfWork unitOfWork,
        IProcurementCatalogService catalog,
        IProductUnitConversionService productUnits,
        IBarcodeLookupService barcodes,
        ITenantContext tenant,
        ICurrentUser user,
        IReceiptIntakeCatalog intakeCatalog)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _catalog = catalog;
        _productUnits = productUnits;
        _barcodes = barcodes;
        _tenant = tenant;
        _user = user;
        _intakeCatalog = intakeCatalog;
    }

    public async Task<ProvisionalReceivingStateDto> GetAsync(
        int stockDocumentId, CancellationToken ct = default)
    {
        var document = await _repository.GetDocumentAsync(RequireStore(), stockDocumentId, ct)
            ?? throw new BusinessRuleException("Phiếu nhận hàng không tồn tại.");
        return Map(document);
    }

    public Task<ProvisionalReceivingStateDto> CaptureAsync(
        int stockDocumentId, CaptureProvisionalItemRequest request,
        CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureCommand(request.CommandId);
            EnsureWarehouseAccess(document);
            var name = RequiredText(request.Name, 200, "Vui lòng nhập tên hàng thực nhận.");
            var rawBarcode = NormalizeOptional(request.RawBarcode, 256);
            var barcode = NormalizeBarcode(rawBarcode);
            var unitName = NormalizeOptional(request.UnitName, 100);
            var quantity = NormalizeQuantity(request.Quantity);
            var note = NormalizeOptional(request.Note, 500);
            var payloadHash = PayloadHash("capture", new
            {
                name, rawBarcode, request.UnitId, unitName, quantity, note
            });
            if (await IsReplayAsync(document, request.CommandId, payloadHash, ct))
                return;
            EnsureRowVersion(document.RowVersion, request.DocumentRowVersion, "Phiếu");

            var normalizedUnit = NormalizeIdentity(unitName);
            Unit? unit = null;
            if (request.UnitId is > 0)
            {
                unit = await _repository.GetUnitAsync(document.StoreId, request.UnitId.Value, ct)
                    ?? throw new BusinessRuleException("Đơn vị không tồn tại hoặc đã ngừng hoạt động.");
                unitName = unit.Name;
                normalizedUnit = NormalizeIdentity(unit.Name);
            }
            StockDocumentProvisionalItem? item = null;
            if (barcode is not null && (unit is not null || normalizedUnit is not null))
                item = await _repository.FindAccumulationTargetAsync(
                    document.StoreId, document.Id, barcode, unit?.Id, normalizedUnit, ct);

            var isNew = item is null;
            var before = item?.Quantity ?? quantity;
            var beforeRemoved = isNew;
            var beforeState = item is null ? null : Snapshot(item);
            var actionType = item is null
                ? PurchaseReceivingActionType.ProvisionalCapture
                : PurchaseReceivingActionType.ProvisionalAccumulate;
            if (item is null)
            {
                item = new StockDocumentProvisionalItem
                {
                    StoreId = document.StoreId,
                    StockDocumentId = document.Id,
                    StockDocument = document,
                    NameSnapshot = name,
                    RawBarcodeSnapshot = rawBarcode,
                    NormalizedBarcode = barcode,
                    UnitId = unit?.Id,
                    Unit = unit,
                    UnitNameSnapshot = unitName,
                    NormalizedUnitNameSnapshot = normalizedUnit,
                    Quantity = quantity,
                    Note = note,
                    Status = StockDocumentProvisionalItemStatus.Unresolved,
                    ReceivingRevision = document.ReceivingRevision
                };
                await _repository.AddItemAsync(item, ct);
                await _repository.SaveChangesAsync(ct);
                beforeState = Snapshot(item, StockDocumentProvisionalItemStatus.Removed);
            }
            else
            {
                item.Quantity = NormalizeQuantity(checked(item.Quantity + quantity));
                item.Note = note ?? item.Note;
            }

            Touch(document);
            await AddActionAsync(document, item, request.CommandId, payloadHash, actionType,
                before, item.Quantity, beforeRemoved, false, beforeState, Snapshot(item), null, ct);
            await AddAuditAsync(document, item, actionType == PurchaseReceivingActionType.ProvisionalCapture
                ? PurchaseReceiptAuditEventType.ProvisionalItemCaptured
                : PurchaseReceiptAuditEventType.ProvisionalItemChanged, ct);
            await _repository.SaveChangesAsync(ct);
        }, ct);

    public Task<ProvisionalReceivingStateDto> IncrementAsync(
        int stockDocumentId, int itemId, IncrementProvisionalItemRequest request,
        CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureCommand(request.CommandId);
            EnsureWarehouseAccess(document);
            var quantity = NormalizeQuantity(request.Quantity);
            var payloadHash = PayloadHash("increment", new { itemId, quantity });
            if (await IsReplayAsync(document, request.CommandId, payloadHash, ct))
                return;
            EnsureRowVersion(document.RowVersion, request.DocumentRowVersion, "Phiếu");
            var item = RequireUnresolved(document, itemId);
            EnsureRowVersion(item.RowVersion, request.ItemRowVersion, "Mặt hàng mới");
            var beforeState = Snapshot(item);
            var before = item.Quantity;
            item.Quantity = NormalizeQuantity(checked(item.Quantity + quantity));
            Touch(document);
            await AddActionAsync(document, item, request.CommandId, payloadHash,
                PurchaseReceivingActionType.ProvisionalAccumulate, before, item.Quantity,
                false, false, beforeState, Snapshot(item), null, ct);
            await AddAuditAsync(document, item, PurchaseReceiptAuditEventType.ProvisionalItemChanged, ct);
            await _repository.SaveChangesAsync(ct);
        }, ct);

    public Task<ProvisionalReceivingStateDto> EditAsync(
        int stockDocumentId, int itemId, EditProvisionalItemRequest request,
        CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureCommand(request.CommandId);
            EnsureWarehouseAccess(document);
            var payloadHash = PayloadHash("edit", new
            {
                itemId, request.Name, request.UnitId, request.UnitName,
                request.Quantity, request.Note
            });
            if (await IsReplayAsync(document, request.CommandId, payloadHash, ct))
                return;
            EnsureRowVersion(document.RowVersion, request.DocumentRowVersion, "Phiếu");
            var item = RequireUnresolved(document, itemId);
            EnsureRowVersion(item.RowVersion, request.ItemRowVersion, "Mặt hàng mới");
            var unit = request.UnitId is > 0
                ? await _repository.GetUnitAsync(document.StoreId, request.UnitId.Value, ct)
                : null;
            if (request.UnitId is > 0 && unit is null)
                throw new BusinessRuleException("Đơn vị không tồn tại hoặc đã ngừng hoạt động.");
            if (item.ProposedFactor.HasValue && (item.UnitId != unit?.Id ||
                NormalizeIdentity(item.UnitNameSnapshot) != NormalizeIdentity(unit?.Name ?? request.UnitName)))
                throw new BusinessRuleException("Quy cách đã được khai báo cùng tỷ lệ quy đổi. Hãy loại dòng và ghi nhận lại để đổi quy cách.");
            var before = item.Quantity;
            var beforeState = Snapshot(item);
            item.NameSnapshot = RequiredText(request.Name, 200, "Vui lòng nhập tên hàng thực nhận.");
            item.UnitId = unit?.Id;
            item.Unit = unit;
            item.UnitNameSnapshot = unit?.Name ?? NormalizeOptional(request.UnitName, 100);
            item.NormalizedUnitNameSnapshot = NormalizeIdentity(item.UnitNameSnapshot);
            item.Quantity = NormalizeQuantity(request.Quantity);
            item.Note = NormalizeOptional(request.Note, 500);
            Touch(document);
            await AddActionAsync(document, item, request.CommandId, payloadHash,
                PurchaseReceivingActionType.ProvisionalEdit, before, item.Quantity,
                false, false, beforeState, Snapshot(item), null, ct);
            await AddAuditAsync(document, item, PurchaseReceiptAuditEventType.ProvisionalItemChanged, ct);
            await _repository.SaveChangesAsync(ct);
        }, ct);

    public Task<ProvisionalReceivingStateDto> RemoveAsync(
        int stockDocumentId, int itemId, RemoveProvisionalItemRequest request,
        bool managerRemoval, CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureCommand(request.CommandId);
            if (managerRemoval) EnsureManagerAccess(document);
            else EnsureWarehouseAccess(document);
            var payloadHash = PayloadHash("remove", new { itemId, managerRemoval });
            if (await IsReplayAsync(document, request.CommandId, payloadHash, ct))
                return;
            EnsureRowVersion(document.RowVersion, request.DocumentRowVersion, "Phiếu");
            var item = RequireUnresolved(document, itemId);
            EnsureRowVersion(item.RowVersion, request.ItemRowVersion, "Mặt hàng mới");
            var beforeState = Snapshot(item);
            item.Status = StockDocumentProvisionalItemStatus.Removed;
            item.RemovedAtUtc = DateTime.UtcNow;
            item.RemovedByUserId = RequireUser();
            Touch(document);
            await AddActionAsync(document, item, request.CommandId, payloadHash,
                PurchaseReceivingActionType.ProvisionalRemove, item.Quantity, item.Quantity,
                false, true, beforeState, Snapshot(item), null, ct);
            await AddAuditAsync(document, item, PurchaseReceiptAuditEventType.ProvisionalItemRemoved, ct);
            await _repository.SaveChangesAsync(ct);
        }, ct);

    public Task<ProvisionalReceivingStateDto> UndoAsync(
        int stockDocumentId, ProvisionalReceivingMutationRequest request,
        CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureCommand(request.CommandId);
            EnsureWarehouseAccess(document);
            var payloadHash = PayloadHash("undo", new { document.ReceivingRevision });
            if (await IsReplayAsync(document, request.CommandId, payloadHash, ct))
                return;
            EnsureRowVersion(document.RowVersion, request.DocumentRowVersion, "Phiếu");
            var target = await _repository.GetLatestProvisionalUndoableAsync(
                document.StoreId, document.Id, document.ReceivingRevision, ct)
                ?? throw new BusinessRuleException("Không có thao tác hàng mới gần nhất để hoàn tác.");
            var item = document.ProvisionalItems.SingleOrDefault(x =>
                x.Id == target.StockDocumentProvisionalItemId)
                ?? throw new BusinessRuleException("Bằng chứng hàng mới không còn tồn tại.");
            if (item.Quantity != target.AfterQuantity ||
                (item.Status == StockDocumentProvisionalItemStatus.Removed) != target.AfterIsDeleted)
                throw new BusinessRuleException("Dữ liệu đã thay đổi; không thể hoàn tác thao tác cũ.");
            var before = item.Quantity;
            var beforeRemoved = item.Status == StockDocumentProvisionalItemStatus.Removed;
            var beforeState = Snapshot(item);
            RestoreSnapshot(item, target.BeforeProvisionalStateJson, target);
            Touch(document);
            await AddActionAsync(document, item, request.CommandId, payloadHash,
                PurchaseReceivingActionType.Undo, before, item.Quantity,
                beforeRemoved, item.Status == StockDocumentProvisionalItemStatus.Removed,
                beforeState, Snapshot(item), target.Id, ct);
            await AddAuditAsync(document, item, PurchaseReceiptAuditEventType.ProvisionalItemRestored, ct);
            await _repository.SaveChangesAsync(ct);
        }, ct);

    public Task<ProvisionalReceivingStateDto> LinkExistingAsync(
        int stockDocumentId, int itemId, ResolveProvisionalItemRequest request,
        bool canCreateBarcode, CancellationToken ct = default)
        => ResolveAsync(stockDocumentId, itemId, request.CommandId,
            request.DocumentRowVersion, request.ItemRowVersion,
            request.ProductVariantId, request.ProductUnitConversionId,
            request.RememberRawBarcode, canCreateBarcode,
            ProvisionalItemResolutionMethod.LinkExisting,
            PurchaseReceivingActionType.ProvisionalLinkExisting,
            PurchaseReceiptAuditEventType.ProvisionalItemResolvedExisting, ct);

    public Task<ProvisionalReceivingStateDto> QuickCreateAndResolveAsync(
        int stockDocumentId, int itemId, QuickCreateAndResolveProvisionalItemRequest request,
        bool canCreateUnit, bool canCreateBarcode, CancellationToken ct = default)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureCommand(request.CommandId);
            EnsureManagerAccess(document);
            var payloadHash = PayloadHash("quick-create", new
            {
                itemId, request.CategoryId, request.SupplierId, request.UnitId,
                request.NewUnitName, request.ProductName, request.RememberRawBarcode
            });
            if (await IsReplayAsync(document, request.CommandId, payloadHash, ct))
                return;
            EnsureRowVersion(document.RowVersion, request.DocumentRowVersion, "Phiếu");
            var item = RequireUnresolved(document, itemId);
            EnsureRowVersion(item.RowVersion, request.ItemRowVersion, "Mặt hàng mới");
            var supplierId = document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder
                ? document.PurchaseOrder?.SupplierId
                : document.SupplierId;
            if (!supplierId.HasValue)
                throw new BusinessRuleException("Phiếu chưa có nhà cung cấp hợp lệ để tạo sản phẩm.");
            if (document.ReceiptSource != PurchaseReceiptSource.PurchaseOrder &&
                request.SupplierId.HasValue && request.SupplierId != supplierId)
                throw new BusinessRuleException("Nhà cung cấp phải trùng nhà cung cấp của phiếu trực tiếp.");

            var created = await _catalog.CreateProductWithinTransactionAsync(new()
            {
                GenerateDefaultBarcode = !request.RememberRawBarcode || string.IsNullOrWhiteSpace(item.RawBarcodeSnapshot),
                Name = NormalizeOptional(request.ProductName, 200) ?? item.NameSnapshot,
                CategoryId = request.CategoryId,
                SupplierId = supplierId.Value,
                UnitId = request.UnitId ?? item.UnitId,
                NewUnitName = NormalizeOptional(request.NewUnitName, 200) ??
                    (item.UnitId.HasValue ? null : item.UnitNameSnapshot)
            }, canCreateUnit, ct);
            await ResolveCoreAsync(document, item, request.CommandId,
                created.ProductVariantId, created.ProductUnitConversionId,
                request.RememberRawBarcode, canCreateBarcode,
                ProvisionalItemResolutionMethod.QuickCreate,
                PurchaseReceivingActionType.ProvisionalQuickCreate,
                PurchaseReceiptAuditEventType.ProvisionalItemResolvedQuickCreate,
                payloadHash, ct);
        }, ct);

    private Task<ProvisionalReceivingStateDto> ResolveAsync(
        int stockDocumentId, int itemId, Guid commandId,
        string documentVersion, string itemVersion,
        int variantId, int conversionId, bool rememberBarcode, bool canCreateBarcode,
        ProvisionalItemResolutionMethod method, PurchaseReceivingActionType actionType,
        PurchaseReceiptAuditEventType auditType, CancellationToken ct)
        => WithLockedDocumentAsync(stockDocumentId, async document =>
        {
            EnsureCommand(commandId);
            EnsureManagerAccess(document);
            var payloadHash = PayloadHash("link", new
            {
                itemId, variantId, conversionId, rememberBarcode
            });
            if (await IsReplayAsync(document, commandId, payloadHash, ct))
                return;
            EnsureRowVersion(document.RowVersion, documentVersion, "Phiếu");
            var item = RequireUnresolved(document, itemId);
            EnsureRowVersion(item.RowVersion, itemVersion, "Mặt hàng mới");
            await ResolveCoreAsync(document, item, commandId, variantId, conversionId,
                rememberBarcode, canCreateBarcode, method, actionType, auditType,
                payloadHash, ct);
        }, ct);

    private async Task ResolveCoreAsync(
        StockDocument document, StockDocumentProvisionalItem item, Guid commandId,
        int variantId, int conversionId, bool rememberBarcode, bool canCreateBarcode,
        ProvisionalItemResolutionMethod method, PurchaseReceivingActionType actionType,
        PurchaseReceiptAuditEventType auditType, string payloadHash, CancellationToken ct)
    {
        var beforeState = Snapshot(item);
        var conversion = await _repository.GetConversionAsync(
            document.StoreId, variantId, conversionId, ct)
            ?? throw new BusinessRuleException("Sản phẩm hoặc đơn vị quy đổi không hợp lệ.");
        if (item.ProposedFactor.HasValue &&
            (conversion.Factor != item.ProposedFactor.Value ||
             (item.ProposedProductVariantId.HasValue && item.ProposedProductVariantId != variantId) ||
             (item.ProposedBaseUnitId.HasValue && item.ProposedBaseUnitId != conversion.ProductVariant.Product.BaseUnitId)))
            throw new BusinessRuleException("Sản phẩm hoặc quy đổi không khớp khai báo của nhân viên.");
        if (item.UnitId.HasValue && item.UnitId != conversion.UnitId)
            throw new BusinessRuleException(
                "Đơn vị danh mục không khớp đơn vị vật lý đã ghi nhận.");
        if (!item.UnitId.HasValue &&
            !string.IsNullOrWhiteSpace(item.NormalizedUnitNameSnapshot) &&
            item.NormalizedUnitNameSnapshot != NormalizeIdentity(conversion.Unit.Name))
            throw new BusinessRuleException(
                "Tên đơn vị danh mục không khớp đơn vị vật lý đã ghi nhận.");
        PurchaseOrderLine? poLine = null;
        var allocation = ReceiptAllocationKind.Direct;
        if (document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder)
        {
            var candidates = document.PurchaseOrder?.Lines.Where(x =>
                !x.IsDeleted && x.ProductVariantId == variantId).ToArray() ?? [];
            if (candidates.Length > 1)
                throw new BusinessRuleException("Có nhiều dòng PO cùng sản phẩm; không thể phân bổ an toàn.");
            poLine = candidates.SingleOrDefault();
            allocation = poLine is null
                ? ReceiptAllocationKind.OutsidePo : ReceiptAllocationKind.PurchaseOrder;
        }

        var line = document.Lines.FirstOrDefault(x => !x.IsDeleted &&
            x.ProductUnitConversionId == conversion.Id &&
            x.ReceiptAllocationKind == allocation &&
            (allocation == ReceiptAllocationKind.PurchaseOrder
                ? x.PurchaseOrderLineId == poLine!.Id
                : allocation == ReceiptAllocationKind.OutsidePo
                    ? x.ProductVariantId == variantId
                    : x.ProductVariantId == variantId));
        if (line is null)
        {
            line = new StockDocumentLine
            {
                StockDocument = document,
                StockDocumentId = document.Id,
                LineNo = await _repository.GetNextLineNoAsync(document.StoreId, document.Id, ct),
                ProductVariantId = variantId,
                ProductVariant = conversion.ProductVariant,
                PurchaseOrderLineId = poLine?.Id,
                PurchaseOrderLine = poLine,
                ProductUnitConversionId = conversion.Id,
                ProductUnitConversion = conversion,
                UnitId = conversion.UnitId,
                Unit = conversion.Unit,
                UnitNameSnapshot = conversion.Unit.Name,
                Factor = conversion.Factor,
                Quantity = item.Quantity,
                BaseQuantity = RoundQuantity(item.Quantity * conversion.Factor),
                ProductNameSnapshot = string.IsNullOrWhiteSpace(conversion.ProductVariant.ProductVariantName)
                    ? conversion.ProductVariant.Product.Name : conversion.ProductVariant.ProductVariantName,
                SkuSnapshot = conversion.ProductVariant.Sku,
                BarcodeSnapshot = item.RawBarcodeSnapshot,
                ReceiptAllocationKind = allocation,
                OutsidePoDecisionStatus = allocation == ReceiptAllocationKind.OutsidePo
                    ? OutsidePoDecisionStatus.Pending : OutsidePoDecisionStatus.NotApplicable,
                ShortageDisposition = allocation == ReceiptAllocationKind.PurchaseOrder
                    ? PurchaseShortageDisposition.WaitForBackorder : PurchaseShortageDisposition.None
            };
            document.Lines.Add(line);
            await _repository.SaveChangesAsync(ct);
        }
        else
        {
            line.Quantity = RoundQuantity(checked(line.Quantity + item.Quantity));
            line.BaseQuantity = RoundQuantity(line.Quantity * line.Factor);
            if (line.ReceiptAllocationKind == ReceiptAllocationKind.OutsidePo)
                line.OutsidePoDecisionStatus = OutsidePoDecisionStatus.Pending;
        }

        var barcodeRemembered = false;
        if (rememberBarcode && !string.IsNullOrWhiteSpace(item.RawBarcodeSnapshot) &&
            item.RawBarcodeSnapshot.Trim().Length <= 64)
        {
            var rememberResult = await RememberBarcodeAsync(
                item.RawBarcodeSnapshot, conversion, canCreateBarcode, ct);
            item.CreatedBarcodeId = rememberResult.BarcodeId;
            barcodeRemembered = rememberResult.Remembered;
        }

        item.Status = StockDocumentProvisionalItemStatus.Resolved;
        item.ResolutionMethod = method;
        item.ResolvedStockDocumentLineId = line.Id;
        item.ResolvedStockDocumentLine = line;
        item.ResolvedProductVariantId = variantId;
        item.ResolvedProductUnitConversionId = conversionId;
        item.ResolvedByUserId = RequireUser();
        item.ResolvedAtUtc = DateTime.UtcNow;
        item.RawBarcodeRemembered = barcodeRemembered;
        Touch(document);
        await AddActionAsync(document, item, commandId, payloadHash, actionType,
            item.Quantity, item.Quantity, false, false,
            beforeState, Snapshot(item), null, ct);
        await AddAuditAsync(document, item, auditType, ct);
        if (!string.IsNullOrWhiteSpace(item.RawBarcodeSnapshot))
            await AddAuditAsync(document, item, barcodeRemembered
                ? PurchaseReceiptAuditEventType.ProvisionalBarcodeRemembered
                : PurchaseReceiptAuditEventType.ProvisionalBarcodeDeclined, ct);
        await _repository.SaveChangesAsync(ct);
    }

    private async Task<(int? BarcodeId, bool Remembered)> RememberBarcodeAsync(
        string rawBarcode, ProductUnitConversion conversion,
        bool canCreateBarcode, CancellationToken ct)
    {
        var existing = await _barcodes.FindAsync(rawBarcode.Trim(), ct);
        if (existing is { Found: true })
        {
            if (existing.ProductUnitConversionId != conversion.Id)
                throw new BusinessRuleException("[BARCODE_CONFLICT] Barcode đã thuộc sản phẩm hoặc đơn vị khác.");
            return (existing.BarcodeRecordId, true);
        }
        var normalized = NormalizeBarcode(rawBarcode)
            ?? throw new BusinessRuleException("Barcode không hợp lệ để ghi vào danh mục.");
        var evidence = await _repository.GetBarcodeEvidenceAsync(
            RequireStore(), normalized, ct);
        var activeEvidence = evidence.FirstOrDefault(x => x.IsActiveRecord);
        if (activeEvidence is not null)
        {
            if (activeEvidence.ProductUnitConversionId != conversion.Id)
                throw new BusinessRuleException("[BARCODE_CONFLICT] Barcode đã thuộc sản phẩm hoặc đơn vị khác.");
            return (activeEvidence.BarcodeRecordId, true);
        }
        if (evidence.Count > 0)
            return (null, false);
        if (!canCreateBarcode)
            throw new BusinessRuleException("Cần quyền Catalog.Barcode.Create để ghi nhớ barcode.");
        var createdId = await _productUnits.SaveBarcodeAsync(new UpsertProductVariantUnitBarcodeRequest
        {
            ProductUnitConversionId = conversion.Id,
            Barcode = rawBarcode,
            BarcodeType = BarcodeType.External,
            IsPrimary = false,
            IsActive = true,
            Note = "Ghi nhớ từ mặt hàng nhận chưa có trong danh mục."
        }, ct);
        return (createdId, true);
    }

    private async Task<ProvisionalReceivingStateDto> WithLockedDocumentAsync(
        int stockDocumentId, Func<StockDocument, Task> operation, CancellationToken ct, bool lockCatalog = false)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            if (lockCatalog) await _intakeCatalog.LockAsync(RequireStore(), ct);
            var document = await _repository.LockAndGetDocumentAsync(
                RequireStore(), stockDocumentId, ct)
                ?? throw new BusinessRuleException("Phiếu nhận hàng không tồn tại.");
            await operation(document);
            await transaction.CommitAsync(ct);
            var refreshed = await _repository.GetDocumentAsync(
                document.StoreId, document.Id, ct)
                ?? throw new InvalidOperationException("Receipt disappeared after commit.");
            return Map(refreshed);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(ct);
            var detail = exception.ToString();
            if (detail.Contains("UX_ProductVariantUnitBarcode", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException(
                    "[BARCODE_CONFLICT] Barcode vừa được người khác ghi nhận. Vui lòng tải lại và kiểm tra.");
            if (detail.Contains("UX_StockDocumentProvisionalItems", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("UX_PurchaseReceivingActions_Document_Command", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException(
                    "Dữ liệu nhận hàng vừa được cập nhật ở nơi khác. Vui lòng tải lại.");
            throw;
        }
    }

    private void EnsureWarehouseAccess(StockDocument document)
    {
        EnsureReceiptOwnership(document);
        if (document.Status is not (StockDocumentStatus.Draft or StockDocumentStatus.Rejected))
            throw new BusinessRuleException("Phiếu không còn ở trạng thái kho được chỉnh sửa.");
        if (document.ReceiptSource != PurchaseReceiptSource.PurchaseOrder) return;
        if (document.ReceivingSessionState != ReceivingSessionState.Active ||
            document.ReceivingOwnerUserId != RequireUser() ||
            document.ReceivingLeaseExpiresAtUtc <= DateTime.UtcNow)
            throw new BusinessRuleException("Quyền nhận hàng đã hết hạn hoặc thuộc nhân viên khác.");
    }

    private static void EnsureManagerAccess(StockDocument document)
    {
        EnsureReceiptOwnership(document);
        if (document.Status != StockDocumentStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ phiếu chờ duyệt mới xử lý được sản phẩm chưa nhận diện.");
    }

    private static void EnsureReceiptOwnership(StockDocument document)
    {
        if (document.Type != StockDocumentType.Receipt || document.IsDeleted)
            throw new BusinessRuleException("Chứng từ không phải phiếu nhận hàng hợp lệ.");
        if (document.Warehouse is null || document.Warehouse.IsDeleted ||
            !document.Warehouse.IsActive || document.Warehouse.StoreId != document.StoreId ||
            document.Warehouse.LegalEntity is null ||
            document.Warehouse.LegalEntity.IsDeleted ||
            !document.Warehouse.LegalEntity.IsActive ||
            document.Warehouse.LegalEntity.StoreId != document.StoreId)
            throw new BusinessRuleException("Kho hoặc chủ thể pháp lý của phiếu không còn hợp lệ.");
        if (document.Supplier is not null &&
            (document.Supplier.IsDeleted || !document.Supplier.IsActive ||
             document.Supplier.StoreId != document.StoreId))
            throw new BusinessRuleException("Nhà cung cấp của phiếu không còn hợp lệ.");
        if (document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder)
        {
            if (document.PurchaseOrder is null || document.PurchaseOrder.IsDeleted ||
                document.PurchaseOrder.StoreId != document.StoreId ||
                document.PurchaseOrder.SupplierId != document.SupplierId ||
                document.PurchaseOrder.ExpectedWarehouseId != document.WarehouseId)
                throw new BusinessRuleException("Liên kết PO, nhà cung cấp hoặc kho nhận không còn hợp lệ.");
        }
    }

    private static StockDocumentProvisionalItem RequireUnresolved(
        StockDocument document, int itemId)
        => document.ProvisionalItems.SingleOrDefault(x => x.Id == itemId &&
            !x.IsDeleted && x.Status == StockDocumentProvisionalItemStatus.Unresolved)
            ?? throw new BusinessRuleException("Mặt hàng mới không còn ở trạng thái chờ xử lý.");

    private async Task AddActionAsync(
        StockDocument document, StockDocumentProvisionalItem item, Guid commandId,
        string payloadHash, PurchaseReceivingActionType type, decimal before, decimal after,
        bool beforeRemoved, bool afterRemoved, string? beforeState, string? afterState,
        long? undoOf, CancellationToken ct)
        => await _repository.AddActionAsync(new PurchaseReceivingAction
        {
            StoreId = document.StoreId,
            StockDocumentId = document.Id,
            ReceivingRevision = document.ReceivingRevision,
            CommandId = commandId,
            CommandPayloadHash = payloadHash,
            ActionType = type,
            StockDocumentProvisionalItemId = item.Id,
            BeforeQuantity = before,
            AfterQuantity = after,
            BeforeIsDeleted = beforeRemoved,
            AfterIsDeleted = afterRemoved,
            BeforeProvisionalStateJson = beforeState,
            AfterProvisionalStateJson = afterState,
            ActorUserId = RequireUser(),
            OccurredAtUtc = DateTime.UtcNow,
            UndoOfActionId = undoOf
        }, ct);

    private Task AddAuditAsync(
        StockDocument document, StockDocumentProvisionalItem item,
        PurchaseReceiptAuditEventType eventType, CancellationToken ct)
        => _repository.AddAuditAsync(new PurchaseReceiptAuditEvent
        {
            StoreId = document.StoreId,
            StockDocumentId = document.Id,
            StockDocumentProvisionalItemId = item.Id,
            EventType = eventType,
            ActorUserId = RequireUser(),
            ActorUserName = _user.UserName,
            OccurredAtUtc = DateTime.UtcNow,
            ChangedFieldsJson = JsonSerializer.Serialize(new[] { "ProvisionalItem" }),
            OldValuesJson = "{}",
            NewValuesJson = JsonSerializer.Serialize(new
            {
                item.NameSnapshot, item.Quantity, item.Status,
                item.ResolvedStockDocumentLineId, item.ProposedProductVariantId,
                item.ProposedBaseUnitId, item.ProposedBaseUnitName, item.ProposedFactor,
                item.UnitNameSnapshot, item.ProposedCategoryId, item.ReviewDraftJson, item.OriginalDeclarationJson,
                ReviewPhotoHash = PhotoHash(item.ReviewPhoto)
            }),
            IsSuccess = true
        }, ct);

    private ProvisionalReceivingStateDto Map(StockDocument document)
    {
        var warehouseMutable = (document.Status is StockDocumentStatus.Draft or StockDocumentStatus.Rejected) &&
            (document.ReceiptSource != PurchaseReceiptSource.PurchaseOrder ||
             (document.ReceivingSessionState == ReceivingSessionState.Active &&
              document.ReceivingOwnerUserId == _user.UserId &&
              document.ReceivingLeaseExpiresAtUtc > DateTime.UtcNow));
        var managerMutable = document.Status == StockDocumentStatus.PendingApproval;
        var items = document.ProvisionalItems.Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.Id).Select(x =>
            {
                var line = x.ResolvedStockDocumentLineId.HasValue
                    ? document.Lines.FirstOrDefault(l => l.Id == x.ResolvedStockDocumentLineId)
                    : null;
                var actions = new List<string>();
                if (x.Status == StockDocumentProvisionalItemStatus.Unresolved)
                {
                    if (warehouseMutable) actions.AddRange(["Increment", "Edit", "Remove", "Undo"]);
                    if (managerMutable) actions.AddRange(["LinkExisting", "QuickCreate", "Remove"]);
                }
                return new StockDocumentProvisionalItemDto
                {
                    Id = x.Id,
                    Name = x.NameSnapshot,
                    RawBarcode = x.RawBarcodeSnapshot,
                    UnitId = x.UnitId,
                    UnitName = x.UnitNameSnapshot,
                    Quantity = x.Quantity,
                    ProposedProductVariantId = x.ProposedProductVariantId,
                    ProposedBaseUnitId = x.ProposedBaseUnitId,
                    ProposedBaseUnitName = x.ProposedBaseUnitName,
                    ProposedFactor = x.ProposedFactor,
                    ProposedCategoryId = x.ProposedCategoryId,
                    Note = x.Note,
                    HasPhoto = x.PackagingPhoto != null,
                    HasReviewPhoto = x.ReviewPhoto != null,
                    ReviewDraft = x.ReviewDraftJson is null ? null : JsonSerializer.Deserialize<ReceiptIntakeCompletionDto>(x.ReviewDraftJson),
                    Status = x.Status,
                    ResolvedStockDocumentLineId = x.ResolvedStockDocumentLineId,
                    ResolutionAllocationKind = line?.ReceiptAllocationKind,
                    OutsidePoDecisionStatus = line?.OutsidePoDecisionStatus,
                    ResolutionOutcome = x.Status switch
                    {
                        StockDocumentProvisionalItemStatus.Resolved when line is not null =>
                            line.ReceiptAllocationKind.ToString(),
                        StockDocumentProvisionalItemStatus.Removed => "Removed",
                        _ => "Unresolved"
                    },
                    BarcodeState = x.RawBarcodeRemembered
                        ? "Remembered"
                        : string.IsNullOrWhiteSpace(x.RawBarcodeSnapshot) ? "None" : "SnapshotOnly",
                    PermittedActions = actions,
                    RawBarcodeRemembered = x.RawBarcodeRemembered,
                    RowVersion = Convert.ToBase64String(x.RowVersion ?? [])
                };
            }).ToList();
        var permittedActions = new List<string>();
        if (warehouseMutable)
            permittedActions.AddRange(["Capture", "Increment", "Edit", "Remove", "Undo"]);
        if (managerMutable)
            permittedActions.AddRange(["LinkExisting", "QuickCreate", "Remove"]);
        return new ProvisionalReceivingStateDto
        {
            StockDocumentId = document.Id,
            ReceiptSource = document.ReceiptSource,
            ReceivingRevision = document.ReceivingRevision,
            DocumentRowVersion = Convert.ToBase64String(document.RowVersion ?? []),
            UnresolvedCount = items.Count(x =>
                x.Status == StockDocumentProvisionalItemStatus.Unresolved),
            PermittedActions = permittedActions,
            Items = items
        };
    }

    private static string RequiredText(string? value, int max, string emptyMessage)
        => NormalizeOptional(value, max) ?? throw new BusinessRuleException(emptyMessage);

    private static string? NormalizeOptional(string? value, int max)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > max) throw new BusinessRuleException($"Dữ liệu không được vượt quá {max} ký tự.");
        return value;
    }

    private static string? NormalizeBarcode(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) || value.Length > 64
            ? null
            : value.ToUpperInvariant();
    }

    private static string? NormalizeIdentity(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static decimal NormalizeQuantity(decimal quantity)
    {
        var result = RoundQuantity(quantity);
        if (result <= 0m) throw new BusinessRuleException("Số lượng phải lớn hơn 0.");
        return result;
    }

    private static decimal RoundQuantity(decimal quantity)
        => Math.Round(quantity, 3, MidpointRounding.AwayFromZero);

    private static void EnsureCommand(Guid commandId)
    {
        if (commandId == Guid.Empty)
            throw new BusinessRuleException("Thiếu mã lệnh idempotency.");
    }

    private static void EnsureRowVersion(byte[] current, string? posted, string target)
    {
        if (string.IsNullOrWhiteSpace(posted))
            throw new BusinessRuleException($"Thiếu phiên bản {target.ToLowerInvariant()}.");
        byte[] expected;
        try { expected = Convert.FromBase64String(posted); }
        catch (FormatException) { throw new BusinessRuleException($"Phiên bản {target.ToLowerInvariant()} không hợp lệ."); }
        if (!current.SequenceEqual(expected))
            throw new BusinessRuleException($"{target} đã được cập nhật ở nơi khác. Vui lòng tải lại.");
    }

    private static void Touch(StockDocument document)
    {
        document.ReceivingLastSavedAtUtc = DateTime.UtcNow;
        if (document.ReceivingSessionState == ReceivingSessionState.Active)
            document.ReceivingLeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(
                PurchaseReceivingWorkbenchService.LeaseSeconds);
    }

    private async Task<bool> IsReplayAsync(
        StockDocument document, Guid commandId, string payloadHash, CancellationToken ct)
    {
        var existing = await _repository.GetCommandAsync(
            document.StoreId, document.Id, commandId, ct);
        if (existing is null) return false;
        if (!string.Equals(existing.CommandPayloadHash, payloadHash,
                StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException(
                "[IDEMPOTENCY_CONFLICT] Mã lệnh đã được dùng cho nội dung khác.");
        return true;
    }

    private static string PayloadHash(string operation, object payload)
    {
        var json = JsonSerializer.Serialize(new { operation, payload });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string Snapshot(
        StockDocumentProvisionalItem item,
        StockDocumentProvisionalItemStatus? status = null)
        => JsonSerializer.Serialize(new ProvisionalStateSnapshot(
            item.NameSnapshot,
            item.UnitId,
            item.UnitNameSnapshot,
            item.NormalizedUnitNameSnapshot,
            item.Quantity,
            item.Note,
            status ?? item.Status, item.ReviewDraftJson, PhotoHash(item.ReviewPhoto)));

    private void RestoreSnapshot(
        StockDocumentProvisionalItem item,
        string? json,
        PurchaseReceivingAction target)
    {
        ProvisionalStateSnapshot? snapshot = null;
        if (!string.IsNullOrWhiteSpace(json))
            snapshot = JsonSerializer.Deserialize<ProvisionalStateSnapshot>(json);

        if (snapshot?.ReviewPhotoHash != PhotoHash(item.ReviewPhoto))
            throw new BusinessRuleException("Ảnh bản nháp đã thay đổi. Hãy mở phần hoàn thiện sản phẩm để đổi hoặc bỏ ảnh.");
        item.NameSnapshot = snapshot?.Name ?? item.NameSnapshot;
        item.UnitId = snapshot?.UnitId ?? (snapshot is null ? item.UnitId : null);
        item.UnitNameSnapshot = snapshot?.UnitName;
        item.NormalizedUnitNameSnapshot = snapshot?.NormalizedUnitName;
        item.Quantity = snapshot?.Quantity ??
            (target.BeforeQuantity > 0m ? target.BeforeQuantity : item.Quantity);
        item.Note = snapshot?.Note;
        item.ReviewDraftJson = snapshot?.ReviewDraftJson;
        item.Status = snapshot?.Status ?? (target.BeforeIsDeleted
            ? StockDocumentProvisionalItemStatus.Removed
            : StockDocumentProvisionalItemStatus.Unresolved);
        item.RemovedAtUtc = item.Status == StockDocumentProvisionalItemStatus.Removed
            ? DateTime.UtcNow : null;
        item.RemovedByUserId = item.Status == StockDocumentProvisionalItemStatus.Removed
            ? RequireUser() : null;
    }

    private sealed record ProvisionalStateSnapshot(
        string Name,
        int? UnitId,
        string? UnitName,
        string? NormalizedUnitName,
        decimal Quantity,
        string? Note,
        StockDocumentProvisionalItemStatus Status, string? ReviewDraftJson = null, string? ReviewPhotoHash = null);

    private static string? PhotoHash(byte[]? photo) => photo is null ? null :
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo));

    private int RequireStore() => _tenant.StoreId is > 0 and var id
        ? id : throw new InvalidOperationException("Current store context is unavailable.");

    private int RequireUser() => _user.UserId is > 0 and var id
        ? id : throw new BusinessRuleException("Không xác định được người thao tác.");
}
