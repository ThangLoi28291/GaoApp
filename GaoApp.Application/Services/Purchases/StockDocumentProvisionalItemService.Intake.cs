using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

public sealed partial class StockDocumentProvisionalItemService
{
    public Task<IReadOnlyList<ReceiptIntakeRecentDto>> GetRecentAsync(int documentId, CancellationToken ct)
        => _repository.GetRecentReceiptsAsync(RequireStore(), documentId, ct);

    public async Task<byte[]?> GetPhotoAsync(int documentId, int itemId, CancellationToken ct)
    {
        var document = await _repository.GetDocumentAsync(RequireStore(), documentId, ct)
            ?? throw new BusinessRuleException("Phiếu nhận hàng không tồn tại.");
        EnsureReceiptOwnership(document);
        return document.ProvisionalItems.FirstOrDefault(x => x.Id == itemId && !x.IsDeleted)?.PackagingPhoto;
    }

    public Task<ProvisionalReceivingStateDto> RecordKnownAsync(int documentId,
        RecordKnownReceiptItemRequest request, CancellationToken ct)
        => WithLockedDocumentAsync(documentId, async document =>
        {
            EnsureCommand(request.CommandId);
            EnsureWarehouseAccess(document);
            var hash = PayloadHash("intake-known", new { request.ProductUnitConversionId, request.Factor,
                request.Quantity, request.Barcode, request.Note });
            var photo = ReceiptIntakePhoto.Parse(request.PhotoDataUrl);
            if (photo is not null) hash = PayloadHash("intake-photo", new { hash, photoHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo)) });
            if (await IsReplayAsync(document, request.CommandId, hash, ct)) return;
            EnsureRowVersion(document.RowVersion, request.DocumentRowVersion, "Phiếu");
            if (document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder && request.LeaseToken != document.ReceivingLeaseToken)
                throw new BusinessRuleException("Phiên nhận hàng đã thay đổi.");
            var quantity = IntakeNumber(request.Quantity, "Số lượng");
            IntakeNumber(request.Factor, "Quy đổi");
            IntakeBaseQuantity(quantity, request.Factor);
            var conversion = await _intakeCatalog.PrepareKnownAsync(document, request, ct);
            var item = new StockDocumentProvisionalItem
            {
                StoreId = document.StoreId, StockDocument = document, StockDocumentId = document.Id,
                NameSnapshot = conversion.ProductVariant.ProductVariantName ?? conversion.ProductVariant.Product.Name,
                UnitId = conversion.UnitId, UnitNameSnapshot = conversion.Unit.Name,
                NormalizedUnitNameSnapshot = NormalizeIdentity(conversion.Unit.Name),
                Quantity = quantity, RawBarcodeSnapshot = NormalizeOptional(request.Barcode, 64),
                Note = NormalizeOptional(request.Note, 500), ReceivingRevision = document.ReceivingRevision
                , PackagingPhoto = photo
            };
            await _repository.AddItemAsync(item, ct);
            await _repository.SaveChangesAsync(ct);
            await ResolveCoreAsync(document, item, request.CommandId, conversion.ProductVariantId, conversion.Id,
                false, false, ProvisionalItemResolutionMethod.LinkExisting,
                PurchaseReceivingActionType.ProvisionalLinkExisting,
                PurchaseReceiptAuditEventType.ProvisionalItemResolvedExisting, hash, ct);
        }, ct, lockCatalog: true);

    public Task<ProvisionalReceivingStateDto> CaptureIntakeAsync(int documentId,
        CaptureReceiptIntakeRequest request, ReceiptIntakePermissions permissions, CancellationToken ct)
        => WithLockedDocumentAsync(documentId, async document =>
        {
            EnsureCommand(request.CommandId);
            EnsureWarehouseAccess(document);
            var hash = PayloadHash("intake", new { request.Name, request.Barcode, request.ProductVariantId,
                request.UnitId, request.UnitName, request.BaseUnitId, request.BaseUnitName, request.Factor,
                request.Quantity, request.CategoryId, request.Note, request.ApproveNow });
            var photo = ReceiptIntakePhoto.Parse(request.PhotoDataUrl);
            if (photo is not null) hash = PayloadHash("intake-photo", new { hash, photoHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo)) });
            if (await IsReplayAsync(document, request.CommandId, hash, ct)) return;
            EnsureRowVersion(document.RowVersion, request.DocumentRowVersion, "Phiếu");
            if (document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder &&
                request.LeaseToken != document.ReceivingLeaseToken)
                throw new BusinessRuleException("Phiên nhận hàng đã thay đổi. Vui lòng mở lại phiếu.");
            await _intakeCatalog.ValidateAsync(document.StoreId, request, ct);
            var quantity = IntakeNumber(request.Quantity, "Số lượng");
            var factor = IntakeNumber(request.Factor, "Tỷ lệ quy đổi");
            IntakeBaseQuantity(quantity, factor);
            var code = NormalizeBarcode(request.Barcode);
            var item = code is null ? null : document.ProvisionalItems.FirstOrDefault(x => !x.IsDeleted &&
                x.Status == StockDocumentProvisionalItemStatus.Unresolved && x.NormalizedBarcode == code);
            if (item is not null && (item.ProposedProductVariantId != request.ProductVariantId ||
                item.ProposedFactor != factor || item.ProposedBaseUnitId != request.BaseUnitId ||
                NormalizeIdentity(item.ProposedBaseUnitName) != NormalizeIdentity(request.BaseUnitName) ||
                NormalizeIdentity(item.UnitNameSnapshot) != NormalizeIdentity(request.UnitName)))
                throw new BusinessRuleException("Mã này đã được ghi nhận với sản phẩm hoặc quy cách khác trong phiếu. Hãy kiểm tra mục cần xử lý.");
            var previousQuantity = item?.Quantity ?? quantity;
            var isNew = item is null;
            var before = item is null ? null : Snapshot(item);
            if (item is null)
            {
                item = new StockDocumentProvisionalItem
                {
                    StoreId = document.StoreId, StockDocument = document, StockDocumentId = document.Id,
                    NameSnapshot = RequiredText(request.Name, 200, "Vui lòng nhập tên hàng."),
                    RawBarcodeSnapshot = NormalizeOptional(request.Barcode, 64), NormalizedBarcode = code,
                    UnitId = request.UnitId, UnitNameSnapshot = request.UnitName,
                    NormalizedUnitNameSnapshot = NormalizeIdentity(request.UnitName),
                    ProposedProductVariantId = request.ProductVariantId,
                    ProposedBaseUnitId = request.BaseUnitId, ProposedBaseUnitName = request.BaseUnitName,
                    ProposedFactor = factor, ProposedCategoryId = request.CategoryId,
                    Quantity = quantity, Note = NormalizeOptional(request.Note, 500), PackagingPhoto = photo,
                    ReceivingRevision = document.ReceivingRevision
                };
                await _repository.AddItemAsync(item, ct);
                await _repository.SaveChangesAsync(ct);
                before = Snapshot(item, StockDocumentProvisionalItemStatus.Removed);
            }
            else
            {
                item.Quantity = IntakeNumber(item.Quantity + quantity, "Số lượng");
                if (photo is not null) item.PackagingPhoto = photo;
            }
            IntakeBaseQuantity(item.Quantity, factor);
            Touch(document);
            if (request.ApproveNow)
            {
                // The controller requires receipt Update + Approve as well as the catalog grants.
                var conversion = await _intakeCatalog.ResolveAsync(document, item, request.CategoryId, permissions, ct);
                await ResolveCoreAsync(document, item, request.CommandId, conversion.ProductVariantId, conversion.Id,
                    !string.IsNullOrWhiteSpace(item.RawBarcodeSnapshot), permissions.CanCreateBarcode,
                    item.ProposedProductVariantId.HasValue ? ProvisionalItemResolutionMethod.LinkExisting : ProvisionalItemResolutionMethod.QuickCreate,
                    PurchaseReceivingActionType.ProvisionalQuickCreate,
                    PurchaseReceiptAuditEventType.ProvisionalItemResolvedQuickCreate, hash, ct);
            }
            else
            {
                await AddActionAsync(document, item, request.CommandId, hash,
                    isNew ? PurchaseReceivingActionType.ProvisionalCapture : PurchaseReceivingActionType.ProvisionalAccumulate,
                    previousQuantity, item.Quantity, isNew, false, before, Snapshot(item), null, ct);
                await AddAuditAsync(document, item, PurchaseReceiptAuditEventType.ProvisionalItemCaptured, ct);
                await _repository.SaveChangesAsync(ct);
            }
        }, ct, lockCatalog: true);

    public Task<ProvisionalReceivingStateDto> UpdateQuantityAsync(int documentId, int itemId,
        UpdateReceiptIntakeQuantityRequest request, CancellationToken ct)
        => WithLockedDocumentAsync(documentId, async document =>
        {
            EnsureCommand(request.CommandId);
            EnsureWarehouseAccess(document);
            var hash = PayloadHash("intake-quantity", new { itemId, request.Quantity });
            if (await IsReplayAsync(document, request.CommandId, hash, ct)) return;
            EnsureRowVersion(document.RowVersion, request.DocumentRowVersion, "Phiếu");
            if (document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder && request.LeaseToken != document.ReceivingLeaseToken)
                throw new BusinessRuleException("Phiên nhận hàng đã thay đổi.");
            var item = RequireUnresolved(document, itemId);
            EnsureRowVersion(item.RowVersion, request.ItemRowVersion, "Hàng nhận");
            if (item.ProposedFactor is not > 0)
                throw new BusinessRuleException("Dòng này chưa khai báo quy đổi.");
            var quantity = IntakeNumber(request.Quantity, "Số lượng");
            IntakeBaseQuantity(quantity, item.ProposedFactor.Value);
            var before = Snapshot(item);
            var previousQuantity = item.Quantity;
            item.Quantity = quantity;
            Touch(document);
            await AddActionAsync(document, item, request.CommandId, hash,
                PurchaseReceivingActionType.ProvisionalEdit, previousQuantity, quantity,
                false, false, before, Snapshot(item), null, ct);
            await AddAuditAsync(document, item, PurchaseReceiptAuditEventType.ProvisionalItemChanged, ct);
            await _repository.SaveChangesAsync(ct);
        }, ct);

    public Task<ProvisionalReceivingStateDto> ReviewIntakeAsync(int documentId, int itemId,
        ReviewReceiptIntakeRequest request, ReceiptIntakePermissions permissions, CancellationToken ct)
        => WithLockedDocumentAsync(documentId, async document =>
        {
            EnsureCommand(request.CommandId);
            EnsureReceiptOwnership(document);
            if (document.Status is not (StockDocumentStatus.PendingApproval or StockDocumentStatus.Draft or StockDocumentStatus.Rejected))
                throw new BusinessRuleException("Phiếu không còn cho phép xử lý hàng mới.");
            if (document.Status != StockDocumentStatus.PendingApproval)
            {
                EnsureWarehouseAccess(document);
                if (document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder && request.LeaseToken != document.ReceivingLeaseToken)
                    throw new BusinessRuleException("Phiên nhận hàng đã thay đổi.");
            }
            var reviewPhoto = ReceiptIntakePhoto.Parse(request.PhotoDataUrl);
            if (reviewPhoto is not null && request.RemoveReviewPhoto)
                throw new BusinessRuleException("Không thể vừa thêm và bỏ ảnh trong cùng yêu cầu.");
            var hash = PayloadHash("intake-review", new { itemId, request.Approve, request.CategoryId, request.SaveDraftOnly,
                request.Completion, request.RemoveReviewPhoto,
                photoHash = reviewPhoto is null ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(reviewPhoto)) });
            if (await IsReplayAsync(document, request.CommandId, hash, ct)) return;
            EnsureRowVersion(document.RowVersion, request.DocumentRowVersion, "Phiếu");
            var item = RequireUnresolved(document, itemId);
            EnsureRowVersion(item.RowVersion, request.ItemRowVersion, "Mặt hàng");
            if (!item.ProposedFactor.HasValue)
                throw new BusinessRuleException("Dòng cũ cần được liên kết sản phẩm theo thông tin đã ghi nhận.");
            var completion = request.Completion ?? (item.ReviewDraftJson is null ? null :
                System.Text.Json.JsonSerializer.Deserialize<ReceiptIntakeCompletionDto>(item.ReviewDraftJson));
            if (request.SaveDraftOnly || request.Approve && completion is not null)
            {
                if (completion is null) throw new BusinessRuleException("Thiếu thông tin cần lưu nháp.");
                if (!request.SaveDraftOnly && completion.GenerateBaseBarcode && !permissions.CanCreateBarcode)
                    throw new BusinessRuleException("Cần quyền tạo barcode để sinh mã đơn vị gốc.");
                IntakeBaseQuantity(IntakeNumber(completion.Quantity, "Số lượng"), IntakeNumber(completion.Factor, "Quy đổi"));
                await _intakeCatalog.ValidateCompletionAsync(document.StoreId, completion, !request.SaveDraftOnly, ct);
                if (completion.ProductVariantId.HasValue && reviewPhoto is not null)
                    throw new BusinessRuleException("Chỉ tải ảnh ở bước tạo sản phẩm mới. Với sản phẩm có sẵn, sửa ảnh trong danh mục sản phẩm.");
                var beforeDraft = Snapshot(item);
                if (request.RemoveReviewPhoto) item.ReviewPhoto = null;
                else if (reviewPhoto is not null) { item.ReviewPhoto = reviewPhoto; completion.UsePackagingPhoto = false; }
                item.ReviewDraftJson = System.Text.Json.JsonSerializer.Serialize(completion);
                if (item.ReviewDraftJson.Length > 8000) throw new BusinessRuleException("Thông tin khai báo quá dài.");
                if (request.SaveDraftOnly)
                {
                    Touch(document);
                    await AddActionAsync(document, item, request.CommandId, hash, PurchaseReceivingActionType.ProvisionalEdit,
                        item.Quantity, item.Quantity, false, false, beforeDraft, Snapshot(item), null, ct);
                    await AddAuditAsync(document, item, PurchaseReceiptAuditEventType.ProvisionalItemChanged, ct);
                    await _repository.SaveChangesAsync(ct);
                    return;
                }
                item.OriginalDeclarationJson ??= System.Text.Json.JsonSerializer.Serialize(new {
                    item.NameSnapshot, item.RawBarcodeSnapshot, item.UnitId, item.UnitNameSnapshot, item.Quantity,
                    item.ProposedProductVariantId, item.ProposedBaseUnitId, item.ProposedBaseUnitName,
                    item.ProposedFactor, item.ProposedCategoryId, item.Note });
                item.NameSnapshot = completion.Name; item.RawBarcodeSnapshot = completion.Barcode;
                item.NormalizedBarcode = NormalizeBarcode(completion.Barcode);
                item.UnitId = completion.UnitId; item.UnitNameSnapshot = completion.UnitName;
                item.NormalizedUnitNameSnapshot = NormalizeIdentity(completion.UnitName);
                item.Quantity = completion.Quantity; item.ProposedFactor = completion.Factor;
                item.ProposedBaseUnitId = completion.BaseUnitId; item.ProposedBaseUnitName = completion.BaseUnitName;
                item.ProposedProductVariantId = completion.ProductVariantId; item.ProposedCategoryId = completion.CategoryId;
                item.Note = completion.Note;
            }
            if (!request.Approve)
            {
                var before = Snapshot(item);
                item.Status = StockDocumentProvisionalItemStatus.Removed;
                item.RemovedAtUtc = DateTime.UtcNow; item.RemovedByUserId = RequireUser();
                Touch(document);
                await AddActionAsync(document, item, request.CommandId, hash,
                    PurchaseReceivingActionType.ProvisionalRemove, item.Quantity, item.Quantity, false, true,
                    before, Snapshot(item), null, ct);
                await AddAuditAsync(document, item, PurchaseReceiptAuditEventType.ProvisionalItemRemoved, ct);
                await _repository.SaveChangesAsync(ct);
                return;
            }
            var conversion = await _intakeCatalog.ResolveAsync(document, item, completion?.CategoryId ?? request.CategoryId, permissions, ct);
            await ResolveCoreAsync(document, item, request.CommandId, conversion.ProductVariantId, conversion.Id,
                !string.IsNullOrWhiteSpace(item.RawBarcodeSnapshot), permissions.CanCreateBarcode,
                item.ProposedProductVariantId.HasValue ? ProvisionalItemResolutionMethod.LinkExisting : ProvisionalItemResolutionMethod.QuickCreate,
                PurchaseReceivingActionType.ProvisionalQuickCreate,
                PurchaseReceiptAuditEventType.ProvisionalItemResolvedQuickCreate, hash, ct);
            if (completion is not null)
                await _intakeCatalog.CompleteAsync(document, item, completion, ct);
        }, ct, lockCatalog: true);

    private static decimal IntakeNumber(decimal value, string label)
    {
        if (value <= 0 || value > 999999999999999.999m || decimal.Round(value, 3) != value)
            throw new BusinessRuleException($"{label} phải lớn hơn 0, trong giới hạn và có tối đa 3 chữ số thập phân.");
        return value;
    }

    private static void IntakeBaseQuantity(decimal quantity, decimal factor)
    {
        if (factor <= 0 || quantity > 999999999999999.999m / factor)
            throw new BusinessRuleException("Số lượng quy đổi vượt giới hạn lưu trữ.");
        IntakeNumber(quantity * factor, "Số lượng quy đổi");
    }
}
