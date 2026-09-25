using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class StockDocumentSplitService : IStockDocumentSplitService
{
    private readonly IInputInvoiceRepository _inputInvoices;
    private readonly IStockDocumentRepository _stockDocuments;
    private readonly IDocumentNumberSequenceRepository _numbers;
    private readonly IInputInvoiceDocumentLibrary _library;
    private readonly IInputInvoiceXmlService _xml;
    private readonly IInputInvoiceSupplierResolutionService _supplierResolution;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IInputInvoiceReceiptOwnerGuard? _ownerGuard;
    private readonly IInputInvoiceReceiptLinkService? _linkService;
    private readonly IInputInvoiceOwnerGuardAuditService? _ownerGuardAudit;
    private readonly IInputInvoiceReconciliationService? _reconciliation;

    public StockDocumentSplitService(
        IInputInvoiceRepository inputInvoices,
        IStockDocumentRepository stockDocuments,
        IDocumentNumberSequenceRepository numbers,
        IInputInvoiceDocumentLibrary library,
        IInputInvoiceXmlService xml,
        IInputInvoiceSupplierResolutionService supplierResolution,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IInputInvoiceReceiptOwnerGuard? ownerGuard = null,
        IInputInvoiceReceiptLinkService? linkService = null,
        IInputInvoiceOwnerGuardAuditService? ownerGuardAudit = null,
        IInputInvoiceReconciliationService? reconciliation = null)
    {
        _inputInvoices = inputInvoices;
        _stockDocuments = stockDocuments;
        _numbers = numbers;
        _library = library;
        _xml = xml;
        _supplierResolution = supplierResolution;
        _tenant = tenant;
        _currentUser = currentUser;
        _ownerGuard = ownerGuard;
        _linkService = linkService;
        _ownerGuardAudit = ownerGuardAudit;
        _reconciliation = reconciliation;
    }

    public async Task<PurchaseReceiptSplitWorkspaceDto> GetWorkspaceAsync(
        int sourceReceiptId,
        CancellationToken ct = default)
    {
        var storeId = RequireStoreAndActor();
        await _inputInvoices.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var source = await _inputInvoices.LockReceiptAggregateForSplitAsync(
                storeId, sourceReceiptId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            ValidateSourceState(source);
            var activeMaps = source.InputInvoiceMaps.Where(x => !x.IsDeleted).ToArray();
            if (activeMaps.Length > 1)
                throw new BusinessRuleException(
                    "Phiếu nhập đang liên kết nhiều hóa đơn. Vui lòng xử lý dữ liệu trước khi tách.");

            var suppliers = await _inputInvoices.GetSplitSuppliersAsync(storeId, ct);
            var warehouses = await _inputInvoices.GetSplitWarehousesAsync(storeId, ct);
            var dto = new PurchaseReceiptSplitWorkspaceDto
            {
                SourceReceiptId = source.Id,
                SourceDocumentNo = source.DocumentNo,
                DocumentDate = source.DocumentDate,
                RowVersion = Encode(source.RowVersion),
                WarehouseId = source.WarehouseId,
                SupplierId = source.SupplierId!.Value,
                SourceInvoiceHeadId = activeMaps.SingleOrDefault()?.InputInvoiceHeadId,
                SourceInvoiceLabel = FormatInvoice(activeMaps.SingleOrDefault()?.InputInvoiceHead),
                IsMerchandisePaid = source.IsMerchandisePaid,
                MerchandisePayeeName = source.MerchandisePayeeName,
                FreightTotal = source.FreightTotal,
                Lines = source.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineNo).Select(x =>
                    new PurchaseReceiptSplitWorkspaceLineDto
                    {
                        SourceLineId = x.Id,
                        LineNo = x.LineNo,
                        ProductName = x.ProductNameSnapshot,
                        Sku = x.SkuSnapshot,
                        UnitName = x.UnitNameSnapshot,
                        Factor = x.Factor,
                        Quantity = x.Quantity,
                        BaseQuantity = x.BaseQuantity,
                        RowVersion = Encode(x.RowVersion)
                    }).ToList(),
                Suppliers = suppliers.Select(x => new PurchaseReceiptSplitSupplierOptionDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    TaxCode = x.TaxCode
                }).ToList(),
                Warehouses = warehouses.Select(x => new PurchaseReceiptSplitWarehouseOptionDto
                    {
                        Id = x.Id,
                        LegalEntityId = x.LegalEntityId,
                        Code = x.Code,
                        Name = x.Name
                    }).ToList()
            };
            await _inputInvoices.CommitSupplierResolutionTransactionAsync(ct);
            return dto;
        }
        catch
        {
            await _inputInvoices.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    public async Task<PurchaseReceiptSplitResultDto> SplitAsync(
        int sourceReceiptId,
        PurchaseReceiptSplitRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var storeId = RequireStoreAndActor();
        await _inputInvoices.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var source = await _inputInvoices.LockReceiptAggregateForSplitAsync(
                storeId, sourceReceiptId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            ValidateSourceState(source);
            ValidateSnapshot(source, request);

            var suppliers = await ResolveSuppliersAsync(storeId, request.Targets, ct);
            var warehouses = await ResolveWarehousesAsync(storeId, source, request.Targets, ct);
            PurchaseReceiptSplitPlan plan;
            try
            {
                plan = PurchaseReceiptSplitPolicy.Build(new PurchaseReceiptSplitPolicyInput
                {
                    SourceSupplierId = source.SupplierId!.Value,
                    SourceIsMerchandisePaid = source.IsMerchandisePaid,
                    SourceMerchandisePayeeName = source.MerchandisePayeeName,
                    HasFreight = source.HasFreight,
                    CapitalizeFreightInInventoryCost = source.CapitalizeFreightInInventoryCost,
                    FreightTotal = source.FreightTotal,
                    Lines = source.Lines.Where(x => !x.IsDeleted).Select(x =>
                        new PurchaseReceiptSplitSourceLine(
                            x.Id, x.BaseQuantity, x.Factor, x.LineTotal, x.FreightAllocation)).ToArray(),
                    Targets = request.Targets.Select(x => new PurchaseReceiptSplitTargetInput(
                        x.TargetIndex,
                        x.SupplierId,
                        x.IsMerchandisePaid,
                        x.MerchandisePayeeName,
                        x.PaymentStateConfirmed,
                        x.Allocations.Select(a => new PurchaseReceiptSplitAllocationInput(
                            a.SourceLineId, a.BaseQuantity)).ToArray())).ToArray()
                });
            }
            catch (InvalidOperationException exception)
            {
                throw new BusinessRuleException(exception.Message);
            }

            var desiredInvoices = await ResolveTargetInvoicesAsync(
                storeId, source, request.Targets, suppliers, ct);

            // Validate every target before number allocation, line-map reset, or
            // any source/child mutation. One invalid owner aborts the whole split.
            if (_ownerGuard is not null)
            {
                foreach (var target in request.Targets.OrderBy(x => x.TargetIndex))
                {
                    var invoice = desiredInvoices[target.TargetIndex];
                    if (invoice is null) continue;
                    var warehouse = warehouses[target.WarehouseId];
                    await _ownerGuard.ValidateLinkWithinTransactionAsync(
                        storeId,
                        new StockDocument
                        {
                            StoreId = storeId,
                            Type = StockDocumentType.Receipt,
                            Status = StockDocumentStatus.PendingApproval,
                            WarehouseId = warehouse.Id,
                            Warehouse = warehouse
                        },
                        invoice,
                        ct);
                }
            }
            var documentNumbers = await AllocateChildNumbersAsync(
                storeId, source.DocumentDate, plan.Targets.Count - 1, ct);
            var splitNowUtc = DateTime.UtcNow;
            if (_reconciliation is not null)
                await _reconciliation.InvalidateWithinTransactionAsync(
                    storeId, source.Id, "Phiếu nhập được tách; đối chiếu phải tính lại.", ct);
            await _inputInvoices.ResetReceiptLineMapsAsync(storeId, source.Id, ct);

            var sourceLines = source.Lines.Where(x => !x.IsDeleted)
                .ToDictionary(x => x.Id);
            var results = new List<StockDocument>();
            foreach (var target in plan.Targets.OrderBy(x => x.TargetIndex))
            {
                var requestTarget = request.Targets.Single(x => x.TargetIndex == target.TargetIndex);
                if (target.TargetIndex == 1)
                {
                    await ApplyTargetToSource(
                        source, target, requestTarget, suppliers[target.SupplierId],
                        warehouses[requestTarget.WarehouseId], sourceLines, ct);
                    results.Add(source);
                }
                else
                {
                    var child = CreateChild(
                        source,
                        documentNumbers[target.TargetIndex - 2],
                        target,
                        requestTarget,
                        suppliers[target.SupplierId],
                        splitNowUtc,
                        sourceLines);
                    await _stockDocuments.AddAsync(child, ct);
                    results.Add(child);
                }
            }

            await _stockDocuments.SaveChangesAsync(ct);
            await ApplyInvoiceRelationsAsync(
                storeId, source, results, desiredInvoices, ct);
            if (_reconciliation is not null)
            {
                foreach (var result in results)
                    await _reconciliation.RefreshWithinTransactionAsync(
                        storeId, result.Id, ct);
            }
            await AddSplitAuditAsync(storeId, source, results, plan, splitNowUtc, ct);
            await _stockDocuments.SaveChangesAsync(ct);
            await _inputInvoices.CommitSupplierResolutionTransactionAsync(ct);

            return new PurchaseReceiptSplitResultDto
            {
                Results = results.Select((document, index) =>
                    new PurchaseReceiptSplitResultLinkDto
                    {
                        TargetIndex = index + 1,
                        StockDocumentId = document.Id,
                        DocumentNo = document.DocumentNo,
                        Url = $"/admin/stock-documents/{document.Id}"
                    }).ToList()
            };
        }
        catch (InputInvoiceOwnerGuardException exception)
        {
            await _inputInvoices.RollbackSupplierResolutionTransactionAsync(ct);
            if (_ownerGuardAudit is not null)
                await _ownerGuardAudit.RecordBlockedLinkAsync(
                    storeId, sourceReceiptId, exception, ct);
            throw;
        }
        catch
        {
            await _inputInvoices.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    private int RequireStoreAndActor()
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
            throw new BusinessRuleException("Không xác định được người thực hiện.");
        return _tenant.StoreId is > 0
            ? _tenant.StoreId.Value
            : throw new BusinessRuleException("Không xác định được cửa hàng hiện tại.");
    }

    private static void ValidateSourceState(StockDocument source)
    {
        if (source.Type != StockDocumentType.Receipt)
            throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập.");
        if (source.ReceiptSource != PurchaseReceiptSource.Direct || source.PurchaseOrderId.HasValue)
            throw new BusinessRuleException("Chỉ phiếu nhập trực tiếp mới được tách.");
        if (source.Status != StockDocumentStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ phiếu nhập đang chờ duyệt mới được tách.");
        if (!source.SupplierId.HasValue || source.Supplier is null)
            throw new BusinessRuleException("Phiếu nhập chưa có nhà cung cấp.");
        if (source.Warehouse is null)
            throw new BusinessRuleException("Phiếu nhập chưa có kho hợp lệ.");
        if (!source.Lines.Any(x => !x.IsDeleted))
            throw new BusinessRuleException("Phiếu nhập chưa có dòng hợp lệ để tách.");
    }

    private static void ValidateSnapshot(
        StockDocument source,
        PurchaseReceiptSplitRequest request)
    {
        if (!Matches(source.RowVersion, request.SourceRowVersion))
            throw new BusinessRuleException("Phiếu nhập đã thay đổi. Vui lòng tải lại trước khi tách.");

        var activeLines = source.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.Id).ToArray();
        var snapshots = request.SourceLines.OrderBy(x => x.SourceLineId).ToArray();
        if (activeLines.Length != snapshots.Length ||
            !activeLines.Select(x => x.Id).SequenceEqual(snapshots.Select(x => x.SourceLineId)))
            throw new BusinessRuleException("Danh sách dòng phiếu đã thay đổi. Vui lòng tải lại.");
        foreach (var pair in activeLines.Zip(snapshots))
        {
            if (!Matches(pair.First.RowVersion, pair.Second.RowVersion) ||
                pair.First.BaseQuantity != pair.Second.BaseQuantity)
                throw new BusinessRuleException("Dòng phiếu đã thay đổi. Vui lòng tải lại trước khi tách.");
        }

        var maps = source.InputInvoiceMaps.Where(x => !x.IsDeleted).ToArray();
        if (maps.Length > 1)
            throw new BusinessRuleException("Phiếu nhập đang liên kết nhiều hóa đơn.");
        if (maps.SingleOrDefault()?.InputInvoiceHeadId != request.SourceInvoiceHeadId)
            throw new BusinessRuleException("Liên kết hóa đơn đã thay đổi. Vui lòng tải lại trước khi tách.");
    }

    private async Task<Dictionary<int, Supplier>> ResolveSuppliersAsync(
        int storeId,
        IReadOnlyList<PurchaseReceiptSplitTargetRequest> targets,
        CancellationToken ct)
    {
        var result = new Dictionary<int, Supplier>();
        foreach (var id in targets.Select(x => x.SupplierId).Distinct())
        {
            var supplier = await _stockDocuments.GetSupplierAsync(id, ct);
            if (supplier is null || supplier.StoreId != storeId || !supplier.IsActive)
                throw new BusinessRuleException("Nhà cung cấp của phiếu kết quả không hợp lệ.");
            result[id] = supplier;
        }
        return result;
    }

    private async Task<Dictionary<int, Warehouse>> ResolveWarehousesAsync(
        int storeId,
        StockDocument source,
        IReadOnlyList<PurchaseReceiptSplitTargetRequest> targets,
        CancellationToken ct)
    {
        var result = new Dictionary<int, Warehouse>();
        foreach (var id in targets.Select(x => x.WarehouseId).Distinct())
        {
            var warehouse = await _inputInvoices.GetSplitWarehouseAsync(storeId, id, ct)
                ?? throw new BusinessRuleException("Kho của phiếu kết quả không hợp lệ.");
            if (warehouse.LegalEntityId != source.Warehouse.LegalEntityId)
                throw new BusinessRuleException("Kho của phiếu kết quả phải cùng chủ thể pháp lý.");
            result[id] = warehouse;
        }
        return result;
    }

    private async Task<Dictionary<int, InputInvoiceHead?>> ResolveTargetInvoicesAsync(
        int storeId,
        StockDocument source,
        IReadOnlyList<PurchaseReceiptSplitTargetRequest> targets,
        IReadOnlyDictionary<int, Supplier> suppliers,
        CancellationToken ct)
    {
        var existing = source.InputInvoiceMaps.Where(x => !x.IsDeleted)
            .SingleOrDefault()?.InputInvoiceHead;
        var result = new Dictionary<int, InputInvoiceHead?>();
        foreach (var target in targets)
        {
            if (target.ExistingInputInvoiceHeadId.HasValue &&
                !string.IsNullOrWhiteSpace(target.InvoiceDocumentKey))
                throw new BusinessRuleException("Mỗi phiếu kết quả chỉ được chọn một hóa đơn.");
            if (target.ExistingInputInvoiceHeadId.HasValue)
            {
                if (target.TargetIndex != 1 || existing?.Id != target.ExistingInputInvoiceHeadId)
                    throw new BusinessRuleException("Hóa đơn hiện có chỉ được giữ trên phiếu kết quả thứ nhất.");
                EnsureInvoiceSupplier(suppliers[target.SupplierId], existing);
                result[target.TargetIndex] = existing;
                continue;
            }
            if (string.IsNullOrWhiteSpace(target.InvoiceDocumentKey))
            {
                result[target.TargetIndex] = null;
                continue;
            }

            var taxCode = TaxCodeIdentityNormalizer.Normalize(suppliers[target.SupplierId].TaxCode)
                ?? throw new BusinessRuleException("Nhà cung cấp chưa có mã số thuế.");
            var bytes = await _library.GetXmlBytesAsync(taxCode, target.InvoiceDocumentKey, ct);
            var resolution = await _xml.ResolveInvoiceWithinTransactionAsync(
                storeId, taxCode, target.InvoiceDocumentKey + ".xml", bytes, ct);
            result[target.TargetIndex] = resolution.Invoice;
        }
        return result;
    }

    private static void EnsureInvoiceSupplier(Supplier supplier, InputInvoiceHead invoice)
    {
        if (!string.Equals(
                TaxCodeIdentityNormalizer.Normalize(supplier.TaxCode),
                invoice.NormalizedSellerTaxCode,
                StringComparison.Ordinal))
            throw new BusinessRuleException("MST hóa đơn không khớp nhà cung cấp của phiếu kết quả.");
    }

    private async Task<List<string>> AllocateChildNumbersAsync(
        int storeId,
        DateTime documentDate,
        int count,
        CancellationToken ct)
    {
        var result = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            var next = await _numbers.GetNextNumberAsync(
                storeId, DocumentNumberSequenceType.StockReceipt, documentDate, ct);
            result.Add($"NK-{documentDate:yyyyMMdd}-{next:0000}");
        }
        return result;
    }

    private async Task ApplyTargetToSource(
        StockDocument source,
        PurchaseReceiptSplitPlannedTarget target,
        PurchaseReceiptSplitTargetRequest request,
        Supplier supplier,
        Warehouse warehouse,
        IReadOnlyDictionary<int, StockDocumentLine> sourceLines,
        CancellationToken ct)
    {
        source.WarehouseId = warehouse.Id;
        source.SupplierId = supplier.Id;
        source.Supplier = supplier;
        source.IsMerchandisePaid = target.IsMerchandisePaid;
        source.MerchandisePayeeName = target.MerchandisePayeeName;
        source.FreightTotal = target.FreightTotal;
        source.HasFreight = target.FreightTotal > 0m;

        var targetLines = target.Lines.ToDictionary(x => x.SourceLineId);
        foreach (var line in sourceLines.Values)
        {
            if (!targetLines.TryGetValue(line.Id, out var planned))
            {
                await _stockDocuments.RemoveLineAsync(line, ct);
                source.Lines.Remove(line);
                continue;
            }
            ApplyLine(line, planned);
        }
        Recalculate(source);
        _ = request;
    }

    private StockDocument CreateChild(
        StockDocument source,
        string documentNo,
        PurchaseReceiptSplitPlannedTarget target,
        PurchaseReceiptSplitTargetRequest request,
        Supplier supplier,
        DateTime splitNowUtc,
        IReadOnlyDictionary<int, StockDocumentLine> sourceLines)
    {
        var child = new StockDocument
        {
            StoreId = source.StoreId,
            DocumentNo = documentNo,
            DocumentTitle = source.DocumentTitle,
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval,
            DocumentDate = source.DocumentDate,
            WarehouseId = request.WarehouseId,
            SupplierId = supplier.Id,
            Supplier = supplier,
            ReceiptSource = PurchaseReceiptSource.Direct,
            PurchaseOrderId = null,
            DirectReceiptReason = source.DirectReceiptReason,
            HasVat = source.HasVat,
            IncludeVatInInventoryCost = source.IncludeVatInInventoryCost,
            HasFreight = target.FreightTotal > 0m,
            CapitalizeFreightInInventoryCost = source.CapitalizeFreightInInventoryCost,
            FreightTotal = target.FreightTotal,
            FreightPayeeName = source.FreightPayeeName,
            FreightNote = source.FreightNote,
            IsFreightPaid = source.IsFreightPaid,
            IsMerchandisePaid = target.IsMerchandisePaid,
            MerchandisePayeeName = target.MerchandisePayeeName,
            Note = source.Note,
            SubmittedAtUtc = splitNowUtc,
            SubmittedByUserId = _currentUser.UserId
        };
        var lineNo = 1;
        foreach (var planned in target.Lines.OrderBy(x => sourceLines[x.SourceLineId].LineNo))
        {
            var clone = CloneLine(sourceLines[planned.SourceLineId], lineNo++);
            ApplyLine(clone, planned);
            child.Lines.Add(clone);
        }
        Recalculate(child);
        return child;
    }

    private static StockDocumentLine CloneLine(StockDocumentLine source, int lineNo) => new()
    {
        LineNo = lineNo,
        ProductVariantId = source.ProductVariantId,
        PurchaseOrderLineId = null,
        ProductUnitConversionId = source.ProductUnitConversionId,
        TaxId = source.TaxId,
        TaxNameSnapshot = source.TaxNameSnapshot,
        UnitId = source.UnitId,
        UnitNameSnapshot = source.UnitNameSnapshot,
        Factor = source.Factor,
        UnitCost = source.UnitCost,
        TaxRate = source.TaxRate,
        ProductNameSnapshot = source.ProductNameSnapshot,
        SkuSnapshot = source.SkuSnapshot,
        BarcodeSnapshot = source.BarcodeSnapshot,
        ShortageDisposition = source.ShortageDisposition,
        ShortageReason = source.ShortageReason,
        Note = source.Note
    };

    private static void ApplyLine(
        StockDocumentLine line,
        PurchaseReceiptSplitPlannedLine planned)
    {
        var amounts = PurchasePricingPolicy.CalculateLine(
            planned.Quantity, line.UnitCost, line.TaxRate > 0m, line.TaxRate);
        line.Quantity = planned.Quantity;
        line.BaseQuantity = planned.BaseQuantity;
        line.UnitPriceBeforeVat = amounts.UnitPriceBeforeVat;
        line.UnitPriceAfterVat = amounts.UnitPriceAfterVat;
        line.TaxRate = amounts.TaxRate;
        line.VatAmount = amounts.VatAmount;
        line.LineTotal = amounts.LineTotalAfterVat;
        line.FreightAllocation = planned.FreightAllocation;
    }

    private static void Recalculate(StockDocument document)
    {
        var lines = document.Lines.Where(x => !x.IsDeleted).ToArray();
        document.TotalAmount = PurchasePricingPolicy.RoundMoney(lines.Sum(x => x.LineTotal));
        document.VatAmount = document.HasVat
            ? PurchasePricingPolicy.RoundMoney(lines.Sum(x => x.VatAmount))
            : 0m;
        document.SubtotalBeforeVat = PurchasePricingPolicy.RoundMoney(
            document.TotalAmount - document.VatAmount);
    }

    private async Task ApplyInvoiceRelationsAsync(
        int storeId,
        StockDocument source,
        IReadOnlyList<StockDocument> results,
        IReadOnlyDictionary<int, InputInvoiceHead?> desiredInvoices,
        CancellationToken ct)
    {
        var oldInvoiceId = source.InputInvoiceMaps.Where(x => !x.IsDeleted)
            .SingleOrDefault()?.InputInvoiceHeadId;
        var desiredSourceId = desiredInvoices[1]?.Id;
        if (oldInvoiceId.HasValue && oldInvoiceId != desiredSourceId)
        {
            await _inputInvoices.DeleteReconciliationsAsync(
                storeId, source.Id, oldInvoiceId.Value, ct);
            await _inputInvoices.DeleteStockDocumentInputInvoiceMapAsync(
                storeId, source.Id, oldInvoiceId.Value, ct);
            await _stockDocuments.SaveChangesAsync(ct);
        }

        for (var index = 0; index < results.Count; index++)
        {
            var invoice = desiredInvoices[index + 1];
            if (invoice is null) continue;
            var document = results[index];
            if (_linkService is not null)
            {
                await _linkService.LinkWithinTransactionAsync(
                    storeId, document, invoice, "Liên kết khi tách phiếu nhập.", ct);
            }
            else
            {
                // Direct unit-test construction compatibility; runtime DI uses
                // the central owner-aware link service.
                await _supplierResolution.BindCanonicalSupplierWithinTransactionAsync(
                    storeId, document.Id, invoice.Id, ct);
                await _inputInvoices.EnsureSingleReceiptInvoiceMapAsync(new()
                {
                    StoreId = storeId,
                    StockDocumentId = document.Id,
                    InputInvoiceHeadId = invoice.Id,
                    Note = "Liên kết khi tách phiếu nhập."
                }, ct);
            }
        }
    }

    private async Task AddSplitAuditAsync(
        int storeId,
        StockDocument source,
        IReadOnlyList<StockDocument> results,
        PurchaseReceiptSplitPlan plan,
        DateTime splitNowUtc,
        CancellationToken ct)
    {
        if (plan.Targets.Count != results.Count)
            throw new InvalidOperationException(
                "Split audit lineage does not match the persisted result count.");

        var lineage = plan.Targets
            .OrderBy(x => x.TargetIndex)
            .Zip(results, (target, result) => new SplitAuditResult(
                target.TargetIndex,
                result.Id,
                result.DocumentNo,
                target.Lines
                    .Where(x => x.BaseQuantity > 0m)
                    .OrderBy(x => x.SourceLineId)
                    .Select(x => new SplitAuditLineAllocation(
                        x.SourceLineId,
                        x.BaseQuantity))
                    .ToArray()))
            .ToArray();
        await _inputInvoices.AddPurchaseReceiptAuditEventAsync(new PurchaseReceiptAuditEvent
        {
            StoreId = storeId,
            StockDocumentId = source.Id,
            EventType = PurchaseReceiptAuditEventType.ReceiptSplitSource,
            OccurredAtUtc = splitNowUtc,
            Note = "Tách phiếu nhập trực tiếp đang chờ duyệt.",
            ChangedFieldsJson = PurchaseReceiptAuditEvidence.SerializeChangedFields(
                ["SplitResults"]),
            OldValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    ["SourceReceiptId"] = source.Id,
                    ["SourceDocumentNo"] = source.DocumentNo
                }),
            NewValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    ["ResultCount"] = lineage.Length,
                    ["SplitResults"] = lineage
                })
        }, ct);
        foreach (var child in lineage.Skip(1))
        {
            await _inputInvoices.AddPurchaseReceiptAuditEventAsync(new PurchaseReceiptAuditEvent
            {
                StoreId = storeId,
                StockDocumentId = child.ResultReceiptId,
                EventType = PurchaseReceiptAuditEventType.ReceiptSplitChild,
                OccurredAtUtc = splitNowUtc,
                Note = $"Tạo từ phiếu nguồn {source.DocumentNo}.",
                ChangedFieldsJson = PurchaseReceiptAuditEvidence.SerializeChangedFields(
                    ["SourceReceiptId", "TargetIndex", "LineAllocations"]),
                OldValuesJson = "{}",
                NewValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(
                    new Dictionary<string, object?>
                    {
                        ["SourceReceiptId"] = source.Id,
                        ["SourceDocumentNo"] = source.DocumentNo,
                        ["ResultReceiptId"] = child.ResultReceiptId,
                        ["ResultDocumentNo"] = child.ResultDocumentNo,
                        ["TargetIndex"] = child.TargetIndex,
                        ["LineAllocations"] = child.LineAllocations
                    })
            }, ct);
        }
    }

    private static bool Matches(byte[]? rowVersion, string encoded)
    {
        try
        {
            return (rowVersion ?? []).SequenceEqual(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Encode(byte[]? rowVersion) =>
        Convert.ToBase64String(rowVersion ?? []);

    private static string? FormatInvoice(InputInvoiceHead? invoice) => invoice is null
        ? null
        : $"{invoice.InvoiceSeries} - {invoice.InvoiceNumber}";

    private sealed record SplitAuditLineAllocation(
        int SourceLineId,
        decimal BaseQuantity);

    private sealed record SplitAuditResult(
        int TargetIndex,
        int ResultReceiptId,
        string ResultDocumentNo,
        IReadOnlyList<SplitAuditLineAllocation> LineAllocations);
}
