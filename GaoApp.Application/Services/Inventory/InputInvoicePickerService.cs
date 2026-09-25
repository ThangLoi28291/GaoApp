using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoicePickerService : IInputInvoicePickerService
{
    private readonly IInputInvoiceRepository _repository;
    private readonly IInputInvoiceDocumentLibrary _library;
    private readonly IInputInvoiceXmlDocumentParser _parser;
    private readonly IInputInvoiceXmlService _xmlService;
    private readonly IInputInvoiceBuyerOwnerResolutionService? _buyerOwnerResolver;
    private readonly IInputInvoiceReconciliationService? _reconciliationService;
    private readonly IInputInvoiceReceiptLinkService? _receiptLinkService;
    private readonly IInputInvoiceItemCatalogMappingService? _itemCatalogMappingService;

    public InputInvoicePickerService(
        IInputInvoiceRepository repository,
        IInputInvoiceDocumentLibrary library,
        IInputInvoiceXmlDocumentParser parser,
        IInputInvoiceXmlService xmlService,
        IInputInvoiceBuyerOwnerResolutionService? buyerOwnerResolver = null,
        IInputInvoiceReconciliationService? reconciliationService = null,
        IInputInvoiceReceiptLinkService? receiptLinkService = null,
        IInputInvoiceItemCatalogMappingService? itemCatalogMappingService = null)
    {
        _repository = repository;
        _library = library;
        _parser = parser;
        _xmlService = xmlService;
        _buyerOwnerResolver = buyerOwnerResolver;
        _reconciliationService = reconciliationService;
        _receiptLinkService = receiptLinkService;
        _itemCatalogMappingService = itemCatalogMappingService;
    }

    public async Task<InputInvoiceAssociationContextDto> GetAssociationContextAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default)
    {
        var context = await RequireContextAsync(storeId, stockDocumentId, ct);
        var linked = await _repository.GetLinkedInvoicesForSupplierResolutionAsync(
            storeId, stockDocumentId, ct);
        if (linked.Count > 1)
            throw new BusinessRuleException(
                "Phiếu nhập đang liên kết nhiều hơn một hóa đơn. Vui lòng liên hệ quản trị viên.");

        var current = linked.SingleOrDefault();
        InputInvoiceReconciliationDto? reconciliation = null;
        if (current is not null && _reconciliationService is not null)
            reconciliation = await _reconciliationService.GetForReceiptAsync(
                storeId, stockDocumentId, ct);

        var confirmed = context.Dto.IsConfirmed;
        var supportedStatus = confirmed || string.Equals(
            context.Dto.ReceiptStatus,
            StockDocumentStatus.PendingApproval.ToString(),
            StringComparison.Ordinal);
        return new InputInvoiceAssociationContextDto
        {
            StockDocumentId = stockDocumentId,
            ReceiptStatus = context.Dto.ReceiptStatus,
            IsConfirmed = confirmed,
            LifecycleState = current is not null
                ? InputInvoiceAssociationLifecycleStates.Linked
                : confirmed
                    ? InputInvoiceAssociationLifecycleStates.WaitingXml
                    : InputInvoiceAssociationLifecycleStates.Unlinked,
            CurrentInputInvoiceHeadId = current?.Id,
            CurrentInvoiceSeries = current?.InvoiceSeries,
            CurrentInvoiceNumber = current?.InvoiceNumber,
            ReconciliationState = reconciliation?.StateName,
            IsLateAssociationException = reconciliation?.IsLateAssociationException == true,
            Capabilities = new InputInvoiceAssociationCapabilitiesDto
            {
                CanLink = supportedStatus && current is null,
                CanUnlink = supportedStatus && current is not null,
                CanRelink = confirmed && current is not null,
                BlockReasonCode = supportedStatus ? null : "ReceiptStatusNotSupported",
                BlockMessage = supportedStatus
                    ? null
                    : "Trạng thái phiếu hiện tại không cho phép thay đổi liên kết hóa đơn."
            }
        };
    }

    public async Task<InputInvoicePickerBrowseResultDto> BrowseAsync(
        int storeId,
        int stockDocumentId,
        InputInvoicePickerBrowseRequest request,
        CancellationToken ct = default)
    {
        var context = await RequireContextAsync(storeId, stockDocumentId, ct);
        var candidates = (await _library.BrowseAsync(
            context.NormalizedSupplierTaxCode, request, ct)).ToList();
        var identities = candidates.Where(x => x.XmlValid && x.InvoiceDate.HasValue)
            .Select(ToIdentity)
            .Where(x => x is not null)
            .Cast<InputInvoiceBusinessIdentityDto>()
            .Distinct()
            .ToList();
        var canonical = await _repository.GetByBusinessIdentitiesAsync(
            storeId, identities, ct);
        var byIdentity = canonical.ToDictionary(EntityIdentityKey, StringComparer.Ordinal);
        var linkedInvoices = await _repository.GetLinkedInvoicesForSupplierResolutionAsync(
            storeId, stockDocumentId, ct);
        if (linkedInvoices.Count > 1)
            throw new BusinessRuleException(
                "Phiếu nhập đang liên kết nhiều hơn một hóa đơn. Vui lòng liên hệ quản trị viên.");
        var linkedInvoiceId = linkedInvoices.SingleOrDefault()?.Id;
        context.Dto.CurrentInputInvoiceHeadId = linkedInvoiceId;
        context.Dto.IsRelinkAvailable = context.Dto.IsConfirmed && linkedInvoiceId.HasValue;
        foreach (var candidate in candidates)
        {
            await ApplyOwnerPreviewAsync(storeId, context.ReceiptOwnerLegalEntityId,
                candidate, ct);
            var identity = ToIdentity(candidate);
            InputInvoiceHead? invoice = null;
            if (identity is not null)
                byIdentity.TryGetValue(IdentityKey(identity), out invoice);
            candidate.LinkedCurrentReceipt = invoice is not null &&
                invoice.Id == linkedInvoiceId;
            candidate.LinkedOtherReceiptCount = invoice?.StockDocumentMaps.Count(x =>
                x.StoreId == storeId && x.StockDocumentId != stockDocumentId) ?? 0;
            if (candidate.LinkedCurrentReceipt)
            {
                candidate.SelectionAllowed = false;
                candidate.SelectionBlockReasonCode = "AlreadyLinkedCurrentReceipt";
                candidate.SelectionBlockMessage = "Hóa đơn đã được liên kết với phiếu hiện tại.";
            }
            else if (linkedInvoiceId.HasValue)
            {
                candidate.RequiresRelinkReason = context.Dto.IsConfirmed;
                if (!context.Dto.IsConfirmed)
                {
                    candidate.SelectionAllowed = false;
                    candidate.SelectionBlockReasonCode = "ReceiptAlreadyLinkedDifferentInvoice";
                    candidate.SelectionBlockMessage =
                        "Phiếu nhập chỉ được liên kết một hóa đơn. Vui lòng gỡ hóa đơn hiện tại trước.";
                }
            }
        }

        return new InputInvoicePickerBrowseResultDto
        {
            Context = context.Dto,
            Candidates = candidates
        };
    }

    public async Task<InputInvoicePdfPreviewDto> GetPdfAsync(
        int storeId,
        int stockDocumentId,
        string documentKey,
        CancellationToken ct = default)
    {
        var context = await RequireContextAsync(storeId, stockDocumentId, ct);
        return await _library.GetPdfAsync(
            context.NormalizedSupplierTaxCode, documentKey, ct);
    }

    public async Task<InputInvoiceXmlPreviewDto> GetXmlPreviewAsync(
        int storeId,
        int stockDocumentId,
        string documentKey,
        CancellationToken ct = default)
    {
        var context = await RequireContextAsync(storeId, stockDocumentId, ct);
        var bytes = await _library.GetXmlBytesAsync(
            context.NormalizedSupplierTaxCode, documentKey, ct);
        var invoice = _parser.Parse(bytes);
        EnsureSellerMatches(context.NormalizedSupplierTaxCode, invoice);
        var preview = ToPreview(invoice);
        await ApplyOwnerPreviewAsync(storeId, context.ReceiptOwnerLegalEntityId,
            invoice, preview, ct);
        return preview;
    }

    public async Task<InputInvoicePickerBrowseResultDto> BrowseForSupplierAsync(
        int storeId,
        int supplierId,
        InputInvoicePickerBrowseRequest request,
        CancellationToken ct = default)
    {
        var context = await RequireSupplierContextAsync(storeId, supplierId, ct);
        var candidates = (await _library.BrowseAsync(
            context.NormalizedSupplierTaxCode, request, ct)).ToList();
        var identities = candidates.Where(x => x.XmlValid && x.InvoiceDate.HasValue)
            .Select(ToIdentity)
            .Where(x => x is not null)
            .Cast<InputInvoiceBusinessIdentityDto>()
            .Distinct()
            .ToList();
        var canonical = await _repository.GetByBusinessIdentitiesAsync(
            storeId, identities, ct);
        var byIdentity = canonical.ToDictionary(EntityIdentityKey, StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var identity = ToIdentity(candidate);
            if (identity is null || !byIdentity.TryGetValue(IdentityKey(identity), out var invoice))
                continue;
            candidate.LinkedOtherReceiptCount = invoice.StockDocumentMaps.Count(x =>
                x.StoreId == storeId);
        }

        return new InputInvoicePickerBrowseResultDto
        {
            Context = context.Dto,
            Candidates = candidates
        };
    }

    public async Task<InputInvoicePickerBrowseResultDto> BrowseForSupplierAndWarehouseAsync(
        int storeId, int supplierId, int warehouseId,
        InputInvoicePickerBrowseRequest request, CancellationToken ct = default)
    {
        var result = await BrowseForSupplierAsync(storeId, supplierId, request, ct);
        var warehouse = await _repository.GetSplitWarehouseAsync(storeId, warehouseId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy kho đích đang hoạt động.");
        result.Context.ReceiptOwnerLegalEntityId = warehouse.LegalEntityId;
        result.Context.ReceiptOwnerLegalEntityName = warehouse.LegalEntity?.Name ?? string.Empty;
        foreach (var candidate in result.Candidates)
            await ApplyOwnerPreviewAsync(storeId, warehouse.LegalEntityId, candidate, ct);
        return result;
    }

    public async Task<InputInvoicePdfPreviewDto> GetPdfForSupplierAsync(
        int storeId,
        int supplierId,
        string documentKey,
        CancellationToken ct = default)
    {
        var context = await RequireSupplierContextAsync(storeId, supplierId, ct);
        return await _library.GetPdfAsync(
            context.NormalizedSupplierTaxCode, documentKey, ct);
    }

    public async Task<InputInvoiceXmlPreviewDto> GetXmlPreviewForSupplierAsync(
        int storeId,
        int supplierId,
        string documentKey,
        CancellationToken ct = default)
    {
        var context = await RequireSupplierContextAsync(storeId, supplierId, ct);
        var bytes = await _library.GetXmlBytesAsync(
            context.NormalizedSupplierTaxCode, documentKey, ct);
        var invoice = _parser.Parse(bytes);
        EnsureSellerMatches(context.NormalizedSupplierTaxCode, invoice);
        return ToPreview(invoice);
    }

    public async Task<InputInvoiceXmlPreviewDto> GetXmlPreviewForSupplierAndWarehouseAsync(
        int storeId, int supplierId, int warehouseId, string documentKey,
        CancellationToken ct = default)
    {
        var context = await RequireSupplierContextAsync(storeId, supplierId, ct);
        var warehouse = await _repository.GetSplitWarehouseAsync(storeId, warehouseId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy kho đích đang hoạt động.");
        var bytes = await _library.GetXmlBytesAsync(
            context.NormalizedSupplierTaxCode, documentKey, ct);
        var invoice = _parser.Parse(bytes);
        EnsureSellerMatches(context.NormalizedSupplierTaxCode, invoice);
        var preview = ToPreview(invoice);
        await ApplyOwnerPreviewAsync(storeId, warehouse.LegalEntityId, invoice, preview, ct);
        return preview;
    }

    public async Task<InputInvoicePdfPreviewDto> GetLinkedPdfAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        var linked = await ResolveLinkedCandidateAsync(
            storeId, stockDocumentId, inputInvoiceHeadId, ct);
        if (!linked.Candidate.HasPdf)
            throw new BusinessRuleException("Không tìm thấy PDF của hóa đơn đã liên kết.");
        return await _library.GetPdfAsync(
            linked.Context.NormalizedSupplierTaxCode,
            linked.Candidate.DocumentKey,
            ct);
    }

    public async Task<InputInvoiceXmlPreviewDto> GetLinkedXmlPreviewAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        var linked = await ResolveLinkedCandidateAsync(
            storeId, stockDocumentId, inputInvoiceHeadId, ct);
        if (!linked.Candidate.HasXml)
            throw new BusinessRuleException("Không tìm thấy XML của hóa đơn đã liên kết.");
        var bytes = await _library.GetXmlBytesAsync(
            linked.Context.NormalizedSupplierTaxCode,
            linked.Candidate.DocumentKey,
            ct);
        var parsed = _parser.Parse(bytes);
        EnsureSellerMatches(linked.Context.NormalizedSupplierTaxCode, parsed);
        if (!string.Equals(
                EntityIdentityKey(linked.Invoice),
                EntityIdentityKey(parsed),
                StringComparison.Ordinal))
            throw new BusinessRuleException(
                "Tệp hóa đơn không còn khớp định danh hóa đơn đã liên kết.");
        var preview = ToPreview(parsed);
        await ApplyOwnerPreviewAsync(
            storeId,
            linked.Context.ReceiptOwnerLegalEntityId,
            parsed,
            preview,
            ct);
        return preview;
    }

    public async Task<bool> UnlinkAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        var result = await UnlinkCoreAsync(
            storeId,
            stockDocumentId,
            new UnlinkInputInvoiceRequest
            {
                ExpectedCurrentInputInvoiceHeadId = inputInvoiceHeadId
            },
            enforceReason: false,
            ct);
        return string.Equals(
            result.Outcome,
            InputInvoiceAssociationMutationOutcomes.Applied,
            StringComparison.Ordinal);
    }

    public Task<InputInvoiceAssociationMutationResultDto> UnlinkAsync(
        int storeId,
        int stockDocumentId,
        UnlinkInputInvoiceRequest request,
        CancellationToken ct = default)
        => UnlinkCoreAsync(storeId, stockDocumentId, request, enforceReason: true, ct);

    private async Task<InputInvoiceAssociationMutationResultDto> UnlinkCoreAsync(
        int storeId,
        int stockDocumentId,
        UnlinkInputInvoiceRequest request,
        bool enforceReason,
        CancellationToken ct)
    {
        if (storeId <= 0) throw new BusinessRuleException("StoreId không hợp lệ.");
        if (stockDocumentId <= 0) throw new BusinessRuleException("Phiếu nhập không hợp lệ.");
        if (request is null || request.ExpectedCurrentInputInvoiceHeadId <= 0)
            throw new BusinessRuleException("Hóa đơn hiện tại không hợp lệ.");

        await _repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var receipt = await _repository.LockReceiptForInputInvoiceMutationAsync(
                storeId, stockDocumentId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            if (receipt.Type != StockDocumentType.Receipt)
                throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập.");
            var reasonRequired = enforceReason ||
                receipt.Status == StockDocumentStatus.Confirmed;
            var reason = NormalizeReason(request.Reason, reasonRequired);
            var linked = await _repository.GetLinkedInvoicesForSupplierResolutionAsync(
                storeId, stockDocumentId, ct);
            if (linked.Count > 1)
                throw new BusinessRuleException(
                    "Phiếu nhập đang liên kết nhiều hơn một hóa đơn. Vui lòng liên hệ quản trị viên.");
            var current = linked.SingleOrDefault();
            if (current is null)
            {
                await _repository.CommitSupplierResolutionTransactionAsync(ct);
                return new InputInvoiceAssociationMutationResultDto
                {
                    Outcome = InputInvoiceAssociationMutationOutcomes.AlreadyApplied,
                    Association = await GetAssociationContextAsync(storeId, stockDocumentId, ct)
                };
            }
            if (current.Id != request.ExpectedCurrentInputInvoiceHeadId)
                throw InputInvoiceAssociationException.AssociationChanged(current.Id);

            await _repository.DeleteReceiptInputInvoiceLineMapsAsync(
                storeId, stockDocumentId, current.Id, ct);
            if (_reconciliationService is not null)
                await _reconciliationService.InvalidateWithinTransactionAsync(
                    storeId, stockDocumentId, "Liên kết hóa đơn bị gỡ.", ct);
            await _repository.DeleteReconciliationsAsync(
                storeId, stockDocumentId, current.Id, ct);
            var removed = await _repository.DeleteStockDocumentInputInvoiceMapAsync(
                storeId, stockDocumentId, current.Id, ct);
            if (!removed)
                throw InputInvoiceAssociationException.AssociationChanged();

            await _repository.AddPurchaseReceiptAuditEventAsync(
                CreateUnlinkAuditEvent(storeId, stockDocumentId, current.Id, reason),
                ct);
            await _repository.SaveChangesAsync(ct);
            await _repository.CommitSupplierResolutionTransactionAsync(ct);
            return new InputInvoiceAssociationMutationResultDto
            {
                Outcome = InputInvoiceAssociationMutationOutcomes.Applied,
                Association = await GetAssociationContextAsync(storeId, stockDocumentId, ct)
            };
        }
        catch
        {
            await _repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    public async Task<InputInvoiceAssociationMutationResultDto> RelinkAsync(
        int storeId,
        int stockDocumentId,
        RelinkInputInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.DocumentKey))
            throw new BusinessRuleException("Vui lòng chọn hóa đơn thay thế.");
        if (request.ExpectedCurrentInputInvoiceHeadId <= 0)
            throw new BusinessRuleException("Hóa đơn hiện tại không hợp lệ.");
        var reason = NormalizeReason(request.Reason, required: true)!;
        if (_receiptLinkService is null || _itemCatalogMappingService is null ||
            _reconciliationService is null)
            throw new InvalidOperationException(
                "Post-confirm input-invoice association services are not configured.");

        var context = await RequireContextAsync(storeId, stockDocumentId, ct);
        var candidate = await _library.ResolveAsync(
            context.NormalizedSupplierTaxCode, request.DocumentKey, ct);
        if (!candidate.SelectionAllowed)
            throw new BusinessRuleException(
                candidate.SelectionBlockMessage ?? "Hóa đơn không thể được chọn.");
        var bytes = await _library.GetXmlBytesAsync(
            context.NormalizedSupplierTaxCode, request.DocumentKey, ct);

        await _repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var receipt = await _repository.LockReceiptForInputInvoiceMutationAsync(
                storeId, stockDocumentId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            if (receipt.Type != StockDocumentType.Receipt ||
                receipt.Status != StockDocumentStatus.Confirmed)
                throw new BusinessRuleException(
                    "Chỉ phiếu đã xác nhận mới dùng luồng thay hóa đơn nguyên tử.");
            if (receipt.SupplierId != context.Dto.SupplierId)
                throw InputInvoiceAssociationException.AssociationChanged();

            var currentLinks = await _repository
                .GetLinkedInvoicesForSupplierResolutionAsync(storeId, stockDocumentId, ct);
            if (currentLinks.Count != 1)
                throw InputInvoiceAssociationException.AssociationChanged(
                    currentLinks.SingleOrDefault()?.Id);
            var current = currentLinks[0];

            var resolution = await _xmlService.ResolveInvoiceWithinTransactionAsync(
                storeId,
                context.Dto.SupplierTaxCode,
                request.DocumentKey + ".xml",
                bytes,
                ct);
            if (resolution.Invoice.Id == current.Id)
            {
                await _repository.CommitSupplierResolutionTransactionAsync(ct);
                return new InputInvoiceAssociationMutationResultDto
                {
                    Outcome = InputInvoiceAssociationMutationOutcomes.AlreadyApplied,
                    Association = await GetAssociationContextAsync(storeId, stockDocumentId, ct)
                };
            }
            if (current.Id != request.ExpectedCurrentInputInvoiceHeadId)
                throw InputInvoiceAssociationException.AssociationChanged(current.Id);

            await _receiptLinkService.ValidateLinkWithinTransactionAsync(
                storeId, receipt, resolution.Invoice, ct);

            await _repository.DeleteReceiptInputInvoiceLineMapsAsync(
                storeId, stockDocumentId, current.Id, ct);
            await _repository.DeleteReconciliationsAsync(
                storeId, stockDocumentId, current.Id, ct);
            if (!await _repository.DeleteStockDocumentInputInvoiceMapAsync(
                    storeId, stockDocumentId, current.Id, ct))
                throw InputInvoiceAssociationException.AssociationChanged();

            // Persist the soft-delete under the still-uncommitted serializable
            // transaction so the filtered unique index can accept the replacement.
            await _repository.SaveChangesAsync(ct);

            await _receiptLinkService.LinkWithinTransactionAsync(
                storeId,
                receipt,
                resolution.Invoice,
                "Thay hóa đơn sau xác nhận.",
                refreshReconciliation: false,
                writeLinkAudit: false,
                ct);
            await _itemCatalogMappingService.AutoApplyKnownMappingsWithinTransactionAsync(
                storeId, stockDocumentId, resolution.Invoice.Id, ct);
            await _reconciliationService.RefreshWithinTransactionAsync(
                storeId, stockDocumentId, ct);
            await _repository.AddPurchaseReceiptAuditEventAsync(
                CreateRelinkAuditEvent(
                    storeId, stockDocumentId, current, resolution.Invoice, reason),
                ct);
            await _repository.SaveChangesAsync(ct);
            await _repository.CommitSupplierResolutionTransactionAsync(ct);

            return new InputInvoiceAssociationMutationResultDto
            {
                Outcome = InputInvoiceAssociationMutationOutcomes.Applied,
                Association = await GetAssociationContextAsync(storeId, stockDocumentId, ct)
            };
        }
        catch
        {
            await _repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    public async Task<InputInvoicePickerSelectionResultDto> SelectAsync(
        int storeId,
        int stockDocumentId,
        SelectInputInvoiceDocumentRequest request,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.DocumentKey))
            throw new BusinessRuleException("Vui lòng chọn hóa đơn.");
        var context = await RequireContextAsync(storeId, stockDocumentId, ct);
        var candidate = await _library.ResolveAsync(
            context.NormalizedSupplierTaxCode, request.DocumentKey, ct);
        if (!candidate.SelectionAllowed)
            throw new BusinessRuleException(
                candidate.SelectionBlockMessage ?? "Hóa đơn không thể được chọn.");
        var bytes = await _library.GetXmlBytesAsync(
            context.NormalizedSupplierTaxCode, request.DocumentKey, ct);
        return await _xmlService.ImportAndLinkAsync(
            storeId,
            stockDocumentId,
            context.Dto.SupplierId,
            context.Dto.SupplierTaxCode,
            request.DocumentKey + ".xml",
            bytes,
            ct);
    }

    private async Task<PickerContext> RequireContextAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct)
    {
        if (storeId <= 0) throw new BusinessRuleException("StoreId không hợp lệ.");
        if (stockDocumentId <= 0) throw new BusinessRuleException("Phiếu nhập không hợp lệ.");
        var receipt = await _repository.GetReceiptForSupplierResolutionAsync(
            storeId, stockDocumentId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
        if (receipt.Type != StockDocumentType.Receipt)
            throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập.");
        if (!receipt.SupplierId.HasValue || receipt.Supplier is null)
            throw new BusinessRuleException(
                "Chưa chọn nhà cung cấp. Vui lòng chọn nhà cung cấp trước khi chọn hóa đơn.");
        var normalizedTaxCode = TaxCodeIdentityNormalizer.Normalize(receipt.Supplier.TaxCode)
            ?? throw new BusinessRuleException(
                "Nhà cung cấp chưa có mã số thuế. Vui lòng cập nhật danh mục nhà cung cấp trước khi chọn hóa đơn.");
        return new PickerContext(
            normalizedTaxCode,
            new InputInvoicePickerContextDto
            {
                StockDocumentId = receipt.Id,
                DocumentNo = receipt.DocumentNo,
                ReceiptStatus = receipt.Status.ToString(),
                SupplierId = receipt.SupplierId.Value,
                SupplierCode = receipt.Supplier.Code,
                SupplierName = receipt.Supplier.Name,
                SupplierTaxCode = receipt.Supplier.TaxCode ?? string.Empty,
                IsConfirmed = receipt.Status == StockDocumentStatus.Confirmed,
                ReceiptOwnerLegalEntityId = receipt.Status == StockDocumentStatus.Confirmed
                    ? receipt.ConfirmedLegalEntityId ?? 0
                    : receipt.Warehouse?.LegalEntityId ?? 0,
                ReceiptOwnerLegalEntityName = receipt.Status == StockDocumentStatus.Confirmed
                    ? receipt.ConfirmedLegalEntity?.Name ?? receipt.Warehouse?.LegalEntity?.Name ?? string.Empty
                    : receipt.Warehouse?.LegalEntity?.Name ?? string.Empty
            },
            receipt.Status == StockDocumentStatus.Confirmed
                ? receipt.ConfirmedLegalEntityId ?? 0
                : receipt.Warehouse?.LegalEntityId ?? 0);
    }

    private async Task<PickerContext> RequireSupplierContextAsync(
        int storeId,
        int supplierId,
        CancellationToken ct)
    {
        if (storeId <= 0) throw new BusinessRuleException("StoreId không hợp lệ.");
        if (supplierId <= 0) throw new BusinessRuleException("Nhà cung cấp không hợp lệ.");
        var supplier = await _repository.GetSplitSupplierAsync(storeId, supplierId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy nhà cung cấp đang hoạt động.");
        var normalizedTaxCode = TaxCodeIdentityNormalizer.Normalize(supplier.TaxCode)
            ?? throw new BusinessRuleException(
                "Nhà cung cấp chưa có mã số thuế. Vui lòng cập nhật danh mục nhà cung cấp trước khi chọn hóa đơn.");
        return new PickerContext(
            normalizedTaxCode,
            new InputInvoicePickerContextDto
            {
                StockDocumentId = 0,
                DocumentNo = string.Empty,
                ReceiptStatus = "SplitTarget",
                SupplierId = supplier.Id,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                SupplierTaxCode = supplier.TaxCode ?? string.Empty,
                IsConfirmed = false
            }, 0);
    }

    private async Task<LinkedCandidate> ResolveLinkedCandidateAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct)
    {
        if (inputInvoiceHeadId <= 0)
            throw new BusinessRuleException("Hóa đơn không hợp lệ.");
        var context = await RequireContextAsync(storeId, stockDocumentId, ct);
        var invoice = await _repository.GetLinkedInputInvoiceAsync(
            storeId, stockDocumentId, inputInvoiceHeadId, ct)
            ?? throw new BusinessRuleException(
                "Hóa đơn không được liên kết với phiếu nhập hiện tại.");
        if (!invoice.InvoiceIdentityDate.HasValue ||
            string.IsNullOrWhiteSpace(invoice.NormalizedSellerTaxCode) ||
            string.IsNullOrWhiteSpace(invoice.NormalizedInvoiceSeries) ||
            string.IsNullOrWhiteSpace(invoice.NormalizedInvoiceNumber))
            throw new BusinessRuleException(
                "Hóa đơn đã liên kết chưa có định danh đầy đủ để tìm bản xem trước.");

        var identityDate = invoice.InvoiceIdentityDate.Value.Date;
        var candidates = await _library.BrowseAsync(
            context.NormalizedSupplierTaxCode,
            new InputInvoicePickerBrowseRequest
            {
                Year = identityDate.Year,
                Month = identityDate.Month
            },
            ct);
        var identityKey = EntityIdentityKey(invoice);
        var candidate = candidates.FirstOrDefault(value =>
        {
            var identity = ToIdentity(value);
            return identity is not null && string.Equals(
                IdentityKey(identity), identityKey, StringComparison.Ordinal);
        });
        if (candidate is null)
            throw new BusinessRuleException(
                "Không tìm thấy PDF hoặc XML tương ứng trong thư viện hóa đơn.");
        return new LinkedCandidate(context, invoice, candidate);
    }

    private static void EnsureSellerMatches(string normalizedSupplierTaxCode, InputInvoiceHead invoice)
    {
        if (!string.Equals(
                TaxCodeIdentityNormalizer.Normalize(invoice.SellerTaxCode),
                normalizedSupplierTaxCode,
                StringComparison.Ordinal))
            throw new BusinessRuleException(
                "MST người bán trên XML không khớp nhà cung cấp của phiếu nhập.");
    }

    private static InputInvoiceBusinessIdentityDto? ToIdentity(
        InputInvoicePickerCandidateDto candidate)
    {
        var taxCode = TaxCodeIdentityNormalizer.Normalize(candidate.SellerTaxCode);
        var series = InputInvoiceIdentityPolicy.NormalizeIdentityToken(candidate.InvoiceSeries);
        var number = InputInvoiceIdentityPolicy.NormalizeIdentityToken(candidate.InvoiceNumber);
        if (taxCode is null || series is null || number is null || !candidate.InvoiceDate.HasValue)
            return null;
        return new InputInvoiceBusinessIdentityDto(
            taxCode, series, number, candidate.InvoiceDate.Value.Date);
    }

    private static string IdentityKey(InputInvoiceBusinessIdentityDto identity) =>
        $"{identity.NormalizedSellerTaxCode}|{identity.NormalizedInvoiceSeries}|" +
        $"{identity.NormalizedInvoiceNumber}|{identity.InvoiceIdentityDate:yyyy-MM-dd}";

    private static string EntityIdentityKey(InputInvoiceHead invoice) =>
        $"{invoice.NormalizedSellerTaxCode}|{invoice.NormalizedInvoiceSeries}|" +
        $"{invoice.NormalizedInvoiceNumber}|{invoice.InvoiceIdentityDate:yyyy-MM-dd}";

    private static InputInvoiceXmlPreviewDto ToPreview(InputInvoiceHead invoice) => new()
    {
        InvoiceTemplateCode = invoice.InvoiceTemplateCode,
        InvoiceSeries = invoice.InvoiceSeries,
        InvoiceNumber = invoice.InvoiceNumber,
        InvoiceDate = invoice.InvoiceDate,
        TaxAuthorityCode = invoice.TaxAuthorityCode,
        SellerName = invoice.SellerName,
        SellerTaxCode = invoice.SellerTaxCode,
        SellerAddress = invoice.SellerAddress,
        BuyerName = invoice.BuyerName,
        BuyerTaxCode = invoice.BuyerTaxCode,
        BuyerAddress = invoice.BuyerAddress,
        TotalBeforeTax = invoice.TotalBeforeTax,
        TotalTaxAmount = invoice.TotalTaxAmount,
        TotalPaymentAmount = invoice.TotalPaymentAmount,
        Lines = invoice.Details.OrderBy(x => x.LineNo).Select(x =>
            new InputInvoiceXmlPreviewLineDto
            {
                LineNo = x.LineNo,
                SupplierItemCode = x.SupplierItemCode,
                ItemName = x.ItemName,
                UnitName = x.UnitName,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice,
                LineAmount = x.LineAmount,
                VatRate = x.VatRate,
                VatAmount = x.VatAmount
            }).ToList()
    };

    private async Task ApplyOwnerPreviewAsync(
        int storeId, int receiptOwnerId, InputInvoicePickerCandidateDto candidate,
        CancellationToken ct)
    {
        if (_buyerOwnerResolver is null || !candidate.XmlValid)
            return;
        var invoice = new InputInvoiceHead { StoreId = storeId, BuyerTaxCode = candidate.BuyerTaxCode };
        var resolution = await _buyerOwnerResolver.ResolveWithinTransactionAsync(storeId, invoice, ct);
        candidate.BuyerOwnerResolutionStatus = resolution.Status.ToString();
        candidate.ResolvedBuyerLegalEntityId = resolution.LegalEntityId;
        candidate.ResolvedBuyerLegalEntityName = resolution.LegalEntityName;
        candidate.BuyerOwnerMatchesReceipt = receiptOwnerId > 0 && resolution.LegalEntityId == receiptOwnerId;
        if (!candidate.BuyerOwnerMatchesReceipt)
        {
            candidate.SelectionAllowed = false;
            candidate.SelectionBlockReasonCode = resolution.Status == InputInvoiceBuyerOwnerResolutionStatus.Resolved
                ? "BuyerOwnerMismatch" : $"BuyerOwner{resolution.Status}";
            candidate.SelectionBlockMessage = resolution.Status == InputInvoiceBuyerOwnerResolutionStatus.Resolved
                ? "MST người mua không khớp chủ thể sở hữu phiếu nhập."
                : "MST người mua chưa xác định duy nhất một chủ thể pháp lý cùng cửa hàng.";
        }
    }

    private async Task ApplyOwnerPreviewAsync(
        int storeId, int receiptOwnerId, InputInvoiceHead invoice,
        InputInvoiceXmlPreviewDto preview, CancellationToken ct)
    {
        if (_buyerOwnerResolver is null) return;
        var resolution = await _buyerOwnerResolver.ResolveWithinTransactionAsync(storeId, invoice, ct);
        preview.BuyerOwnerResolutionStatus = resolution.Status.ToString();
        preview.ResolvedBuyerLegalEntityId = resolution.LegalEntityId;
        preview.ResolvedBuyerLegalEntityName = resolution.LegalEntityName;
        preview.BuyerOwnerMatchesReceipt = receiptOwnerId > 0 && resolution.LegalEntityId == receiptOwnerId;
    }

    private sealed record PickerContext(
        string NormalizedSupplierTaxCode,
        InputInvoicePickerContextDto Dto,
        int ReceiptOwnerLegalEntityId);

    private sealed record LinkedCandidate(
        PickerContext Context,
        InputInvoiceHead Invoice,
        InputInvoicePickerCandidateDto Candidate);

    private static string? NormalizeReason(string? reason, bool required)
    {
        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (required && normalized is null)
            throw new BusinessRuleException("Vui lòng nhập lý do.");
        if (normalized?.Length > 1000)
            throw new BusinessRuleException("Lý do không được vượt quá 1000 ký tự.");
        return normalized;
    }

    private static PurchaseReceiptAuditEvent CreateUnlinkAuditEvent(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        string? reason)
        => new()
        {
            StoreId = storeId,
            StockDocumentId = stockDocumentId,
            EventType = PurchaseReceiptAuditEventType.InputInvoiceUnlinked,
            Reason = reason,
            Note = "Gỡ liên kết hóa đơn đầu vào khỏi phiếu nhập.",
            ChangedFieldsJson = PurchaseReceiptAuditEvidence.SerializeChangedFields(
                [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId)]),
            OldValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId)] = inputInvoiceHeadId
                }),
            NewValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId)] = null
                })
        };

    private static PurchaseReceiptAuditEvent CreateRelinkAuditEvent(
        int storeId,
        int stockDocumentId,
        InputInvoiceHead oldInvoice,
        InputInvoiceHead newInvoice,
        string reason)
        => new()
        {
            StoreId = storeId,
            StockDocumentId = stockDocumentId,
            EventType = PurchaseReceiptAuditEventType.InputInvoiceRelinked,
            Reason = reason,
            Note = "Thay liên kết hóa đơn đầu vào sau xác nhận.",
            ChangedFieldsJson = PurchaseReceiptAuditEvidence.SerializeChangedFields(
                [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId)]),
            OldValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId)] = oldInvoice.Id,
                    [nameof(InputInvoiceHead.InvoiceSeries)] = oldInvoice.InvoiceSeries,
                    [nameof(InputInvoiceHead.InvoiceNumber)] = oldInvoice.InvoiceNumber
                }),
            NewValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId)] = newInvoice.Id,
                    [nameof(InputInvoiceHead.InvoiceSeries)] = newInvoice.InvoiceSeries,
                    [nameof(InputInvoiceHead.InvoiceNumber)] = newInvoice.InvoiceNumber
                })
        };
}
