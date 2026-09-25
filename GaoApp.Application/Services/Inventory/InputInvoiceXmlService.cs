using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Security.Cryptography;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoiceXmlService : IInputInvoiceXmlService
{
    private readonly IInputInvoiceRepository _repository;
    private readonly IInputInvoiceSupplierResolutionService? _supplierResolutionService;
    private readonly IInputInvoiceReceiptLinkService? _receiptLinkService;
    private readonly IInputInvoiceOwnerGuardAuditService? _ownerGuardAudit;
    private readonly IInputInvoiceBuyerOwnerResolutionService? _buyerOwnerResolver;
    private readonly IInputInvoiceItemCatalogMappingService? _itemCatalogMappingService;
    private readonly IInputInvoiceReconciliationService? _reconciliationService;
    private readonly IInputInvoiceXmlDocumentParser _parser;

    public InputInvoiceXmlService(
        IInputInvoiceRepository repository,
        IInputInvoiceSupplierResolutionService? supplierResolutionService = null,
        IInputInvoiceXmlDocumentParser? parser = null,
        IInputInvoiceReceiptLinkService? receiptLinkService = null,
        IInputInvoiceOwnerGuardAuditService? ownerGuardAudit = null,
        IInputInvoiceBuyerOwnerResolutionService? buyerOwnerResolver = null,
        IInputInvoiceItemCatalogMappingService? itemCatalogMappingService = null,
        IInputInvoiceReconciliationService? reconciliationService = null)
    {
        _repository = repository;
        _supplierResolutionService = supplierResolutionService;
        _parser = parser ?? new InputInvoiceXmlDocumentParser();
        _receiptLinkService = receiptLinkService;
        _ownerGuardAudit = ownerGuardAudit;
        _buyerOwnerResolver = buyerOwnerResolver;
        _itemCatalogMappingService = itemCatalogMappingService;
        _reconciliationService = reconciliationService;
    }

    public async Task<InputInvoiceXmlUploadResultDto> UploadXmlAsync(
        int storeId,
        UploadInputInvoiceXmlRequest request,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new BusinessRuleException("StoreId không hợp lệ.");

        if (request.StockDocumentId <= 0)
            throw new BusinessRuleException("Phiếu nhập không hợp lệ.");

        if (request.FileBytes == null || request.FileBytes.Length == 0)
            throw new BusinessRuleException("File XML rỗng.");

        await _repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var document = await _repository.LockReceiptForInputInvoiceMutationAsync(
                storeId, request.StockDocumentId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            if (document.Type != StockDocumentType.Receipt)
                throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập.");
            var receiptSupplierTaxCode = document.Supplier?.TaxCode;
            InputInvoiceResolution? resolution = null;
            if (TaxCodeIdentityNormalizer.Normalize(receiptSupplierTaxCode) is null)
            {
                // Legacy upload remains compatible for receipts created before Supplier
                // became a confirm prerequisite. The XML seller remains the fixed context;
                // normal picker/split flows always supply the selected Supplier context.
                try
                {
                    receiptSupplierTaxCode = _parser.Parse(request.FileBytes).SellerTaxCode;
                }
                catch (BusinessRuleException)
                {
                    var xmlHash = ComputeSha256(request.FileBytes);
                    var legacy = await _repository.FindActiveByXmlHashAsync(
                        storeId, xmlHash, ct);
                    if (legacy is null ||
                        (!string.IsNullOrWhiteSpace(legacy.NormalizedSellerTaxCode) &&
                         !string.IsNullOrWhiteSpace(legacy.NormalizedInvoiceSeries) &&
                         !string.IsNullOrWhiteSpace(legacy.NormalizedInvoiceNumber)))
                        throw;
                    resolution = new InputInvoiceResolution(legacy, IsExisting: true);
                }
            }

            resolution ??= await ResolveInvoiceWithinTransactionAsync(
                    storeId,
                    receiptSupplierTaxCode ?? string.Empty,
                    request.OriginalFileName,
                    request.FileBytes,
                    ct);
            var head = resolution.Invoice;
            var linkCreated = _receiptLinkService is not null
                ? await _receiptLinkService.LinkWithinTransactionAsync(
                    storeId, document, head,
                    $"Upload XML: {Path.GetFileName(request.OriginalFileName)}",
                    refreshReconciliation: false,
                    writeLinkAudit: true,
                    ct)
                : await LinkLegacyTestHarnessAsync(storeId, document.Id, head.Id,
                    $"Upload XML: {Path.GetFileName(request.OriginalFileName)}", ct);
            if (_itemCatalogMappingService is not null)
                await _itemCatalogMappingService.AutoApplyKnownMappingsWithinTransactionAsync(
                    storeId, document.Id, head.Id, ct);
            if (_reconciliationService is not null)
                await _reconciliationService.RefreshWithinTransactionAsync(
                    storeId, document.Id, ct);
            await _repository.SaveChangesAsync(ct);
            await _repository.CommitSupplierResolutionTransactionAsync(ct);

            return new InputInvoiceXmlUploadResultDto
            {
                InputInvoiceHeadId = head.Id,
                StockDocumentId = request.StockDocumentId,
                InvoiceTemplateCode = head.InvoiceTemplateCode,
                InvoiceSeries = head.InvoiceSeries,
                InvoiceNumber = head.InvoiceNumber,
                InvoiceDate = head.InvoiceDate,
                SellerTaxCode = head.SellerTaxCode,
                SellerName = head.SellerName,
                TotalBeforeTax = head.TotalBeforeTax,
                TotalTaxAmount = head.TotalTaxAmount,
                TotalPaymentAmount = head.TotalPaymentAmount,
                DetailCount = head.Details?.Count ?? 0,
                IsExistingInvoice = resolution.IsExisting
            };
        }
        catch (InputInvoiceOwnerGuardException exception)
        {
            await _repository.RollbackSupplierResolutionTransactionAsync(ct);
            if (_ownerGuardAudit is not null)
                await _ownerGuardAudit.RecordBlockedLinkAsync(
                    storeId, request.StockDocumentId, exception, ct);
            throw;
        }
        catch
        {
            await _repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    public async Task<InputInvoiceResolution> ResolveInvoiceWithinTransactionAsync(
        int storeId,
        string receiptSupplierTaxCode,
        string originalFileName,
        byte[] fileBytes,
        CancellationToken ct = default)
    {
        if (storeId <= 0 || fileBytes is null || fileBytes.Length == 0)
            throw new BusinessRuleException("Dữ liệu hóa đơn không hợp lệ.");
        var expectedTaxCode = TaxCodeIdentityNormalizer.Normalize(receiptSupplierTaxCode)
            ?? throw new BusinessRuleException("Nhà cung cấp chưa có mã số thuế.");
        var xmlHash = ComputeSha256(fileBytes);
        InputInvoiceHead candidate;
        try
        {
            candidate = _parser.Parse(fileBytes);
        }
        catch (BusinessRuleException)
        {
            var legacy = await _repository.FindActiveByXmlHashAsync(storeId, xmlHash, ct);
            if (legacy is null ||
                (!string.IsNullOrWhiteSpace(legacy.NormalizedSellerTaxCode) &&
                 !string.IsNullOrWhiteSpace(legacy.NormalizedInvoiceSeries) &&
                 !string.IsNullOrWhiteSpace(legacy.NormalizedInvoiceNumber)))
                throw;
            return new InputInvoiceResolution(legacy, IsExisting: true);
        }

        if (!string.Equals(
                TaxCodeIdentityNormalizer.Normalize(candidate.SellerTaxCode),
                expectedTaxCode,
                StringComparison.Ordinal))
            throw new BusinessRuleException(
                "MST người bán trên XML không khớp nhà cung cấp của phiếu nhập.");
        candidate.StoreId = storeId;
        candidate.OriginalFileName = Path.GetFileName(originalFileName);
        candidate.XmlHash = xmlHash;
        return await ResolveCandidateAsync(storeId, candidate, xmlHash, ct);
    }

    public async Task<InputInvoicePickerSelectionResultDto> ImportAndLinkAsync(
        int storeId,
        int stockDocumentId,
        int receiptSupplierId,
        string receiptSupplierTaxCode,
        string originalFileName,
        byte[] fileBytes,
        CancellationToken ct = default)
    {
        if (storeId <= 0 || stockDocumentId <= 0 || receiptSupplierId <= 0)
            throw new BusinessRuleException("Ngữ cảnh phiếu nhập không hợp lệ.");
        var expectedTaxCode = TaxCodeIdentityNormalizer.Normalize(receiptSupplierTaxCode)
            ?? throw new BusinessRuleException("Nhà cung cấp chưa có mã số thuế.");
        if (_supplierResolutionService is null)
            throw new InvalidOperationException("Canonical Supplier binding is not configured.");

        await _repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var receipt = await _repository.LockReceiptForInputInvoiceMutationAsync(
                storeId, stockDocumentId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            if (receipt.Type != StockDocumentType.Receipt)
                throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập.");
            if (receipt.SupplierId != receiptSupplierId)
                throw new BusinessRuleException("Nhà cung cấp phiếu nhập đã thay đổi. Vui lòng tải lại.");

            var resolution = await ResolveInvoiceWithinTransactionAsync(
                storeId, expectedTaxCode, originalFileName, fileBytes, ct);

            var linkCreated = _receiptLinkService is not null
                ? await _receiptLinkService.LinkWithinTransactionAsync(
                    storeId, receipt, resolution.Invoice,
                    "Liên kết từ thư viện hóa đơn.",
                    refreshReconciliation: false,
                    writeLinkAudit: true,
                    ct)
                : await LinkLegacyTestHarnessAsync(storeId, stockDocumentId,
                    resolution.Invoice.Id, "Liên kết từ thư viện hóa đơn.", ct);
            if (_itemCatalogMappingService is not null)
                await _itemCatalogMappingService.AutoApplyKnownMappingsWithinTransactionAsync(
                    storeId, stockDocumentId, resolution.Invoice.Id, ct);
            if (_reconciliationService is not null)
                await _reconciliationService.RefreshWithinTransactionAsync(
                    storeId, stockDocumentId, ct);
            await _repository.SaveChangesAsync(ct);
            await _repository.CommitSupplierResolutionTransactionAsync(ct);

            return new InputInvoicePickerSelectionResultDto
            {
                InputInvoiceHeadId = resolution.Invoice.Id,
                StockDocumentId = stockDocumentId,
                InvoiceSeries = resolution.Invoice.InvoiceSeries,
                InvoiceNumber = resolution.Invoice.InvoiceNumber,
                InvoiceDate = resolution.Invoice.InvoiceDate,
                TotalPaymentAmount = resolution.Invoice.TotalPaymentAmount,
                IsExistingInvoice = resolution.IsExisting,
                WasAlreadyLinked = !linkCreated
            };
        }
        catch (InputInvoiceOwnerGuardException exception)
        {
            await _repository.RollbackSupplierResolutionTransactionAsync(ct);
            if (_ownerGuardAudit is not null)
                await _ownerGuardAudit.RecordBlockedLinkAsync(
                    storeId, stockDocumentId, exception, ct);
            throw;
        }
        catch
        {
            await _repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    // Compatibility only for direct legacy unit construction. Runtime DI always
    // supplies the central link service and therefore cannot enter this branch.
    private async Task<bool> LinkLegacyTestHarnessAsync(
        int storeId, int stockDocumentId, int invoiceId, string note,
        CancellationToken ct)
    {
        if (_supplierResolutionService is not null)
            await _supplierResolutionService.BindCanonicalSupplierWithinTransactionAsync(
                storeId, stockDocumentId, invoiceId, ct);
        var created = await _repository.EnsureSingleReceiptInvoiceMapAsync(new()
        {
            StoreId = storeId,
            StockDocumentId = stockDocumentId,
            InputInvoiceHeadId = invoiceId,
            Note = note
        }, ct);
        await _repository.AddMissingLineMapsAsync(storeId, stockDocumentId, ct);
        if (created)
            await _repository.AddPurchaseReceiptAuditEventAsync(
                CreateLinkAuditEvent(storeId, stockDocumentId, invoiceId), ct);
        return created;
    }

    private static PurchaseReceiptAuditEvent CreateLinkAuditEvent(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId)
        => new()
        {
            StoreId = storeId,
            StockDocumentId = stockDocumentId,
            EventType = PurchaseReceiptAuditEventType.InputInvoiceLinked,
            Note = "Liên kết hóa đơn đầu vào với phiếu nhập.",
            ChangedFieldsJson = PurchaseReceiptAuditEvidence.SerializeChangedFields(
                [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId)]),
            OldValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId)] = null
                }),
            NewValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId)] = inputInvoiceHeadId
                })
        };
    public async Task<List<InputInvoiceHeadDto>> GetInvoicesByStockDocumentAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new BusinessRuleException("StoreId không hợp lệ.");

        if (stockDocumentId <= 0)
            throw new BusinessRuleException("Phiếu nhập không hợp lệ.");

        var receipt = await _repository.GetReceiptForSupplierResolutionAsync(
            storeId,
            stockDocumentId,
            ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
        if (receipt.Type != StockDocumentType.Receipt)
            throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập kho.");

        var receiptOwnerId = receipt.Status == StockDocumentStatus.Confirmed
            ? receipt.ConfirmedLegalEntityId
            : receipt.Warehouse?.LegalEntityId;
        var receiptOwnerName = receipt.Status == StockDocumentStatus.Confirmed
            ? receipt.ConfirmedLegalEntity?.Name
            : receipt.Warehouse?.LegalEntity?.Name;

        var invoices = await _repository.GetByStockDocumentAsync(
            storeId,
            stockDocumentId,
            ct);
        var itemMappings = _itemCatalogMappingService is null
            ? new Dictionary<int, InputInvoiceItemCatalogResolutionDto>()
            : await _itemCatalogMappingService.ResolveForReceiptAsync(
                storeId, stockDocumentId, ct);

        var result = new List<InputInvoiceHeadDto>(invoices.Count);
        foreach (var invoice in invoices)
        {
            var resolution = _buyerOwnerResolver is null
                ? null
                : await _buyerOwnerResolver.ResolveWithinTransactionAsync(
                    storeId, invoice, ct);
            var status = resolution?.Status ?? invoice.BuyerOwnerResolutionStatus;
            var resolvedOwnerId = resolution?.LegalEntityId ?? invoice.ResolvedBuyerLegalEntityId;
            var resolvedOwnerName = resolution?.LegalEntityName ?? invoice.ResolvedBuyerLegalEntity?.Name;
            var matches = receiptOwnerId is > 0 &&
                status == InputInvoiceBuyerOwnerResolutionStatus.Resolved &&
                resolvedOwnerId == receiptOwnerId;
            var warning = BuildOwnerWarning(status, matches, receiptOwnerId);

            result.Add(new InputInvoiceHeadDto
            {
                Id = invoice.Id,
                InvoiceTemplateCode = invoice.InvoiceTemplateCode,
                InvoiceSeries = invoice.InvoiceSeries,
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceDate = invoice.InvoiceDate,
                SellerTaxCode = invoice.SellerTaxCode,
                SellerName = invoice.SellerName,
                BuyerTaxCode = invoice.BuyerTaxCode,
                BuyerOwnerResolutionStatus = status.ToString(),
                ResolvedBuyerLegalEntityId = resolvedOwnerId,
                ResolvedBuyerLegalEntityName = resolvedOwnerName,
                ReceiptOwnerLegalEntityId = receiptOwnerId,
                ReceiptOwnerLegalEntityName = receiptOwnerName,
                BuyerOwnerMatchesReceipt = matches,
                OwnerWarningReasonCode = warning.ReasonCode,
                OwnerWarningMessage = warning.Message,
                TotalBeforeTax = invoice.TotalBeforeTax,
                TotalTaxAmount = invoice.TotalTaxAmount,
                TotalPaymentAmount = invoice.TotalPaymentAmount,
                DetailCount = invoice.Details?.Count ?? 0,
                Details = (invoice.Details ?? new List<InputInvoiceDetail>())
                    .OrderBy(d => d.LineNo)
                    .Select(d => new InputInvoiceDetailDto
                    {
                        Id = d.Id,
                        LineNo = d.LineNo,
                        SupplierItemCode = d.SupplierItemCode,
                        ItemName = d.ItemName,
                        UnitName = d.UnitName,
                        Quantity = d.Quantity,
                        UnitPrice = d.UnitPrice,
                        LineAmount = d.LineAmount,
                        VatRate = d.VatRate,
                        VatAmount = d.VatAmount,
                        ItemCatalogMapping = itemMappings.GetValueOrDefault(d.Id)
                    })
                    .ToList()
            });
        }

        return result;
    }

    private static (string? ReasonCode, string? Message) BuildOwnerWarning(
        InputInvoiceBuyerOwnerResolutionStatus status,
        bool matches,
        int? receiptOwnerId)
    {
        if (matches) return (null, null);
        if (receiptOwnerId is null or <= 0)
            return ("ReceiptOwnerMissing", "Không xác định được chủ thể sở hữu phiếu nhập.");
        return status switch
        {
            InputInvoiceBuyerOwnerResolutionStatus.Resolved =>
                ("BuyerOwnerMismatch", "MST người mua không còn khớp chủ thể sở hữu phiếu nhập."),
            InputInvoiceBuyerOwnerResolutionStatus.MissingBuyerTaxCode =>
                ("BuyerOwnerMissingBuyerTaxCode", "Hóa đơn thiếu MST người mua."),
            InputInvoiceBuyerOwnerResolutionStatus.NotFound =>
                ("BuyerOwnerNotFound", "Không còn tìm thấy chủ thể pháp lý khớp MST người mua."),
            InputInvoiceBuyerOwnerResolutionStatus.Ambiguous =>
                ("BuyerOwnerAmbiguous", "MST người mua hiện khớp nhiều chủ thể pháp lý."),
            _ => ("BuyerOwnerNotEvaluated", "Chưa xác định được chủ thể người mua của hóa đơn.")
        };
    }

    public async Task<List<StockDocumentLineInputInvoiceMapDto>> GetLineMapsAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default)
    {
        await _repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            _ = await _repository.LockReceiptForInputInvoiceMutationAsync(
                    storeId, stockDocumentId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            await _repository.AddMissingLineMapsAsync(storeId, stockDocumentId, ct);
            await _repository.SaveChangesAsync(ct);
            if (_reconciliationService is not null)
                await _reconciliationService.RefreshWithinTransactionAsync(
                    storeId, stockDocumentId, ct);

            var maps = await _repository.GetLineMapsByStockDocumentAsync(
                storeId,
                stockDocumentId,
                ct);
            var itemMappings = _itemCatalogMappingService is null
                ? new Dictionary<int, InputInvoiceItemCatalogResolutionDto>()
                : await _itemCatalogMappingService.ResolveForReceiptAsync(
                    storeId, stockDocumentId, ct);
            var result = maps.Select(x => new StockDocumentLineInputInvoiceMapDto
            {
                StockDocumentId = x.StockDocumentId,
                StockDocumentLineId = x.StockDocumentLineId,
                UseInputInvoice = x.UseInputInvoice,
                InputInvoiceDetailId = x.InputInvoiceDetailId,
                MatchStatus = x.MatchStatus,
                QuantityDifference = x.QuantityDifference,
                AmountDifference = x.AmountDifference,
                ExclusionReason = x.ExclusionReason,
                XmlItemName = x.InputInvoiceDetail?.ItemName,
                XmlUnitName = x.InputInvoiceDetail?.UnitName,
                XmlQuantity = x.InputInvoiceDetail?.Quantity,
                XmlLineAmount = x.InputInvoiceDetail?.LineAmount,
                ItemCatalogMapping = x.InputInvoiceDetailId.HasValue
                    ? itemMappings.GetValueOrDefault(x.InputInvoiceDetailId.Value)
                    : null
            }).ToList();
            await _repository.CommitSupplierResolutionTransactionAsync(ct);
            return result;
        }
        catch
        {
            await _repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    public async Task UpdateLineMapAsync(
        int storeId,
        int stockDocumentId,
        UpdateStockDocumentLineInputInvoiceMapRequest request,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new BusinessRuleException("StoreId không hợp lệ.");

        if (stockDocumentId <= 0)
            throw new BusinessRuleException("Phiếu nhập không hợp lệ.");

        if (request.StockDocumentLineId <= 0)
            throw new BusinessRuleException("Dòng nhập không hợp lệ.");

        await _repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
        var receipt = await _repository.LockReceiptForInputInvoiceMutationAsync(
                storeId, stockDocumentId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
        PurchaseReceiptWorkflowPolicy.EnsureInputInvoiceMappingEditable(receipt.Status);

        var line = await _repository.GetStockDocumentLineAsync(
            storeId,
            request.StockDocumentLineId,
            ct);

        if (line == null || line.StockDocumentId != stockDocumentId)
            throw new BusinessRuleException("Dòng nhập không thuộc phiếu hiện tại.");

        var map = await _repository.GetLineMapAsync(
            storeId,
            stockDocumentId,
            request.StockDocumentLineId,
            ct);

        if (map == null ||
            map.StoreId != storeId ||
            map.StockDocumentId != stockDocumentId ||
            map.StockDocumentLineId != request.StockDocumentLineId)
            throw new BusinessRuleException("Map dòng không hợp lệ hoặc không thuộc phiếu hiện tại.");

        if (!request.UseInputInvoice)
        {
            if (string.IsNullOrWhiteSpace(request.ExclusionReason))
                throw new BusinessRuleException(
                    "Vui lòng nhập lý do loại dòng nhập khỏi hóa đơn XML.");
            map.UseInputInvoice = false;
            map.InputInvoiceDetailId = null;
            map.MatchStatus = InputInvoiceMatchStatus.Excluded;
            map.QuantityDifference = 0;
            map.AmountDifference = 0;
            map.Note = "User chọn dòng này không thuộc hóa đơn XML.";
            map.ExclusionReason = request.ExclusionReason.Trim();
        }
        else
        {
            // CHỐT PHƯƠNG ÁN 2.6:
            // UseInputInvoice = true là đủ xác nhận dòng nhập này thuộc hóa đơn XML.
            // InputInvoiceDetailId không bắt buộc, chỉ dùng để đối chiếu sâu nếu user chọn.
            if (!request.InputInvoiceDetailId.HasValue || request.InputInvoiceDetailId.Value <= 0)
            {
                map.UseInputInvoice = true;
                map.InputInvoiceDetailId = null;
                map.MatchStatus = InputInvoiceMatchStatus.None;
                map.QuantityDifference = 0;
                map.AmountDifference = 0;
                map.Note = "User chọn dòng này thuộc hóa đơn XML nhưng chưa map chi tiết dòng XML.";
                map.ExclusionReason = null;
            }
            else
            {
                var xmlLine = await _repository.GetInputInvoiceDetailAsync(
                    storeId,
                    stockDocumentId,
                    request.StockDocumentLineId,
                    request.InputInvoiceDetailId.Value,
                    ct);

                if (xmlLine == null)
                    throw new BusinessRuleException("Không thể map dòng XML cho phiếu hiện tại.");

                if (request.RememberItemCatalogMapping)
                {
                    var itemCatalogMapping = _itemCatalogMappingService
                        ?? throw new BusinessRuleException(
                            "Chức năng ghi nhớ mặt hàng XML chưa sẵn sàng.");
                    var normalizedXmlUnit = xmlLine.NormalizedUnitName ??
                        InputInvoiceItemIdentityNormalizer.NormalizeText(xmlLine.UnitName);
                    if (normalizedXmlUnit is null)
                        throw new BusinessRuleException(
                            "Không thể ghi nhớ vì dòng XML thiếu đơn vị. " +
                            "Bạn vẫn có thể ghép dòng XML khi bỏ chọn ghi nhớ.");
                    var xmlTargets = await _repository
                        .GetInputInvoiceItemCatalogTargetsByUnitAsync(
                            storeId, line.ProductVariantId, normalizedXmlUnit, ct);
                    if (xmlTargets.Count != 1)
                        throw new BusinessRuleException(xmlTargets.Count > 1
                            ? "Không thể ghi nhớ vì đơn vị XML khớp nhiều quy đổi sản phẩm. " +
                              "Bạn vẫn có thể ghép dòng XML khi bỏ chọn ghi nhớ."
                            : "Không thể ghi nhớ vì đơn vị XML chưa có quy đổi sản phẩm hợp lệ. " +
                              "Bạn vẫn có thể ghép dòng XML khi bỏ chọn ghi nhớ.");
                    var conversionId = xmlTargets[0].Conversion.Id;

                    var itemResolution = await itemCatalogMapping
                        .ConfirmWithinTransactionAsync(
                            storeId,
                            stockDocumentId,
                            request.StockDocumentLineId,
                            xmlLine.Id,
                            line.ProductVariantId,
                            conversionId,
                            request.MappingRowVersion,
                            ct);

                    if (itemResolution.State !=
                        InputInvoiceItemCatalogResolutionState.Confirmed)
                        throw new BusinessRuleException(
                            itemResolution.Message ??
                            "Mapping danh mục cần được xác nhận.");
                    if (itemResolution.ProductVariantId != line.ProductVariantId ||
                        itemResolution.ProductUnitConversionId != conversionId)
                        throw new BusinessRuleException(
                            "Sản phẩm/đơn vị mapping không khớp mục tiêu XML đã xác định.");
                }

                map.UseInputInvoice = true;
                map.InputInvoiceDetailId = xmlLine.Id;

                var quantityDiff = line.Quantity - xmlLine.Quantity;
                var amountDiff = line.LineTotal - xmlLine.LineAmount;

                map.QuantityDifference = quantityDiff;
                map.AmountDifference = amountDiff;
                map.MatchStatus = ResolveMatchStatus(quantityDiff, amountDiff);
                map.Note = null;
                map.ExclusionReason = null;
            }
        }

        if (_reconciliationService is not null)
            await _reconciliationService.RefreshWithinTransactionAsync(
                storeId, stockDocumentId, ct);
        await _repository.SaveChangesAsync(ct);
        await _repository.CommitSupplierResolutionTransactionAsync(ct);
        }
        catch
        {
            await _repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }
    public async Task BulkUpdateLineMapsAsync(
    int storeId,
    int stockDocumentId,
    bool useInputInvoice,
    string? exclusionReason,
    CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new BusinessRuleException("StoreId không hợp lệ.");

        if (stockDocumentId <= 0)
            throw new BusinessRuleException("Phiếu nhập không hợp lệ.");
        if (!useInputInvoice && string.IsNullOrWhiteSpace(exclusionReason))
            throw new BusinessRuleException(
                "Vui lòng nhập lý do loại các dòng nhập khỏi hóa đơn XML.");

        await _repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
        var receipt = await _repository.LockReceiptForInputInvoiceMutationAsync(
                storeId, stockDocumentId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
        PurchaseReceiptWorkflowPolicy.EnsureInputInvoiceMappingEditable(receipt.Status);

        // Đảm bảo mỗi dòng nhập đều có map
        await _repository.AddMissingLineMapsAsync(storeId, stockDocumentId, ct);
        await _repository.SaveChangesAsync(ct);

        var maps = await _repository.GetLineMapsByStockDocumentAsync(
            storeId,
            stockDocumentId,
            ct);

        foreach (var map in maps)
        {
            map.UseInputInvoice = useInputInvoice;

            if (!useInputInvoice)
            {
                map.InputInvoiceDetailId = null;
                map.MatchStatus = InputInvoiceMatchStatus.Excluded;
                map.QuantityDifference = 0;
                map.AmountDifference = 0;
                map.Note = "User chọn bỏ tất cả dòng khỏi hóa đơn XML.";
                map.ExclusionReason = exclusionReason!.Trim();
            }
            else
            {
                // Không bắt buộc map chi tiết dòng XML
                map.InputInvoiceDetailId = map.InputInvoiceDetailId;
                map.MatchStatus = map.InputInvoiceDetailId.HasValue
                    ? map.MatchStatus
                    : InputInvoiceMatchStatus.None;

                map.QuantityDifference = map.InputInvoiceDetailId.HasValue
                    ? map.QuantityDifference
                    : 0;

                map.AmountDifference = map.InputInvoiceDetailId.HasValue
                    ? map.AmountDifference
                    : 0;

                map.Note = map.InputInvoiceDetailId.HasValue
                    ? map.Note
                    : "User chọn tất cả dòng thuộc hóa đơn XML nhưng chưa map chi tiết dòng XML.";
                map.ExclusionReason = null;
            }
        }

        if (_reconciliationService is not null)
            await _reconciliationService.RefreshWithinTransactionAsync(
                storeId, stockDocumentId, ct);
        await _repository.SaveChangesAsync(ct);
        await _repository.CommitSupplierResolutionTransactionAsync(ct);
        }
        catch
        {
            await _repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }
    private async Task<InputInvoiceResolution> ResolveCandidateAsync(
    int storeId,
    InputInvoiceHead candidate,
    string xmlHash,
    CancellationToken ct)
    {
        try
        {
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(
                candidate);
        }
        catch (BusinessRuleException)
        {
            var existingByHash =
                await _repository.FindActiveByXmlHashAsync(
                    storeId,
                    xmlHash,
                    ct);

            if (existingByHash is null)
                throw;

            return new InputInvoiceResolution(
                existingByHash,
                IsExisting: true);
        }

        return await _repository.ResolveInputInvoiceAsync(
            candidate,
            ct);
    }
    private static InputInvoiceMatchStatus ResolveMatchStatus(decimal quantityDiff, decimal amountDiff)
    {
        var qtyMismatch = Math.Abs(quantityDiff) > 0.0001m;
        var amountMismatch = Math.Abs(amountDiff) > 1m;

        if (qtyMismatch && amountMismatch)
            return InputInvoiceMatchStatus.QuantityAndAmountMismatch;

        if (qtyMismatch)
            return InputInvoiceMatchStatus.QuantityMismatch;

        if (amountMismatch)
            return InputInvoiceMatchStatus.AmountMismatch;

        return InputInvoiceMatchStatus.Matched;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

}
