using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoiceItemCatalogMappingService(
    IInputInvoiceRepository repository,
    ICurrentUser currentUser)
    : IInputInvoiceItemCatalogMappingService
{
    public async Task<IReadOnlyDictionary<int, InputInvoiceItemCatalogResolutionDto>>
        ResolveForReceiptAsync(
            int storeId,
            int stockDocumentId,
            CancellationToken ct = default)
    {
        if (storeId <= 0 || stockDocumentId <= 0)
            return new Dictionary<int, InputInvoiceItemCatalogResolutionDto>();

        var receipt = await repository.GetReceiptForSupplierResolutionAsync(
            storeId, stockDocumentId, ct);
        if (receipt is null || receipt.Type != StockDocumentType.Receipt)
            return new Dictionary<int, InputInvoiceItemCatalogResolutionDto>();
        var invoices = await repository.GetByStockDocumentAsync(
            storeId, stockDocumentId, ct);
        var result = new Dictionary<int, InputInvoiceItemCatalogResolutionDto>();
        foreach (var invoice in invoices)
        {
            foreach (var detail in invoice.Details.OrderBy(x => x.LineNo))
            {
                result[detail.Id] = invoice.ResolvedSupplierId is > 0 &&
                    invoice.ResolvedSupplierId == receipt.SupplierId
                    ? await ResolveDetailAsync(
                        storeId, invoice.ResolvedSupplierId.Value, detail, ct)
                    : Unresolved(
                        detail,
                        invoice.ResolvedSupplierId is > 0
                            ? "SupplierReceiptMismatch"
                            : "SupplierUnresolved",
                        invoice.ResolvedSupplierId is > 0
                            ? "Nhà cung cấp canonical của hóa đơn không khớp phiếu nhập."
                            : "Nhà cung cấp canonical của hóa đơn chưa được xác định.");
            }
        }

        return result;
    }

    public async Task<InputInvoiceItemCatalogResolutionDto> ConfirmWithinTransactionAsync(
        int storeId,
        int stockDocumentId,
        int stockDocumentLineId,
        int inputInvoiceDetailId,
        int productVariantId,
        int productUnitConversionId,
        string? expectedMappingRowVersion,
        CancellationToken ct = default)
    {
        var receipt = await repository.GetReceiptForSupplierResolutionAsync(
            storeId, stockDocumentId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
        if (receipt.Type != StockDocumentType.Receipt)
            throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập.");
        PurchaseReceiptWorkflowPolicy.EnsureInputInvoiceMappingEditable(receipt.Status);

        var line = await repository.GetStockDocumentLineAsync(
            storeId, stockDocumentLineId, ct);
        if (line is null || line.StockDocumentId != stockDocumentId)
            throw new BusinessRuleException("Dòng nhập không thuộc phiếu hiện tại.");

        var detail = await repository.GetInputInvoiceDetailAsync(
            storeId,
            stockDocumentId,
            stockDocumentLineId,
            inputInvoiceDetailId,
            ct)
            ?? throw new BusinessRuleException(
                "Dòng XML không thuộc hóa đơn đang liên kết với phiếu hiện tại.");
        var supplierId = detail.InputInvoiceHead?.ResolvedSupplierId
            ?? throw new BusinessRuleException(
                "Nhà cung cấp canonical của hóa đơn chưa được xác định.");
        if (receipt.SupplierId != supplierId)
            throw new BusinessRuleException(
                "Nhà cung cấp canonical của hóa đơn không khớp phiếu nhập.");

        var target = await repository.GetInputInvoiceItemCatalogTargetAsync(
            storeId, productVariantId, productUnitConversionId, ct);
        ValidateTarget(storeId, supplierId, productVariantId,
            productUnitConversionId, target);

        if (line.ProductVariantId != productVariantId)
        {
            return Mismatch(detail,
                "Sản phẩm mapping khác dòng nhập hiện tại. " +
                "Hãy sửa dòng nhập bằng flow hiện có rồi xác nhận mapping lại.");
        }

        var identity = RequireIdentity(detail);
        var xmlUnitTargets = await repository
            .GetInputInvoiceItemCatalogTargetsByUnitAsync(
                storeId, productVariantId, identity.Unit, ct);
        if (xmlUnitTargets.Count != 1 ||
            xmlUnitTargets[0].Conversion.Id != productUnitConversionId)
            throw new BusinessRuleException(xmlUnitTargets.Count > 1
                ? "Đơn vị XML khớp nhiều đơn vị quy đổi; không thể ghi nhớ an toàn."
                : "Đơn vị quy đổi được chọn không khớp đơn vị XML.");

        var mapping = await RememberMappingWithinTransactionAsync(
            storeId, stockDocumentId, stockDocumentLineId, detail,
            supplierId, target!, expectedMappingRowVersion, pricing: false, ct);
        return Confirmed(detail, mapping, target!);
    }

    public async Task<InputInvoiceItemCatalogResolutionDto> ConfirmForPricingAsync(
        int storeId,
        int stockDocumentId,
        ConfirmInputInvoicePricingMappingRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (storeId <= 0 || stockDocumentId <= 0 ||
            request.InputInvoiceHeadId <= 0 || request.InputInvoiceDetailId <= 0 ||
            request.ProductVariantId <= 0 || request.ProductUnitConversionId <= 0)
            throw new BusinessRuleException("Thông tin ghi nhớ sản phẩm XML không hợp lệ.");

        await repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var receipt = await repository.LockReceiptForInputInvoiceMutationAsync(
                storeId, stockDocumentId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            if (receipt.Type != StockDocumentType.Receipt)
                throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập.");
            PurchaseReceiptWorkflowPolicy.EnsureInputInvoiceMappingEditable(receipt.Status);
            ValidateReceiptVersion(receipt.RowVersion, request.ReceiptRowVersion);

            var invoices = await repository.GetByStockDocumentAsync(
                storeId, stockDocumentId, ct);
            if (invoices.Count != 1 || invoices[0].Id != request.InputInvoiceHeadId ||
                invoices[0].StoreId != storeId)
                throw new PurchaseReceiptPricingConflictException(
                    "Hóa đơn XML liên kết đã thay đổi. Vui lòng tải lại trước khi thêm bill.");
            var invoice = invoices[0];
            var detail = invoice.Details.SingleOrDefault(x =>
                x.Id == request.InputInvoiceDetailId &&
                x.InputInvoiceHeadId == invoice.Id && !x.IsDeleted)
                ?? throw new BusinessRuleException(
                    "Dòng XML không thuộc hóa đơn đang liên kết với phiếu hiện tại.");
            var supplierId = invoice.ResolvedSupplierId
                ?? throw new BusinessRuleException(
                    "Nhà cung cấp canonical của hóa đơn chưa được xác định.");
            if (supplierId <= 0 || receipt.SupplierId != supplierId)
                throw new BusinessRuleException(
                    "Nhà cung cấp canonical của hóa đơn không khớp phiếu nhập.");

            var withLines = await repository.GetStockDocumentWithLinesAsync(
                storeId, stockDocumentId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            if (!withLines.Lines.Any(x => !x.IsDeleted &&
                x.ProductVariantId == request.ProductVariantId))
                throw new BusinessRuleException(
                    "Chỉ được chọn sản phẩm có trong hàng thực nhận của phiếu hiện tại.");
            var target = await repository.GetInputInvoiceItemCatalogTargetAsync(
                storeId, request.ProductVariantId, request.ProductUnitConversionId, ct);
            ValidateTarget(storeId, supplierId, request.ProductVariantId,
                request.ProductUnitConversionId, target);

            // An explicit pricing selection can teach XML unit aliases. It only
            // remembers the catalog target; it never pairs a physical receipt line.
            var mapping = await RememberMappingWithinTransactionAsync(
                storeId, stockDocumentId, null, detail, supplierId, target!,
                request.MappingRowVersion, pricing: true, ct);
            await repository.SaveChangesAsync(ct);
            var result = Confirmed(detail, mapping, target!);
            await repository.CommitSupplierResolutionTransactionAsync(ct);
            return result;
        }
        catch
        {
            await repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    private async Task<InputInvoiceItemCatalogMap> RememberMappingWithinTransactionAsync(
        int storeId,
        int stockDocumentId,
        int? stockDocumentLineId,
        InputInvoiceDetail detail,
        int supplierId,
        InputInvoiceItemCatalogTarget target,
        string? expectedMappingRowVersion,
        bool pricing,
        CancellationToken ct)
    {
        var identity = RequireIdentity(detail);
        var productVariantId = target.Variant.Id;
        var productUnitConversionId = target.Conversion.Id;
        await repository.AcquireInputInvoiceItemCatalogKeyLockAsync(
            storeId,
            supplierId,
            identity.Code ?? identity.Name,
            identity.Unit,
            ct);
        var mapping = await repository.GetInputInvoiceItemCatalogMapAsync(
            storeId,
            supplierId,
            identity.Code,
            identity.Name,
            identity.Unit,
            tracking: true,
            ct);

        ValidateExpectedVersion(mapping, expectedMappingRowVersion,
            productVariantId, productUnitConversionId, target, pricing);
        var oldValues = mapping is null ? null : MappingValues(mapping);
        var changed = mapping is null ||
            mapping.ProductVariantId != productVariantId ||
            mapping.ProductUnitConversionId != productUnitConversionId ||
            mapping.ConfirmedUnitId != target!.Unit.Id ||
            mapping.ConfirmedFactor != target.Conversion.Factor ||
            mapping.ConfirmedBaseUnitId != target.BaseUnit.Id;

        if (mapping is null)
        {
            mapping = new InputInvoiceItemCatalogMap
            {
                StoreId = storeId,
                SupplierId = supplierId,
                IsActive = true
            };
            ApplyIdentity(mapping, detail, identity);
            await repository.AddInputInvoiceItemCatalogMapAsync(mapping, ct);
        }

        mapping.ProductVariantId = productVariantId;
        mapping.ProductUnitConversionId = productUnitConversionId;
        mapping.ConfirmedUnitId = target!.Unit.Id;
        mapping.ConfirmedFactor = target.Conversion.Factor;
        mapping.ConfirmedBaseUnitId = target.BaseUnit.Id;
        mapping.IsActive = true;

        if (changed)
        {
            await repository.AddPurchaseReceiptAuditEventAsync(
                CreateConfirmedAudit(
                    storeId, stockDocumentId, stockDocumentLineId,
                    detail, mapping, oldValues, currentUser), ct);
        }

        return mapping;
    }

    private static void ValidateReceiptVersion(byte[] actual, string? expected)
    {
        byte[] posted;
        try { posted = Convert.FromBase64String(expected ?? string.Empty); }
        catch (FormatException) { posted = []; }
        if (posted.Length == 0 || actual is null || !actual.SequenceEqual(posted))
            throw new PurchaseReceiptPricingConflictException(
                "Phiếu nhập đã thay đổi. Vui lòng tải lại trước khi ghi nhớ sản phẩm XML.");
    }

    public async Task AutoApplyKnownMappingsWithinTransactionAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        var resolutions = await ResolveForReceiptAsync(
            storeId, stockDocumentId, ct);
        var invoices = await repository.GetByStockDocumentAsync(
            storeId, stockDocumentId, ct);
        var invoice = invoices.SingleOrDefault(x => x.Id == inputInvoiceHeadId);
        if (invoice is null)
            return;

        var receipt = await repository.GetStockDocumentWithLinesAsync(
            storeId, stockDocumentId, ct);
        if (receipt is null || receipt.Type != StockDocumentType.Receipt ||
            !PurchaseReceiptWorkflowPolicy.CanManageInputInvoiceMapping(receipt.Status))
            return;
        var lineMaps = await repository.GetLineMapsByStockDocumentAsync(
            storeId, stockDocumentId, ct);

        var assignedDetailIds = lineMaps
            .Where(x => x.InputInvoiceDetailId.HasValue)
            .Select(x => x.InputInvoiceDetailId!.Value)
            .ToHashSet();
        var eligibleGroups = invoice.Details
            .Where(x => !assignedDetailIds.Contains(x.Id) &&
                        resolutions.TryGetValue(x.Id, out var value) &&
                        IsEligibleForCurrentDocumentAutoAssociation(value))
            .Select(x => (Detail: x, Resolution: resolutions[x.Id]))
            .GroupBy(x => (
                ProductVariantId: x.Resolution.ProductVariantId!.Value,
                ProductUnitConversionId:
                    x.Resolution.ProductUnitConversionId!.Value))
            .Where(x => x.Count() == 1)
            .ToList();

        foreach (var group in eligibleGroups)
        {
            var candidates = receipt.Lines
                .Where(x =>
                    !x.IsDeleted &&
                    x.ProductVariantId == group.Key.ProductVariantId &&
                    x.ProductUnitConversionId ==
                        group.Key.ProductUnitConversionId)
                .Select(x => (
                    Line: x,
                    Map: lineMaps.SingleOrDefault(map =>
                        map.StockDocumentLineId == x.Id)))
                .Where(x =>
                    x.Map is not null &&
                    !x.Map.InputInvoiceDetailId.HasValue &&
                    x.Map.MatchStatus == InputInvoiceMatchStatus.None)
                .ToList();
            if (candidates.Count != 1)
                continue;

            var line = candidates[0].Line;
            var lineMap = candidates[0].Map!;
            var detail = group.Single().Detail;

            lineMap.UseInputInvoice = true;
            lineMap.InputInvoiceDetailId = detail.Id;
            lineMap.QuantityDifference = line.Quantity - detail.Quantity;
            lineMap.AmountDifference = line.LineTotal - detail.LineAmount;
            lineMap.MatchStatus = ResolveMatchStatus(
                lineMap.QuantityDifference, lineMap.AmountDifference);
            lineMap.Note = "Tự áp dụng mapping danh mục đã được xác nhận.";
            await repository.AddPurchaseReceiptAuditEventAsync(
                CreateAutoAppliedAudit(
                    storeId, stockDocumentId, line.Id, detail,
                    group.Single().Resolution, currentUser), ct);
        }
    }

    private static bool IsEligibleForCurrentDocumentAutoAssociation(
        InputInvoiceItemCatalogResolutionDto resolution)
        => resolution.ProductVariantId.HasValue &&
           resolution.ProductUnitConversionId.HasValue &&
           (resolution.State == InputInvoiceItemCatalogResolutionState.Confirmed ||
            (resolution.State ==
                 InputInvoiceItemCatalogResolutionState.NeedsConfirmation &&
             string.Equals(
                 resolution.ReasonCode,
                 "CrossUnitProductInherited",
                 StringComparison.Ordinal)));

    private async Task<InputInvoiceItemCatalogResolutionDto> ResolveDetailAsync(
        int storeId,
        int supplierId,
        InputInvoiceDetail detail,
        CancellationToken ct)
    {
        var identity = TryIdentity(detail);
        if (identity is null)
            return Unresolved(detail, "MissingItemOrUnitIdentity",
                "Dòng XML thiếu tên hàng hoặc đơn vị để tạo khóa mapping an toàn.");

        var mapping = await repository.GetInputInvoiceItemCatalogMapAsync(
            storeId, supplierId, identity.Value.Code, identity.Value.Name,
            identity.Value.Unit, tracking: false, ct);
        if (mapping is null)
        {
            if (identity.Value.Code is null)
                return Unresolved(detail, "MappingNotFound",
                    "Chưa có mapping cùng nhà cung cấp đã được xác nhận.");
            return await ResolveCrossUnitCandidateAsync(
                storeId, supplierId, detail, identity.Value, ct);
        }

        var target = await repository.GetInputInvoiceItemCatalogTargetAsync(
            storeId, mapping.ProductVariantId,
            mapping.ProductUnitConversionId, ct);
        var invalidReason = TargetInvalidReason(storeId, supplierId,
            mapping.ProductVariantId, mapping.ProductUnitConversionId, target);
        if (invalidReason is not null)
            return NeedsConfirmation(detail, mapping, target,
                invalidReason.Value.Code, invalidReason.Value.Message);
        if (mapping.ConfirmedUnitId != target!.Unit.Id ||
            mapping.ConfirmedFactor != target.Conversion.Factor ||
            mapping.ConfirmedBaseUnitId != target.BaseUnit.Id)
        {
            return NeedsConfirmation(detail, mapping, target,
                "ConversionSignatureDrift",
                "Đơn vị, hệ số hoặc đơn vị gốc đã thay đổi; cần xác nhận mapping lại.");
        }

        if (identity.Value.Code is null)
        {
            return NeedsConfirmation(detail, mapping, target,
                "NameUnitSuggestionRequiresConfirmation",
                "Gợi ý theo tên hàng và đơn vị cần được quản lý xác nhận trước khi áp dụng.");
        }

        return Confirmed(detail, mapping, target);
    }

    private async Task<InputInvoiceItemCatalogResolutionDto>
        ResolveCrossUnitCandidateAsync(
            int storeId,
            int supplierId,
            InputInvoiceDetail detail,
            ItemIdentity identity,
            CancellationToken ct)
    {
        var authoritative = await repository
            .GetActiveInputInvoiceItemCatalogMapsByCodeAsync(
                storeId, supplierId, identity.Code!, ct);
        var valid = new List<(InputInvoiceItemCatalogMap Mapping,
            InputInvoiceItemCatalogTarget Target)>();
        foreach (var mapping in authoritative)
        {
            var mappedTarget = await repository.GetInputInvoiceItemCatalogTargetAsync(
                storeId, mapping.ProductVariantId,
                mapping.ProductUnitConversionId, ct);
            if (TargetInvalidReason(storeId, supplierId,
                    mapping.ProductVariantId,
                    mapping.ProductUnitConversionId,
                    mappedTarget) is null &&
                SignatureMatches(mapping, mappedTarget!))
                valid.Add((mapping, mappedTarget!));
        }

        var productVariants = valid.Select(x => x.Target.Variant.Id)
            .Distinct().ToArray();
        if (productVariants.Length == 0)
            return Unresolved(detail, "MappingNotFound",
                "Chưa có mapping mã hàng cùng nhà cung cấp còn hiệu lực.");
        if (productVariants.Length > 1)
            return Unresolved(detail, "CrossUnitProductAmbiguous",
                "Mã hàng XML đang trỏ tới nhiều sản phẩm; cần quản lý xác nhận.");

        var targets = await repository.GetInputInvoiceItemCatalogTargetsByUnitAsync(
            storeId, productVariants[0], identity.Unit, ct);
        if (targets.Count == 0)
            return Unresolved(detail, "XmlUnitConversionNotFound",
                "Sản phẩm đã nhận diện nhưng chưa có đơn vị quy đổi khớp đơn vị XML.");
        if (targets.Count > 1)
            return Unresolved(detail, "XmlUnitConversionAmbiguous",
                "Đơn vị XML khớp nhiều đơn vị quy đổi; cần quản lý xác nhận.");

        var target = targets[0];
        var candidate = new InputInvoiceItemCatalogMap
        {
            StoreId = storeId,
            SupplierId = supplierId,
            SupplierItemCode = detail.SupplierItemCode,
            NormalizedSupplierItemCode = identity.Code,
            SupplierItemName = detail.ItemName.Trim(),
            NormalizedSupplierItemName = identity.Name,
            SupplierUnitName = detail.UnitName!.Trim(),
            NormalizedSupplierUnitName = identity.Unit,
            ProductVariantId = target.Variant.Id,
            ProductUnitConversionId = target.Conversion.Id,
            ConfirmedUnitId = target.Unit.Id,
            ConfirmedFactor = target.Conversion.Factor,
            ConfirmedBaseUnitId = target.BaseUnit.Id,
            IsActive = true
        };
        return NeedsConfirmation(detail, candidate, target,
            "CrossUnitProductInherited",
            "Đã nhận diện sản phẩm từ mã hàng; có thể ghi nhớ đơn vị XML sau khi quản lý xác nhận.");
    }

    private static void ValidateTarget(
        int storeId,
        int supplierId,
        int productVariantId,
        int productUnitConversionId,
        InputInvoiceItemCatalogTarget? target)
    {
        var invalid = TargetInvalidReason(storeId, supplierId,
            productVariantId, productUnitConversionId, target);
        if (invalid is not null)
            throw new BusinessRuleException(invalid.Value.Message);
    }

    private static (string Code, string Message)? TargetInvalidReason(
        int storeId,
        int supplierId,
        int productVariantId,
        int productUnitConversionId,
        InputInvoiceItemCatalogTarget? target)
    {
        if (target is null)
            return ("TargetNotFound", "Sản phẩm hoặc đơn vị quy đổi không tồn tại.");
        if (target.Variant.Id != productVariantId ||
            target.Conversion.Id != productUnitConversionId ||
            target.Conversion.ProductVariantId != target.Variant.Id)
            return ("TargetRelationInvalid", "Đơn vị quy đổi không thuộc sản phẩm đã chọn.");
        if (target.Variant.StoreId != storeId ||
            target.Product.StoreId != storeId ||
            target.Conversion.StoreId != storeId ||
            target.Unit.StoreId != storeId ||
            target.BaseUnit.StoreId != storeId)
            return ("CrossStoreTarget", "Sản phẩm/đơn vị không thuộc cửa hàng hiện tại.");
        if (!target.Product.IsActive || !target.Variant.IsActive ||
            !target.Conversion.IsActive || !target.Unit.IsActive ||
            !target.BaseUnit.IsActive || target.Conversion.Factor <= 0m)
            return ("TargetInactive", "Sản phẩm hoặc đơn vị quy đổi không còn hợp lệ.");
        if (target.Product.BaseUnitId != target.BaseUnit.Id ||
            target.Conversion.UnitId != target.Unit.Id)
            return ("TargetSignatureInvalid", "Quan hệ đơn vị của sản phẩm không hợp lệ.");
        return null;
    }

    private static bool SignatureMatches(
        InputInvoiceItemCatalogMap mapping,
        InputInvoiceItemCatalogTarget target)
        => mapping.ProductVariantId == target.Variant.Id &&
           mapping.ProductUnitConversionId == target.Conversion.Id &&
           mapping.ConfirmedUnitId == target.Unit.Id &&
           mapping.ConfirmedFactor == target.Conversion.Factor &&
           mapping.ConfirmedBaseUnitId == target.BaseUnit.Id;

    private static void ValidateExpectedVersion(
        InputInvoiceItemCatalogMap? mapping,
        string? expected,
        int productVariantId,
        int productUnitConversionId,
        InputInvoiceItemCatalogTarget target,
        bool pricing = false)
    {
        if (mapping is null)
        {
            if (!string.IsNullOrWhiteSpace(expected))
                throw MappingConflict(
                    "Mapping đã thay đổi. Hãy tải lại trước khi xác nhận.", pricing);
            return;
        }

        var sameTarget = mapping.ProductVariantId == productVariantId &&
            mapping.ProductUnitConversionId == productUnitConversionId &&
            mapping.ConfirmedUnitId == target.Unit.Id &&
            mapping.ConfirmedFactor == target.Conversion.Factor &&
            mapping.ConfirmedBaseUnitId == target.BaseUnit.Id;
        if (string.IsNullOrWhiteSpace(expected))
        {
            if (!sameTarget)
                throw MappingConflict(
                    "Mapping đã được người khác xác nhận. Hãy tải lại trước khi đổi mapping.", pricing);
            return;
        }

        byte[] posted;
        try
        {
            posted = Convert.FromBase64String(expected);
        }
        catch (FormatException)
        {
            throw MappingConflict("Phiên bản mapping không hợp lệ.", pricing);
        }
        if (mapping.RowVersion is null ||
            !mapping.RowVersion.SequenceEqual(posted))
            throw MappingConflict(
                "Mapping đã được người khác cập nhật. Hãy tải lại trước khi xác nhận.", pricing);
    }

    private static BusinessRuleException MappingConflict(string message, bool pricing)
        => pricing ? new PurchaseReceiptPricingConflictException(message)
            : new BusinessRuleException(message);

    private static ItemIdentity RequireIdentity(InputInvoiceDetail detail)
        => TryIdentity(detail) ?? throw new BusinessRuleException(
            "Dòng XML thiếu tên hàng hoặc đơn vị để xác nhận mapping an toàn.");

    private static ItemIdentity? TryIdentity(InputInvoiceDetail detail)
    {
        var name = detail.NormalizedItemName ??
            InputInvoiceItemIdentityNormalizer.NormalizeText(detail.ItemName);
        var unit = detail.NormalizedUnitName ??
            InputInvoiceItemIdentityNormalizer.NormalizeText(detail.UnitName);
        if (name is null || unit is null)
            return null;
        var code = detail.NormalizedSupplierItemCode ??
            InputInvoiceItemIdentityNormalizer.NormalizeCode(detail.SupplierItemCode);
        return new ItemIdentity(code, name, unit);
    }

    private static void ApplyIdentity(
        InputInvoiceItemCatalogMap mapping,
        InputInvoiceDetail detail,
        ItemIdentity identity)
    {
        mapping.SupplierItemCode = detail.SupplierItemCode;
        mapping.NormalizedSupplierItemCode = identity.Code;
        mapping.SupplierItemName = detail.ItemName.Trim();
        mapping.NormalizedSupplierItemName = identity.Name;
        mapping.SupplierUnitName = detail.UnitName!.Trim();
        mapping.NormalizedSupplierUnitName = identity.Unit;
    }

    private static InputInvoiceItemCatalogResolutionDto Confirmed(
        InputInvoiceDetail detail,
        InputInvoiceItemCatalogMap mapping,
        InputInvoiceItemCatalogTarget target)
        => MappingResult(detail, mapping, target,
            InputInvoiceItemCatalogResolutionState.Confirmed, null, null);

    private static InputInvoiceItemCatalogResolutionDto NeedsConfirmation(
        InputInvoiceDetail detail,
        InputInvoiceItemCatalogMap mapping,
        InputInvoiceItemCatalogTarget? target,
        string reason,
        string message)
        => MappingResult(detail, mapping, target,
            InputInvoiceItemCatalogResolutionState.NeedsConfirmation,
            reason, message);

    private static InputInvoiceItemCatalogResolutionDto Unresolved(
        InputInvoiceDetail detail,
        string reason,
        string message)
        => new()
        {
            InputInvoiceDetailId = detail.Id,
            State = InputInvoiceItemCatalogResolutionState.NeedsConfirmation,
            ReasonCode = reason,
            Message = message,
            SupplierItemCode = detail.SupplierItemCode,
            ItemName = detail.ItemName,
            UnitName = detail.UnitName,
            XmlQuantity = detail.Quantity
        };

    private static InputInvoiceItemCatalogResolutionDto Mismatch(
        InputInvoiceDetail detail,
        string message)
        => new()
        {
            InputInvoiceDetailId = detail.Id,
            State = InputInvoiceItemCatalogResolutionState.NeedsConfirmation,
            ReasonCode = "ReceiptLineTargetMismatch",
            Message = message,
            SupplierItemCode = detail.SupplierItemCode,
            ItemName = detail.ItemName,
            UnitName = detail.UnitName,
            XmlQuantity = detail.Quantity
        };

    private static InputInvoiceItemCatalogResolutionDto MappingResult(
        InputInvoiceDetail detail,
        InputInvoiceItemCatalogMap mapping,
        InputInvoiceItemCatalogTarget? target,
        InputInvoiceItemCatalogResolutionState state,
        string? reason,
        string? message)
        => new()
        {
            InputInvoiceDetailId = detail.Id,
            MappingId = mapping.Id > 0 ? mapping.Id : null,
            State = state,
            ReasonCode = reason,
            Message = message,
            SupplierItemCode = detail.SupplierItemCode,
            ItemName = detail.ItemName,
            UnitName = detail.UnitName,
            XmlQuantity = detail.Quantity,
            ProductVariantId = mapping.ProductVariantId,
            ProductName = target?.Variant.ProductVariantName ?? target?.Product.Name,
            VariantSku = target?.Variant.Sku,
            ProductUnitConversionId = mapping.ProductUnitConversionId,
            ConfirmedUnitId = target?.Unit.Id ?? mapping.ConfirmedUnitId,
            ConfirmedUnitName = target?.Unit.Name ?? mapping.ConfirmedUnit?.Name,
            ConfirmedFactor = target?.Conversion.Factor ?? mapping.ConfirmedFactor,
            ConfirmedBaseUnitId = target?.BaseUnit.Id ?? mapping.ConfirmedBaseUnitId,
            ConfirmedBaseUnitName = target?.BaseUnit.Name ?? mapping.ConfirmedBaseUnit?.Name,
            IsDefaultForSale = target?.Conversion.IsDefaultForSale ?? false,
            MappingRowVersion = mapping.RowVersion is { Length: > 0 }
                ? Convert.ToBase64String(mapping.RowVersion)
                : null
        };

    private static PurchaseReceiptAuditEvent CreateConfirmedAudit(
        int storeId,
        int receiptId,
        int? lineId,
        InputInvoiceDetail detail,
        InputInvoiceItemCatalogMap mapping,
        Dictionary<string, object?>? oldValues,
        ICurrentUser actor)
        => new()
        {
            StoreId = storeId,
            StockDocumentId = receiptId,
            StockDocumentLineId = lineId,
            EventType = PurchaseReceiptAuditEventType.InputInvoiceItemMappingConfirmed,
            ActorUserId = actor.UserId ?? 0,
            ActorUserName = actor.UserName,
            OccurredAtUtc = DateTime.UtcNow,
            IsSuccess = true,
            Note = "Xác nhận mapping dòng XML với danh mục sản phẩm.",
            ChangedFieldsJson = PurchaseReceiptAuditEvidence.SerializeChangedFields(
                [nameof(InputInvoiceItemCatalogMap.ProductVariantId),
                 nameof(InputInvoiceItemCatalogMap.ProductUnitConversionId),
                 nameof(InputInvoiceItemCatalogMap.ConfirmedUnitId),
                 nameof(InputInvoiceItemCatalogMap.ConfirmedFactor),
                 nameof(InputInvoiceItemCatalogMap.ConfirmedBaseUnitId)]),
            OldValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                oldValues ?? new Dictionary<string, object?>()),
            NewValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                MappingValues(mapping, detail))
        };

    private static PurchaseReceiptAuditEvent CreateAutoAppliedAudit(
        int storeId,
        int receiptId,
        int lineId,
        InputInvoiceDetail detail,
        InputInvoiceItemCatalogResolutionDto resolution,
        ICurrentUser actor)
        => new()
        {
            StoreId = storeId,
            StockDocumentId = receiptId,
            StockDocumentLineId = lineId,
            EventType = PurchaseReceiptAuditEventType.InputInvoiceItemMappingAutoApplied,
            ActorUserId = actor.UserId ?? 0,
            ActorUserName = actor.UserName,
            OccurredAtUtc = DateTime.UtcNow,
            IsSuccess = true,
            Note = "Tự áp dụng mapping danh mục đã được Human xác nhận.",
            ChangedFieldsJson = PurchaseReceiptAuditEvidence.SerializeChangedFields(
                [nameof(StockDocumentLineInputInvoiceMap.InputInvoiceDetailId)]),
            NewValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    [nameof(StockDocumentLineInputInvoiceMap.InputInvoiceDetailId)] = detail.Id,
                    [nameof(InputInvoiceItemCatalogMap.Id)] = resolution.MappingId,
                    [nameof(InputInvoiceItemCatalogMap.ProductVariantId)] = resolution.ProductVariantId,
                    [nameof(InputInvoiceItemCatalogMap.ProductUnitConversionId)] = resolution.ProductUnitConversionId,
                    [nameof(InputInvoiceItemCatalogMap.ConfirmedFactor)] = resolution.ConfirmedFactor,
                    [nameof(InputInvoiceItemCatalogMap.ConfirmedBaseUnitId)] = resolution.ConfirmedBaseUnitId
                })
        };

    private static Dictionary<string, object?> MappingValues(
        InputInvoiceItemCatalogMap mapping,
        InputInvoiceDetail? detail = null)
        => new()
        {
            [nameof(InputInvoiceItemCatalogMap.Id)] = mapping.Id > 0
                ? mapping.Id
                : null,
            [nameof(InputInvoiceDetail.InputInvoiceHeadId)] = detail?.InputInvoiceHeadId,
            [nameof(InputInvoiceItemCatalogResolutionDto.InputInvoiceDetailId)] = detail?.Id,
            [nameof(InputInvoiceItemCatalogMap.SupplierId)] = mapping.SupplierId,
            [nameof(InputInvoiceItemCatalogMap.SupplierItemCode)] = mapping.SupplierItemCode,
            [nameof(InputInvoiceItemCatalogMap.NormalizedSupplierItemCode)] = mapping.NormalizedSupplierItemCode,
            [nameof(InputInvoiceItemCatalogMap.NormalizedSupplierItemName)] = mapping.NormalizedSupplierItemName,
            [nameof(InputInvoiceItemCatalogMap.NormalizedSupplierUnitName)] = mapping.NormalizedSupplierUnitName,
            [nameof(InputInvoiceItemCatalogMap.ProductVariantId)] = mapping.ProductVariantId,
            [nameof(InputInvoiceItemCatalogMap.ProductUnitConversionId)] = mapping.ProductUnitConversionId,
            [nameof(InputInvoiceItemCatalogMap.ConfirmedUnitId)] = mapping.ConfirmedUnitId,
            [nameof(InputInvoiceItemCatalogMap.ConfirmedFactor)] = mapping.ConfirmedFactor,
            [nameof(InputInvoiceItemCatalogMap.ConfirmedBaseUnitId)] = mapping.ConfirmedBaseUnitId
        };

    private static InputInvoiceMatchStatus ResolveMatchStatus(
        decimal quantityDifference,
        decimal amountDifference)
    {
        var quantityMismatch = Math.Abs(quantityDifference) > 0.0001m;
        var amountMismatch = Math.Abs(amountDifference) > 1m;
        if (quantityMismatch && amountMismatch)
            return InputInvoiceMatchStatus.QuantityAndAmountMismatch;
        if (quantityMismatch)
            return InputInvoiceMatchStatus.QuantityMismatch;
        if (amountMismatch)
            return InputInvoiceMatchStatus.AmountMismatch;
        return InputInvoiceMatchStatus.Matched;
    }

    private readonly record struct ItemIdentity(
        string? Code,
        string Name,
        string Unit);
}
