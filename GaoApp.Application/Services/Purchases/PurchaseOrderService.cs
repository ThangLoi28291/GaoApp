using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Purchases;

public sealed class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IPurchaseOrderRepository _repository;
    private readonly IDocumentNumberSequenceRepository _numberSequence;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IAppUnitOfWork _unitOfWork;

    public PurchaseOrderService(
        IPurchaseOrderRepository repository,
        IDocumentNumberSequenceRepository numberSequence,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IAppUnitOfWork unitOfWork)
    {
        _repository = repository;
        _numberSequence = numberSequence;
        _tenant = tenant;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<PurchaseOrderListResultDto> GetListAsync(
        PurchaseOrderListQueryDto query,
        bool includeCost,
        bool canApprove,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Tab = NormalizeListTab(query.Tab);
        query.Search = NormalizeLookupTerm(query.Search);
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 1, 100);
        if (query.FromDate.HasValue && query.ToDate.HasValue &&
            query.FromDate.Value.Date > query.ToDate.Value.Date)
            throw new BusinessRuleException("Từ ngày không được sau đến ngày.");

        var isAwaiting = query.Tab == "awaiting";
        var canUseAwaitingTab = canApprove;
        var statuses = ResolveListStatuses(query.Tab);

        var statusCounts = await _repository.GetStatusCountsAsync(ct);
        var awaitingCount = canUseAwaitingTab
            ? await _repository.CountPendingApprovalAsync(ct)
            : 0;

        GaoApp.Application.Common.PagedResult<PurchaseOrder> page;
        if (isAwaiting && !canUseAwaitingTab)
        {
            page = new GaoApp.Application.Common.PagedResult<PurchaseOrder>(
                query.Page, query.PageSize, 0, new List<PurchaseOrder>());
        }
        else
        {
            page = await _repository.SearchAsync(
                query.Search,
                query.LegalEntityId,
                query.FromDate,
                query.ToDate,
                statuses,
                query.Page,
                query.PageSize,
                ct);
        }

        return new PurchaseOrderListResultDto
        {
            Query = query,
            Orders = new GaoApp.Application.Common.PagedResult<PurchaseOrderListItemDto>(
                page.Page,
                page.PageSize,
                page.TotalItems,
                page.Items.Select(x => MapListItem(x, includeCost)).ToList()),
            TabCounts = BuildTabCounts(statusCounts, awaitingCount)
        };
    }

    public async Task<PurchaseOrderDetailDto?> GetDetailAsync(
        int id,
        bool includeCost,
        CancellationToken ct = default)
    {
        var order = await _repository.GetDetailAsync(id, false, ct);
        if (order == null) return null;

        var actorIds = order.Actions.Where(x => x.ActorUserId.HasValue)
            .Select(x => x.ActorUserId!.Value)
            .Concat(new int?[]
            {
                order.CreatedBy, order.SubmittedByUserId, order.ApprovedByUserId,
                order.ReturnedByUserId, order.RejectedByUserId,
                order.SentToSupplierByUserId, order.CancelledByUserId
            }.Where(x => x.HasValue).Select(x => x!.Value));
        var names = await _repository.GetUserDisplayNamesAsync(actorIds, ct);
        return MapDetail(order, names, includeCost);
    }

    public async Task<PurchaseOrderFormOptionsDto> GetFormOptionsAsync(CancellationToken ct = default)
        => await GetFormOptionsAsync(null, null, ct);

    public async Task<PurchaseOrderFormOptionsDto> GetFormOptionsAsync(
        int? selectedSupplierId,
        IReadOnlyCollection<int>? selectedProductUnitConversionIds,
        CancellationToken ct = default)
    {
        // All repository queries share the scoped AppDbContext. Execute them
        // sequentially because EF Core does not allow concurrent operations on
        // the same context instance.
        var supplierIds = selectedSupplierId.HasValue && selectedSupplierId.Value > 0
            ? new[] { selectedSupplierId.Value }
            : Array.Empty<int>();
        var conversionIds = selectedProductUnitConversionIds?
            .Where(x => x > 0)
            .Distinct()
            .ToArray() ?? Array.Empty<int>();

        // Only hydrate values already selected by the form. The create page no
        // longer materializes the complete supplier/product catalog.
        var suppliers = await _repository.GetSuppliersByIdsAsync(supplierIds, ct);
        var warehouses = await _repository.GetWarehousesAsync(ct);
        var legalEntities = (await _repository.GetLegalEntitiesAsync(ct)).Where(x => x.IsActive).ToList();
        var taxes = await _repository.GetTaxesAsync(ct);
        var products = await _repository.GetProductOptionsByIdsAsync(conversionIds, ct);

        return new PurchaseOrderFormOptionsDto
        {
            DefaultLegalEntityId = legalEntities.FirstOrDefault(x => x.IsDefaultForPurchase)?.Id
                ?? legalEntities.OrderBy(x => x.SalePriority).Select(x => (int?)x.Id).FirstOrDefault(),
            Suppliers = suppliers.Select(x => new PurchaseLookupOptionDto
            {
                Id = x.Id,
                Text = x.Name,
                Code = x.Code,
                Phone = x.Phone,
                TaxCode = x.TaxCode
            }).ToList(),
            Warehouses = warehouses.Where(x => x.IsActive).Select(x => new PurchaseLookupOptionDto
            {
                Id = x.Id,
                Text = $"{x.Code} - {x.Name}",
                LegalEntityId = x.LegalEntityId
            }).ToList(),
            LegalEntities = legalEntities.Select(x => new PurchaseLookupOptionDto { Id = x.Id, Text = $"{x.Code} - {x.Name}" }).ToList(),
            Taxes = taxes.Where(x => x.IsActive).Select(x => new PurchaseLookupOptionDto
            {
                Id = x.Id,
                Text = $"{x.Name} ({x.Rate:0.##}%)",
                TaxRate = x.Rate
            }).ToList(),
            Products = products.Where(x => x.IsActive && x.ProductVariant.IsActive)
                .Select(MapProductOption)
                .OrderBy(x => x.Text).ThenBy(x => x.UnitName).ToList()
        };
    }

    public async Task<PurchaseLookupPageDto<PurchaseSupplierLookupDto>> SearchSuppliersAsync(
        string? term, int page, CancellationToken ct = default)
    {
        var storeId = _tenant.StoreId
            ?? throw new InvalidOperationException(
                "Current store context is unavailable.");
        return await _repository.SearchSuppliersAsync(storeId, NormalizeLookupTerm(term), Math.Max(1, page), 20, ct);
    }

    public async Task<PurchaseLookupPageDto<PurchaseProductLookupDto>> SearchProductsAsync(
        string? term, int? preferredSupplierId, int page, CancellationToken ct = default)
    {
        var storeId = _tenant.StoreId
            ?? throw new InvalidOperationException(
                "Current store context is unavailable.");

        int? validPreferredSupplierId = null;
        if (preferredSupplierId is > 0 && await _repository.GetSupplierAsync(preferredSupplierId.Value, ct) != null)
            validPreferredSupplierId = preferredSupplierId;

        var result = await _repository.SearchProductsAsync(
            storeId,
            NormalizeLookupTerm(term),
            validPreferredSupplierId,
            Math.Max(1, page),
            20,
            ct);

        foreach (var item in result.Items)
        {
            item.ImageUrl = NormalizeImageUrl(item.ImageUrl);
            item.DefaultTaxId = null;
            item.DefaultTaxRate = 0m;
            item.SuggestedUnitPriceAfterVat = 0m;
        }

        return result;
    }

    public async Task<int> SaveAsync(SavePurchaseOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Title?.Trim().Length > 250)
            throw new BusinessRuleException("Tên đơn đặt hàng không được vượt quá 250 ký tự.");
        if (!_tenant.StoreId.HasValue)
            throw new InvalidOperationException(
                "Current store context is unavailable.");
        if (request.Lines.Count == 0) throw new BusinessRuleException("Đơn đặt hàng phải có ít nhất một dòng.");
        if (request.ExpectedDeliveryDate.HasValue && request.ExpectedDeliveryDate.Value.Date < request.OrderDate.Date)
            throw new BusinessRuleException("Ngày dự kiến giao không được trước ngày đặt hàng.");

        var supplier = await _repository.GetSupplierAsync(request.SupplierId, ct)
            ?? throw new BusinessRuleException("Nhà cung cấp không tồn tại trong cửa hàng hiện tại.");
        var warehouse = await _repository.GetWarehouseAsync(request.ExpectedWarehouseId, ct)
            ?? throw new BusinessRuleException("Kho dự kiến nhận không tồn tại trong cửa hàng hiện tại.");
        var legalEntity = await _repository.GetLegalEntityAsync(request.LegalEntityId, ct)
            ?? throw new BusinessRuleException("HKD không tồn tại trong cửa hàng hiện tại.");
        if (!warehouse.IsActive) throw new BusinessRuleException("Kho dự kiến nhận đang ngừng hoạt động.");
        if (!legalEntity.IsActive) throw new BusinessRuleException("HKD đang ngừng hoạt động.");
        if (warehouse.LegalEntityId != legalEntity.Id)
            throw new BusinessRuleException("Kho dự kiến nhận không thuộc HKD đã chọn.");

        PurchaseOrder order;
        var isNew = !request.Id.HasValue;
        if (isNew)
        {
            var sequence = await _numberSequence.GetNextNumberAsync(
                _tenant.StoreId.Value,
                DocumentNumberSequenceType.PurchaseOrder,
                request.OrderDate,
                ct);
            order = new PurchaseOrder
            {
                OrderNumber = $"PO-{request.OrderDate:yyyyMMdd}-{sequence:D4}",
                Status = PurchaseOrderStatus.Draft
            };
            await _repository.AddAsync(order, ct);
        }
        else
        {
            order = await _repository.GetDetailAsync(request.Id!.Value, true, ct)
                ?? throw new BusinessRuleException("Đơn đặt hàng không tồn tại.");
            if (order.SourcePurchaseRequestId.HasValue)
                throw new BusinessRuleException(
                    "Đơn từ yêu cầu mua phải được cập nhật tại màn hình điều khoản đặt hàng.");
            PurchaseOrderWorkflowPolicy.EnsureCommerciallyEditable(order.Status);
            EnsureRowVersion(order.RowVersion, request.RowVersion);
        }

        var isSourceOrder = order.SourcePurchaseRequestId.HasValue;
        var outsideRequestReason = request.OutsideRequestReason?.Trim();
        if (!isSourceOrder && string.IsNullOrWhiteSpace(outsideRequestReason))
            throw new BusinessRuleException(
                "Đơn lập ngoài yêu cầu mua phải ghi rõ lý do.");

        order.SupplierId = supplier.Id;
        order.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        order.ExpectedWarehouseId = warehouse.Id;
        order.LegalEntityId = legalEntity.Id;
        order.OrderDate = request.OrderDate;
        order.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
        order.Note = request.Note?.Trim();
        order.OutsideRequestReason = isSourceOrder ? null : outsideRequestReason;
        // PO là chứng từ xác nhận tên hàng/đơn vị/số lượng. Giá và VAT được chốt
        // theo từng lần giao tại phiếu nhập, không lấy từ PO làm giá vốn/công nợ.
        order.HasVat = false;

        var requestedExistingIdList = request.Lines.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToList();
        if (requestedExistingIdList.Count != requestedExistingIdList.Distinct().Count())
            throw new BusinessRuleException("Dữ liệu dòng hàng bị trùng. Vui lòng tải lại trang và thử lại.");

        var requestedExistingIds = requestedExistingIdList.ToHashSet();
        var activeExistingLines = order.Lines.Where(x => !x.IsDeleted).ToList();
        if (isSourceOrder &&
            (request.Lines.Any(x => !x.Id.HasValue) ||
             !activeExistingLines.Select(x => x.Id).ToHashSet().SetEquals(requestedExistingIds)))
            throw new BusinessRuleException(
                "Sản phẩm của đơn lập từ yêu cầu mua đã bị thay đổi. Chỉ được sửa nhà cung cấp, giá, VAT, HKD, kho, ngày và ghi chú.");

        foreach (var oldLine in activeExistingLines
                     .Where(x => !isSourceOrder && !requestedExistingIds.Contains(x.Id))
                     .ToList())
        {
            // Remove() được AppDbContext chuyển thành soft-delete lúc SaveChanges. Đánh dấu ngay
            // để phép tính tổng bên dưới không còn tính dòng người dùng vừa xóa.
            oldLine.IsDeleted = true;
            await _repository.RemoveLineAsync(oldLine, ct);
        }

        var rowNumber = 1;
        var nextLineNo = isNew
            ? 1
            : await _repository.GetNextLineNumberAsync(order.Id, ct);
        var itemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in request.Lines)
        {
            var quantity = PurchasePricingPolicy.RoundQuantity(input.Quantity);
            if (quantity <= 0m)
                throw new BusinessRuleException($"Dòng {rowNumber}: số lượng phải lớn hơn 0.");
            var line = input.Id.HasValue
                ? order.Lines.FirstOrDefault(x => x.Id == input.Id.Value && !x.IsDeleted)
                    ?? throw new BusinessRuleException($"Dòng {rowNumber} không thuộc đơn đặt hàng này.")
                : new PurchaseOrderLine();

            if (line.ReceivedQuantity > 0 || line.ShortClosedQuantity > 0)
                throw new BusinessRuleException($"Dòng {rowNumber} đã phát sinh nhận hàng nên không thể sửa dữ liệu gốc.");

            if (isSourceOrder &&
                (line.ItemKind != input.ItemKind ||
                 line.ProductVariantId != input.ProductVariantId ||
                 line.ProductUnitConversionId != input.ProductUnitConversionId ||
                 line.OrderedQuantity != quantity))
                throw new BusinessRuleException(
                    $"Dòng {rowNumber}: sản phẩm, đơn vị và số lượng đã được duyệt nên không thể thay đổi.");

            if (!input.Id.HasValue)
                line.LineNo = nextLineNo++;
            if (!isSourceOrder)
            {
                if (input.ItemKind == PurchaseItemKind.Catalog)
                {
                    if (!input.ProductVariantId.HasValue || !input.ProductUnitConversionId.HasValue)
                        throw new BusinessRuleException($"Dòng {rowNumber}: phải chọn sản phẩm và đơn vị mua.");
                    var conversion = await _repository.GetConversionAsync(input.ProductUnitConversionId.Value, ct)
                        ?? throw new BusinessRuleException($"Dòng {rowNumber}: đơn vị quy đổi không tồn tại.");
                    var variant = await _repository.GetVariantAsync(input.ProductVariantId.Value, ct)
                        ?? throw new BusinessRuleException($"Dòng {rowNumber}: sản phẩm không tồn tại.");
                    if (conversion.ProductVariantId != variant.Id || !conversion.IsActive)
                        throw new BusinessRuleException($"Dòng {rowNumber}: đơn vị mua không thuộc sản phẩm đã chọn.");
                    if (!itemKeys.Add($"catalog:{conversion.Id}"))
                        throw new BusinessRuleException($"Dòng {rowNumber}: sản phẩm và đơn vị mua đã bị trùng.");

                    line.ItemKind = PurchaseItemKind.Catalog;
                    line.ProductVariantId = variant.Id;
                    line.UnitId = conversion.UnitId;
                    line.ProductUnitConversionId = conversion.Id;
                    line.ProductNameSnapshot = string.IsNullOrWhiteSpace(variant.ProductVariantName)
                        ? variant.Product.Name
                        : variant.ProductVariantName;
                    line.SkuSnapshot = variant.Sku;
                    line.UnitNameSnapshot = conversion.Unit.Name;
                    line.ConversionFactor = conversion.Factor;
                }
                else if (input.ItemKind == PurchaseItemKind.FreeText)
                {
                    if (input.ProductVariantId.HasValue || input.ProductUnitConversionId.HasValue)
                        throw new BusinessRuleException($"Dòng {rowNumber}: hàng tự nhập không được gửi ID sản phẩm danh mục.");
                    var productName = NormalizeRequiredSnapshot(input.ProductName, 250,
                        $"Dòng {rowNumber}: bắt buộc nhập tên hàng.");
                    var unitName = NormalizeRequiredSnapshot(input.UnitName, 100,
                        $"Dòng {rowNumber}: bắt buộc nhập đơn vị.");
                    if (!itemKeys.Add($"free:{productName}:{unitName}"))
                        throw new BusinessRuleException($"Dòng {rowNumber}: tên hàng và đơn vị tự nhập đã bị trùng.");

                    line.ItemKind = PurchaseItemKind.FreeText;
                    line.ProductVariantId = null;
                    line.ProductVariant = null;
                    line.UnitId = null;
                    line.Unit = null;
                    line.ProductUnitConversionId = null;
                    line.ProductUnitConversion = null;
                    line.ProductNameSnapshot = productName;
                    line.SkuSnapshot = null;
                    line.UnitNameSnapshot = unitName;
                    line.ConversionFactor = 1m;
                }
                else
                {
                    throw new BusinessRuleException($"Dòng {rowNumber}: loại hàng mua không hợp lệ.");
                }

                line.OrderedQuantity = quantity;
                line.ResolvedAtUtc = null;
                line.ResolvedByUserId = null;
                line.ResolutionNote = null;
            }
            line.TaxId = null;
            line.TaxNameSnapshot = null;
            line.UnitPriceBeforeVat = 0m;
            line.TaxRate = 0m;
            line.VatAmount = 0m;
            line.UnitPriceAfterVat = 0m;
            line.LineTotalBeforeVat = 0m;
            line.LineTotalAfterVat = 0m;
            if (!input.Id.HasValue) order.Lines.Add(line);
            rowNumber++;
        }

        RecalculateHeader(order);
        if (!isNew)
        {
            // Ép aggregate header tham gia optimistic concurrency kể cả khi
            // người dùng sửa giá/thuế nhưng tổng cuối cùng tình cờ không đổi.
            order.UpdatedAtUtc = DateTime.UtcNow;
        }
        AddAction(
            order,
            isNew ? PurchaseOrderActionType.Created : PurchaseOrderActionType.Updated,
            order.Status,
            order.Status,
            isNew
                ? (isSourceOrder ? "Tạo từ yêu cầu mua." : $"Đơn ngoài yêu cầu: {outsideRequestReason}")
                : "Cập nhật điều khoản thương mại của đơn.");
        await _repository.SaveChangesAsync(ct);
        return order.Id;
    }

    public async Task UpdateSourceCommercialAsync(
        int id,
        UpdateSourcePurchaseOrderCommercialRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedDeliveryDate.HasValue &&
            request.ExpectedDeliveryDate.Value.Date < request.OrderDate.Date)
            throw new BusinessRuleException(
                "Ngày dự kiến giao không được trước ngày đặt hàng.");

        var order = await _repository.GetDetailAsync(id, true, ct)
            ?? throw new BusinessRuleException("Đơn đặt hàng không tồn tại.");
        if (!order.SourcePurchaseRequestId.HasValue)
            throw new BusinessRuleException(
                "Đơn ngoài yêu cầu phải được sửa tại màn hình đơn đặt hàng trực tiếp.");
        PurchaseOrderWorkflowPolicy.EnsureCommerciallyEditable(order.Status);
        EnsureRowVersion(order.RowVersion, request.RowVersion);

        var supplier = await _repository.GetSupplierAsync(request.SupplierId, ct)
            ?? throw new BusinessRuleException(
                "Nhà cung cấp không tồn tại trong cửa hàng hiện tại.");
        var warehouse = await _repository.GetWarehouseAsync(request.ExpectedWarehouseId, ct)
            ?? throw new BusinessRuleException(
                "Kho dự kiến nhận không tồn tại trong cửa hàng hiện tại.");
        var legalEntity = await _repository.GetLegalEntityAsync(request.LegalEntityId, ct)
            ?? throw new BusinessRuleException(
                "HKD không tồn tại trong cửa hàng hiện tại.");
        if (!warehouse.IsActive)
            throw new BusinessRuleException("Kho dự kiến nhận đang ngừng hoạt động.");
        if (!legalEntity.IsActive)
            throw new BusinessRuleException("HKD đang ngừng hoạt động.");
        if (warehouse.LegalEntityId != legalEntity.Id)
            throw new BusinessRuleException(
                "Kho dự kiến nhận không thuộc HKD đã chọn.");

        var activeLines = order.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineNo).ToList();
        var postedIds = request.Lines.Select(x => x.PurchaseOrderLineId).ToList();
        if (postedIds.Count > 0 &&
            (postedIds.Count != postedIds.Distinct().Count() ||
             !activeLines.Select(x => x.Id).ToHashSet().SetEquals(postedIds)))
            throw new BusinessRuleException(
                "Danh sách dòng hàng không còn khớp với đơn. Vui lòng tải lại trang.");

        foreach (var line in activeLines)
        {
            line.TaxId = null;
            line.TaxNameSnapshot = null;
            line.UnitPriceBeforeVat = 0m;
            line.TaxRate = 0m;
            line.VatAmount = 0m;
            line.UnitPriceAfterVat = 0m;
            line.LineTotalBeforeVat = 0m;
            line.LineTotalAfterVat = 0m;
        }

        order.SupplierId = supplier.Id;
        order.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        order.ExpectedWarehouseId = warehouse.Id;
        order.LegalEntityId = legalEntity.Id;
        order.OrderDate = request.OrderDate.Date;
        order.ExpectedDeliveryDate = request.ExpectedDeliveryDate?.Date;
        order.Note = request.Note?.Trim();
        order.HasVat = false;
        order.UpdatedAtUtc = DateTime.UtcNow;
        RecalculateHeader(order);
        AddAction(
            order,
            PurchaseOrderActionType.Updated,
            order.Status,
            order.Status,
            "Cập nhật điều khoản đặt hàng; sản phẩm và số lượng được giữ nguyên theo yêu cầu đã duyệt.");
        await _repository.SaveChangesAsync(ct);
    }

    public Task SubmitAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default)
        => TransitionAsync(id, request, new[] { PurchaseOrderStatus.Draft, PurchaseOrderStatus.ReturnedForRevision },
            PurchaseOrderStatus.PendingApproval, PurchaseOrderActionType.Submitted, ct);

    public Task ApproveAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default)
        => ReviewTransitionAsync(
            id,
            request,
            PurchaseOrderStatus.Approved,
            PurchaseOrderActionType.Approved,
            ct);

    public Task ReturnForRevisionAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default)
        => ReviewTransitionAsync(
            id,
            RequireNote(request, "Bắt buộc nhập lý do trả về chỉnh sửa."),
            PurchaseOrderStatus.ReturnedForRevision,
            PurchaseOrderActionType.ReturnedForRevision,
            ct);

    public async Task RejectAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default)
    {
        request = RequireNote(request, "Bắt buộc nhập lý do từ chối.");
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        var order = await _repository.GetDetailAsync(id, true, ct)
            ?? throw new BusinessRuleException("Đơn đặt hàng không tồn tại.");
        EnsureRowVersion(order.RowVersion, request.RowVersion);
        EnsureReviewer(order);

        var now = DateTime.UtcNow;
        var from = order.Status;
        order.Status = PurchaseOrderStatus.Rejected;
        order.RejectedAtUtc = now;
        order.RejectedByUserId = _currentUser.UserId;
        order.WorkflowNote = request.Note!.Trim();
        AddAction(order, PurchaseOrderActionType.Rejected, from, order.Status, request.Note);
        RecordSourceOrderTermination(
            order,
            PurchaseRequestActionType.PurchaseOrderRejected,
            $"Đơn đặt hàng {order.OrderNumber} bị từ chối. Yêu cầu mua nguồn vẫn giữ liên kết một-yêu-cầu/một-đơn và không thể tạo đơn thay thế.");

        await _repository.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public Task MarkSentAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default)
        => TransitionAsync(id, request, new[] { PurchaseOrderStatus.Approved },
            PurchaseOrderStatus.SentToSupplier, PurchaseOrderActionType.SentToSupplier, ct);

    public async Task CancelAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default)
    {
        request = RequireNote(request, "Bắt buộc nhập lý do hủy đơn đặt hàng.");
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        var order = await _repository.GetDetailAsync(id, true, ct)
            ?? throw new BusinessRuleException("Đơn đặt hàng không tồn tại.");
        EnsureRowVersion(order.RowVersion, request.RowVersion);
        PurchaseOrderWorkflowPolicy.EnsureCanCancel(
            order.Status,
            order.Lines.Any(x => !x.IsDeleted && x.ReceivedQuantity > 0));
        var from = order.Status;
        order.Status = PurchaseOrderStatus.Cancelled;
        order.CancelledAtUtc = DateTime.UtcNow;
        order.CancelledByUserId = _currentUser.UserId;
        order.WorkflowNote = request.Note?.Trim();
        AddAction(order, PurchaseOrderActionType.Cancelled, from, order.Status, request.Note);

        RecordSourceOrderTermination(
            order,
            PurchaseRequestActionType.PurchaseOrderCancelled,
            $"Hủy đơn đặt hàng {order.OrderNumber}. Yêu cầu mua nguồn vẫn giữ liên kết một-yêu-cầu/một-đơn và không thể tạo đơn khác.");
        await _repository.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task ReviewTransitionAsync(
        int id,
        PurchaseWorkflowRequest request,
        PurchaseOrderStatus target,
        PurchaseOrderActionType action,
        CancellationToken ct)
    {
        var order = await _repository.GetDetailAsync(id, true, ct)
            ?? throw new BusinessRuleException("Đơn đặt hàng không tồn tại.");
        EnsureRowVersion(order.RowVersion, request.RowVersion);
        EnsureReviewer(order);

        var now = DateTime.UtcNow;
        var from = order.Status;
        order.Status = target;
        order.WorkflowNote = request.Note?.Trim();
        if (target == PurchaseOrderStatus.Approved)
        {
            order.ApprovedAtUtc = now;
            order.ApprovedByUserId = _currentUser.UserId;
        }
        else if (target == PurchaseOrderStatus.ReturnedForRevision)
        {
            order.ReturnedAtUtc = now;
            order.ReturnedByUserId = _currentUser.UserId;
        }

        AddAction(order, action, from, target, request.Note);
        await _repository.SaveChangesAsync(ct);
    }

    private async Task TransitionAsync(
        int id,
        PurchaseWorkflowRequest request,
        IReadOnlyCollection<PurchaseOrderStatus> allowed,
        PurchaseOrderStatus target,
        PurchaseOrderActionType action,
        CancellationToken ct)
    {
        var order = await _repository.GetDetailAsync(id, true, ct)
            ?? throw new BusinessRuleException("Đơn đặt hàng không tồn tại.");
        EnsureRowVersion(order.RowVersion, request.RowVersion);
        if (!allowed.Contains(order.Status))
            throw new BusinessRuleException($"Không thể chuyển đơn từ trạng thái {order.Status} sang {target}.");
        if (target == PurchaseOrderStatus.PendingApproval)
        {
            PurchaseOrderWorkflowPolicy.EnsureCanSubmit(order.Status);
            EnsureReadyForSubmission(order);
        }

        var now = DateTime.UtcNow;
        var from = order.Status;
        order.Status = target;
        order.WorkflowNote = request.Note?.Trim();
        switch (target)
        {
            case PurchaseOrderStatus.PendingApproval: order.SubmittedAtUtc = now; order.SubmittedByUserId = _currentUser.UserId; break;
            case PurchaseOrderStatus.Approved: order.ApprovedAtUtc = now; order.ApprovedByUserId = _currentUser.UserId; break;
            case PurchaseOrderStatus.Rejected: order.RejectedAtUtc = now; order.RejectedByUserId = _currentUser.UserId; break;
            case PurchaseOrderStatus.ReturnedForRevision: order.ReturnedAtUtc = now; order.ReturnedByUserId = _currentUser.UserId; break;
            case PurchaseOrderStatus.SentToSupplier: order.SentToSupplierAtUtc = now; order.SentToSupplierByUserId = _currentUser.UserId; break;
        }
        AddAction(order, action, from, target, request.Note);
        await _repository.SaveChangesAsync(ct);
    }

    private void EnsureReviewer(PurchaseOrder order)
        => PurchaseOrderWorkflowPolicy.EnsureCanReview(order.Status, _currentUser.UserId);

    private static void EnsureReadyForSubmission(PurchaseOrder order)
    {
        var lines = order.Lines.Where(x => !x.IsDeleted).ToList();
        if (lines.Count == 0)
            throw new BusinessRuleException("Đơn đặt hàng chưa có dòng hàng.");
        if (lines.Any(x => x.OrderedQuantity <= 0m))
            throw new BusinessRuleException("Đơn có dòng hàng với số lượng không hợp lệ.");
        foreach (var line in lines)
        {
            if (line.ItemKind == PurchaseItemKind.Catalog)
            {
                if (!line.ProductVariantId.HasValue || !line.UnitId.HasValue ||
                    !line.ProductUnitConversionId.HasValue || line.ConversionFactor <= 0m)
                    throw new BusinessRuleException(
                        $"Dòng {line.LineNo}: liên kết sản phẩm/đơn vị danh mục không hợp lệ.");
            }
            else if (line.ItemKind == PurchaseItemKind.FreeText)
            {
                _ = NormalizeRequiredSnapshot(line.ProductNameSnapshot, 250,
                    $"Dòng {line.LineNo}: thiếu tên hàng tự nhập.");
                _ = NormalizeRequiredSnapshot(line.UnitNameSnapshot, 100,
                    $"Dòng {line.LineNo}: thiếu đơn vị hàng tự nhập.");
            }
            else
            {
                throw new BusinessRuleException($"Dòng {line.LineNo}: loại hàng mua không hợp lệ.");
            }
        }
        if (order.SourcePurchaseRequestId.HasValue)
        {
            if (lines.Any(x => !x.SourcePurchaseRequestLineId.HasValue))
                throw new BusinessRuleException(
                    "Dòng hàng của đơn không còn liên kết đầy đủ với yêu cầu mua nguồn.");
        }
        else if (string.IsNullOrWhiteSpace(order.OutsideRequestReason))
        {
            throw new BusinessRuleException(
                "Đơn lập ngoài yêu cầu mua phải ghi rõ lý do trước khi gửi duyệt.");
        }
        if (order.ExpectedWarehouse.LegalEntityId != order.LegalEntityId)
            throw new BusinessRuleException("Kho nhận không thuộc HKD đã chọn.");
    }

    private void RecordSourceOrderTermination(
        PurchaseOrder order,
        PurchaseRequestActionType actionType,
        string note)
    {
        var sourceRequest = order.SourcePurchaseRequest;
        if (sourceRequest == null) return;

        // Một yêu cầu mua chỉ được sinh đúng một đơn đặt hàng trong toàn bộ vòng đời.
        // Vì vậy khi PO bị từ chối/hủy, tuyệt đối không trừ ConvertedQuantity và
        // không đưa yêu cầu về Approved; nếu cần chỉnh thì phải dùng ReturnForRevision
        // trên chính PO trước khi kết thúc đơn.
        var sourceFrom = sourceRequest.Status;
        sourceRequest.Actions.Add(new PurchaseRequestAction
        {
            ActionType = actionType,
            FromStatus = sourceFrom,
            ToStatus = sourceRequest.Status,
            ActorUserId = _currentUser.UserId,
            OccurredAtUtc = DateTime.UtcNow,
            Note = note,
            PurchaseOrderId = order.Id
        });
    }

    private static PurchaseWorkflowRequest RequireNote(PurchaseWorkflowRequest request, string message)
    {
        if (string.IsNullOrWhiteSpace(request.Note)) throw new BusinessRuleException(message);
        return request;
    }

    private void AddAction(PurchaseOrder order, PurchaseOrderActionType action, PurchaseOrderStatus from, PurchaseOrderStatus to, string? note)
        => order.Actions.Add(new PurchaseOrderAction
        {
            ActionType = action,
            FromStatus = from,
            ToStatus = to,
            ActorUserId = _currentUser.UserId,
            OccurredAtUtc = DateTime.UtcNow,
            Note = note?.Trim()
        });

    private static void RecalculateHeader(PurchaseOrder order)
    {
        var lines = order.Lines.Where(x => !x.IsDeleted).ToList();
        order.SubtotalBeforeVat = PurchasePricingPolicy.RoundMoney(lines.Sum(x => x.LineTotalBeforeVat));
        order.VatTotal = PurchasePricingPolicy.RoundMoney(lines.Sum(x => x.VatAmount));
        order.TotalAfterVat = PurchasePricingPolicy.RoundMoney(lines.Sum(x => x.LineTotalAfterVat));
        if (order.TotalAfterVat != order.SubtotalBeforeVat + order.VatTotal)
            order.VatTotal = order.TotalAfterVat - order.SubtotalBeforeVat;
    }

    private static void EnsureRowVersion(byte[] current, string? posted)
    {
        if (string.IsNullOrWhiteSpace(posted))
            throw new BusinessRuleException("Thiếu RowVersion. Vui lòng tải lại đơn.");
        byte[] expected;
        try { expected = Convert.FromBase64String(posted); }
        catch (FormatException) { throw new BusinessRuleException("RowVersion không hợp lệ."); }
        if (!current.SequenceEqual(expected)) throw new BusinessRuleException("Đơn đã được người khác cập nhật. Vui lòng tải lại trang.");
    }

    private static PurchaseProductOptionDto MapProductOption(ProductUnitConversion x)
    {
        var product = x.ProductVariant.Product;
        var barcode = x.Barcodes
            .Where(b => b.IsActive)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .Select(b => b.Barcode)
            .FirstOrDefault();

        return new PurchaseProductOptionDto
        {
            ProductVariantId = x.ProductVariantId,
            ProductUnitConversionId = x.Id,
            UnitId = x.UnitId,
            Text = string.IsNullOrWhiteSpace(x.ProductVariant.ProductVariantName)
                ? product.Name
                : x.ProductVariant.ProductVariantName,
            Sku = x.ProductVariant.Sku,
            UnitName = x.Unit.Name,
            Factor = x.Factor,
            DefaultTaxId = null,
            DefaultTaxRate = 0m,
            SuggestedUnitPriceAfterVat = 0m,
            Barcode = barcode,
            ImageUrl = NormalizeImageUrl(x.ProductVariant.PrimaryProductImage?.MediaAsset?.StoragePath),
            SupplierId = product.SupplierId,
            SupplierName = product.Supplier?.Name ?? string.Empty
        };
    }

    private static string? NormalizeLookupTerm(string? term)
    {
        term = term?.Trim();
        if (string.IsNullOrEmpty(term)) return null;
        return term.Length <= 100 ? term : term[..100];
    }

    private static string NormalizeRequiredSnapshot(string? value, int maxLength, string requiredMessage)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new BusinessRuleException(requiredMessage);
        if (value.Length > maxLength)
            throw new BusinessRuleException($"Giá trị không được vượt quá {maxLength} ký tự.");
        return value;
    }

    private static string? NormalizeImageUrl(string? storagePath)
    {
        storagePath = storagePath?.Trim();
        if (string.IsNullOrEmpty(storagePath)) return null;
        if (storagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            storagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return storagePath;
        return "/" + storagePath.TrimStart('/');
    }

    private static PurchaseOrderListItemDto MapListItem(PurchaseOrder x, bool includeCost) => new()
    {
        Id = x.Id,
        OrderNumber = x.OrderNumber,
        Title = x.Title,
        SourcePurchaseRequestId = x.SourcePurchaseRequestId,
        OrderDate = x.OrderDate,
        SupplierName = x.Supplier.Name,
        WarehouseName = x.ExpectedWarehouse.Name,
        LegalEntityName = x.LegalEntity.Name,
        Status = x.Status,
        TotalAfterVat = includeCost ? x.TotalAfterVat : 0m,
        OrderedQuantity = x.Lines.Where(l => !l.IsDeleted).Sum(l => l.OrderedQuantity),
        ReceivedQuantity = x.Lines.Where(l => !l.IsDeleted).Sum(l => l.ReceivedQuantity),
        PendingQuantity = x.Lines.Where(l => !l.IsDeleted).Sum(l => l.PendingQuantity),
        CreatedByUserId = x.CreatedBy
    };

    private static string NormalizeListTab(string? tab)
    {
        tab = tab?.Trim().ToLowerInvariant();
        return tab is "awaiting" or "draft" or "approved" or "sent" or
            "receiving" or "completed" or "closed" or "all"
            ? tab
            : "awaiting";
    }

    private static IReadOnlyCollection<PurchaseOrderStatus>? ResolveListStatuses(string tab)
        => tab switch
        {
            "awaiting" => new[] { PurchaseOrderStatus.PendingApproval },
            "draft" => new[] { PurchaseOrderStatus.Draft, PurchaseOrderStatus.ReturnedForRevision },
            "approved" => new[] { PurchaseOrderStatus.Approved },
            "sent" => new[] { PurchaseOrderStatus.SentToSupplier },
            "receiving" => new[] { PurchaseOrderStatus.PartiallyReceived },
            "completed" => new[] { PurchaseOrderStatus.FullyReceived, PurchaseOrderStatus.ShortClosed },
            "closed" => new[] { PurchaseOrderStatus.Rejected, PurchaseOrderStatus.Cancelled },
            _ => null
        };

    private static Dictionary<string, int> BuildTabCounts(
        IReadOnlyDictionary<PurchaseOrderStatus, int> counts,
        int awaitingCount)
    {
        int Count(params PurchaseOrderStatus[] statuses)
            => statuses.Sum(x => counts.TryGetValue(x, out var value) ? value : 0);

        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["awaiting"] = awaitingCount,
            ["draft"] = Count(PurchaseOrderStatus.Draft, PurchaseOrderStatus.ReturnedForRevision),
            ["approved"] = Count(PurchaseOrderStatus.Approved),
            ["sent"] = Count(PurchaseOrderStatus.SentToSupplier),
            ["receiving"] = Count(PurchaseOrderStatus.PartiallyReceived),
            ["completed"] = Count(PurchaseOrderStatus.FullyReceived, PurchaseOrderStatus.ShortClosed),
            ["closed"] = Count(PurchaseOrderStatus.Rejected, PurchaseOrderStatus.Cancelled),
            ["all"] = counts.Values.Sum()
        };
    }

    private static PurchaseOrderDetailDto MapDetail(
        PurchaseOrder x,
        IReadOnlyDictionary<int, string> names,
        bool includeCost) => new()
    {
        Id = x.Id,
        OrderNumber = x.OrderNumber,
        Title = x.Title,
        SourcePurchaseRequestId = x.SourcePurchaseRequestId,
        SupplierId = x.SupplierId,
        SupplierName = x.Supplier.Name,
        SupplierCode = x.Supplier.Code,
        SupplierPhone = x.Supplier.Phone,
        SupplierEmail = x.Supplier.Email,
        SupplierAddress = x.Supplier.Address,
        SupplierContactName = x.Supplier.ContactName,
        SupplierTaxCode = x.Supplier.TaxCode,
        ExpectedWarehouseId = x.ExpectedWarehouseId,
        WarehouseName = x.ExpectedWarehouse.Name,
        WarehouseCode = x.ExpectedWarehouse.Code,
        WarehouseLocation = x.ExpectedWarehouse.Location,
        LegalEntityId = x.LegalEntityId,
        LegalEntityName = x.LegalEntity.Name,
        LegalEntityCode = x.LegalEntity.Code,
        LegalEntityLegalName = x.LegalEntity.LegalName,
        LegalEntityTaxCode = x.LegalEntity.TaxCode,
        LegalEntityAddress = x.LegalEntity.Address,
        LegalEntityPhone = x.LegalEntity.Phone,
        LegalEntityEmail = x.LegalEntity.Email,
        OrderDate = x.OrderDate,
        ExpectedDeliveryDate = x.ExpectedDeliveryDate,
        Note = x.Note,
        OutsideRequestReason = x.OutsideRequestReason,
        WorkflowNote = x.WorkflowNote,
        HasVat = x.HasVat,
        Status = x.Status,
        SubtotalBeforeVat = includeCost ? x.SubtotalBeforeVat : 0m,
        VatTotal = includeCost ? x.VatTotal : 0m,
        TotalAfterVat = includeCost ? x.TotalAfterVat : 0m,
        CreatedByUserId = x.CreatedBy,
        CreatedByName = UserName(x.CreatedBy, names),
        SubmittedAtUtc = x.SubmittedAtUtc,
        SubmittedByUserId = x.SubmittedByUserId,
        SubmittedByName = UserNameOrNull(x.SubmittedByUserId, names),
        ApprovedAtUtc = x.ApprovedAtUtc,
        ApprovedByUserId = x.ApprovedByUserId,
        ApprovedByName = UserNameOrNull(x.ApprovedByUserId, names),
        ReturnedAtUtc = x.ReturnedAtUtc,
        ReturnedByUserId = x.ReturnedByUserId,
        ReturnedByName = UserNameOrNull(x.ReturnedByUserId, names),
        RejectedAtUtc = x.RejectedAtUtc,
        RejectedByUserId = x.RejectedByUserId,
        RejectedByName = UserNameOrNull(x.RejectedByUserId, names),
        SentToSupplierAtUtc = x.SentToSupplierAtUtc,
        SentToSupplierByUserId = x.SentToSupplierByUserId,
        SentToSupplierByName = UserNameOrNull(x.SentToSupplierByUserId, names),
        CancelledAtUtc = x.CancelledAtUtc,
        CancelledByUserId = x.CancelledByUserId,
        CancelledByName = UserNameOrNull(x.CancelledByUserId, names),
        RowVersion = Convert.ToBase64String(x.RowVersion ?? Array.Empty<byte>()),
        Lines = x.Lines.Where(l => !l.IsDeleted).OrderBy(l => l.LineNo).Select(l => new PurchaseOrderLineDto
        {
            Id = l.Id, SourcePurchaseRequestLineId = l.SourcePurchaseRequestLineId,
            LineNo = l.LineNo, ItemKind = l.ItemKind, ProductVariantId = l.ProductVariantId,
            ProductUnitConversionId = l.ProductUnitConversionId, UnitId = l.UnitId, TaxId = l.TaxId,
            ProductName = l.ProductNameSnapshot, Sku = l.SkuSnapshot, UnitName = l.UnitNameSnapshot,
            ConversionFactor = l.ConversionFactor, OrderedQuantity = l.OrderedQuantity,
            UnitPriceBeforeVat = includeCost ? l.UnitPriceBeforeVat : 0m,
            TaxRate = includeCost ? l.TaxRate : 0m,
            VatAmount = includeCost ? l.VatAmount : 0m,
            UnitPriceAfterVat = includeCost ? l.UnitPriceAfterVat : 0m,
            LineTotalBeforeVat = includeCost ? l.LineTotalBeforeVat : 0m,
            LineTotalAfterVat = includeCost ? l.LineTotalAfterVat : 0m,
            ReceivedQuantity = l.ReceivedQuantity,
            PendingQuantity = l.PendingQuantity, ShortClosedQuantity = l.ShortClosedQuantity,
            ReceiptStatus = l.ReceiptStatus, ShortCloseReason = l.ShortCloseReason,
            ResolvedAtUtc = l.ResolvedAtUtc, ResolvedByUserId = l.ResolvedByUserId,
            ResolutionNote = l.ResolutionNote,
            ResolvedProductName = l.ProductVariant == null
                ? null
                : string.IsNullOrWhiteSpace(l.ProductVariant.ProductVariantName)
                    ? l.ProductVariant.Product.Name
                    : l.ProductVariant.ProductVariantName,
            ResolvedSku = l.ProductVariant == null ? null : l.ProductVariant.Sku,
            ResolvedUnitName = l.ProductUnitConversion == null
                ? null
                : l.ProductUnitConversion.Unit.Name
        }).ToList(),
        Actions = x.Actions.Where(a => !a.IsDeleted).OrderByDescending(a => a.OccurredAtUtc).Select(a => new PurchaseOrderActionDto
        {
            ActionType = a.ActionType, FromStatus = a.FromStatus, ToStatus = a.ToStatus,
            OccurredAtUtc = a.OccurredAtUtc, ActorUserId = a.ActorUserId,
            ActorName = UserName(a.ActorUserId, names),
            Note = a.Note, StockDocumentId = a.StockDocumentId
        }).ToList(),
        Receipts = x.Receipts.Where(r => !r.IsDeleted).OrderByDescending(r => r.DocumentDate).Select(r => new PurchaseOrderReceiptDto
        {
            Id = r.Id, DocumentNo = r.DocumentNo, Status = r.Status,
            DocumentDate = r.DocumentDate, TotalAmount = includeCost ? r.TotalAmount : 0m
        }).ToList()
    };

    private static string UserName(int? userId, IReadOnlyDictionary<int, string> names)
        => userId.HasValue && names.TryGetValue(userId.Value, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : userId.HasValue ? $"Người dùng #{userId.Value}" : "Hệ thống";

    private static string? UserNameOrNull(int? userId, IReadOnlyDictionary<int, string> names)
        => userId.HasValue ? UserName(userId, names) : null;
}
