using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Purchases;

public sealed class PurchaseRequestService : IPurchaseRequestService
{
    private readonly IPurchaseRequestRepository _repository;
    private readonly IDocumentNumberSequenceRepository _numberSequence;
    private readonly IAppUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public PurchaseRequestService(
        IPurchaseRequestRepository repository,
        IDocumentNumberSequenceRepository numberSequence,
        IAppUnitOfWork unitOfWork,
        ITenantContext tenant,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _numberSequence = numberSequence;
        _unitOfWork = unitOfWork;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    public async Task<List<PurchaseRequestListItemDto>> GetListAsync(
        bool onlyMine,
        CancellationToken ct = default)
    {
        var userId = onlyMine ? RequireCurrentUserId() : (int?)null;
        var entities = await _repository.GetListAsync(userId, ct);
        var names = await _repository.GetUserDisplayNamesAsync(entities.Select(x => x.RequestedByUserId), ct);
        return entities.Select(x => new PurchaseRequestListItemDto
        {
            Id = x.Id,
            RequestNumber = x.RequestNumber,
            Title = x.Title,
            RequestDate = x.RequestDate,
            NeedByDate = x.NeedByDate,
            Status = x.Status,
            RequestedByUserId = x.RequestedByUserId,
            RequestedByName = names.GetValueOrDefault(x.RequestedByUserId, $"User #{x.RequestedByUserId}"),
            LineCount = x.Lines.Count(l => !l.IsDeleted),
            RequestedQuantity = x.Lines.Where(l => !l.IsDeleted).Sum(l => l.RequestedQuantity),
            ApprovedQuantity = x.Lines.Where(l => !l.IsDeleted).Sum(l => l.ApprovedQuantity ?? 0m),
            ConvertedQuantity = x.Lines.Where(l => !l.IsDeleted).Sum(l => l.ConvertedQuantity)
        }).ToList();
    }

    public async Task<PurchaseRequestDetailDto?> GetDetailAsync(
        int id,
        bool requireOwnership,
        CancellationToken ct = default)
        => await GetDetailAsync(id, requireOwnership, false, ct);

    public async Task<PurchaseRequestDetailDto?> GetDetailAsync(
        int id,
        bool requireOwnership,
        bool includeCost,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetDetailAsync(id, false, ct);
        if (entity == null) return null;
        if (requireOwnership && entity.RequestedByUserId != RequireCurrentUserId()) return null;

        var actorIds = entity.Actions.Where(x => x.ActorUserId.HasValue).Select(x => x.ActorUserId!.Value)
            .Append(entity.RequestedByUserId);
        var names = await _repository.GetUserDisplayNamesAsync(actorIds, ct);
        var inventory = await _repository.GetInventoryContextAsync(
            entity.StoreId,
            entity.Lines.Where(x => !x.IsDeleted && x.ProductVariantId.HasValue)
                .Select(x => x.ProductVariantId!.Value).Distinct().ToArray(),
            ct);
        return MapDetail(entity, names, inventory, includeCost);
    }

    public async Task<int> SaveAsync(
        SavePurchaseRequestRequest request,
        bool submitAfterSave = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Lines ??= new List<PurchaseRequestLineInputDto>();
        if (request.Lines.Any(x => x is null))
            throw new BusinessRuleException("Dữ liệu dòng hàng không hợp lệ.");
        var storeId = RequireStoreId();
        var userId = RequireCurrentUserId();
        ValidateHeader(request);

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            PurchaseRequest entity;
            var isNew = !request.Id.HasValue;
            if (isNew)
            {
                var sequence = await _numberSequence.GetNextNumberAsync(
                    storeId, DocumentNumberSequenceType.PurchaseRequest, request.RequestDate, ct);
                entity = new PurchaseRequest
                {
                    RequestNumber = $"PR-{request.RequestDate:yyyyMMdd}-{sequence:D4}",
                    RequestedByUserId = userId,
                    Status = PurchaseRequestStatus.Draft
                };
                await _repository.AddAsync(entity, ct);
            }
            else
            {
                entity = await _repository.GetDetailAsync(request.Id!.Value, true, ct)
                    ?? throw new BusinessRuleException("Yêu cầu mua hàng không tồn tại trong cửa hàng hiện tại.");
                EnsureOwner(entity, userId);
                EnsureEditable(entity.Status);
                EnsureRowVersion(entity.RowVersion, request.RowVersion);
            }

            entity.Title = request.Title.Trim();
            entity.RequestDate = request.RequestDate.Date;
            entity.NeedByDate = request.NeedByDate?.Date;
            entity.Note = NormalizeNote(request.Note);
            entity.WorkflowNote = null;

            var existingIdList = request.Lines.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToList();
            if (existingIdList.Count != existingIdList.Distinct().Count())
                throw new BusinessRuleException("Dữ liệu dòng hàng bị trùng. Vui lòng tải lại trang.");

            var existingIds = existingIdList.ToHashSet();
            foreach (var oldLine in entity.Lines.Where(x => !x.IsDeleted && !existingIds.Contains(x.Id)).ToList())
            {
                if (oldLine.ConvertedQuantity > 0)
                    throw new BusinessRuleException("Không thể xóa dòng đã được chuyển sang đơn đặt hàng.");
                oldLine.IsDeleted = true;
                await _repository.RemoveLineAsync(oldLine, ct);
            }

            var nextLineNo = isNew ? 1 : await _repository.GetNextLineNumberAsync(entity.Id, ct);
            var itemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var row = 1;
            foreach (var input in request.Lines)
            {
                var quantity = PurchasePricingPolicy.RoundQuantity(input.Quantity);
                if (quantity <= 0) throw new BusinessRuleException($"Dòng {row}: số lượng phải lớn hơn 0.");

                var line = input.Id.HasValue
                    ? entity.Lines.FirstOrDefault(x => x.Id == input.Id.Value && !x.IsDeleted)
                        ?? throw new BusinessRuleException($"Dòng {row} không thuộc yêu cầu mua hàng này.")
                    : new PurchaseRequestLine { LineNo = nextLineNo++ };

                if (line.ConvertedQuantity > 0)
                    throw new BusinessRuleException($"Dòng {row} đã được chuyển sang đơn đặt hàng nên không thể sửa.");

                if (input.ItemKind == PurchaseItemKind.Catalog)
                {
                    if (!input.ProductVariantId.HasValue || !input.ProductUnitConversionId.HasValue)
                        throw new BusinessRuleException($"Dòng {row}: phải chọn sản phẩm và đơn vị mua.");
                    var conversion = await _repository.GetConversionAsync(input.ProductUnitConversionId.Value, ct)
                        ?? throw new BusinessRuleException($"Dòng {row}: đơn vị mua không tồn tại hoặc đã ngừng hoạt động.");
                    var variant = await _repository.GetVariantAsync(input.ProductVariantId.Value, ct)
                        ?? throw new BusinessRuleException($"Dòng {row}: sản phẩm không tồn tại hoặc đã ngừng hoạt động.");
                    if (conversion.ProductVariantId != variant.Id)
                        throw new BusinessRuleException($"Dòng {row}: đơn vị mua không thuộc sản phẩm đã chọn.");
                    if (!itemKeys.Add($"catalog:{conversion.Id}"))
                        throw new BusinessRuleException($"Dòng {row}: sản phẩm và đơn vị mua đã bị trùng.");

                    line.ItemKind = PurchaseItemKind.Catalog;
                    line.ProductVariantId = variant.Id;
                    line.UnitId = conversion.UnitId;
                    line.ProductUnitConversionId = conversion.Id;
                    line.ProductNameSnapshot = DisplayProductName(variant);
                    line.SkuSnapshot = variant.Sku;
                    line.UnitNameSnapshot = conversion.Unit.Name;
                    line.ConversionFactor = conversion.Factor;
                }
                else if (input.ItemKind == PurchaseItemKind.FreeText)
                {
                    if (input.ProductVariantId.HasValue || input.ProductUnitConversionId.HasValue)
                        throw new BusinessRuleException($"Dòng {row}: hàng tự nhập không được gửi ID sản phẩm danh mục.");
                    var productName = NormalizeRequiredSnapshot(input.ProductName, 250,
                        $"Dòng {row}: bắt buộc nhập tên hàng.");
                    var unitName = NormalizeRequiredSnapshot(input.UnitName, 100,
                        $"Dòng {row}: bắt buộc nhập đơn vị.");
                    if (!itemKeys.Add($"free:{productName}:{unitName}"))
                        throw new BusinessRuleException($"Dòng {row}: tên hàng và đơn vị tự nhập đã bị trùng.");

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
                    throw new BusinessRuleException($"Dòng {row}: loại hàng mua không hợp lệ.");
                }

                line.RequestedQuantity = quantity;
                line.ApprovedQuantity = null;
                line.ConvertedQuantity = 0m;
                if (!input.Id.HasValue) entity.Lines.Add(line);
                row++;
            }

            // Luôn chạm header khi sửa line để RowVersion của toàn chứng từ thay đổi,
            // tránh một phiên khác tiếp tục dùng token cũ dù chỉ dòng hàng đã đổi.
            if (!isNew) entity.UpdatedAtUtc = DateTime.UtcNow;

            AddAction(entity,
                isNew ? PurchaseRequestActionType.Created : PurchaseRequestActionType.Updated,
                entity.Status,
                entity.Status,
                null);

            if (submitAfterSave)
                ApplySubmit(entity);

            await _repository.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return entity.Id;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task SubmitAsync(
        int id,
        PurchaseRequestWorkflowRequest request,
        CancellationToken ct = default)
    {
        var entity = await GetTrackingOwnedAsync(id, ct);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        ApplySubmit(entity, request.Note);
        await _repository.SaveChangesAsync(ct);
    }

    public async Task ApproveAsync(
        int id,
        ApprovePurchaseRequestRequest request,
        CancellationToken ct = default)
    {
        request.Lines ??= new List<PurchaseRequestApprovalLineRequest>();
        if (request.Lines.Any(x => x is null))
            throw new BusinessRuleException("Dữ liệu số lượng duyệt không hợp lệ.");
        var entity = await GetTrackingAsync(id, ct);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        if (entity.Status != PurchaseRequestStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ yêu cầu đang chờ duyệt mới có thể được duyệt.");

        var activeLines = entity.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineNo).ToList();
        if (activeLines.Count == 0) throw new BusinessRuleException("Yêu cầu mua hàng chưa có dòng hàng.");

        if (request.Lines.Count == 0)
        {
            foreach (var line in activeLines) line.ApprovedQuantity = line.RequestedQuantity;
        }
        else
        {
            var duplicates = request.Lines.GroupBy(x => x.PurchaseRequestLineId).FirstOrDefault(x => x.Count() > 1);
            if (duplicates != null) throw new BusinessRuleException("Danh sách số lượng duyệt có dòng bị trùng.");
            if (request.Lines.Count != activeLines.Count ||
                request.Lines.Any(x => activeLines.All(l => l.Id != x.PurchaseRequestLineId)))
                throw new BusinessRuleException("Phải xác nhận số lượng cho đầy đủ mọi dòng của yêu cầu.");

            foreach (var line in activeLines)
            {
                var approved = PurchasePricingPolicy.RoundQuantity(
                    request.Lines.Single(x => x.PurchaseRequestLineId == line.Id).ApprovedQuantity);
                if (approved < 0 || approved > 999999999999m)
                    throw new BusinessRuleException(
                        $"Dòng {line.LineNo}: số lượng duyệt nằm ngoài giới hạn cho phép.");
                line.ApprovedQuantity = approved;
            }
        }

        if (activeLines.All(x => (x.ApprovedQuantity ?? 0m) == 0m))
            throw new BusinessRuleException("Phải duyệt ít nhất một sản phẩm với số lượng lớn hơn 0.");

        var quantityWasAdjusted = activeLines.Any(x =>
            PurchasePricingPolicy.RoundQuantity(x.ApprovedQuantity ?? 0m) !=
            PurchasePricingPolicy.RoundQuantity(x.RequestedQuantity));
        if (quantityWasAdjusted && string.IsNullOrWhiteSpace(request.Note))
            throw new BusinessRuleException(
                "Bắt buộc nhập lý do khi số lượng duyệt khác số lượng nhân viên đề nghị.");

        var from = entity.Status;
        entity.Status = PurchaseRequestStatus.Approved;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        entity.ApprovedByUserId = RequireCurrentUserId();
        entity.WorkflowNote = NormalizeNote(request.Note);
        AddAction(entity, PurchaseRequestActionType.Approved, from, entity.Status, request.Note);
        await _repository.SaveChangesAsync(ct);
    }

    public Task ReturnForRevisionAsync(
        int id,
        PurchaseRequestWorkflowRequest request,
        CancellationToken ct = default)
        => ReviewTransitionAsync(id, RequireNote(request, "Bắt buộc nhập lý do trả về chỉnh sửa."),
            PurchaseRequestStatus.ReturnedForRevision, PurchaseRequestActionType.ReturnedForRevision, ct);

    public Task RejectAsync(
        int id,
        PurchaseRequestWorkflowRequest request,
        CancellationToken ct = default)
        => ReviewTransitionAsync(id, RequireNote(request, "Bắt buộc nhập lý do từ chối."),
            PurchaseRequestStatus.Rejected, PurchaseRequestActionType.Rejected, ct);

    public async Task CancelAsync(
        int id,
        PurchaseRequestWorkflowRequest request,
        bool requireOwnership,
        CancellationToken ct = default)
    {
        var entity = await GetTrackingAsync(id, ct);
        if (requireOwnership) EnsureOwner(entity, RequireCurrentUserId());
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        if (entity.Status is PurchaseRequestStatus.Converted or PurchaseRequestStatus.PartiallyConverted or PurchaseRequestStatus.Cancelled)
            throw new BusinessRuleException("Không thể hủy yêu cầu đã chuyển thành đơn đặt hàng hoặc đã hủy.");
        var from = entity.Status;
        entity.Status = PurchaseRequestStatus.Cancelled;
        entity.CancelledAtUtc = DateTime.UtcNow;
        entity.CancelledByUserId = RequireCurrentUserId();
        entity.WorkflowNote = NormalizeNote(request.Note);
        AddAction(entity, PurchaseRequestActionType.Cancelled, from, entity.Status, request.Note);
        await _repository.SaveChangesAsync(ct);
    }

    public async Task<PurchaseRequestPreparationDto> GetPreparationAsync(
        int id,
        bool includeCost,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetDetailAsync(id, false, ct)
            ?? throw new BusinessRuleException("Yêu cầu mua hàng không tồn tại.");
        EnsureCleanApprovedRequestForConversion(entity);

        var legalEntities = await _repository.GetLegalEntitiesAsync(ct);
        var warehouses = await _repository.GetWarehousesAsync(ct);
        var taxes = await _repository.GetTaxesAsync(ct);
        return new PurchaseRequestPreparationDto
        {
            PurchaseRequestId = entity.Id,
            RequestNumber = entity.RequestNumber,
            Title = entity.Title,
            RowVersion = ToRowVersion(entity.RowVersion),
            DefaultLegalEntityId = legalEntities.FirstOrDefault(x => x.IsDefaultForPurchase)?.Id
                ?? legalEntities.OrderBy(x => x.SalePriority).Select(x => (int?)x.Id).FirstOrDefault(),
            LegalEntities = legalEntities.Select(x => new PurchaseLookupOptionDto
                { Id = x.Id, Text = $"{x.Code} - {x.Name}" }).ToList(),
            Warehouses = warehouses.Select(x => new PurchaseLookupOptionDto
                { Id = x.Id, Text = $"{x.Code} - {x.Name}", LegalEntityId = x.LegalEntityId }).ToList(),
            Taxes = taxes.Select(x => new PurchaseLookupOptionDto
                { Id = x.Id, Text = $"{x.Rate:0.##}% — {x.Name}", TaxRate = x.Rate }).ToList(),
            Lines = entity.Lines.Where(x => !x.IsDeleted)
                .OrderBy(x => x.LineNo)
                .Where(x => (x.ApprovedQuantity ?? 0m) > 0m)
                .Select(x => new PurchaseRequestPreparationLineDto
                {
                    PurchaseRequestLineId = x.Id,
                    ItemKind = x.ItemKind,
                    ProductVariantId = x.ProductVariantId,
                    ProductUnitConversionId = x.ProductUnitConversionId,
                    ProductName = x.ProductNameSnapshot,
                    Sku = x.SkuSnapshot,
                    UnitName = x.UnitNameSnapshot,
                    ConversionFactor = x.ConversionFactor,
                    ApprovedQuantity = x.ApprovedQuantity ?? 0m,
                    ConvertedQuantity = x.ConvertedQuantity,
                    RemainingQuantity = Remaining(x),
                    SuggestedSupplierId = x.ItemKind == PurchaseItemKind.Catalog
                        ? x.ProductVariant!.Product.SupplierId : null,
                    SuggestedSupplierName = x.ItemKind == PurchaseItemKind.Catalog
                        ? x.ProductVariant!.Product.Supplier.Name : string.Empty,
                    DefaultTaxId = null,
                    DefaultTaxRate = 0m,
                    SuggestedUnitPriceAfterVat = null
                }).ToList()
        };
    }

    public async Task<ConvertPurchaseRequestResultDto> ConvertAsync(
        int id,
        ConvertPurchaseRequestRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Orders ??= new List<PurchaseRequestOrderGroupRequest>();
        if (request.Orders.Any(x => x is null))
            throw new BusinessRuleException("Dữ liệu nhóm đơn không hợp lệ.");
        if (request.Orders.Count != 1)
            throw new BusinessRuleException("Mỗi yêu cầu mua chỉ được tạo đúng một đơn đặt hàng.");

        var onlyOrderInput = request.Orders[0];
        onlyOrderInput.Lines ??= new List<PurchaseRequestConversionLineRequest>();
        if (onlyOrderInput.Lines.Any(x => x is null))
            throw new BusinessRuleException("Đơn đặt hàng có dòng dữ liệu không hợp lệ.");

        var keys = request.Orders.Select(x => NormalizeConversionKey(x.ClientGroupKey)).ToList();
        if (keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Count)
            throw new BusinessRuleException("Khóa nhóm đơn bị trùng.");

        // Fast idempotency path: retry nguyên request đã thành công trả lại đúng các PO cũ,
        // kể cả RowVersion phía trình duyệt đã cũ.
        var existingOrders = new List<PurchaseOrder>();
        foreach (var key in keys)
        {
            var existing = await _repository.GetPurchaseOrderByConversionKeyAsync(id, key, false, ct);
            if (existing != null) existingOrders.Add(existing);
        }
        if (existingOrders.Count == keys.Count)
        {
            var current = await _repository.GetDetailAsync(id, false, ct)
                ?? throw new BusinessRuleException("Yêu cầu mua hàng không tồn tại.");
            return new ConvertPurchaseRequestResultDto
            {
                RequestStatus = current.Status,
                RowVersion = ToRowVersion(current.RowVersion),
                PurchaseOrderIds = existingOrders.Select(x => x.Id).ToList()
            };
        }
        if (existingOrders.Count > 0)
            throw new BusinessRuleException("Một phần nhóm đơn đã được tạo. Vui lòng tải lại yêu cầu trước khi tiếp tục.");

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var entity = await _repository.GetDetailForConversionAsync(id, RequireStoreId(), ct)
                ?? throw new BusinessRuleException("Yêu cầu mua hàng không tồn tại trong cửa hàng hiện tại.");

            // Recheck sau khi đã giữ lock để concurrent retry cùng key vẫn idempotent.
            var concurrentlyCreated = entity.PurchaseOrders.FirstOrDefault(x =>
                !x.IsDeleted && string.Equals(x.SourceConversionKey, keys[0], StringComparison.OrdinalIgnoreCase));
            if (concurrentlyCreated != null)
            {
                await tx.CommitAsync(ct);
                return new ConvertPurchaseRequestResultDto
                {
                    RequestStatus = entity.Status,
                    RowVersion = ToRowVersion(entity.RowVersion),
                    PurchaseOrderIds = new List<int> { concurrentlyCreated.Id }
                };
            }

            EnsureRowVersion(entity.RowVersion, request.RowVersion);
            EnsureCleanApprovedRequestForConversion(entity);

            var activeLines = entity.Lines.Where(x => !x.IsDeleted).ToDictionary(x => x.Id);
            var approvedLines = activeLines.Values
                .Where(x => (x.ApprovedQuantity ?? 0m) > 0m)
                .ToDictionary(x => x.Id);
            var allConversionLines = onlyOrderInput.Lines.ToList();
            if (allConversionLines.Count != approvedLines.Count)
                throw new BusinessRuleException(
                    "Đơn đặt hàng phải chứa đầy đủ, đúng một lần mọi dòng có số lượng duyệt lớn hơn 0.");
            var duplicateAcrossGroups = allConversionLines.GroupBy(x => x.PurchaseRequestLineId)
                .FirstOrDefault(x => x.Count() > 1);
            if (duplicateAcrossGroups != null)
                throw new BusinessRuleException(
                    $"Dòng yêu cầu #{duplicateAcrossGroups.Key} bị gửi lặp trong đơn đặt hàng.");
            var proposed = allConversionLines.ToDictionary(
                x => x.PurchaseRequestLineId,
                x => PurchasePricingPolicy.RoundQuantity(x.Quantity));
            foreach (var item in proposed)
            {
                if (!approvedLines.TryGetValue(item.Key, out var sourceLine))
                    throw new BusinessRuleException($"Dòng yêu cầu #{item.Key} không thuộc yêu cầu này.");
                var approvedQuantity = PurchasePricingPolicy.RoundQuantity(sourceLine.ApprovedQuantity ?? 0m);
                if (item.Value != approvedQuantity)
                    throw new BusinessRuleException(
                        $"Dòng {sourceLine.LineNo}: số lượng lập đơn phải đúng bằng số lượng đã duyệt {approvedQuantity.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))} {sourceLine.UnitNameSnapshot}.");
            }
            const PurchaseRequestStatus targetRequestStatus = PurchaseRequestStatus.Converted;

            var createdOrders = new List<PurchaseOrder>();
            for (var groupIndex = 0; groupIndex < request.Orders.Count; groupIndex++)
            {
                var input = request.Orders[groupIndex];
                input.Lines ??= new List<PurchaseRequestConversionLineRequest>();
                if (input.Lines.Any(x => x is null))
                    throw new BusinessRuleException($"Nhóm đơn {groupIndex + 1} có dòng dữ liệu không hợp lệ.");
                if (input.Lines.Count == 0)
                    throw new BusinessRuleException($"Nhóm đơn {groupIndex + 1} chưa có sản phẩm.");
                if (input.Lines.Select(x => x.PurchaseRequestLineId).Distinct().Count() != input.Lines.Count)
                    throw new BusinessRuleException($"Nhóm đơn {groupIndex + 1} có dòng sản phẩm bị trùng.");
                ValidateOrderDates(input, groupIndex + 1);

                var supplier = await _repository.GetSupplierAsync(input.SupplierId, ct)
                    ?? throw new BusinessRuleException($"Nhóm đơn {groupIndex + 1}: nhà cung cấp không hợp lệ.");
                var warehouse = await _repository.GetWarehouseAsync(input.ExpectedWarehouseId, ct)
                    ?? throw new BusinessRuleException($"Nhóm đơn {groupIndex + 1}: kho nhận không hợp lệ.");
                var legalEntity = await _repository.GetLegalEntityAsync(input.LegalEntityId, ct)
                    ?? throw new BusinessRuleException($"Nhóm đơn {groupIndex + 1}: HKD không hợp lệ.");
                if (warehouse.LegalEntityId != legalEntity.Id)
                    throw new BusinessRuleException($"Nhóm đơn {groupIndex + 1}: kho nhận không thuộc HKD đã chọn.");

                var sequence = await _numberSequence.GetNextNumberAsync(
                    RequireStoreId(), DocumentNumberSequenceType.PurchaseOrder, input.OrderDate, ct);
                var order = new PurchaseOrder
                {
                    OrderNumber = $"PO-{input.OrderDate:yyyyMMdd}-{sequence:D4}",
                    Title = NormalizeTitle(input.Title) ?? ComposeOrderTitle(entity.Title, supplier.Name),
                    SourcePurchaseRequestId = entity.Id,
                    SourcePurchaseRequest = entity,
                    SourceConversionKey = keys[groupIndex],
                    SupplierId = supplier.Id,
                    ExpectedWarehouseId = warehouse.Id,
                    LegalEntityId = legalEntity.Id,
                    OrderDate = input.OrderDate.Date,
                    ExpectedDeliveryDate = input.ExpectedDeliveryDate?.Date,
                    Note = NormalizeNote(input.Note),
                    HasVat = false,
                    Status = PurchaseOrderStatus.Draft
                };

                var lineNo = 1;
                foreach (var lineInput in input.Lines)
                {
                    if (!activeLines.TryGetValue(lineInput.PurchaseRequestLineId, out var sourceLine))
                        throw new BusinessRuleException($"Dòng yêu cầu #{lineInput.PurchaseRequestLineId} không hợp lệ.");
                    var quantity = PurchasePricingPolicy.RoundQuantity(lineInput.Quantity);
                    if (quantity <= 0) throw new BusinessRuleException("Số lượng lập đơn phải lớn hơn 0.");

                    if (sourceLine.ItemKind == PurchaseItemKind.Catalog)
                    {
                        if (!sourceLine.ProductVariantId.HasValue ||
                            !sourceLine.ProductUnitConversionId.HasValue ||
                            !sourceLine.UnitId.HasValue)
                            throw new BusinessRuleException(
                                $"Dòng {sourceLine.LineNo}: thiếu liên kết sản phẩm danh mục.");
                        var currentConversion = await _repository.GetConversionAsync(
                            sourceLine.ProductUnitConversionId.Value, ct)
                            ?? throw new BusinessRuleException(
                                $"Dòng {sourceLine.LineNo}: đơn vị mua đã ngừng hoạt động hoặc không còn thuộc cửa hàng.");
                        var currentVariant = await _repository.GetVariantAsync(sourceLine.ProductVariantId.Value, ct)
                            ?? throw new BusinessRuleException(
                                $"Dòng {sourceLine.LineNo}: sản phẩm đã ngừng hoạt động hoặc không còn thuộc cửa hàng.");
                        if (currentConversion.ProductVariantId != currentVariant.Id ||
                            currentConversion.UnitId != sourceLine.UnitId.Value)
                            throw new BusinessRuleException(
                                $"Dòng {sourceLine.LineNo}: cấu hình sản phẩm/đơn vị đã thay đổi. Vui lòng tạo yêu cầu mới.");
                    }
                    else if (sourceLine.ItemKind == PurchaseItemKind.FreeText)
                    {
                        _ = NormalizeRequiredSnapshot(sourceLine.ProductNameSnapshot, 250,
                            $"Dòng {sourceLine.LineNo}: thiếu tên hàng tự nhập.");
                        _ = NormalizeRequiredSnapshot(sourceLine.UnitNameSnapshot, 100,
                            $"Dòng {sourceLine.LineNo}: thiếu đơn vị hàng tự nhập.");
                    }
                    else
                    {
                        throw new BusinessRuleException($"Dòng {sourceLine.LineNo}: loại hàng mua không hợp lệ.");
                    }

                    order.Lines.Add(new PurchaseOrderLine
                    {
                        LineNo = lineNo++,
                        SourcePurchaseRequestLineId = sourceLine.Id,
                        SourcePurchaseRequestLine = sourceLine,
                        ItemKind = sourceLine.ItemKind,
                        ProductVariantId = sourceLine.ProductVariantId,
                        UnitId = sourceLine.UnitId,
                        ProductUnitConversionId = sourceLine.ProductUnitConversionId,
                        TaxId = null,
                        ProductNameSnapshot = sourceLine.ProductNameSnapshot,
                        SkuSnapshot = sourceLine.SkuSnapshot,
                        UnitNameSnapshot = sourceLine.UnitNameSnapshot,
                        TaxNameSnapshot = null,
                        ConversionFactor = sourceLine.ConversionFactor,
                        OrderedQuantity = quantity,
                        UnitPriceBeforeVat = 0m,
                        TaxRate = 0m,
                        VatAmount = 0m,
                        UnitPriceAfterVat = 0m,
                        LineTotalBeforeVat = 0m,
                        LineTotalAfterVat = 0m,
                        ReceiptStatus = PurchaseOrderLineReceiptStatus.NotReceived
                    });
                    sourceLine.ConvertedQuantity = PurchasePricingPolicy.RoundQuantity(
                        sourceLine.ConvertedQuantity + quantity);
                }

                RecalculateOrder(order);
                order.Actions.Add(new PurchaseOrderAction
                {
                    ActionType = PurchaseOrderActionType.Created,
                    FromStatus = PurchaseOrderStatus.Draft,
                    ToStatus = PurchaseOrderStatus.Draft,
                    ActorUserId = RequireCurrentUserId(),
                    OccurredAtUtc = DateTime.UtcNow,
                    Note = $"Tạo từ yêu cầu mua {entity.RequestNumber}."
                });
                await _repository.AddPurchaseOrderAsync(order, ct);
                entity.Actions.Add(new PurchaseRequestAction
                {
                    ActionType = PurchaseRequestActionType.ConvertedToPurchaseOrder,
                    FromStatus = entity.Status,
                    ToStatus = targetRequestStatus,
                    ActorUserId = RequireCurrentUserId(),
                    OccurredAtUtc = DateTime.UtcNow,
                    Note = $"Tạo đơn đặt hàng {order.OrderNumber} cho {supplier.Name}.",
                    PurchaseOrder = order
                });
                createdOrders.Add(order);
            }

            entity.Status = targetRequestStatus;
            if (targetRequestStatus == PurchaseRequestStatus.Converted)
            {
                entity.ConvertedAtUtc = DateTime.UtcNow;
                entity.ConvertedByUserId = RequireCurrentUserId();
            }

            await _repository.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new ConvertPurchaseRequestResultDto
            {
                RequestStatus = entity.Status,
                RowVersion = ToRowVersion(entity.RowVersion),
                PurchaseOrderIds = createdOrders.Select(x => x.Id).ToList()
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public Task<PurchaseLookupPageDto<PurchaseRequestProductLookupDto>> SearchProductsAsync(
        string? term,
        int page,
        CancellationToken ct = default)
        => _repository.SearchProductsAsync(
            RequireStoreId(), NormalizeSearchTerm(term), Math.Max(1, page), 20, ct);

    public async Task<List<PurchaseRequestProductLookupDto>> GetSelectedProductsAsync(
        IReadOnlyCollection<int> productUnitConversionIds,
        CancellationToken ct = default)
    {
        var ids = productUnitConversionIds.Where(x => x > 0).Distinct().ToArray();
        var values = await _repository.GetProductOptionsByIdsAsync(ids, ct);
        return values.Where(x => x.IsActive && x.ProductVariant.IsActive && x.ProductVariant.Product.IsActive)
            .Select(x => new PurchaseRequestProductLookupDto
            {
                ProductVariantId = x.ProductVariantId,
                ProductUnitConversionId = x.Id,
                UnitId = x.UnitId,
                Text = DisplayProductName(x.ProductVariant),
                Sku = x.ProductVariant.Sku,
                UnitName = x.Unit.Name,
                Factor = x.Factor,
                Barcode = x.Barcodes.Where(b => b.IsActive).OrderByDescending(b => b.IsPrimary)
                    .ThenBy(b => b.Id).Select(b => b.Barcode).FirstOrDefault(),
                ImageUrl = NormalizeImageUrl(x.ProductVariant.PrimaryProductImage?.MediaAsset?.StoragePath)
            })
            .OrderBy(x => x.Text).ThenBy(x => x.UnitName).ToList();
    }

    private async Task ReviewTransitionAsync(
        int id,
        PurchaseRequestWorkflowRequest request,
        PurchaseRequestStatus target,
        PurchaseRequestActionType action,
        CancellationToken ct)
    {
        var entity = await GetTrackingAsync(id, ct);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        if (entity.Status != PurchaseRequestStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ yêu cầu đang chờ duyệt mới có thể xử lý.");
        var from = entity.Status;
        entity.Status = target;
        entity.WorkflowNote = NormalizeNote(request.Note);
        var now = DateTime.UtcNow;
        if (target == PurchaseRequestStatus.ReturnedForRevision)
        {
            entity.ReturnedAtUtc = now;
            entity.ReturnedByUserId = RequireCurrentUserId();
        }
        else
        {
            entity.RejectedAtUtc = now;
            entity.RejectedByUserId = RequireCurrentUserId();
        }
        AddAction(entity, action, from, target, request.Note);
        await _repository.SaveChangesAsync(ct);
    }

    private void ApplySubmit(PurchaseRequest entity, string? note = null)
    {
        if (entity.Status is not (PurchaseRequestStatus.Draft or PurchaseRequestStatus.ReturnedForRevision))
            throw new BusinessRuleException("Chỉ yêu cầu nháp hoặc được trả sửa mới có thể gửi duyệt.");
        if (!entity.Lines.Any(x => !x.IsDeleted))
            throw new BusinessRuleException("Yêu cầu mua hàng chưa có sản phẩm.");
        var from = entity.Status;
        entity.Status = PurchaseRequestStatus.PendingApproval;
        entity.SubmittedAtUtc = DateTime.UtcNow;
        entity.SubmittedByUserId = RequireCurrentUserId();
        entity.WorkflowNote = NormalizeNote(note);
        AddAction(entity, PurchaseRequestActionType.Submitted, from, entity.Status, note);
    }

    private async Task<PurchaseRequest> GetTrackingOwnedAsync(int id, CancellationToken ct)
    {
        var entity = await GetTrackingAsync(id, ct);
        EnsureOwner(entity, RequireCurrentUserId());
        return entity;
    }

    private async Task<PurchaseRequest> GetTrackingAsync(int id, CancellationToken ct)
        => await _repository.GetDetailAsync(id, true, ct)
           ?? throw new BusinessRuleException("Yêu cầu mua hàng không tồn tại trong cửa hàng hiện tại.");

    private void AddAction(
        PurchaseRequest entity,
        PurchaseRequestActionType type,
        PurchaseRequestStatus from,
        PurchaseRequestStatus to,
        string? note)
        => entity.Actions.Add(new PurchaseRequestAction
        {
            ActionType = type,
            FromStatus = from,
            ToStatus = to,
            ActorUserId = RequireCurrentUserId(),
            OccurredAtUtc = DateTime.UtcNow,
            Note = NormalizeNote(note)
        });

    private static void ValidateHeader(SavePurchaseRequestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new BusinessRuleException("Bắt buộc nhập tên yêu cầu mua hàng.");
        if (request.Title.Trim().Length > 250)
            throw new BusinessRuleException("Tên yêu cầu mua hàng không được vượt quá 250 ký tự.");
        if (request.Lines.Count == 0)
            throw new BusinessRuleException("Yêu cầu mua hàng phải có ít nhất một sản phẩm.");
        if (request.NeedByDate.HasValue && request.NeedByDate.Value.Date < request.RequestDate.Date)
            throw new BusinessRuleException("Ngày cần hàng không được trước ngày yêu cầu.");
        if (request.Note?.Length > 1000)
            throw new BusinessRuleException("Ghi chú không được vượt quá 1.000 ký tự.");
    }

    private static void ValidateOrderDates(PurchaseRequestOrderGroupRequest request, int groupNo)
    {
        if (request.ExpectedDeliveryDate.HasValue &&
            request.ExpectedDeliveryDate.Value.Date < request.OrderDate.Date)
            throw new BusinessRuleException($"Nhóm đơn {groupNo}: ngày dự kiến giao không được trước ngày đặt.");
        if (request.Title?.Trim().Length > 250)
            throw new BusinessRuleException($"Nhóm đơn {groupNo}: tên đơn không được vượt quá 250 ký tự.");
        if (request.Note?.Length > 1000)
            throw new BusinessRuleException($"Nhóm đơn {groupNo}: ghi chú không được vượt quá 1.000 ký tự.");
    }

    private static void EnsureEditable(PurchaseRequestStatus status)
    {
        if (status is not (PurchaseRequestStatus.Draft or PurchaseRequestStatus.ReturnedForRevision))
            throw new BusinessRuleException("Yêu cầu đã gửi duyệt không được sửa dữ liệu cốt lõi.");
    }

    private static void EnsureCleanApprovedRequestForConversion(PurchaseRequest entity)
    {
        if (entity.Status != PurchaseRequestStatus.Approved)
            throw new BusinessRuleException(
                "Chỉ yêu cầu đã duyệt và chưa chuyển đơn mới có thể lập đơn đặt hàng.");
        if (entity.Lines.Any(x => !x.IsDeleted && x.ConvertedQuantity != 0m))
            throw new BusinessRuleException(
                "Yêu cầu có số lượng đã chuyển từ luồng cũ; không thể tạo thêm đơn đặt hàng.");
        if (entity.PurchaseOrders.Any(x => !x.IsDeleted))
            throw new BusinessRuleException(
                "Yêu cầu này đã từng tạo một đơn đặt hàng và không được phép tạo đơn thứ hai, kể cả khi đơn trước đã bị hủy hoặc từ chối.");
        if (!entity.Lines.Any(x => !x.IsDeleted && (x.ApprovedQuantity ?? 0m) > 0m))
            throw new BusinessRuleException("Yêu cầu không có số lượng được duyệt để lập đơn.");
    }

    private static void EnsureOwner(PurchaseRequest entity, int userId)
    {
        if (entity.RequestedByUserId != userId)
            throw new BusinessRuleException("Bạn chỉ được sửa hoặc gửi yêu cầu do chính mình tạo.");
    }

    private static void EnsureRowVersion(byte[] current, string? posted)
    {
        if (string.IsNullOrWhiteSpace(posted))
            throw new BusinessRuleException("Thiếu RowVersion. Vui lòng tải lại yêu cầu.");
        byte[] expected;
        try { expected = Convert.FromBase64String(posted); }
        catch (FormatException) { throw new BusinessRuleException("RowVersion không hợp lệ."); }
        if (!current.SequenceEqual(expected))
            throw new BusinessRuleException("Yêu cầu đã được người khác cập nhật. Vui lòng tải lại trang.");
    }

    private static PurchaseRequestWorkflowRequest RequireNote(
        PurchaseRequestWorkflowRequest request,
        string error)
    {
        if (string.IsNullOrWhiteSpace(request.Note)) throw new BusinessRuleException(error);
        return request;
    }

    private int RequireStoreId()
        => _tenant.StoreId ?? throw new InvalidOperationException(
            "Current store context is unavailable.");

    private int RequireCurrentUserId()
        => _currentUser.UserId ?? throw new InvalidOperationException(
            "Current user context is unavailable.");

    private static decimal Remaining(PurchaseRequestLine line)
        => Math.Max(0m, PurchasePricingPolicy.RoundQuantity(
            (line.ApprovedQuantity ?? 0m) - line.ConvertedQuantity));

    private static decimal ConvertBaseQuantityToPurchaseUnit(decimal baseQuantity, decimal conversionFactor)
    {
        if (conversionFactor <= 0m) return 0m;
        return PurchasePricingPolicy.RoundQuantity(baseQuantity / conversionFactor);
    }

    private static void RecalculateOrder(PurchaseOrder order)
    {
        order.SubtotalBeforeVat = PurchasePricingPolicy.RoundMoney(order.Lines.Sum(x => x.LineTotalBeforeVat));
        order.VatTotal = PurchasePricingPolicy.RoundMoney(order.Lines.Sum(x => x.VatAmount));
        order.TotalAfterVat = PurchasePricingPolicy.RoundMoney(order.Lines.Sum(x => x.LineTotalAfterVat));
        if (order.TotalAfterVat != order.SubtotalBeforeVat + order.VatTotal)
            order.VatTotal = order.TotalAfterVat - order.SubtotalBeforeVat;
    }

    private static string DisplayProductName(ProductVariant variant)
        => string.IsNullOrWhiteSpace(variant.ProductVariantName)
            ? variant.Product.Name
            : variant.ProductVariantName;

    private static string NormalizeRequiredSnapshot(string? value, int maxLength, string requiredMessage)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new BusinessRuleException(requiredMessage);
        if (value.Length > maxLength)
            throw new BusinessRuleException($"Giá trị không được vượt quá {maxLength} ký tự.");
        return value;
    }

    private static string? NormalizeTitle(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ComposeOrderTitle(string requestTitle, string supplierName)
    {
        var value = $"{requestTitle} - {supplierName}";
        return value.Length <= 250 ? value : value[..250];
    }

    private static string? NormalizeNote(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeConversionKey(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is < 8 or > 64)
            throw new BusinessRuleException("Khóa nhóm đơn phải từ 8 đến 64 ký tự.");
        return value;
    }

    private static string? NormalizeSearchTerm(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= 100 ? value : value[..100];
    }

    private static string? NormalizeImageUrl(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return value;
        return "/" + value.TrimStart('/');
    }

    private static string ToRowVersion(byte[]? value)
        => Convert.ToBase64String(value ?? Array.Empty<byte>());

    private static PurchaseRequestDetailDto MapDetail(
        PurchaseRequest x,
        IReadOnlyDictionary<int, string> userNames,
        IReadOnlyDictionary<int, PurchaseRequestInventoryContextDto> inventory,
        bool includeCost)
        => new()
        {
            Id = x.Id,
            RequestNumber = x.RequestNumber,
            Title = x.Title,
            RequestDate = x.RequestDate,
            NeedByDate = x.NeedByDate,
            Note = x.Note,
            WorkflowNote = x.WorkflowNote,
            Status = x.Status,
            RequestedByUserId = x.RequestedByUserId,
            RequestedByName = userNames.GetValueOrDefault(x.RequestedByUserId, $"User #{x.RequestedByUserId}"),
            SubmittedAtUtc = x.SubmittedAtUtc,
            ReturnedAtUtc = x.ReturnedAtUtc,
            RejectedAtUtc = x.RejectedAtUtc,
            ApprovedAtUtc = x.ApprovedAtUtc,
            ConvertedAtUtc = x.ConvertedAtUtc,
            RowVersion = ToRowVersion(x.RowVersion),
            Lines = x.Lines.Where(l => !l.IsDeleted).OrderBy(l => l.LineNo).Select(l =>
            {
                var context = l.ProductVariantId.HasValue
                    ? inventory.GetValueOrDefault(l.ProductVariantId.Value)
                    : null;
                return new PurchaseRequestLineDto
                {
                    Id = l.Id,
                    LineNo = l.LineNo,
                    ItemKind = l.ItemKind,
                    ProductVariantId = l.ProductVariantId,
                    ProductUnitConversionId = l.ProductUnitConversionId,
                    UnitId = l.UnitId,
                    ProductName = l.ProductNameSnapshot,
                    Sku = l.SkuSnapshot,
                    UnitName = l.UnitNameSnapshot,
                    ConversionFactor = l.ConversionFactor,
                    RequestedQuantity = l.RequestedQuantity,
                    ApprovedQuantity = l.ApprovedQuantity,
                    ConvertedQuantity = l.ConvertedQuantity,
                    RemainingQuantity = Remaining(l),
                    CurrentStockQuantity = ConvertBaseQuantityToPurchaseUnit(
                        context?.CurrentStockBaseQuantity ?? 0m, l.ConversionFactor),
                    IncomingQuantity = ConvertBaseQuantityToPurchaseUnit(
                        context?.IncomingBaseQuantity ?? 0m, l.ConversionFactor)
                };
            }).ToList(),
            Actions = x.Actions.Where(a => !a.IsDeleted).OrderByDescending(a => a.OccurredAtUtc)
                .Select(a => new PurchaseRequestActionDto
                {
                    ActionType = a.ActionType,
                    FromStatus = a.FromStatus,
                    ToStatus = a.ToStatus,
                    ActorUserId = a.ActorUserId,
                    ActorName = a.ActorUserId.HasValue
                        ? userNames.GetValueOrDefault(a.ActorUserId.Value, $"User #{a.ActorUserId}")
                        : "Hệ thống",
                    OccurredAtUtc = a.OccurredAtUtc,
                    Note = a.Note,
                    PurchaseOrderId = a.PurchaseOrderId
                }).ToList(),
            PurchaseOrders = x.PurchaseOrders.Where(o => !o.IsDeleted).OrderByDescending(o => o.Id)
                .Select(o => new PurchaseRequestPurchaseOrderDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    Title = o.Title,
                    SupplierName = o.Supplier.Name,
                    Status = o.Status,
                    TotalAfterVat = includeCost ? o.TotalAfterVat : 0m
                }).ToList()
        };
}
