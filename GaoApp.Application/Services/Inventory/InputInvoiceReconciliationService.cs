using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoiceReconciliationService(
    IInputInvoiceRepository repository,
    IInputInvoiceItemCatalogMappingService itemCatalogMapping,
    ICurrentUser currentUser,
    IStockDocumentRepository? stockDocuments = null) : IInputInvoiceReconciliationService
{
    public async Task<InputInvoiceReconciliationDto> CalculateAsync(
        int storeId, int stockDocumentId, CancellationToken ct = default)
    {
        var receipt = await repository.LockReceiptForReconciliationAsync(
            storeId, stockDocumentId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
        var existing = await repository.GetReconciliationsAsync(
            storeId, stockDocumentId, tracking: false, ct);
        return await BuildAsync(receipt, existing.SingleOrDefault(), ct);
    }

    public async Task<InputInvoiceReconciliationDto> PreviewCommercialAsync(
        int storeId, int stockDocumentId,
        InputInvoiceCommercialPreviewRequest request,
        CancellationToken ct = default)
    {
        if (stockDocuments is null)
            throw new BusinessRuleException("Dịch vụ xem trước đối chiếu chưa sẵn sàng.");
        if (request.StockDocumentId != stockDocumentId)
            throw new BusinessRuleException("Phiếu xem trước không khớp với đường dẫn yêu cầu.");

        var receipt = await repository.LockReceiptForReconciliationAsync(
            storeId, stockDocumentId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
        if (receipt.StoreId != storeId)
            throw new BusinessRuleException("Phiếu nhập không thuộc cửa hàng hiện tại.");
        if (receipt.Type != StockDocumentType.Receipt)
            throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập kho.");
        if (receipt.Status != StockDocumentStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ phiếu đang chờ duyệt mới được xem trước đối chiếu thương mại.");
        EnsureRowVersion(receipt.RowVersion, request.RowVersion);

        var activeLines = receipt.Lines.Where(x => !x.IsDeleted)
            .OrderBy(x => x.Id).ToList();
        if (activeLines.Count == 0)
            throw new BusinessRuleException("Phiếu nhập chưa có dòng hàng.");
        if (request.Lines is null || request.Lines.Count != activeLines.Count ||
            request.Lines.Any(x => x.StockDocumentLineId <= 0) ||
            request.Lines.Select(x => x.StockDocumentLineId).Distinct().Count() !=
            request.Lines.Count)
            throw new BusinessRuleException("Danh sách dòng xem trước không hợp lệ.");

        var requestLines = request.Lines.ToDictionary(x => x.StockDocumentLineId);
        if (activeLines.Any(x => !requestLines.ContainsKey(x.Id)) ||
            requestLines.Keys.Any(id => activeLines.All(x => x.Id != id)))
            throw new BusinessRuleException(
                "Dòng phiếu đã thay đổi. Vui lòng tải lại trước khi xem đối chiếu.");

        var taxes = new Dictionary<int, Tax>();
        var commercialAmounts = new Dictionary<int, PurchasePricingPolicy.LineAmounts>();
        foreach (var line in activeLines)
        {
            var input = requestLines[line.Id];
            if (input.UnitPriceBeforeVat <= 0m)
                throw new BusinessRuleException(
                    $"Giá nhập trước VAT dòng {line.LineNo} phải lớn hơn 0.");

            Tax? tax = null;
            if (request.HasVat)
            {
                if (input.TaxId is not > 0)
                    throw new BusinessRuleException(
                        $"Dòng {line.LineNo} phải chọn thuế suất để xem trước.");
                if (!taxes.TryGetValue(input.TaxId.Value, out tax))
                {
                    tax = await stockDocuments.GetTaxAsync(input.TaxId.Value, ct);
                    if (tax is null || tax.StoreId != storeId || !tax.IsActive ||
                        tax.Rate is < 0m or > 100m)
                        throw new BusinessRuleException(
                            $"Thuế suất dòng {line.LineNo} không hợp lệ hoặc không còn hoạt động.");
                    taxes[tax.Id] = tax;
                }
            }

            try
            {
                commercialAmounts[line.Id] =
                    PurchasePricingPolicy.CalculateLineFromBeforeVat(
                        line.Quantity, input.UnitPriceBeforeVat,
                        request.HasVat, tax?.Rate ?? 0m);
            }
            catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
            {
                throw new BusinessRuleException(
                    $"Không thể tính giá và VAT dòng {line.LineNo} từ dữ liệu hiện tại.");
            }
        }

        var existing = await repository.GetReconciliationsAsync(
            storeId, stockDocumentId, tracking: false, ct);
        if (existing.Count > 1)
            throw new BusinessRuleException("Phiếu có nhiều snapshot đối chiếu không hợp lệ.");
        var result = await BuildAsync(receipt, existing.SingleOrDefault(), ct,
            commercialAmounts);
        result.IsCommercialPreview = true;
        return result;
    }

    public async Task<InputInvoiceReconciliationDto> GetForReceiptAsync(
        int storeId, int stockDocumentId, CancellationToken ct = default)
    {
        await repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var receipt = await repository.LockReceiptForReconciliationAsync(
                storeId, stockDocumentId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            var existing = await repository.GetReconciliationsAsync(
                storeId, stockDocumentId, tracking: true, ct);
            if (receipt.Status == StockDocumentStatus.Confirmed)
            {
                if (existing.Count > 1)
                    throw new BusinessRuleException(
                        "Phiếu đã duyệt có nhiều snapshot đối chiếu không hợp lệ.");
                var frozen = existing.Count == 1
                    ? ToDto(existing[0], receipt)
                    : await BuildAsync(receipt, null, ct);
                if (existing.Count == 1)
                {
                    var itemMappings = await itemCatalogMapping.ResolveForReceiptAsync(
                        receipt.StoreId, receipt.Id, ct);
                    ApplyProductSummaries(frozen, receipt, itemMappings);
                }
                if (existing.Count == 0 &&
                    frozen.State != InputInvoiceReconciliationState.NotApplicable)
                    frozen.EvidenceFingerprint =
                        InputInvoiceReconciliationPolicy.Fingerprint(frozen);
                await repository.CommitSupplierResolutionTransactionAsync(ct);
                return frozen;
            }

            var result = await RefreshCoreAsync(receipt, existing, ct);
            await repository.CommitSupplierResolutionTransactionAsync(ct);
            return result;
        }
        catch
        {
            await repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    public async Task<InputInvoiceReconciliationDto> RefreshWithinTransactionAsync(
        int storeId, int stockDocumentId, CancellationToken ct = default)
    {
        var receipt = await repository.LockReceiptForReconciliationAsync(
            storeId, stockDocumentId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
        var existing = await repository.GetReconciliationsAsync(
            storeId, stockDocumentId, tracking: true, ct);
        if (receipt.Status == StockDocumentStatus.Confirmed && existing.Count == 1)
        {
            var frozen = ToDto(existing[0], receipt);
            var itemMappings = await itemCatalogMapping.ResolveForReceiptAsync(
                receipt.StoreId, receipt.Id, ct);
            ApplyProductSummaries(frozen, receipt, itemMappings);
            return frozen;
        }
        return await RefreshCoreAsync(receipt, existing, ct);
    }

    public async Task<InputInvoiceReconciliationDto> IgnoreXmlDetailWithinTransactionAsync(
        int storeId, int stockDocumentId, int inputInvoiceDetailId,
        bool ignored, string? reason, CancellationToken ct = default)
    {
        if (ignored && string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException("Vui lòng nhập lý do bỏ qua dòng XML.");
        await repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var receipt = await RequireEditableLockedReceiptAsync(storeId, stockDocumentId, ct);
            if (receipt.LineInputInvoiceMaps.Any(x =>
                    x.UseInputInvoice && x.InputInvoiceDetailId == inputInvoiceDetailId))
                throw new BusinessRuleException(
                    "Dòng XML đang được map với dòng nhập nên không thể bỏ qua.");

            var existing = await repository.GetReconciliationsAsync(
                storeId, stockDocumentId, tracking: true, ct);
            if (existing.Count == 0)
            {
                _ = await RefreshCoreAsync(receipt, existing, ct);
                existing = await repository.GetReconciliationsAsync(
                    storeId, stockDocumentId, tracking: true, ct);
            }
            var snapshot = existing.SelectMany(x => x.Details)
                .SingleOrDefault(x => x.InputInvoiceDetailId == inputInvoiceDetailId)
                ?? throw new BusinessRuleException(
                    "Dòng XML không thuộc hóa đơn đang liên kết với phiếu hiện tại.");
            if (snapshot.IsIgnored == ignored &&
                (!ignored || string.Equals(snapshot.IgnoreReason, reason?.Trim(),
                    StringComparison.Ordinal)))
            {
                var same = await RefreshCoreAsync(receipt, existing, ct);
                await repository.CommitSupplierResolutionTransactionAsync(ct);
                return same;
            }

            var previousState = existing.Single().OverallState.ToString();
            snapshot.IsIgnored = ignored;
            snapshot.IgnoreReason = ignored ? reason!.Trim() : null;
            var result = await RefreshCoreAsync(receipt, existing, ct);
            await AddAuditAsync(receipt, ignored
                    ? PurchaseReceiptAuditEventType.InputInvoiceXmlDetailIgnored
                    : PurchaseReceiptAuditEventType.InputInvoiceXmlDetailUnignored,
                reason, null, inputInvoiceDetailId, result, ct,
                previousState, result.StateName);
            await repository.SaveChangesAsync(ct);
            await repository.CommitSupplierResolutionTransactionAsync(ct);
            return result;
        }
        catch
        {
            await repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    public async Task<InputInvoiceReconciliationDto> SetReceiptLineExcludedWithinTransactionAsync(
        int storeId, int stockDocumentId, int stockDocumentLineId,
        bool excluded, string? reason, CancellationToken ct = default)
    {
        if (excluded && string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException("Vui lòng nhập lý do loại dòng nhập khỏi hóa đơn XML.");
        var receipt = await RequireEditableLockedReceiptAsync(storeId, stockDocumentId, ct);
        var previousState = (await repository.GetReconciliationsAsync(
                storeId, stockDocumentId, tracking: true, ct))
            .SingleOrDefault()?.OverallState.ToString();
        var map = receipt.LineInputInvoiceMaps.SingleOrDefault(x =>
            x.StockDocumentLineId == stockDocumentLineId)
            ?? throw new BusinessRuleException("Không tìm thấy map dòng nhập.");
        var changed = map.UseInputInvoice == excluded ||
            !string.Equals(map.ExclusionReason, excluded ? reason?.Trim() : null,
                StringComparison.Ordinal);
        map.UseInputInvoice = !excluded;
        if (excluded)
        {
            map.InputInvoiceDetailId = null;
            map.MatchStatus = InputInvoiceMatchStatus.Excluded;
            map.QuantityDifference = 0m;
            map.AmountDifference = 0m;
            map.ExclusionReason = reason!.Trim();
        }
        else
        {
            map.ExclusionReason = null;
            if (!map.InputInvoiceDetailId.HasValue)
                map.MatchStatus = InputInvoiceMatchStatus.None;
        }
        var result = await RefreshWithinTransactionAsync(storeId, stockDocumentId, ct);
        if (changed)
        {
            await AddAuditAsync(receipt, excluded
                    ? PurchaseReceiptAuditEventType.InputInvoiceReceiptLineExcluded
                    : PurchaseReceiptAuditEventType.InputInvoiceReceiptLineIncluded,
                reason, stockDocumentLineId, null, result, ct,
                previousState, result.StateName);
            await repository.SaveChangesAsync(ct);
        }
        return result;
    }

    public async Task<InputInvoiceReconciliationDto> AcceptMismatchWithinTransactionAsync(
        int storeId, int stockDocumentId, string? reason,
        string? expectedEvidenceFingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException("Vui lòng nhập lý do chấp nhận chênh lệch.");
        if (!currentUser.IsAuthenticated || currentUser.UserId is not > 0)
            throw new BusinessRuleException("Không xác định được người duyệt đối chiếu.");
        await repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var receipt = await RequireEditableLockedReceiptAsync(storeId, stockDocumentId, ct);
            var current = await RefreshWithinTransactionAsync(storeId, stockDocumentId, ct);
            if (!string.IsNullOrWhiteSpace(expectedEvidenceFingerprint) &&
                !string.Equals(current.EvidenceFingerprint, expectedEvidenceFingerprint,
                    StringComparison.Ordinal))
                throw new BusinessRuleException(
                    "Dữ liệu đối chiếu đã thay đổi. Vui lòng tải lại trước khi chấp nhận.");
            if (current.State != InputInvoiceReconciliationState.Mismatch)
                throw new BusinessRuleException(current.State == InputInvoiceReconciliationState.Incomplete
                    ? "Đối chiếu chưa đầy đủ nên không thể chấp nhận chênh lệch."
                    : "Chỉ trạng thái có chênh lệch mới cần chấp nhận.");

            var row = (await repository.GetReconciliationsAsync(
                storeId, stockDocumentId, tracking: true, ct)).Single();
            row.AcceptedEvidenceFingerprint = current.EvidenceFingerprint;
            row.AcceptanceReason = reason.Trim();
            row.AcceptedByUserId = currentUser.UserId;
            row.AcceptedAtUtc = DateTime.UtcNow;
            row.OverallState = InputInvoiceReconciliationState.AcceptedMismatch;
            await AddAuditAsync(receipt,
                PurchaseReceiptAuditEventType.InputInvoiceReconciliationAccepted,
                reason, null, null, current, ct,
                InputInvoiceReconciliationState.Mismatch.ToString(),
                InputInvoiceReconciliationState.AcceptedMismatch.ToString());
            await repository.SaveChangesAsync(ct);
            current.State = InputInvoiceReconciliationState.AcceptedMismatch;
            current.AcceptanceReason = row.AcceptanceReason;
            current.AcceptedByUserId = row.AcceptedByUserId;
            current.AcceptedAtUtc = row.AcceptedAtUtc;
            await repository.CommitSupplierResolutionTransactionAsync(ct);
            return current;
        }
        catch
        {
            await repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    public async Task<InputInvoiceReconciliationDto> EnsureConfirmableWithinTransactionAsync(
        int storeId, int stockDocumentId, CancellationToken ct = default)
    {
        var result = await RefreshWithinTransactionAsync(storeId, stockDocumentId, ct);
        if (!result.ConfirmReady)
            throw new BusinessRuleException(result.State == InputInvoiceReconciliationState.Incomplete
                ? "Đối chiếu hóa đơn XML chưa đầy đủ. Vui lòng hoàn tất trước khi duyệt phiếu."
                : "Đối chiếu hóa đơn XML còn chênh lệch chưa được quản lý chấp nhận.");
        return result;
    }

    public async Task InvalidateWithinTransactionAsync(
        int storeId, int stockDocumentId, string reason, CancellationToken ct = default)
    {
        var rows = await repository.GetReconciliationsAsync(
            storeId, stockDocumentId, tracking: true, ct);
        var receipt = await repository.LockReceiptForReconciliationAsync(
            storeId, stockDocumentId, ct);
        foreach (var row in rows.Where(x => x.AcceptedEvidenceFingerprint is not null))
        {
            var previousState = row.OverallState;
            row.AcceptedEvidenceFingerprint = null;
            row.AcceptanceReason = null;
            row.AcceptedByUserId = null;
            row.AcceptedAtUtc = null;
            if (row.OverallState == InputInvoiceReconciliationState.AcceptedMismatch)
                row.OverallState = InputInvoiceReconciliationState.Mismatch;
            if (receipt is not null)
            {
                var snapshot = ToDto(row, receipt);
                await AddAuditAsync(receipt,
                    PurchaseReceiptAuditEventType.InputInvoiceReconciliationAcceptanceInvalidated,
                    reason, null, null, snapshot, ct,
                    previousState.ToString(), row.OverallState.ToString());
            }
        }
        await repository.SaveChangesAsync(ct);
    }

    private async Task<InputInvoiceReconciliationDto> RefreshCoreAsync(
        StockDocument receipt,
        List<StockDocumentInputInvoiceReconciliation> existing,
        CancellationToken ct)
    {
        var activeLinks = receipt.InputInvoiceMaps.Where(x => !x.IsDeleted).ToList();
        if (activeLinks.Count == 0)
        {
            if (existing.Count > 0)
            {
                await repository.DeleteReconciliationsAsync(
                    receipt.StoreId, receipt.Id, null, ct);
                await repository.SaveChangesAsync(ct);
            }
            return NotApplicable(receipt);
        }
        if (activeLinks.Count != 1)
            throw new BusinessRuleException(
                "Phiếu nhập đang liên kết nhiều hơn một hóa đơn; không thể đối chiếu an toàn.");

        var link = activeLinks[0];
        var old = existing.SingleOrDefault(x =>
            x.StockDocumentInputInvoiceMapId == link.Id);
        var calculated = await BuildAsync(receipt, old, ct);
        var baseState = calculated.State;
        calculated.EvidenceFingerprint = InputInvoiceReconciliationPolicy.Fingerprint(calculated);
        var acceptanceStillValid = baseState == InputInvoiceReconciliationState.Mismatch &&
            old?.AcceptedEvidenceFingerprint is not null &&
            string.Equals(old.AcceptedEvidenceFingerprint,
                calculated.EvidenceFingerprint, StringComparison.Ordinal);
        if (acceptanceStillValid)
            calculated.State = InputInvoiceReconciliationState.AcceptedMismatch;

        var row = old ?? new StockDocumentInputInvoiceReconciliation
        {
            StoreId = receipt.StoreId,
            StockDocumentInputInvoiceMapId = link.Id,
            StockDocumentId = receipt.Id,
            InputInvoiceHeadId = link.InputInvoiceHeadId
        };
        var previousState = row.OverallState;
        var invalidated = old?.AcceptedEvidenceFingerprint is not null &&
            !acceptanceStillValid;
        Apply(row, calculated);
        if (acceptanceStillValid)
        {
            calculated.AcceptanceReason = row.AcceptanceReason;
            calculated.AcceptedByUserId = row.AcceptedByUserId;
            calculated.AcceptedAtUtc = row.AcceptedAtUtc;
        }
        else
        {
            row.AcceptedEvidenceFingerprint = null;
            row.AcceptanceReason = null;
            row.AcceptedByUserId = null;
            row.AcceptedAtUtc = null;
        }

        var now = DateTime.UtcNow;
        foreach (var detail in row.Details.Where(x => !x.IsDeleted).ToList())
        {
            detail.IsDeleted = true;
            detail.DeletedAtUtc = now;
            detail.DeletedBy = currentUser.UserId;
        }
        if (old is not null && row.Details.Any(x => x.IsDeleted))
            await repository.SaveChangesAsync(ct);
        var detailEntities = calculated.Details.Select(x => ToEntity(receipt, link, row, x))
            .ToList();
        if (old is null)
        {
            foreach (var entity in detailEntities) row.Details.Add(entity);
            await repository.AddReconciliationAsync(row, ct);
        }
        else
        {
            await repository.AddDetailReconciliationsAsync(detailEntities, ct);
        }

        if (invalidated)
        {
            await AddAuditAsync(receipt,
                PurchaseReceiptAuditEventType.InputInvoiceReconciliationAcceptanceInvalidated,
                "Dữ liệu đối chiếu đã thay đổi.", null, null, calculated, ct,
                previousState.ToString(), calculated.StateName);
        }
        if (old is not null && previousState != calculated.State)
        {
            await AddAuditAsync(receipt,
                PurchaseReceiptAuditEventType.InputInvoiceReconciliationStateChanged,
                null, null, null, calculated, ct,
                previousState.ToString(), calculated.State.ToString());
        }
        await repository.SaveChangesAsync(ct);
        return calculated;
    }

    private async Task<InputInvoiceReconciliationDto> BuildAsync(
        StockDocument receipt,
        StockDocumentInputInvoiceReconciliation? previous,
        CancellationToken ct,
        IReadOnlyDictionary<int, PurchasePricingPolicy.LineAmounts>?
            commercialAmounts = null)
    {
        var links = receipt.InputInvoiceMaps.Where(x => !x.IsDeleted).ToList();
        if (links.Count == 0) return NotApplicable(receipt);
        if (links.Count != 1)
            throw new BusinessRuleException("Phiếu nhập đang liên kết nhiều hơn một hóa đơn.");
        var link = links[0];
        var invoice = link.InputInvoiceHead;
        var itemMappings = await itemCatalogMapping.ResolveForReceiptAsync(
            receipt.StoreId, receipt.Id, ct);
        var previousIgnored = previous?.Details.Where(x => !x.IsDeleted && x.IsIgnored)
            .ToDictionary(x => x.InputInvoiceDetailId) ?? [];
        var dto = new InputInvoiceReconciliationDto
        {
            StockDocumentId = receipt.Id,
            InputInvoiceHeadId = invoice.Id,
            QuantityTolerance = InputInvoiceReconciliationPolicy.QuantityTolerance,
            MoneyTolerance = InputInvoiceReconciliationPolicy.MoneyTolerance,
            LastCalculatedAtUtc = DateTime.UtcNow,
            IsConfirmedReadOnly = receipt.Status == StockDocumentStatus.Confirmed
        };
        var maps = receipt.LineInputInvoiceMaps.Where(x => !x.IsDeleted).ToList();
        dto.ExcludedLines = maps.Where(x =>
                x.MatchStatus == InputInvoiceMatchStatus.Excluded)
            .OrderBy(x => x.StockDocumentLineId)
            .Select(x => new InputInvoiceExcludedLineDto
            {
                StockDocumentLineId = x.StockDocumentLineId,
                Reason = x.ExclusionReason
            }).ToList();
        dto.ExcludedLineCount = dto.ExcludedLines.Count;

        foreach (var xml in invoice.Details.Where(x => !x.IsDeleted).OrderBy(x => x.LineNo))
        {
            var lineMaps = maps.Where(x => x.UseInputInvoice &&
                x.InputInvoiceDetailId == xml.Id).ToList();
            var lines = lineMaps.Select(x => x.StockDocumentLine)
                .Where(x => x is not null && !x.IsDeleted).DistinctBy(x => x.Id).ToList();
            itemMappings.TryGetValue(xml.Id, out var mapping);
            var ignored = previousIgnored.TryGetValue(xml.Id, out var ignoredSnapshot) &&
                lineMaps.Count == 0;
            var receiptBase = lines.Sum(x => x.BaseQuantity);
            var resolvedProductVariantId = mapping?.ProductVariantId;
            var resolvedConversionId = mapping?.ProductUnitConversionId;
            var resolvedUnitId = mapping?.ConfirmedUnitId;
            var resolvedFactor = mapping?.ConfirmedFactor;
            var resolvedBaseUnitId = mapping?.ConfirmedBaseUnitId;
            var mappingUsable = mapping is not null &&
                (mapping.State == InputInvoiceItemCatalogResolutionState.Confirmed ||
                 mapping.ReasonCode == "CrossUnitProductInherited") &&
                resolvedFactor is > 0m &&
                resolvedBaseUnitId is > 0 &&
                lines.All(x => x.ProductVariantId == resolvedProductVariantId);

            if (!mappingUsable && lines.Count > 0)
            {
                var lineVariants = lines.Select(x => x.ProductVariantId)
                    .Distinct().ToArray();
                var normalizedXmlUnit = xml.NormalizedUnitName ??
                    InputInvoiceItemIdentityNormalizer.NormalizeText(xml.UnitName);
                if (lineVariants.Length == 1 && normalizedXmlUnit is not null)
                {
                    var targets = await repository
                        .GetInputInvoiceItemCatalogTargetsByUnitAsync(
                            receipt.StoreId, lineVariants[0], normalizedXmlUnit, ct);
                    if (targets.Count == 1)
                    {
                        var target = targets[0];
                        resolvedProductVariantId = target.Variant.Id;
                        resolvedConversionId = target.Conversion.Id;
                        resolvedUnitId = target.Unit.Id;
                        resolvedFactor = target.Conversion.Factor;
                        resolvedBaseUnitId = target.BaseUnit.Id;
                        mappingUsable = true;
                    }
                }
            }

            var derived = mappingUsable && resolvedFactor is > 0m
                ? xml.Quantity * resolvedFactor.Value
                : 0m;
            var complete = lines.Count > 0 && mappingUsable &&
                receiptBase > 0m && derived > 0m;
            var receiptBeforeVat = PurchasePricingPolicy.RoundMoney(
                lines.Sum(x => LineBeforeVat(x, commercialAmounts)));
            var receiptVat = PurchasePricingPolicy.RoundMoney(
                lines.Sum(x => LineVat(x, commercialAmounts)));
            var receiptRates = lines.Select(x => LineTaxRate(x, commercialAmounts))
                .Distinct().ToList();
            var xmlVatComparable = InputInvoiceVatRateNormalizer.TryNormalize(
                xml.VatRate, out var xmlVatRate, out var vatReason);
            var vatComparable = complete && xmlVatComparable && receiptRates.Count == 1;
            if (receiptRates.Count > 1)
                vatReason = "Các dòng nhập được gom có nhiều thuế suất khác nhau.";
            var receiptRate = receiptRates.Count == 1 ? receiptRates[0] : (decimal?)null;
            var quantityDifference = receiptBase - derived;
            var amountDifference = receiptBeforeVat - xml.LineAmount;
            var receiptBasePrice = BaseUnitPrice(receiptBeforeVat, receiptBase);
            var xmlBasePrice = BaseUnitPrice(xml.LineAmount, derived);
            var basePriceDifference = receiptBasePrice.HasValue && xmlBasePrice.HasValue
                ? receiptBasePrice.Value - xmlBasePrice.Value
                : (decimal?)null;
            var vatDifference = receiptVat - xml.VatAmount;
            var state = lines.Count == 0
                ? ignored
                    ? InputInvoiceDetailReconciliationState.Ignored
                    : InputInvoiceDetailReconciliationState.Unmatched
                : InputInvoiceReconciliationPolicy.ResolveDetailState(
                    complete, ignored, vatComparable, quantityDifference,
                    basePriceDifference ?? 0m, vatDifference, receiptRate,
                    xmlVatComparable ? xmlVatRate : null);
            dto.Details.Add(new InputInvoiceDetailReconciliationDto
            {
                InputInvoiceDetailId = xml.Id,
                ItemName = xml.ItemName,
                UnitName = xml.UnitName,
                State = state,
                XmlQuantity = xml.Quantity,
                DerivedBaseQuantity = derived,
                ReceiptBaseQuantity = receiptBase,
                QuantityDifference = quantityDifference,
                MappingId = mapping?.MappingId,
                ProductVariantId = resolvedProductVariantId,
                ProductUnitConversionId = resolvedConversionId,
                ConfirmedUnitId = resolvedUnitId,
                ConfirmedFactor = resolvedFactor,
                ConfirmedBaseUnitId = resolvedBaseUnitId,
                ReceiptBeforeVatAmount = receiptBeforeVat,
                XmlBeforeVatAmount = xml.LineAmount,
                AmountDifference = amountDifference,
                ReceiptBaseUnitPriceBeforeVat = receiptBasePrice,
                XmlBaseUnitPriceBeforeVat = xmlBasePrice,
                BaseUnitPriceDifference = basePriceDifference,
                ReceiptVatRate = receiptRate,
                XmlVatRate = xmlVatComparable ? xmlVatRate : null,
                ReceiptVatAmount = receiptVat,
                XmlVatAmount = xml.VatAmount,
                VatAmountDifference = vatDifference,
                IsIgnored = ignored,
                IgnoreReason = ignoredSnapshot?.IgnoreReason,
                StockDocumentLineIds = lines.Select(x => x.Id).Order().ToList(),
                ReceiptLines = lines.OrderBy(x => x.LineNo)
                    .ThenBy(x => x.Id)
                    .Select(ToReceiptLineContext)
                    .ToList(),
                Message = !complete && lines.Count > 0
                    ? mapping?.Message ??
                      "Chưa xác định được quy đổi đơn vị XML tương thích với sản phẩm dòng nhập."
                    : vatReason
            });
        }

        var receiptSubtotalBeforeVat = commercialAmounts is null
            ? receipt.SubtotalBeforeVat
            : PurchasePricingPolicy.RoundMoney(receipt.Lines
                .Where(x => !x.IsDeleted)
                .Sum(x => LineBeforeVat(x, commercialAmounts)));
        var receiptVatAmount = commercialAmounts is null
            ? receipt.VatAmount
            : PurchasePricingPolicy.RoundMoney(receipt.Lines
                .Where(x => !x.IsDeleted)
                .Sum(x => LineVat(x, commercialAmounts)));
        var receiptGoodsTotal = PurchasePricingPolicy.RoundMoney(
            receiptSubtotalBeforeVat + receiptVatAmount);
        var unsupported = Math.Abs(invoice.TotalPaymentAmount -
            (invoice.TotalBeforeTax + invoice.TotalTaxAmount)) >
            InputInvoiceReconciliationPolicy.MoneyTolerance;
        dto.Header = new InputInvoiceHeaderReconciliationDto
        {
            ReceiptSubtotalBeforeVat = receiptSubtotalBeforeVat,
            XmlTotalBeforeTax = invoice.TotalBeforeTax,
            SubtotalDifference = receiptSubtotalBeforeVat - invoice.TotalBeforeTax,
            ReceiptVatAmount = receiptVatAmount,
            XmlTaxAmount = invoice.TotalTaxAmount,
            VatDifference = receiptVatAmount - invoice.TotalTaxAmount,
            ReceiptGoodsTotal = receiptGoodsTotal,
            XmlPaymentAmount = invoice.TotalPaymentAmount,
            PaymentDifference = receiptGoodsTotal - invoice.TotalPaymentAmount,
            NeedsReview = unsupported,
            Reason = unsupported
                ? "Tổng thanh toán XML không giải thích được chỉ từ tiền trước thuế và tiền thuế."
                : null
        };
        var selectedMissingDetail = maps.Any(x => x.UseInputInvoice &&
            !x.InputInvoiceDetailId.HasValue);
        var missingExclusionReason = dto.ExcludedLines.Any(x =>
            string.IsNullOrWhiteSpace(x.Reason));
        var incomplete = selectedMissingDetail || missingExclusionReason ||
            dto.Details.Any(x => x.State is InputInvoiceDetailReconciliationState.Incomplete
                or InputInvoiceDetailReconciliationState.Unmatched);
        var headerMismatch = unsupported ||
            !InputInvoiceReconciliationPolicy.MoneyMatches(dto.Header.SubtotalDifference) ||
            !InputInvoiceReconciliationPolicy.MoneyMatches(dto.Header.VatDifference) ||
            !InputInvoiceReconciliationPolicy.MoneyMatches(dto.Header.PaymentDifference);
        var mismatch = headerMismatch || dto.ExcludedLines.Count > 0 ||
            dto.Details.Any(x => x.State != InputInvoiceDetailReconciliationState.Matched);
        dto.State = incomplete
            ? InputInvoiceReconciliationState.Incomplete
            : mismatch
                ? InputInvoiceReconciliationState.Mismatch
                : InputInvoiceReconciliationState.Matched;
        dto.Message = dto.State switch
        {
            InputInvoiceReconciliationState.Incomplete => "Đối chiếu chưa đầy đủ.",
            InputInvoiceReconciliationState.Mismatch => "Đối chiếu có chênh lệch cần quản lý xem xét.",
            _ => "Phiếu nhập và hóa đơn XML đã khớp trong dung sai."
        };
        ApplyProductSummaries(dto, receipt, itemMappings, commercialAmounts);
        dto.IsLateAssociationException = dto.IsConfirmedReadOnly && dto.State is
            InputInvoiceReconciliationState.Incomplete or
            InputInvoiceReconciliationState.Mismatch;
        return dto;
    }

    private async Task<StockDocument> RequireEditableLockedReceiptAsync(
        int storeId, int stockDocumentId, CancellationToken ct)
    {
        var receipt = await repository.LockReceiptForReconciliationAsync(
            storeId, stockDocumentId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
        if (receipt.Status == StockDocumentStatus.Confirmed)
            throw new BusinessRuleException("Phiếu đã duyệt chỉ cho phép xem đối chiếu.");
        if (receipt.Type != StockDocumentType.Receipt)
            throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập kho.");
        return receipt;
    }

    private static InputInvoiceReconciliationDto NotApplicable(StockDocument receipt)
        => new()
        {
            StockDocumentId = receipt.Id,
            State = InputInvoiceReconciliationState.NotApplicable,
            QuantityTolerance = InputInvoiceReconciliationPolicy.QuantityTolerance,
            MoneyTolerance = InputInvoiceReconciliationPolicy.MoneyTolerance,
            IsConfirmedReadOnly = receipt.Status == StockDocumentStatus.Confirmed,
            Message = "Phiếu không liên kết hóa đơn XML; quy trình duyệt hiện tại không thay đổi."
        };

    private static void Apply(StockDocumentInputInvoiceReconciliation row,
        InputInvoiceReconciliationDto dto)
    {
        var header = dto.Header!;
        row.OverallState = dto.State;
        row.EvidenceFingerprint = dto.EvidenceFingerprint;
        row.LastCalculatedAtUtc = dto.LastCalculatedAtUtc ?? DateTime.UtcNow;
        row.TotalDetailCount = dto.Details.Count;
        row.MatchedDetailCount = dto.Details.Count(x =>
            x.State == InputInvoiceDetailReconciliationState.Matched);
        row.MismatchDetailCount = dto.Details.Count(x => x.State is
            InputInvoiceDetailReconciliationState.QuantityMismatch or
            InputInvoiceDetailReconciliationState.AmountMismatch or
            InputInvoiceDetailReconciliationState.VatMismatch or
            InputInvoiceDetailReconciliationState.CombinedMismatch or
            InputInvoiceDetailReconciliationState.NeedsReview);
        row.UnmatchedDetailCount = dto.Details.Count(x =>
            x.State == InputInvoiceDetailReconciliationState.Unmatched);
        row.IgnoredDetailCount = dto.Details.Count(x => x.IsIgnored);
        row.ExcludedLineCount = dto.ExcludedLineCount;
        row.IncompleteCount = dto.Details.Count(x => x.State is
            InputInvoiceDetailReconciliationState.Incomplete or
            InputInvoiceDetailReconciliationState.Unmatched);
        row.ReceiptSubtotalBeforeVat = header.ReceiptSubtotalBeforeVat;
        row.XmlTotalBeforeTax = header.XmlTotalBeforeTax;
        row.SubtotalDifference = header.SubtotalDifference;
        row.ReceiptVatAmount = header.ReceiptVatAmount;
        row.XmlTaxAmount = header.XmlTaxAmount;
        row.VatDifference = header.VatDifference;
        row.ReceiptGoodsTotal = header.ReceiptGoodsTotal;
        row.XmlPaymentAmount = header.XmlPaymentAmount;
        row.PaymentDifference = header.PaymentDifference;
        row.HasUnsupportedHeader = header.NeedsReview;
        row.UnsupportedHeaderReason = header.Reason;
    }

    private static StockDocumentInputInvoiceDetailReconciliation ToEntity(
        StockDocument receipt, StockDocumentInputInvoiceMap link,
        StockDocumentInputInvoiceReconciliation parent,
        InputInvoiceDetailReconciliationDto dto) => new()
    {
        StoreId = receipt.StoreId,
        Reconciliation = parent,
        StockDocumentId = receipt.Id,
        InputInvoiceHeadId = link.InputInvoiceHeadId,
        InputInvoiceDetailId = dto.InputInvoiceDetailId,
        DetailState = dto.State,
        ProductVariantId = dto.ProductVariantId,
        ProductUnitConversionId = dto.ProductUnitConversionId,
        ConfirmedUnitId = dto.ConfirmedUnitId,
        ConfirmedBaseUnitId = dto.ConfirmedBaseUnitId,
        ConfirmedFactor = dto.ConfirmedFactor,
        XmlQuantity = dto.XmlQuantity,
        DerivedBaseQuantity = dto.DerivedBaseQuantity,
        ReceiptBaseQuantity = dto.ReceiptBaseQuantity,
        QuantityDifference = dto.QuantityDifference,
        ReceiptBeforeVatAmount = dto.ReceiptBeforeVatAmount,
        XmlBeforeVatAmount = dto.XmlBeforeVatAmount,
        AmountDifference = dto.AmountDifference,
        ReceiptVatRate = dto.ReceiptVatRate,
        XmlVatRate = dto.XmlVatRate,
        VatComparable = dto.XmlVatRate.HasValue && dto.ReceiptVatRate.HasValue,
        VatComparisonReason = dto.Message,
        ReceiptVatAmount = dto.ReceiptVatAmount,
        XmlVatAmount = dto.XmlVatAmount,
        VatAmountDifference = dto.VatAmountDifference,
        IsIgnored = dto.IsIgnored,
        IgnoreReason = dto.IgnoreReason
    };

    private static InputInvoiceReconciliationDto ToDto(
        StockDocumentInputInvoiceReconciliation row,
        StockDocument receipt) => new()
    {
        StockDocumentId = row.StockDocumentId,
        InputInvoiceHeadId = row.InputInvoiceHeadId,
        State = row.OverallState,
        EvidenceFingerprint = row.EvidenceFingerprint,
        LastCalculatedAtUtc = row.LastCalculatedAtUtc,
        QuantityTolerance = InputInvoiceReconciliationPolicy.QuantityTolerance,
        MoneyTolerance = InputInvoiceReconciliationPolicy.MoneyTolerance,
        IsConfirmedReadOnly = receipt.Status == StockDocumentStatus.Confirmed,
        IsLateAssociationException = receipt.Status == StockDocumentStatus.Confirmed &&
            row.OverallState is InputInvoiceReconciliationState.Incomplete or
                InputInvoiceReconciliationState.Mismatch,
        ExcludedLineCount = row.ExcludedLineCount,
        AcceptanceReason = row.AcceptanceReason,
        AcceptedByUserId = row.AcceptedByUserId,
        AcceptedAtUtc = row.AcceptedAtUtc,
        Header = new InputInvoiceHeaderReconciliationDto
        {
            ReceiptSubtotalBeforeVat = row.ReceiptSubtotalBeforeVat,
            XmlTotalBeforeTax = row.XmlTotalBeforeTax,
            SubtotalDifference = row.SubtotalDifference,
            ReceiptVatAmount = row.ReceiptVatAmount,
            XmlTaxAmount = row.XmlTaxAmount,
            VatDifference = row.VatDifference,
            ReceiptGoodsTotal = row.ReceiptGoodsTotal,
            XmlPaymentAmount = row.XmlPaymentAmount,
            PaymentDifference = row.PaymentDifference,
            NeedsReview = row.HasUnsupportedHeader,
            Reason = row.UnsupportedHeaderReason
        },
        Details = row.Details.Where(x => !x.IsDeleted).OrderBy(x => x.InputInvoiceDetailId)
            .Select(x => new InputInvoiceDetailReconciliationDto
            {
                InputInvoiceDetailId = x.InputInvoiceDetailId,
                ItemName = x.InputInvoiceDetail?.ItemName,
                UnitName = x.InputInvoiceDetail?.UnitName,
                State = x.DetailState,
                XmlQuantity = x.XmlQuantity,
                DerivedBaseQuantity = x.DerivedBaseQuantity,
                ReceiptBaseQuantity = x.ReceiptBaseQuantity,
                QuantityDifference = x.QuantityDifference,
                ProductVariantId = x.ProductVariantId,
                ProductUnitConversionId = x.ProductUnitConversionId,
                ConfirmedUnitId = x.ConfirmedUnitId,
                ConfirmedFactor = x.ConfirmedFactor,
                ConfirmedBaseUnitId = x.ConfirmedBaseUnitId,
                ReceiptBeforeVatAmount = x.ReceiptBeforeVatAmount,
                XmlBeforeVatAmount = x.XmlBeforeVatAmount,
                AmountDifference = x.AmountDifference,
                ReceiptBaseUnitPriceBeforeVat = BaseUnitPrice(
                    x.ReceiptBeforeVatAmount, x.ReceiptBaseQuantity),
                XmlBaseUnitPriceBeforeVat = BaseUnitPrice(
                    x.XmlBeforeVatAmount, x.DerivedBaseQuantity),
                BaseUnitPriceDifference = BaseUnitPrice(
                        x.ReceiptBeforeVatAmount, x.ReceiptBaseQuantity) is { } receiptPrice &&
                    BaseUnitPrice(x.XmlBeforeVatAmount, x.DerivedBaseQuantity) is { } xmlPrice
                        ? receiptPrice - xmlPrice
                        : null,
                ReceiptVatRate = x.ReceiptVatRate,
                XmlVatRate = x.XmlVatRate,
                ReceiptVatAmount = x.ReceiptVatAmount,
                XmlVatAmount = x.XmlVatAmount,
                VatAmountDifference = x.VatAmountDifference,
                IsIgnored = x.IsIgnored,
                IgnoreReason = x.IgnoreReason,
                StockDocumentLineIds = ReceiptLinesForDetail(
                        receipt, x.InputInvoiceDetailId)
                    .Select(line => line.Id)
                    .ToList(),
                ReceiptLines = ReceiptLinesForDetail(
                        receipt, x.InputInvoiceDetailId)
                    .Select(ToReceiptLineContext)
                    .ToList(),
                Message = x.VatComparisonReason
            }).ToList()
    };

    private static decimal? BaseUnitPrice(decimal amount, decimal baseQuantity)
        => baseQuantity > 0m ? amount / baseQuantity : null;

    private static decimal LineBeforeVat(StockDocumentLine line,
        IReadOnlyDictionary<int, PurchasePricingPolicy.LineAmounts>? commercialAmounts)
        => commercialAmounts is not null && commercialAmounts.TryGetValue(line.Id, out var value)
            ? value.LineTotalBeforeVat
            : PurchasePricingPolicy.RoundMoney(line.LineTotal - line.VatAmount);

    private static decimal LineVat(StockDocumentLine line,
        IReadOnlyDictionary<int, PurchasePricingPolicy.LineAmounts>? commercialAmounts)
        => commercialAmounts is not null && commercialAmounts.TryGetValue(line.Id, out var value)
            ? value.VatAmount
            : line.VatAmount;

    private static decimal LineTaxRate(StockDocumentLine line,
        IReadOnlyDictionary<int, PurchasePricingPolicy.LineAmounts>? commercialAmounts)
        => commercialAmounts is not null && commercialAmounts.TryGetValue(line.Id, out var value)
            ? value.TaxRate
            : line.TaxRate;

    private static void EnsureRowVersion(byte[] current, string? posted)
    {
        if (string.IsNullOrWhiteSpace(posted))
            throw new BusinessRuleException("Thiếu RowVersion. Vui lòng tải lại phiếu.");
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(posted);
        }
        catch (FormatException)
        {
            throw new BusinessRuleException("RowVersion không hợp lệ.");
        }
        if (!current.SequenceEqual(expected))
            throw new BusinessRuleException(
                "Phiếu đã được người khác cập nhật. Vui lòng tải lại trang.");
    }

    private static void ApplyProductSummaries(
        InputInvoiceReconciliationDto dto,
        StockDocument receipt,
        IReadOnlyDictionary<int, InputInvoiceItemCatalogResolutionDto> itemMappings,
        IReadOnlyDictionary<int, PurchasePricingPolicy.LineAmounts>?
            commercialAmounts = null)
    {
        dto.ProductSummaries = [];
        dto.ProductCount = 0;
        dto.MatchedProductCount = 0;
        dto.DifferingProductCount = 0;
        dto.UnresolvedXmlDetailCount = 0;

        var maps = receipt.LineInputInvoiceMaps.Where(x => !x.IsDeleted).ToList();
        var groups = new Dictionary<int, ProductAggregateBuilder>();
        foreach (var line in maps
                     .Where(x => x.UseInputInvoice &&
                                 x.MatchStatus != InputInvoiceMatchStatus.Excluded &&
                                 x.StockDocumentLine is not null &&
                                 !x.StockDocumentLine.IsDeleted &&
                                 x.StockDocumentLine.ProductVariantId > 0)
                     .Select(x => x.StockDocumentLine)
                     .DistinctBy(x => x.Id)
                     .OrderBy(x => x.LineNo)
                     .ThenBy(x => x.Id))
        {
            var group = GetOrAddProductGroup(groups, line.ProductVariantId);
            group.ReceiptLines[line.Id] = line;
            if (string.IsNullOrWhiteSpace(group.ProductDisplayName) &&
                !string.IsNullOrWhiteSpace(line.ProductNameSnapshot))
                group.ProductDisplayName = line.ProductNameSnapshot;
        }

        foreach (var detail in dto.Details.OrderBy(x => x.InputInvoiceDetailId))
        {
            if (detail.IsIgnored) continue;
            var associatedLines = maps
                .Where(x => x.UseInputInvoice &&
                            x.InputInvoiceDetailId == detail.InputInvoiceDetailId &&
                            x.StockDocumentLine is not null &&
                            !x.StockDocumentLine.IsDeleted)
                .Select(x => x.StockDocumentLine)
                .DistinctBy(x => x.Id)
                .ToList();
            var manualVariants = associatedLines.Select(x => x.ProductVariantId)
                .Distinct().ToArray();
            if (manualVariants.Length > 1)
            {
                dto.UnresolvedXmlDetailCount++;
                continue;
            }

            itemMappings.TryGetValue(detail.InputInvoiceDetailId, out var mapping);
            var mappingAuthoritative = mapping is not null &&
                (mapping.State == InputInvoiceItemCatalogResolutionState.Confirmed ||
                 mapping.ReasonCode == "CrossUnitProductInherited") &&
                mapping.ProductVariantId is > 0 &&
                mapping.ConfirmedFactor is > 0m &&
                mapping.ConfirmedBaseUnitId is > 0;
            var manualProductVariantId = manualVariants.Length == 1
                ? manualVariants[0]
                : (int?)null;
            var mappedProductVariantId = mappingAuthoritative
                ? mapping!.ProductVariantId
                : null;
            if (manualProductVariantId.HasValue && mappedProductVariantId.HasValue &&
                manualProductVariantId.Value != mappedProductVariantId.Value)
            {
                dto.UnresolvedXmlDetailCount++;
                continue;
            }

            var productVariantId = manualProductVariantId ?? mappedProductVariantId;
            var hasSafeConversion = productVariantId is > 0 &&
                detail.ProductVariantId == productVariantId &&
                detail.ConfirmedFactor is > 0m &&
                detail.ConfirmedBaseUnitId is > 0;
            if (!hasSafeConversion)
            {
                dto.UnresolvedXmlDetailCount++;
                continue;
            }

            var group = GetOrAddProductGroup(groups, productVariantId!.Value);
            group.XmlDetails[detail.InputInvoiceDetailId] = detail;
            if (string.IsNullOrWhiteSpace(group.ProductDisplayName))
                group.ProductDisplayName = mapping?.ProductName ?? detail.ItemName ?? string.Empty;
            var baseUnitNameIsSafe = mapping?.ProductVariantId == productVariantId &&
                mapping.ConfirmedBaseUnitId == detail.ConfirmedBaseUnitId &&
                mapping.ConfirmedFactor == detail.ConfirmedFactor &&
                !string.IsNullOrWhiteSpace(mapping.ConfirmedBaseUnitName);
            if (baseUnitNameIsSafe)
                group.BaseUnitNames.Add(mapping!.ConfirmedBaseUnitName!.Trim());
            else
                group.HasUnknownBaseUnitName = true;
        }

        var explicitlyExcludedLineIds = maps
            .Where(x => x.MatchStatus == InputInvoiceMatchStatus.Excluded)
            .Select(x => x.StockDocumentLineId)
            .ToHashSet();
        foreach (var group in groups.Values)
        {
            foreach (var line in receipt.Lines
                         .Where(x => !x.IsDeleted &&
                                     x.ProductVariantId == group.ProductVariantId &&
                                     !explicitlyExcludedLineIds.Contains(x.Id))
                         .OrderBy(x => x.LineNo)
                         .ThenBy(x => x.Id))
            {
                group.ReceiptLines[line.Id] = line;
                if (string.IsNullOrWhiteSpace(group.ProductDisplayName) &&
                    !string.IsNullOrWhiteSpace(line.ProductNameSnapshot))
                    group.ProductDisplayName = line.ProductNameSnapshot;
            }
        }

        foreach (var group in groups.Values.OrderBy(x => x.ProductDisplayName)
                     .ThenBy(x => x.ProductVariantId))
        {
            var receiptLines = group.ReceiptLines.Values.OrderBy(x => x.LineNo)
                .ThenBy(x => x.Id).ToList();
            var xmlDetails = group.XmlDetails.Values
                .OrderBy(x => x.InputInvoiceDetailId).ToList();
            var receiptBaseQuantity = receiptLines.Sum(x => x.BaseQuantity);
            var xmlBaseQuantity = xmlDetails.Sum(x => x.DerivedBaseQuantity);
            var receiptBeforeVat = PurchasePricingPolicy.RoundMoney(receiptLines.Sum(x =>
                LineBeforeVat(x, commercialAmounts)));
            var xmlBeforeVat = PurchasePricingPolicy.RoundMoney(
                xmlDetails.Sum(x => x.XmlBeforeVatAmount));
            var receiptBasePrice = BaseUnitPrice(receiptBeforeVat, receiptBaseQuantity);
            var xmlBasePrice = BaseUnitPrice(xmlBeforeVat, xmlBaseQuantity);
            var priceDifference = receiptBasePrice.HasValue && xmlBasePrice.HasValue
                ? xmlBasePrice.Value - receiptBasePrice.Value
                : (decimal?)null;
            var quantityDifference = xmlBaseQuantity - receiptBaseQuantity;
            var coveredReceiptLineIds = xmlDetails.SelectMany(x => x.StockDocumentLineIds)
                .ToHashSet();
            var vatNeedsReview = receiptLines.Count == 0 || xmlDetails.Count == 0 ||
                receiptLines.Any(x => !coveredReceiptLineIds.Contains(x.Id)) ||
                xmlDetails.Any(x => !x.ReceiptVatRate.HasValue || !x.XmlVatRate.HasValue);
            var vatMismatch = xmlDetails.Any(x =>
                x.ReceiptVatRate.HasValue && x.XmlVatRate.HasValue &&
                (x.ReceiptVatRate.Value != x.XmlVatRate.Value ||
                 !InputInvoiceReconciliationPolicy.MoneyMatches(x.VatAmountDifference)));
            var receiptVat = PurchasePricingPolicy.RoundMoney(
                receiptLines.Sum(x => LineVat(x, commercialAmounts)));
            var xmlVat = PurchasePricingPolicy.RoundMoney(
                xmlDetails.Sum(x => x.XmlVatAmount));
            var summary = new InputInvoiceProductReconciliationSummaryDto
            {
                ProductVariantId = group.ProductVariantId,
                ProductDisplayName = string.IsNullOrWhiteSpace(group.ProductDisplayName)
                    ? "Sản phẩm chưa đặt tên"
                    : group.ProductDisplayName,
                ReceiptLines = receiptLines.Select(ToReceiptLineContext).ToList(),
                XmlDetails = xmlDetails.Select(x => new InputInvoiceProductXmlDetailContextDto
                {
                    InputInvoiceDetailId = x.InputInvoiceDetailId,
                    ItemName = x.ItemName ?? string.Empty,
                    UnitName = x.UnitName,
                    Quantity = x.XmlQuantity,
                    Factor = x.ConfirmedFactor ?? 0m,
                    BaseQuantity = x.DerivedBaseQuantity
                }).ToList(),
                ReceiptLineCount = receiptLines.Count,
                XmlDetailCount = xmlDetails.Count,
                ReceiptBaseQuantity = receiptBaseQuantity,
                XmlBaseQuantity = xmlBaseQuantity,
                BaseUnitDisplayName = !group.HasUnknownBaseUnitName &&
                    group.BaseUnitNames.Count == 1
                        ? group.BaseUnitNames.Single()
                        : "đơn vị gốc",
                QuantityDifference = quantityDifference,
                QuantityStatus = InputInvoiceReconciliationPolicy.QuantityMatches(
                        quantityDifference)
                    ? "Matched"
                    : quantityDifference < 0m ? "XmlShort" : "XmlExcess",
                ReceiptBeforeVatAmount = receiptBeforeVat,
                XmlBeforeVatAmount = xmlBeforeVat,
                BeforeVatAmountDifference = xmlBeforeVat - receiptBeforeVat,
                ReceiptBaseUnitPriceBeforeVat = receiptBasePrice,
                XmlBaseUnitPriceBeforeVat = xmlBasePrice,
                BaseUnitPriceDifference = priceDifference,
                PriceStatus = !priceDifference.HasValue
                    ? "InsufficientData"
                    : InputInvoiceReconciliationPolicy.MoneyMatches(priceDifference.Value)
                        ? "Matched"
                        : priceDifference.Value > 0m ? "XmlHigher" : "XmlLower",
                ReceiptVatAmount = receiptVat,
                XmlVatAmount = xmlVat,
                VatAmountDifference = xmlVat - receiptVat,
                VatStatus = vatNeedsReview
                    ? "NeedsReview"
                    : vatMismatch ? "Mismatch" : "Matched"
            };
            dto.ProductSummaries.Add(summary);
        }

        dto.ProductCount = dto.ProductSummaries.Count;
        dto.MatchedProductCount = dto.ProductSummaries.Count(x =>
            x.QuantityStatus == "Matched" && x.PriceStatus == "Matched");
        dto.DifferingProductCount = dto.ProductCount - dto.MatchedProductCount;
    }

    private static ProductAggregateBuilder GetOrAddProductGroup(
        IDictionary<int, ProductAggregateBuilder> groups,
        int productVariantId)
    {
        if (!groups.TryGetValue(productVariantId, out var group))
        {
            group = new ProductAggregateBuilder(productVariantId);
            groups[productVariantId] = group;
        }
        return group;
    }

    private sealed class ProductAggregateBuilder(int productVariantId)
    {
        public int ProductVariantId { get; } = productVariantId;
        public string ProductDisplayName { get; set; } = string.Empty;
        public Dictionary<int, StockDocumentLine> ReceiptLines { get; } = [];
        public Dictionary<int, InputInvoiceDetailReconciliationDto> XmlDetails { get; } = [];
        public HashSet<string> BaseUnitNames { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public bool HasUnknownBaseUnitName { get; set; }
    }

    private static List<StockDocumentLine> ReceiptLinesForDetail(
        StockDocument receipt,
        int inputInvoiceDetailId) => receipt.LineInputInvoiceMaps
        .Where(x => !x.IsDeleted && x.UseInputInvoice &&
                    x.InputInvoiceDetailId == inputInvoiceDetailId &&
                    x.StockDocumentLine is not null &&
                    !x.StockDocumentLine.IsDeleted)
        .Select(x => x.StockDocumentLine)
        .DistinctBy(x => x.Id)
        .OrderBy(x => x.LineNo)
        .ThenBy(x => x.Id)
        .ToList();

    private static InputInvoiceReceiptLineContextDto ToReceiptLineContext(
        StockDocumentLine line) => new()
    {
        StockDocumentLineId = line.Id,
        ProductName = line.ProductNameSnapshot,
        UnitName = line.UnitNameSnapshot,
        Quantity = line.Quantity,
        Factor = line.Factor,
        BaseQuantity = line.BaseQuantity,
        ReceiptAllocationKind = line.ReceiptAllocationKind,
        OutsidePoDecisionStatus = line.OutsidePoDecisionStatus
    };

    private Task AddAuditAsync(StockDocument receipt,
        PurchaseReceiptAuditEventType eventType, string? reason,
        int? lineId, int? detailId,
        InputInvoiceReconciliationDto snapshot,
        CancellationToken ct,
        string? oldState = null,
        string? newState = null)
    {
        var detailEvidence = snapshot.Details
            .OrderBy(x => x.InputInvoiceDetailId)
            .Select(x => new SortedDictionary<string, object?>
            {
                ["AmountDifference"] = x.AmountDifference,
                ["BaseUnitPriceDifference"] = x.BaseUnitPriceDifference,
                ["InputInvoiceDetailId"] = x.InputInvoiceDetailId,
                ["QuantityDifference"] = x.QuantityDifference,
                ["ReceiptVatRate"] = x.ReceiptVatRate,
                ["State"] = x.StateName,
                ["StockDocumentLineIds"] = x.StockDocumentLineIds.Order().ToArray(),
                ["VatAmountDifference"] = x.VatAmountDifference,
                ["XmlVatRate"] = x.XmlVatRate
            })
            .ToArray();
        var headerEvidence = snapshot.Header is null
            ? null
            : new SortedDictionary<string, object?>
            {
                ["NeedsReview"] = snapshot.Header.NeedsReview,
                ["PaymentDifference"] = snapshot.Header.PaymentDifference,
                ["SubtotalDifference"] = snapshot.Header.SubtotalDifference,
                ["VatDifference"] = snapshot.Header.VatDifference
            };
        var relevantLineIds = snapshot.Details
            .SelectMany(x => x.StockDocumentLineIds)
            .Append(lineId ?? 0)
            .Where(x => x > 0)
            .Distinct()
            .Order()
            .ToArray();
        var relevantDetailIds = snapshot.Details
            .Select(x => x.InputInvoiceDetailId)
            .Append(detailId ?? 0)
            .Where(x => x > 0)
            .Distinct()
            .Order()
            .ToArray();
        var evidence = JsonSerializer.Serialize(new SortedDictionary<string, object?>
        {
            ["Details"] = detailEvidence,
            ["EvidenceFingerprint"] = snapshot.EvidenceFingerprint,
            ["Header"] = headerEvidence,
            ["InputInvoiceHeadId"] = snapshot.InputInvoiceHeadId,
            ["InputInvoiceDetailId"] = detailId,
            ["MoneyTolerance"] = snapshot.MoneyTolerance,
            ["QuantityTolerance"] = snapshot.QuantityTolerance,
            ["RelevantInputInvoiceDetailIds"] = relevantDetailIds,
            ["RelevantStockDocumentLineIds"] = relevantLineIds,
            ["OldState"] = oldState,
            ["NewState"] = newState ?? snapshot.StateName,
            ["StockDocumentLineId"] = lineId
        });
        return repository.AddPurchaseReceiptAuditEventAsync(new PurchaseReceiptAuditEvent
        {
            StoreId = receipt.StoreId,
            StockDocumentId = receipt.Id,
            StockDocumentLineId = lineId,
            EventType = eventType,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            ChangedFieldsJson = "[]",
            OldValuesJson = "{}",
            NewValuesJson = evidence,
            IsSuccess = true
        }, ct);
    }
}
