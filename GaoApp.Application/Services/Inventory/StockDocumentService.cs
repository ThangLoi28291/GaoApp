using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Application.Services.Purchases;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Inventory;

public class StockDocumentService : IStockDocumentService
{
    // Deliberately below decimal(18,2)'s database ceiling so aggregate and
    // conversion arithmetic fail as a business validation, not as SQL overflow.
    private const decimal MaximumStoredMoney = 99_999_999_999_999.99m;
    private const decimal MaximumStoredBaseUnitCost = 999_999_999_999m;
    private static readonly string[] AllowedDirectReceiptSources =
    {
        "NCC chào hàng",
        "Mua gấp",
        "Hàng đổi/trả",
        "Khác"
    };

    private readonly IStockDocumentRepository _stockDocumentRepository;
    private readonly ILegalEntityRepository _legalEntityRepository;
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly IBarcodeLookupService _barcodeLookupService;
    private readonly IInventoryUnitResolver _inventoryUnitResolver;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly IInventoryMovementFactory _inventoryMovementFactory;
    private readonly IInventoryRevaluationService _inventoryRevaluationService;
    private readonly IDocumentNumberSequenceRepository _documentNumberSequenceRepository;
    private readonly IInventoryValuationEntryRepository _valuationRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public StockDocumentService(
        IStockDocumentRepository stockDocumentRepository,
        ILegalEntityRepository legalEntityRepository,
        IWarehouseRepository warehouseRepository,
        IBarcodeLookupService barcodeLookupService,
        IInventoryUnitResolver inventoryUnitResolver,
        IInventoryMovementService inventoryMovementService,
        IInventoryMovementFactory inventoryMovementFactory,
        IInventoryRevaluationService inventoryRevaluationService,
        IDocumentNumberSequenceRepository documentNumberSequenceRepository,
        ITenantContext tenantContext,
        IInventoryValuationEntryRepository valuationRepository,
        ICurrentUser currentUser)
    {
        _stockDocumentRepository = stockDocumentRepository;
        _legalEntityRepository = legalEntityRepository;
        _warehouseRepository = warehouseRepository;
        _barcodeLookupService = barcodeLookupService;
        _inventoryUnitResolver = inventoryUnitResolver;
        _inventoryMovementService = inventoryMovementService;
        _inventoryMovementFactory = inventoryMovementFactory;
        _inventoryRevaluationService = inventoryRevaluationService;
        _documentNumberSequenceRepository = documentNumberSequenceRepository;
        _tenantContext = tenantContext;
        _valuationRepository = valuationRepository;
        _currentUser = currentUser;
    }

    /// NOTE:
    /// Phase hiện tại chưa triển khai phân quyền.
    /// Tạm thời cho phép chỉnh sửa chứng từ ở trạng thái:
    /// - Draft
    /// - PendingApproval
    /// - Rejected
    /// Khi hoàn thiện phân quyền:
    /// - PendingApproval chỉ cho Manager/Admin sửa
    /// - Confirmed khóa hoàn toàn

    public async Task<List<StockDocumentListItemDto>> GetReceiptListAsync(CancellationToken ct = default)
    {
        var documents = await _stockDocumentRepository.GetReceiptListAsync(ct);

        return documents.Select(x => new StockDocumentListItemDto
        {
            Id = x.Id,
            DocumentNo = x.DocumentNo,
            DocumentTitle = x.DocumentTitle,
            DocumentDate = x.DocumentDate,
            LegalEntityId = x.Warehouse?.LegalEntityId ?? 0,
            LegalEntityName = x.Warehouse?.LegalEntity?.Name ?? string.Empty,
            WarehouseName = x.Warehouse?.Name ?? string.Empty,
            SupplierName = x.Supplier?.Name,
            Status = x.Status,
            TotalAmount = x.TotalAmount,
            SubmittedAtUtc = x.SubmittedAtUtc,
            ApprovedAtUtc = x.ApprovedAtUtc,

            CreatedAtUtc = x.CreatedAtUtc,
            UpdatedAtUtc = x.UpdatedAtUtc,
            // REVISION REQUEST
            HasRevisionRequest = x.HasRevisionRequest,
            RevisionRequestNote = x.RevisionRequestNote,
            RevisionRequestedAtUtc = x.RevisionRequestedAtUtc,
            TotalLines = x.Lines?.Count(l => !l.IsDeleted) ?? 0,
            TotalProductTypes = x.Lines?
         .Where(l => !l.IsDeleted)
         .Select(l => l.ProductVariantId)
         .Distinct()
         .Count() ?? 0
        }).ToList();
    }

    public async Task<StockReceiptFormOptionsDto> GetReceiptFormOptionsAsync(
        CancellationToken ct = default)
    {
        var legalEntities = await _legalEntityRepository.GetAllAsync(ct);
        var taxes = await _stockDocumentRepository.GetTaxesAsync(ct);
        var result = StockReceiptLegalEntityPolicy.BuildFormOptions(legalEntities);
        result.Taxes = taxes.Select(x => new StockReceiptTaxOptionDto
        {
            Id = x.Id,
            Name = x.Name,
            Rate = x.Rate
        }).ToList();
        return result;
    }

    public async Task<int> CreateReceiptAsync(CreateStockDocumentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DirectReceiptReason))
            throw new BusinessRuleException("Phiếu nhập ngoài đơn bắt buộc phải chọn nguồn nhập.");
        var directReceiptSource = AllowedDirectReceiptSources.FirstOrDefault(x =>
            string.Equals(x, request.DirectReceiptReason.Trim(), StringComparison.OrdinalIgnoreCase));
        if (directReceiptSource == null)
            throw new BusinessRuleException(
                "Nguồn nhập ngoài đơn không hợp lệ. Chỉ chấp nhận: NCC chào hàng, Mua gấp, Hàng đổi/trả hoặc Khác.");

        var warehouse = await _warehouseRepository.GetByIdAsync(request.WarehouseId, ct);
        StockReceiptLegalEntityPolicy.EnsureWarehouseSelectable(
            request.LegalEntityId,
            warehouse);

        if (request.SupplierId.HasValue)
        {
            var supplierExists = await _stockDocumentRepository.SupplierExistsAsync(request.SupplierId.Value, ct);
            if (!supplierExists)
                throw new BusinessRuleException("Nhà cung cấp không tồn tại.");
        }

        var documentDate = request.DocumentDate ?? DateTime.UtcNow;

        var storeId = RequireStoreId();

        var nextNumber = await _documentNumberSequenceRepository.GetNextNumberAsync(
            storeId: storeId,
            sequenceType: DocumentNumberSequenceType.StockReceipt,
            sequenceDate: documentDate,
            ct: ct);

        var documentNo = BuildReceiptDocumentNo(documentDate, nextNumber);

        var document = new StockDocument
        {
            DocumentNo = documentNo,
            DocumentTitle = request.DocumentTitle?.Trim(),
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Draft,
            WarehouseId = request.WarehouseId,
            SupplierId = request.SupplierId,
            DocumentDate = documentDate,
            Note = request.Note,
            TotalAmount = 0,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = directReceiptSource,
            HasVat = request.HasVat
        };

        await _stockDocumentRepository.AddAsync(document, ct);
        await _stockDocumentRepository.SaveChangesAsync(ct);

        return document.Id;
    }

    public async Task<int> CreateReceiptFromPurchaseOrderAsync(
        int purchaseOrderId,
        CreatePurchaseReceiptRequest request,
        CancellationToken ct = default)
    {
        var order = await _stockDocumentRepository.GetPurchaseOrderForReceiptAsync(purchaseOrderId, ct)
            ?? throw new BusinessRuleException("Đơn đặt hàng không tồn tại.");

        if (order.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.SentToSupplier or PurchaseOrderStatus.PartiallyReceived))
            throw new BusinessRuleException("Chỉ đơn đã duyệt/gửi nhà cung cấp hoặc đang nhận một phần mới được tạo phiếu nhập.");
        if (request.Lines.Count == 0)
            throw new BusinessRuleException("Vui lòng chọn ít nhất một dòng cần nhận.");
        if (request.Lines.GroupBy(x => x.PurchaseOrderLineId).Any(x => x.Count() > 1))
            throw new BusinessRuleException("Một dòng đơn đặt hàng chỉ được xuất hiện một lần trên phiếu nhập.");

        var date = request.DocumentDate ?? DateTime.UtcNow;
        var storeId = RequireStoreId();

        var nextNumber = await _documentNumberSequenceRepository.GetNextNumberAsync(
            storeId,
            DocumentNumberSequenceType.StockReceipt,
            date,
            ct);

        var document = new StockDocument
        {
            DocumentNo = BuildReceiptDocumentNo(date, nextNumber),
            DocumentTitle = $"Nhập theo {order.OrderNumber}",
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Draft,
            ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
            PurchaseOrderId = order.Id,
            WarehouseId = order.ExpectedWarehouseId,
            SupplierId = order.SupplierId,
            DocumentDate = date,
            Note = request.Note?.Trim(),
            // Giá/VAT là sự thật thương mại tại từng lần nhận, không kế thừa PO.
            HasVat = false
        };

        var lineNo = 1;
        foreach (var input in request.Lines)
        {
            var orderLine = order.Lines.FirstOrDefault(x => x.Id == input.PurchaseOrderLineId && !x.IsDeleted)
                ?? throw new BusinessRuleException($"Dòng đơn đặt hàng #{input.PurchaseOrderLineId} không hợp lệ.");
            var decision = PurchaseReceiptPolicy.ValidateLine(
                orderLine.PendingQuantity,
                input.Quantity,
                input.ShortageDisposition,
                input.ShortageReason,
                orderLine.LineNo);
            var quantity = decision.ReceivedQuantity;
            if (!orderLine.ProductVariantId.HasValue ||
                !orderLine.ProductUnitConversionId.HasValue ||
                !orderLine.UnitId.HasValue ||
                orderLine.ProductVariant == null ||
                orderLine.ProductUnitConversion == null)
                throw new BusinessRuleException(
                    $"Dòng {orderLine.LineNo} là hàng mô tả chưa liên kết sản phẩm. " +
                    "Vui lòng liên kết hoặc tạo sản phẩm trước khi lập phiếu nhập kho.");
            var variant = orderLine.ProductVariant;
            var conversion = orderLine.ProductUnitConversion;
            if (!variant.IsActive || !variant.Product.IsActive ||
                !conversion.IsActive || !conversion.Unit.IsActive ||
                conversion.Factor <= 0m ||
                conversion.ProductVariantId != variant.Id ||
                conversion.UnitId != orderLine.UnitId.Value ||
                variant.StoreId != order.StoreId || variant.Product.StoreId != order.StoreId ||
                conversion.StoreId != order.StoreId || conversion.Unit.StoreId != order.StoreId)
                throw new BusinessRuleException(
                    $"Dòng {orderLine.LineNo} đang liên kết sản phẩm/đơn vị không hợp lệ hoặc đã ngừng hoạt động. " +
                    "Vui lòng liên kết lại trước khi lập phiếu nhập.");

            var actualProductName = string.IsNullOrWhiteSpace(variant.ProductVariantName)
                ? variant.Product.Name
                : variant.ProductVariantName;
            var actualFactor = conversion.Factor;

            document.Lines.Add(new StockDocumentLine
            {
                LineNo = lineNo++,
                ProductVariantId = orderLine.ProductVariantId.Value,
                PurchaseOrderLineId = orderLine.Id,
                UnitId = conversion.UnitId,
                ProductUnitConversionId = conversion.Id,
                TaxId = null,
                TaxNameSnapshot = null,
                UnitNameSnapshot = conversion.Unit.Name,
                Factor = actualFactor,
                Quantity = quantity,
                BaseQuantity = PurchasePricingPolicy.RoundQuantity(quantity * actualFactor),
                // PO only controls the requested item/unit/quantity. The actual
                // commercial price is confirmed on each physical receipt.
                UnitCost = 0m,
                UnitPriceBeforeVat = 0m,
                TaxRate = 0m,
                VatAmount = 0m,
                UnitPriceAfterVat = 0m,
                LineTotal = 0m,
                ProductNameSnapshot = actualProductName,
                SkuSnapshot = variant.Sku,
                ShortageDisposition = decision.ShortageDisposition,
                ShortageReason = decision.ShortageReason
            });
        }

        RecalculateDocumentTotals(document);
        order.Actions.Add(new PurchaseOrderAction
        {
            ActionType = PurchaseOrderActionType.ReceiptCreated,
            FromStatus = order.Status,
            ToStatus = order.Status,
            ActorUserId = _currentUser.UserId,
            OccurredAtUtc = DateTime.UtcNow,
            Note = $"Tạo phiếu nhập {document.DocumentNo}."
        });
        await _stockDocumentRepository.AddAsync(document, ct);
        await _stockDocumentRepository.SaveChangesAsync(ct);
        order.Actions.Last().StockDocumentId = document.Id;
        await _stockDocumentRepository.SaveChangesAsync(ct);
        return document.Id;
    }

    public async Task<StockDocumentDto?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetDetailAsync(id, ct);
        if (document == null) return null;

        var lastPurchaseBasePrices = await _stockDocumentRepository
            .GetLastPurchaseBaseUnitPricesBeforeVatAsync(
                document.Lines.Where(x => !x.IsDeleted).Select(x => x.ProductVariantId),
                ct);

        // Lấy map XML theo từng dòng nhập kho
        var lineXmlMap = document.LineInputInvoiceMaps?
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.StockDocumentLineId)
            .ToDictionary(x => x.Key, x => x.First())
            ?? new Dictionary<int, StockDocumentLineInputInvoiceMap>();

        return new StockDocumentDto
        {
            Id = document.Id,
            DocumentNo = document.DocumentNo,
            DocumentTitle = document.DocumentTitle,
            Type = document.Type,
            Status = document.Status,
            RowVersion = Convert.ToBase64String(document.RowVersion ?? Array.Empty<byte>()),
            DocumentDate = document.DocumentDate,

            LegalEntityId = document.Warehouse?.LegalEntityId ?? 0,
            LegalEntityName = document.Warehouse?.LegalEntity?.Name ?? string.Empty,

            WarehouseId = document.WarehouseId,
            WarehouseName = document.Warehouse?.Name ?? string.Empty,

            SupplierId = document.SupplierId,
            SupplierName = document.Supplier?.Name,

            Note = document.Note,
            TotalAmount = document.TotalAmount,
            ReceiptSource = document.ReceiptSource,
            PurchaseOrderId = document.PurchaseOrderId,
            PurchaseOrderNumber = document.PurchaseOrder?.OrderNumber,
            DirectReceiptReason = document.DirectReceiptReason,
            HasVat = document.HasVat,
            SubtotalBeforeVat = document.SubtotalBeforeVat,
            VatAmount = document.VatAmount,
            HasFreight = document.HasFreight,
            FreightTotal = document.FreightTotal,
            FreightPayeeName = document.FreightPayeeName,
            FreightNote = document.FreightNote,
            IsFreightPaid = document.IsFreightPaid,
            IsMerchandisePaid = document.IsMerchandisePaid,
            MerchandisePayeeName = document.MerchandisePayeeName,

            SubmittedAtUtc = document.SubmittedAtUtc,
            SubmittedByUserId = document.SubmittedByUserId,
            ApprovedAtUtc = document.ApprovedAtUtc,
            ApprovedByUserId = document.ApprovedByUserId,
            ApprovalNote = document.ApprovalNote,
            ConfirmedAtUtc = document.ConfirmedAtUtc,
            ConfirmedByUserId = document.ConfirmedByUserId,
            HasRevisionRequest = document.HasRevisionRequest,
            RevisionRequestNote = document.RevisionRequestNote,
            RevisionRequestedAtUtc = document.RevisionRequestedAtUtc,
            RevisionRequestedByUserId = document.RevisionRequestedByUserId,
            RevisionResolvedAtUtc = document.RevisionResolvedAtUtc,
            RevisionResolvedByUserId = document.RevisionResolvedByUserId,

            Lines = document.Lines
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.LineNo)
                .Select(x =>
                {
                    lineXmlMap.TryGetValue(x.Id, out var xmlMap);

                    return new StockDocumentLineDto
                    {
                        Id = x.Id,
                        LineNo = x.LineNo,
                        ProductVariantId = x.ProductVariantId,

                        UnitId = x.UnitId,
                        UnitName = x.UnitNameSnapshot,

                        Factor = x.Factor,
                        Quantity = x.Quantity,
                        BaseQuantity = x.BaseQuantity,

                        UnitCost = x.UnitCost,
                        LineTotal = x.LineTotal,
                        PurchaseOrderLineId = x.PurchaseOrderLineId,
                        ProductUnitConversionId = x.ProductUnitConversionId,
                        TaxId = x.TaxId,
                        SuggestedTaxId = x.ProductVariant?.Product?.TaxId,
                        TaxNameSnapshot = x.TaxNameSnapshot,
                        UnitPriceBeforeVat = x.UnitPriceBeforeVat,
                        LastPurchaseUnitPriceBeforeVat = lastPurchaseBasePrices.TryGetValue(
                            x.ProductVariantId,
                            out var lastBaseUnitPrice)
                                ? PurchasePricingPolicy.RoundMoney(lastBaseUnitPrice * x.Factor)
                                : null,
                        TaxRate = x.TaxRate,
                        VatAmount = x.VatAmount,
                        UnitPriceAfterVat = x.UnitPriceAfterVat,
                        FreightAllocation = x.FreightAllocation,
                        ShortageDisposition = x.ShortageDisposition,
                        ShortageReason = x.ShortageReason,

                        ProductNameSnapshot = x.ProductNameSnapshot,
                        ProductImageUrl = BuildProductImageUrl(
    x.ProductVariant?.PrimaryProductImage?.MediaAsset?.StoragePath),
                        SkuSnapshot = x.SkuSnapshot,
                        BarcodeSnapshot = x.BarcodeSnapshot,
                        Note = x.Note,

                        // ===============================
                        // PHASE 2.5 - XML INPUT INVOICE
                        // ===============================
                        UseInputInvoice = xmlMap?.UseInputInvoice ?? false,
                        InputInvoiceDetailId = xmlMap?.InputInvoiceDetailId,

                        XmlItemName = xmlMap?.InputInvoiceDetail?.ItemName,
                        XmlQuantity = xmlMap?.InputInvoiceDetail?.Quantity,
                        XmlLineAmount = xmlMap?.InputInvoiceDetail?.LineAmount,

                        InputInvoiceMatchStatus = xmlMap?.MatchStatus,
                        QuantityDifference = xmlMap?.QuantityDifference ?? 0,
                        AmountDifference = xmlMap?.AmountDifference ?? 0
                    };
                })
                .ToList()
        };
    }
    private static string? BuildProductImageUrl(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        var path = storagePath.Trim().Replace("\\", "/");

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (path.StartsWith("/"))
            return path;

        return "/" + path;
    }

    public async Task<int> AddLineAsync(
     int documentId,
     AddStockDocumentLineRequest request,
     CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetDetailAsync(documentId, ct);
        if (document == null)
            throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");

        EnsureEditable(document.Status);
        EnsureDirectLineEditing(document);

        if (request.Quantity <= 0)
            throw new BusinessRuleException("Số lượng phải lớn hơn 0.");

        var variant = await _stockDocumentRepository.GetVariantForStockDocumentAsync(
            request.ProductVariantId,
            ct);

        if (variant == null)
            throw new BusinessRuleException("Sản phẩm không tồn tại.");

        var conversion = await _inventoryUnitResolver.ResolveAsync(
     request.ProductVariantId,
     request.UnitId,
     ct);

        var factor = NormalizeFactor(conversion.Factor);

        var conversionEntity = await _stockDocumentRepository.GetConversionAsync(
            request.ProductVariantId,
            conversion.UnitId,
            ct);

        Tax? tax = null;
        if (document.HasVat)
        {
            var taxId = request.TaxId ?? variant.Product?.TaxId;
            if (taxId.HasValue)
                tax = await _stockDocumentRepository.GetTaxAsync(taxId.Value, ct)
                    ?? throw new BusinessRuleException("Thuế suất không tồn tại trong cửa hàng hiện tại.");
        }

        // Nhân viên ghi nhận hàng thực nhận, chưa chốt giá thương mại. Giá 0/1
        // từ UI cũ được chuẩn hóa về 0 và quản lý bắt buộc nhập giá thực tế khi duyệt.
        var unitCost = request.UnitCost > 1m ? request.UnitCost : 0m;

        var baseQuantityToAdd = request.Quantity * factor;

        // UnitCost là giá theo đơn vị nhập, không nhân với BaseQuantity.
        var lineAmounts = PurchasePricingPolicy.CalculateLine(request.Quantity, unitCost, document.HasVat, tax?.Rate ?? 0m);
        var lineTotalToAdd = lineAmounts.LineTotalAfterVat;
        var barcodeSnapshot = ResolveBarcodeSnapshot(variant, conversion.UnitId);

        // STOCKDOC.UI:
        // Nếu đã có dòng cùng sản phẩm + đơn vị + giá vốn thì cộng dồn,
        // không tạo dòng mới để tránh trùng như hình.
        var existingLine = document.Lines.FirstOrDefault(x =>
            !x.IsDeleted &&
            x.ProductVariantId == request.ProductVariantId &&
            x.UnitId == conversion.UnitId &&
            x.UnitCost == unitCost &&
            x.TaxId == tax?.Id);

        if (existingLine != null)
        {
            existingLine.Quantity += request.Quantity;
            existingLine.BaseQuantity += baseQuantityToAdd;
            existingLine.LineTotal += lineTotalToAdd;
            var mergedAmounts = PurchasePricingPolicy.CalculateLine(existingLine.Quantity, unitCost, document.HasVat, tax?.Rate ?? 0m);
            existingLine.ProductUnitConversionId = conversionEntity?.Id;
            existingLine.TaxId = tax?.Id;
            existingLine.TaxNameSnapshot = tax?.Name;
            existingLine.UnitPriceBeforeVat = mergedAmounts.UnitPriceBeforeVat;
            existingLine.TaxRate = mergedAmounts.TaxRate;
            existingLine.VatAmount = mergedAmounts.VatAmount;
            existingLine.UnitPriceAfterVat = mergedAmounts.UnitPriceAfterVat;
            existingLine.LineTotal = mergedAmounts.LineTotalAfterVat;
            existingLine.Factor = factor;
            existingLine.UnitNameSnapshot = conversion.UnitName;
            existingLine.BarcodeSnapshot = barcodeSnapshot;
            existingLine.SkuSnapshot = variant.Sku;
            existingLine.ProductNameSnapshot =
                !string.IsNullOrWhiteSpace(variant.ProductVariantName)
                    ? variant.ProductVariantName
                    : (variant.Product?.Name ?? $"Variant #{variant.Id}");

            if (!string.IsNullOrWhiteSpace(request.Note))
                existingLine.Note = request.Note;

            RecalculateDocumentTotals(document);

            await _stockDocumentRepository.SaveChangesAsync(ct);
            return existingLine.Id;
        }

        var line = new StockDocumentLine
        {
            StockDocumentId = document.Id,
            LineNo = await _stockDocumentRepository.GetNextLineNoAsync(document.Id, ct),
            ProductVariantId = request.ProductVariantId,
            UnitId = conversion.UnitId,
            ProductUnitConversionId = conversionEntity?.Id,
            TaxId = tax?.Id,
            TaxNameSnapshot = tax?.Name,
            UnitNameSnapshot = conversion.UnitName,
            Factor = factor,
            Quantity = request.Quantity,
            BaseQuantity = baseQuantityToAdd,
            UnitCost = unitCost,
            UnitPriceBeforeVat = lineAmounts.UnitPriceBeforeVat,
            TaxRate = lineAmounts.TaxRate,
            VatAmount = lineAmounts.VatAmount,
            UnitPriceAfterVat = lineAmounts.UnitPriceAfterVat,
            LineTotal = lineAmounts.LineTotalAfterVat,

            ProductNameSnapshot =
                !string.IsNullOrWhiteSpace(variant.ProductVariantName)
                    ? variant.ProductVariantName
                    : (variant.Product?.Name ?? $"Variant #{variant.Id}"),

            SkuSnapshot = variant.Sku,
            BarcodeSnapshot = barcodeSnapshot,
            Note = request.Note
        };

        document.Lines.Add(line);

        RecalculateDocumentTotals(document);

        await _stockDocumentRepository.SaveChangesAsync(ct);

        return line.Id;
    }

    public async Task<int> AddLineByBarcodeAsync(int documentId, AddStockDocumentLineByBarcodeRequest request, CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetDetailAsync(documentId, ct);
        if (document == null)
            throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");

        EnsureEditable(document.Status);
        EnsureDirectLineEditing(document);

        var barcode = (request.Barcode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(barcode))
            throw new BusinessRuleException("Barcode không được để trống.");

        if (request.Quantity <= 0)
            throw new BusinessRuleException("Số lượng phải lớn hơn 0.");

        var lookup = await _barcodeLookupService.FindAsync(barcode, ct);
        if (lookup == null)
            throw new BusinessRuleException($"Không tìm thấy sản phẩm theo barcode: {barcode}");

        var variantEntity = await _stockDocumentRepository.GetVariantForStockDocumentAsync(lookup.ProductVariantId, ct);
        if (variantEntity == null)
            throw new InvalidOperationException(
                "Barcode lookup returned a missing product variant.");

        var factor = lookup.Factor <= 0 ? 1m : lookup.Factor;
        Tax? tax = null;
        if (document.HasVat)
        {
            var taxId = request.TaxId ?? variantEntity.Product?.TaxId;
            if (taxId.HasValue)
                tax = await _stockDocumentRepository.GetTaxAsync(taxId.Value, ct)
                    ?? throw new BusinessRuleException("Thuế suất không tồn tại trong cửa hàng hiện tại.");
        }
        var unitCost = request.UnitCost > 1m ? request.UnitCost : 0m;
        var amounts = PurchasePricingPolicy.CalculateLine(request.Quantity, unitCost, document.HasVat, tax?.Rate ?? 0m);
        var lineTotal = amounts.LineTotalAfterVat;
        var baseQuantity = request.Quantity * factor;

        var conversionEntity = await _stockDocumentRepository.GetConversionAsync(
            lookup.ProductVariantId,
            lookup.UnitId,
            ct);

        var existing = document.Lines.FirstOrDefault(x =>
            !x.IsDeleted &&
            x.ProductVariantId == lookup.ProductVariantId &&
            x.UnitId == lookup.UnitId &&
            x.UnitCost == unitCost &&
            x.TaxId == tax?.Id);

        if (existing != null)
        {
            existing.Quantity += request.Quantity;
            existing.BaseQuantity += baseQuantity;
            existing.LineTotal += lineTotal;
            var mergedAmounts = PurchasePricingPolicy.CalculateLine(existing.Quantity, unitCost, document.HasVat, tax?.Rate ?? 0m);
            existing.ProductUnitConversionId = conversionEntity?.Id;
            existing.TaxId = tax?.Id;
            existing.TaxNameSnapshot = tax?.Name;
            existing.UnitPriceBeforeVat = mergedAmounts.UnitPriceBeforeVat;
            existing.TaxRate = mergedAmounts.TaxRate;
            existing.VatAmount = mergedAmounts.VatAmount;
            existing.UnitPriceAfterVat = mergedAmounts.UnitPriceAfterVat;
            existing.LineTotal = mergedAmounts.LineTotalAfterVat;
            existing.Factor = factor;
            existing.UnitNameSnapshot = lookup.UnitName;
            existing.BarcodeSnapshot = lookup.Barcode;
            existing.Note = request.Note;

            RecalculateDocumentTotals(document);

            await _stockDocumentRepository.SaveChangesAsync(ct);
            return existing.Id;
        }

        var line = new StockDocumentLine
        {
            StockDocumentId = document.Id,
            LineNo = await _stockDocumentRepository.GetNextLineNoAsync(document.Id, ct),
            ProductVariantId = lookup.ProductVariantId,
            UnitId = lookup.UnitId,
            ProductUnitConversionId = conversionEntity?.Id,
            TaxId = tax?.Id,
            TaxNameSnapshot = tax?.Name,
            UnitNameSnapshot = lookup.UnitName,
            Factor = factor,
            Quantity = request.Quantity,
            BaseQuantity = baseQuantity,
            UnitCost = unitCost,
            UnitPriceBeforeVat = amounts.UnitPriceBeforeVat,
            TaxRate = amounts.TaxRate,
            VatAmount = amounts.VatAmount,
            UnitPriceAfterVat = amounts.UnitPriceAfterVat,
            LineTotal = amounts.LineTotalAfterVat,
            ProductNameSnapshot = lookup.ProductName,
            SkuSnapshot = lookup.VariantSku,
            BarcodeSnapshot = lookup.Barcode,
            Note = request.Note
        };

        document.Lines.Add(line);
        RecalculateDocumentTotals(document);

        await _stockDocumentRepository.SaveChangesAsync(ct);
        return line.Id;
    }

    public async Task UpdateLineAsync(int lineId, UpdateStockDocumentLineRequest request, CancellationToken ct = default)
    {
        var line = await _stockDocumentRepository.GetLineByIdAsync(lineId, ct);
        if (line == null)
            throw new BusinessRuleException("Dòng phiếu nhập không tồn tại.");

        EnsureEditable(line.StockDocument.Status);
        EnsureDirectLineEditing(line.StockDocument);

        if (request.Quantity <= 0)
            throw new BusinessRuleException("Số lượng phải lớn hơn 0.");

        var unitCost = request.UnitCost ?? line.UnitCost;
        if (unitCost < 0)
            throw new BusinessRuleException("Đơn giá nhập không được âm.");

        var conversion = await _inventoryUnitResolver.ResolveAsync(
            line.ProductVariantId,
            request.UnitId,
            ct);

        var factor = NormalizeFactor(conversion.Factor);

        var document = await _stockDocumentRepository.GetDetailAsync(line.StockDocumentId, ct)
            ?? throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");
        Tax? tax = null;
        if (document.HasVat)
        {
            var variant = await _stockDocumentRepository.GetVariantForStockDocumentAsync(line.ProductVariantId, ct);
            var taxId = request.TaxId ?? line.TaxId ?? variant?.Product?.TaxId;
            if (taxId.HasValue)
                tax = await _stockDocumentRepository.GetTaxAsync(taxId.Value, ct)
                    ?? throw new BusinessRuleException("Thuế suất không tồn tại trong cửa hàng hiện tại.");
        }
        var conversionEntity = await _stockDocumentRepository.GetConversionAsync(line.ProductVariantId, conversion.UnitId, ct);
        var amounts = PurchasePricingPolicy.CalculateLine(request.Quantity, unitCost, document.HasVat, tax?.Rate ?? 0m);

        line.UnitId = conversion.UnitId;
        line.ProductUnitConversionId = conversionEntity?.Id;
        line.TaxId = tax?.Id;
        line.TaxNameSnapshot = tax?.Name;
        line.UnitNameSnapshot = conversion.UnitName;
        line.Factor = factor;
        line.Quantity = request.Quantity;
        line.BaseQuantity = request.Quantity * factor;
        line.UnitCost = unitCost;

        // Đúng nghiệp vụ: thành tiền = số lượng nhập * giá nhập theo đơn vị nhập.
        line.UnitPriceBeforeVat = amounts.UnitPriceBeforeVat;
        line.TaxRate = amounts.TaxRate;
        line.VatAmount = amounts.VatAmount;
        line.UnitPriceAfterVat = amounts.UnitPriceAfterVat;
        line.LineTotal = amounts.LineTotalAfterVat;

        line.Note = request.Note;

        if (line.ProductVariant != null)
        {
            line.BarcodeSnapshot = ResolveBarcodeSnapshot(line.ProductVariant, conversion.UnitId);
        }

        RecalculateDocumentTotals(document);

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }
    public async Task DeleteLineAsync(int lineId, CancellationToken ct = default)
    {
        var line = await _stockDocumentRepository.GetLineByIdAsync(lineId, ct);
        if (line == null)
            throw new BusinessRuleException("Dòng phiếu nhập không tồn tại.");

        EnsureEditable(line.StockDocument.Status);
        EnsureDirectLineEditing(line.StockDocument);

        var document = await _stockDocumentRepository.GetDetailAsync(line.StockDocumentId, ct);
        if (document == null)
            throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");

        await _stockDocumentRepository.RemoveLineAsync(line, ct);

        RecalculateDocumentTotals(document, line.Id);

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

    public async Task UpdatePurchaseReceiptApprovalAsync(
        int documentId,
        UpdatePurchaseReceiptApprovalRequest request,
        CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetForConfirmAsync(documentId, ct)
            ?? throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");
        if (document.Status is not (StockDocumentStatus.Draft or StockDocumentStatus.PendingApproval or StockDocumentStatus.Rejected))
            throw new BusinessRuleException("Phiếu đã duyệt nên phân bổ vận chuyển đã bị khóa.");
        EnsureRowVersion(document.RowVersion, request.RowVersion);

        var lines = document.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineNo).ToList();
        if (!request.HasFreight)
        {
            document.HasFreight = false;
            document.FreightTotal = 0m;
            document.FreightPayeeName = null;
            document.FreightNote = null;
            document.IsFreightPaid = false;
            foreach (var line in lines) line.FreightAllocation = 0m;
            await _stockDocumentRepository.SaveChangesAsync(ct);
            return;
        }

        var freightTotal = PurchasePricingPolicy.RoundMoney(request.FreightTotal);
        if (freightTotal <= 0) throw new BusinessRuleException("Tổng phí vận chuyển phải lớn hơn 0.");
        if (string.IsNullOrWhiteSpace(request.FreightPayeeName))
            throw new BusinessRuleException("Vui lòng nhập người hoặc đơn vị nhận tiền vận chuyển.");

        IReadOnlyDictionary<int, decimal> allocations;
        if (request.ResetAutomaticAllocation)
        {
            allocations = PurchasePricingPolicy.AllocateFreight(
                freightTotal,
                lines.Select(x => (x.Id, x.LineTotal)).ToList());
        }
        else
        {
            if (request.Allocations.GroupBy(x => x.StockDocumentLineId).Any(x => x.Count() > 1))
                throw new BusinessRuleException("Dòng phân bổ vận chuyển bị trùng.");
            var ids = lines.Select(x => x.Id).ToHashSet();
            if (request.Allocations.Any(x => !ids.Contains(x.StockDocumentLineId) || x.Amount < 0))
                throw new BusinessRuleException("Phân bổ vận chuyển chứa dòng hoặc số tiền không hợp lệ.");
            allocations = lines.ToDictionary(
                x => x.Id,
                x => PurchasePricingPolicy.RoundMoney(
                    request.Allocations.FirstOrDefault(a => a.StockDocumentLineId == x.Id)?.Amount ?? 0m));
        }

        PurchasePricingPolicy.EnsureFreightBalanced(freightTotal, allocations.Values);
        document.HasFreight = true;
        document.FreightTotal = freightTotal;
        document.FreightPayeeName = request.FreightPayeeName.Trim();
        document.FreightNote = request.FreightNote?.Trim();
        document.IsFreightPaid = request.IsFreightPaid;
        foreach (var line in lines) line.FreightAllocation = allocations[line.Id];
        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

    public async Task SubmitForApprovalAsync(
        int documentId,
        string? approvalNote,
        string? rowVersion,
        CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetForConfirmAsync(documentId, ct);
        if (document == null)
            throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");

        if (document.Status != StockDocumentStatus.Draft &&
            document.Status != StockDocumentStatus.Rejected)
        {
            throw new BusinessRuleException("Chỉ phiếu nháp hoặc phiếu bị từ chối mới được gửi duyệt.");
        }
        EnsureRowVersion(document.RowVersion, rowVersion);

        if (!document.Lines.Any())
            throw new BusinessRuleException("Phiếu nhập kho chưa có dòng chi tiết.");

        ValidateReceiptSourceAndShortages(document);
        if (document.HasFreight)
            PurchasePricingPolicy.EnsureFreightBalanced(
                document.FreightTotal,
                document.Lines.Where(x => !x.IsDeleted).Select(x => x.FreightAllocation));

        RecalculateDocumentTotals(document);

        document.Status = StockDocumentStatus.PendingApproval;
        document.SubmittedAtUtc = DateTime.UtcNow;
        document.SubmittedByUserId = _currentUser.UserId;
        document.ApprovalNote = approvalNote;
        document.HasRevisionRequest = false;
        document.RevisionRequestNote = null;
        document.RevisionRequestedAtUtc = null;
        document.RevisionRequestedByUserId = null;
        document.RevisionResolvedAtUtc = null;
        document.RevisionResolvedByUserId = null;

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

    public async Task ApproveCommercialAsync(
        int documentId,
        ApprovePurchaseReceiptCommercialRequest request,
        CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetForConfirmAsync(documentId, ct)
            ?? throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");

        // Safe retry after a successful commit. Do not apply a newer browser
        // payload to a receipt that has already posted inventory/FIFO/payables.
        if (document.Status == StockDocumentStatus.Confirmed)
            return;

        EnsureRowVersion(document.RowVersion, request.RowVersion);
        if (document.Type != StockDocumentType.Receipt)
            throw new BusinessRuleException("Chứng từ này không phải phiếu nhập kho.");
        if (document.Status != StockDocumentStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ phiếu đang chờ duyệt mới được chốt giá và duyệt nhập kho.");
        if (request.ApprovalNote?.Length > 1000)
            throw new BusinessRuleException("Ghi chú duyệt không được vượt quá 1.000 ký tự.");
        if (request.MerchandisePayeeName?.Length > 250)
            throw new BusinessRuleException("Tên người bán/đơn vị nhận tiền không được vượt quá 250 ký tự.");
        if (request.FreightPayeeName?.Length > 250)
            throw new BusinessRuleException("Tên người/đơn vị nhận phí vận chuyển không được vượt quá 250 ký tự.");
        if (request.FreightNote?.Length > 1000)
            throw new BusinessRuleException("Ghi chú vận chuyển không được vượt quá 1.000 ký tự.");

        var activeLines = document.Lines
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.LineNo)
            .ToList();
        if (activeLines.Count == 0)
            throw new BusinessRuleException("Phiếu nhập kho chưa có dòng chi tiết hợp lệ.");

        var postedLines = request.Lines ?? new List<PurchaseReceiptFinancialLineInputDto>();
        if (postedLines.Any(x => x.StockDocumentLineId <= 0) ||
            postedLines.GroupBy(x => x.StockDocumentLineId).Any(x => x.Count() > 1))
            throw new BusinessRuleException("Danh sách dòng chốt giá bị trùng hoặc không hợp lệ.");

        var activeLineIds = activeLines.Select(x => x.Id).ToHashSet();
        var postedLineIds = postedLines.Select(x => x.StockDocumentLineId).ToHashSet();
        if (!activeLineIds.SetEquals(postedLineIds))
            throw new BusinessRuleException(
                "Danh sách dòng đã thay đổi hoặc không đầy đủ. Vui lòng tải lại phiếu trước khi duyệt.");

        Supplier? supplier = null;
        if (request.SupplierId.HasValue)
        {
            supplier = await _stockDocumentRepository.GetSupplierAsync(request.SupplierId.Value, ct)
                ?? throw new BusinessRuleException("Nhà cung cấp không tồn tại hoặc không thuộc cửa hàng hiện tại.");
        }

        var isPurchaseOrderReceipt =
            document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder || document.PurchaseOrderId.HasValue;
        if (isPurchaseOrderReceipt)
        {
            var order = document.PurchaseOrder
                ?? throw new InvalidOperationException(
                    "Purchase-order receipt is missing its purchase-order relation.");
            if (!request.SupplierId.HasValue || request.SupplierId.Value != order.SupplierId)
                throw new BusinessRuleException("Không thể đổi nhà cung cấp của phiếu nhập theo đơn đặt hàng.");
            supplier ??= order.Supplier;
        }
        else if (!request.IsMerchandisePaid && !request.SupplierId.HasValue)
        {
            throw new BusinessRuleException(
                "Phiếu nhập còn nợ tiền hàng bắt buộc phải chọn nhà cung cấp.");
        }

        var merchandisePayeeName = supplier?.Name
            ?? (string.IsNullOrWhiteSpace(request.MerchandisePayeeName)
                ? null
                : request.MerchandisePayeeName.Trim());
        if (request.IsMerchandisePaid && !request.SupplierId.HasValue &&
            string.IsNullOrWhiteSpace(merchandisePayeeName))
        {
            throw new BusinessRuleException(
                "Phiếu đã trả tiền nhưng không chọn nhà cung cấp thì phải nhập tên người bán.");
        }

        var financialInputs = postedLines.ToDictionary(x => x.StockDocumentLineId);
        var taxCache = new Dictionary<int, Tax>();
        var lineAmounts = new Dictionary<int, PurchasePricingPolicy.LineAmounts>();
        var lineTaxes = new Dictionary<int, Tax?>();

        foreach (var line in activeLines)
        {
            var input = financialInputs[line.Id];
            if (input.UnitPriceBeforeVat <= 0)
                throw new BusinessRuleException(
                    $"Dòng {line.LineNo} phải có đơn giá chưa VAT lớn hơn 0.");
            EnsureStoredMoney(input.UnitPriceBeforeVat, $"Đơn giá dòng {line.LineNo}");

            if (request.HasVat && !input.TaxId.HasValue)
                throw new BusinessRuleException(
                    $"Dòng {line.LineNo} phải chọn thuế suất đã cấu hình khi bật VAT (kể cả thuế suất 0%).");

            Tax? tax = null;
            if (request.HasVat)
            {
                if (!taxCache.TryGetValue(input.TaxId!.Value, out tax))
                {
                    tax = await _stockDocumentRepository.GetTaxAsync(input.TaxId.Value, ct)
                        ?? throw new BusinessRuleException(
                            $"Thuế suất của dòng {line.LineNo} không tồn tại hoặc không thuộc cửa hàng hiện tại.");
                    if (tax.Rate < 0m || tax.Rate > 100m)
                        throw new BusinessRuleException($"Thuế suất của dòng {line.LineNo} nằm ngoài khoảng 0-100%.");
                    taxCache[tax.Id] = tax;
                }
            }

            lineTaxes[line.Id] = tax;
            try
            {
                var amounts = PurchasePricingPolicy.CalculateLineFromBeforeVat(
                    line.Quantity,
                    input.UnitPriceBeforeVat,
                    request.HasVat,
                    tax?.Rate ?? 0m);
                EnsureStoredMoney(amounts.UnitPriceBeforeVat, $"Đơn giá trước VAT dòng {line.LineNo}");
                EnsureStoredMoney(amounts.UnitPriceAfterVat, $"Đơn giá sau VAT dòng {line.LineNo}");
                EnsureStoredMoney(amounts.VatAmount, $"Tiền VAT dòng {line.LineNo}");
                EnsureStoredMoney(amounts.LineTotalAfterVat, $"Thành tiền dòng {line.LineNo}");
                lineAmounts[line.Id] = amounts;
            }
            catch (OverflowException)
            {
                throw new BusinessRuleException($"Giá hoặc thành tiền dòng {line.LineNo} vượt giới hạn cho phép.");
            }
        }

        IReadOnlyDictionary<int, decimal> freightAllocations;
        var freightTotal = 0m;
        if (!request.HasFreight)
        {
            freightAllocations = activeLines.ToDictionary(x => x.Id, _ => 0m);
        }
        else
        {
            freightTotal = PurchasePricingPolicy.RoundMoney(request.FreightTotal);
            if (freightTotal <= 0)
                throw new BusinessRuleException("Tổng phí vận chuyển phải lớn hơn 0.");
            EnsureStoredMoney(freightTotal, "Tổng phí vận chuyển");
            if (string.IsNullOrWhiteSpace(request.FreightPayeeName))
                throw new BusinessRuleException("Vui lòng nhập người hoặc đơn vị nhận tiền vận chuyển.");

            if (request.ResetAutomaticAllocation)
            {
                freightAllocations = PurchasePricingPolicy.AllocateFreight(
                    freightTotal,
                    activeLines.Select(x => (x.Id, lineAmounts[x.Id].LineTotalAfterVat)).ToList());
            }
            else
            {
                var postedAllocations = request.Allocations ?? new List<FreightAllocationInputDto>();
                if (postedAllocations.Any(x => x.StockDocumentLineId <= 0 || x.Amount < 0) ||
                    postedAllocations.GroupBy(x => x.StockDocumentLineId).Any(x => x.Count() > 1))
                    throw new BusinessRuleException("Phân bổ vận chuyển chứa dòng hoặc số tiền không hợp lệ.");
                foreach (var allocation in postedAllocations)
                    EnsureStoredMoney(allocation.Amount, $"Phí phân bổ dòng {allocation.StockDocumentLineId}");

                var allocationIds = postedAllocations.Select(x => x.StockDocumentLineId).ToHashSet();
                if (!activeLineIds.SetEquals(allocationIds))
                    throw new BusinessRuleException(
                        "Bảng phân bổ vận chuyển không đầy đủ. Vui lòng tải lại phiếu.");

                freightAllocations = postedAllocations.ToDictionary(
                    x => x.StockDocumentLineId,
                    x => PurchasePricingPolicy.RoundMoney(x.Amount));
            }

            PurchasePricingPolicy.EnsureFreightBalanced(freightTotal, freightAllocations.Values);
        }

        // Mutations remain only in EF's change tracker here. ApproveAsync starts
        // the single transaction that persists these values together with all
        // inventory/FIFO/payable postings.
        document.HasVat = request.HasVat;
        document.SupplierId = request.SupplierId;
        document.Supplier = supplier;
        document.IsMerchandisePaid = request.IsMerchandisePaid;
        document.MerchandisePayeeName = merchandisePayeeName;

        foreach (var line in activeLines)
        {
            var amounts = lineAmounts[line.Id];
            var tax = lineTaxes[line.Id];
            line.TaxId = request.HasVat ? tax?.Id : null;
            line.TaxNameSnapshot = request.HasVat ? tax?.Name : null;
            line.TaxRate = amounts.TaxRate;
            line.UnitPriceBeforeVat = amounts.UnitPriceBeforeVat;
            line.VatAmount = amounts.VatAmount;
            line.UnitPriceAfterVat = amounts.UnitPriceAfterVat;
            line.UnitCost = amounts.UnitPriceAfterVat;
            line.LineTotal = amounts.LineTotalAfterVat;
            line.FreightAllocation = freightAllocations[line.Id];
        }

        document.HasFreight = request.HasFreight;
        document.FreightTotal = freightTotal;
        document.FreightPayeeName = request.HasFreight ? request.FreightPayeeName!.Trim() : null;
        document.FreightNote = request.HasFreight ? request.FreightNote?.Trim() : null;
        document.IsFreightPaid = request.HasFreight && request.IsFreightPaid;
        RecalculateDocumentTotals(document);

        await ApproveTrackedAsync(document, request.ApprovalNote, request.RowVersion, ct);
    }

    public async Task ApproveAsync(
        int documentId,
        string? approvalNote,
        string? rowVersion,
        CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetForConfirmAsync(documentId, ct);
        if (document == null)
            throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");

        await ApproveTrackedAsync(document, approvalNote, rowVersion, ct);
    }

    private async Task ApproveTrackedAsync(
        StockDocument document,
        string? approvalNote,
        string? rowVersion,
        CancellationToken ct)
    {

        // Idempotent retry: refresh/gửi lại request sau khi commit không được tạo movement/FIFO/công nợ lần hai.
        if (document.Status == StockDocumentStatus.Confirmed)
            return;
        EnsureRowVersion(document.RowVersion, rowVersion);

        if (document.Type != StockDocumentType.Receipt)
            throw new BusinessRuleException("Chứng từ này không phải phiếu nhập kho.");

        if (document.Status != StockDocumentStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ phiếu đang chờ duyệt mới được duyệt nhập kho.");

        var postingWarehouse = await _warehouseRepository.GetByIdAsync(document.WarehouseId, ct);
        StockReceiptLegalEntityPolicy.EnsureWarehouseSelectable(
            postingWarehouse?.LegalEntityId ?? 0,
            postingWarehouse);

        var activeLines = document.Lines
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.LineNo)
            .ToList();

        if (!activeLines.Any())
            throw new BusinessRuleException("Phiếu nhập kho chưa có dòng chi tiết hợp lệ.");

        ValidateReceiptSourceAndShortages(document);
        if (!document.IsMerchandisePaid && !document.SupplierId.HasValue)
            throw new BusinessRuleException(
                "Phiếu nhập còn nợ tiền hàng bắt buộc phải chọn nhà cung cấp.");
        if (document.IsMerchandisePaid && !document.SupplierId.HasValue &&
            string.IsNullOrWhiteSpace(document.MerchandisePayeeName))
            throw new BusinessRuleException(
                "Phiếu đã trả tiền nhưng không chọn nhà cung cấp thì phải nhập tên người bán.");
        if (document.HasFreight)
        {
            if (document.FreightTotal <= 0)
                throw new BusinessRuleException("Tổng phí vận chuyển phải lớn hơn 0.");
            if (string.IsNullOrWhiteSpace(document.FreightPayeeName))
                throw new BusinessRuleException("Thiếu người hoặc đơn vị nhận tiền vận chuyển.");
            PurchasePricingPolicy.EnsureFreightBalanced(document.FreightTotal, activeLines.Select(x => x.FreightAllocation));
        }
        else if (activeLines.Any(x => x.FreightAllocation != 0m))
        {
            throw new BusinessRuleException("Phiếu không bật phí vận chuyển nhưng vẫn còn tiền phân bổ.");
        }

        foreach (var line in activeLines)
        {
            if (line.ProductVariantId <= 0)
            {
                throw new InvalidOperationException(
                    "Stock-document line has an invalid product-variant relation.");
            }

            if (line.Quantity <= 0)
            {
                throw new BusinessRuleException(
                    $"Dòng {line.LineNo} có số lượng nhập không hợp lệ.");
            }

            if (line.Factor <= 0)
            {
                throw new BusinessRuleException(
                    $"Dòng {line.LineNo} có hệ số quy đổi không hợp lệ.");
            }

            if (line.BaseQuantity <= 0)
            {
                throw new BusinessRuleException(
                    $"Dòng {line.LineNo} có số lượng quy đổi không hợp lệ.");
            }

            if (line.UnitCost <= 0)
            {
                throw new BusinessRuleException(
                    $"Dòng {line.LineNo} chưa có đơn giá nhập hợp lệ. " +
                    "Vui lòng sửa lại đơn giá nhập trước khi duyệt.");
            }

            var expectedBaseQuantity = line.Quantity * line.Factor;
            if (Math.Abs(line.BaseQuantity - expectedBaseQuantity) > 0.0001m)
            {
                throw new BusinessRuleException(
                    $"Dòng {line.LineNo} đang lệch số lượng quy đổi. " +
                    $"Đúng phải là SL nhập × Factor = {expectedBaseQuantity:n3}. " +
                    "Vui lòng sửa lại dòng trước khi duyệt.");
            }

            var expectedLineTotalFromAfterVat = PurchasePricingPolicy.RoundMoney(line.Quantity * line.UnitCost);
            var expectedLineTotalFromBeforeVat = PurchasePricingPolicy.CalculateLineFromBeforeVat(
                line.Quantity,
                line.UnitPriceBeforeVat,
                document.HasVat,
                line.TaxRate).LineTotalAfterVat;
            if (Math.Abs(line.LineTotal - expectedLineTotalFromAfterVat) > 0.01m &&
                Math.Abs(line.LineTotal - expectedLineTotalFromBeforeVat) > 0.01m)
            {
                throw new BusinessRuleException(
                    $"Dòng {line.LineNo} đang lệch thành tiền. " +
                    $"Vui lòng tải lại phiếu để hệ thống tính lại giá và VAT. " +
                    "Vui lòng sửa lại dòng trước khi duyệt.");
            }
        }

        await _stockDocumentRepository.BeginTransactionAsync(ct);

        try
        {
            await _inventoryMovementService.PreLockBalancesAsync(
                activeLines.Select(line => new InventoryPostingLockKey(
                    document.StoreId,
                    document.WarehouseId,
                    line.ProductVariantId)),
                ct);

            var occurredAtUtc = DateTime.UtcNow;

            // Đồng bộ lại tổng tiền phiếu theo các dòng hợp lệ trước khi duyệt.
            RecalculateDocumentTotals(document);

            foreach (var line in activeLines)
            {
                // line.UnitCost là giá theo đơn vị nhập.
                // Khi ghi InventoryMovement với qtyBase thì phải đổi về giá vốn đơn vị gốc.
                decimal baseUnitCost;
                try
                {
                    baseUnitCost = PurchasePricingPolicy.CalculateBaseUnitCost(
                        line.LineTotal,
                        line.FreightAllocation,
                        line.BaseQuantity);
                }
                catch (OverflowException)
                {
                    throw new BusinessRuleException(
                        $"Giá vốn đơn vị gốc dòng {line.LineNo} vượt giới hạn cho phép.");
                }

                if (baseUnitCost <= 0 || baseUnitCost > MaximumStoredBaseUnitCost)
                {
                    throw new BusinessRuleException(
                        $"Dòng {line.LineNo} chưa tính được giá vốn đơn vị gốc hợp lệ.");
                }

                var movementRequest = _inventoryMovementFactory.CreatePurchaseReceipt(
                    warehouseId: document.WarehouseId,
                    productVariantId: line.ProductVariantId,
                    qtyBase: line.BaseQuantity,
                    unitCost: baseUnitCost,
                    documentId: document.Id.ToString(),
                    lineId: line.Id,
                    documentNo: document.DocumentNo,
                    lineNo: line.LineNo,
                    occurredAtUtc: occurredAtUtc);

                await _inventoryMovementService.CreateAsync(movementRequest, ct);

                var revaluationNote =
                    $"Revalue provisional sau nhập kho từ phiếu {document.DocumentNo}, dòng {line.LineNo}.";

                var inboundEntries = await _valuationRepository.GetByReferenceAsync(
                    InventoryReferenceType.StockDocument,
                    document.Id.ToString(),
                    line.Id,
                    ct);

                var inboundEntry = inboundEntries
                    .FirstOrDefault(x => x.EntryType == InventoryValuationEntryType.Inbound);

                if (inboundEntry?.InventoryCostLayerId != null)
                {
                    await _inventoryRevaluationService.ResolveByInboundLayerAsync(
                        inboundEntry.InventoryCostLayerId.Value,
                        occurredAtUtc,
                        revaluationNote,
                        ct);
                }
            }

            // =====================================================
            // PHASE 2.7:
            // Dòng nhập nào được đánh dấu UseInputInvoice = true
            // thì bật ProductVariant.HasInputInvoice = true.
            //
            // Lưu ý:
            // - Không set false cho các dòng không thuộc XML.
            // - Vì 1 variant có thể đã từng có hóa đơn đầu vào ở phiếu khác.
            // =====================================================
            var useInputInvoiceLineIds = (document.LineInputInvoiceMaps
                    ?? Enumerable.Empty<StockDocumentLineInputInvoiceMap>())
                .Where(x => !x.IsDeleted && x.UseInputInvoice)
                .Select(x => x.StockDocumentLineId)
                .Distinct()
                .ToHashSet();

            var inputInvoiceVariantIds = activeLines
                .Where(x => useInputInvoiceLineIds.Contains(x.Id))
                .Select(x => x.ProductVariantId)
                .Distinct()
                .ToList();

            await _stockDocumentRepository.MarkVariantsHasInputInvoiceAsync(
                inputInvoiceVariantIds,
                userId: null,
                ct);

            if (document.PurchaseOrder != null)
            {
                ApplyApprovedReceiptToPurchaseOrder(document, activeLines, occurredAtUtc);
            }

            await CreatePayablesIfNeededAsync(document, occurredAtUtc, ct);

            document.Status = StockDocumentStatus.Confirmed;
            document.ApprovedAtUtc = occurredAtUtc;
            document.ApprovedByUserId = _currentUser.UserId;
            document.ConfirmedAtUtc = occurredAtUtc;
            document.ConfirmedByUserId = _currentUser.UserId;
            document.ApprovalNote = approvalNote?.Trim();

            await _stockDocumentRepository.SaveChangesAsync(ct);
            await _stockDocumentRepository.CommitTransactionAsync(ct);
        }
        catch
        {
            await _stockDocumentRepository.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task RejectAsync(
        int documentId,
        string? approvalNote,
        string? rowVersion,
        CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetByIdAsync(documentId, ct);
        if (document == null)
            throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");

        if (document.Status != StockDocumentStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ phiếu đang chờ duyệt mới được từ chối.");
        EnsureRowVersion(document.RowVersion, rowVersion);

        document.Status = StockDocumentStatus.Rejected;
        document.ApprovalNote = approvalNote;
        document.HasRevisionRequest = false;
        document.RevisionResolvedAtUtc = DateTime.UtcNow;
        document.RevisionResolvedByUserId = null;

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

    private void EnsureEditable(StockDocumentStatus status)
    {
        if (status != StockDocumentStatus.Draft &&
            status != StockDocumentStatus.PendingApproval &&
            status != StockDocumentStatus.Rejected)
        {
            throw new BusinessRuleException("Phiếu hiện tại không được phép chỉnh sửa.");
        }
    }

    private static void EnsureRowVersion(byte[] current, string? posted)
    {
        if (string.IsNullOrWhiteSpace(posted))
            throw new BusinessRuleException("Thiếu RowVersion. Vui lòng tải lại phiếu.");
        byte[] expected;
        try { expected = Convert.FromBase64String(posted); }
        catch (FormatException) { throw new BusinessRuleException("RowVersion không hợp lệ."); }
        if (!current.SequenceEqual(expected))
            throw new BusinessRuleException("Phiếu đã được người khác cập nhật. Vui lòng tải lại trang.");
    }

    private static void EnsureDirectLineEditing(StockDocument document)
    {
        if (document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder || document.PurchaseOrderId.HasValue)
            throw new BusinessRuleException("Dòng phiếu nhập từ đơn đặt hàng chỉ được tạo qua chức năng Nhận hàng của đơn.");
    }

    private static void ValidateReceiptSourceAndShortages(StockDocument document)
    {
        if (document.ReceiptSource == PurchaseReceiptSource.Direct && string.IsNullOrWhiteSpace(document.DirectReceiptReason))
            throw new BusinessRuleException("Phiếu nhập ngoài đơn bắt buộc phải có nguồn nhập.");

        if (document.ReceiptSource != PurchaseReceiptSource.PurchaseOrder)
            return;

        var order = document.PurchaseOrder
            ?? throw new InvalidOperationException(
                "Purchase-order receipt is missing its purchase-order relation.");
        if (order.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.SentToSupplier or PurchaseOrderStatus.PartiallyReceived))
            throw new BusinessRuleException("Trạng thái đơn đặt hàng không còn cho phép nhận hàng.");
        if (document.SupplierId != order.SupplierId || document.WarehouseId != order.ExpectedWarehouseId)
            throw new BusinessRuleException("Nhà cung cấp hoặc kho nhận không khớp đơn đặt hàng.");
        if (document.Warehouse?.LegalEntityId != order.LegalEntityId)
            throw new BusinessRuleException("HKD của kho nhận không khớp đơn đặt hàng.");

        var orderLines = order.Lines.Where(x => !x.IsDeleted).ToDictionary(x => x.Id);
        foreach (var line in document.Lines.Where(x => !x.IsDeleted))
        {
            if (!line.PurchaseOrderLineId.HasValue || !orderLines.TryGetValue(line.PurchaseOrderLineId.Value, out var orderLine))
                throw new BusinessRuleException($"Dòng {line.LineNo} không liên kết đúng dòng đơn đặt hàng.");
            if (line.ProductVariantId != orderLine.ProductVariantId ||
                line.ProductUnitConversionId != orderLine.ProductUnitConversionId ||
                line.UnitId != orderLine.UnitId ||
                line.Factor != orderLine.ConversionFactor)
                throw new BusinessRuleException($"Dòng {line.LineNo} không khớp snapshot sản phẩm/đơn vị của đơn đặt hàng.");

            PurchaseReceiptPolicy.ValidateLine(
                orderLine.PendingQuantity,
                line.Quantity,
                line.ShortageDisposition,
                line.ShortageReason,
                line.LineNo);
        }
    }

    private void ApplyApprovedReceiptToPurchaseOrder(
        StockDocument document,
        IReadOnlyCollection<StockDocumentLine> receiptLines,
        DateTime occurredAtUtc)
    {
        var order = document.PurchaseOrder!;
        var orderLines = order.Lines.Where(x => !x.IsDeleted).ToDictionary(x => x.Id);
        foreach (var receiptLine in receiptLines)
        {
            var orderLine = orderLines[receiptLine.PurchaseOrderLineId!.Value];
            PurchaseReceiptPolicy.ApplyApprovedLine(
                orderLine,
                receiptLine.Quantity,
                receiptLine.ShortageDisposition,
                receiptLine.ShortageReason,
                occurredAtUtc,
                _currentUser.UserId);
        }

        var oldStatus = order.Status;
        var activeOrderLines = order.Lines.Where(x => !x.IsDeleted).ToList();
        order.Status = PurchaseReceiptPolicy.ResolveOrderStatus(activeOrderLines, order.Status);

        order.Actions.Add(new PurchaseOrderAction
        {
            ActionType = order.Status == PurchaseOrderStatus.ShortClosed
                ? PurchaseOrderActionType.ShortClosed
                : PurchaseOrderActionType.ReceiptApproved,
            FromStatus = oldStatus,
            ToStatus = order.Status,
            ActorUserId = _currentUser.UserId,
            OccurredAtUtc = occurredAtUtc,
            Note = $"Duyệt phiếu nhập {document.DocumentNo}.",
            StockDocumentId = document.Id
        });
    }

    private async Task CreatePayablesIfNeededAsync(StockDocument document, DateTime occurredAtUtc, CancellationToken ct)
    {
        if (document.TotalAmount > 0)
        {
            var sourceKey = PurchasePostingIdentity.MerchandisePayable(document.Id);
            if (!await _stockDocumentRepository.PurchasePayableExistsAsync(sourceKey, ct))
            {
                await _stockDocumentRepository.AddPurchasePayableAsync(new PurchasePayable
                {
                    StockDocumentId = document.Id,
                    PurchaseOrderId = document.PurchaseOrderId,
                    Type = PurchasePayableType.Merchandise,
                    SourceKey = sourceKey,
                    SupplierId = document.SupplierId,
                    PayeeName = document.MerchandisePayeeName
                        ?? document.Supplier?.Name
                        ?? document.PurchaseOrder?.Supplier?.Name,
                    Amount = document.TotalAmount,
                    Status = document.IsMerchandisePaid
                        ? PurchasePayableStatus.Paid
                        : PurchasePayableStatus.Outstanding,
                    RecognizedAtUtc = occurredAtUtc,
                    PaidAtUtc = document.IsMerchandisePaid ? occurredAtUtc : null,
                    Note = $"Tiền hàng theo phiếu nhập {document.DocumentNo}."
                }, ct);
            }
        }

        if (document.HasFreight && document.FreightTotal > 0)
        {
            var sourceKey = PurchasePostingIdentity.FreightPayable(document.Id);
            if (!await _stockDocumentRepository.PurchasePayableExistsAsync(sourceKey, ct))
            {
                await _stockDocumentRepository.AddPurchasePayableAsync(new PurchasePayable
                {
                    StockDocumentId = document.Id,
                    PurchaseOrderId = document.PurchaseOrderId,
                    Type = PurchasePayableType.Freight,
                    SourceKey = sourceKey,
                    SupplierId = null,
                    PayeeName = document.FreightPayeeName,
                    Amount = document.FreightTotal,
                    Status = document.IsFreightPaid ? PurchasePayableStatus.Paid : PurchasePayableStatus.Outstanding,
                    RecognizedAtUtc = occurredAtUtc,
                    PaidAtUtc = document.IsFreightPaid ? occurredAtUtc : null,
                    Note = document.FreightNote
                }, ct);
            }
        }
    }

    private static void RecalculateDocumentTotals(StockDocument document, int? excludedLineId = null)
    {
        var lines = document.Lines
            .Where(x => !x.IsDeleted && (!excludedLineId.HasValue || x.Id != excludedLineId.Value))
            .ToList();
        decimal totalAmount = 0m;
        decimal vatAmount = 0m;
        try
        {
            checked
            {
                foreach (var line in lines)
                {
                    totalAmount += line.LineTotal;
                    if (document.HasVat) vatAmount += line.VatAmount;
                }
            }
        }
        catch (OverflowException)
        {
            throw new BusinessRuleException("Tổng tiền phiếu vượt giới hạn cho phép.");
        }

        document.TotalAmount = PurchasePricingPolicy.RoundMoney(totalAmount);
        document.VatAmount = document.HasVat ? PurchasePricingPolicy.RoundMoney(vatAmount) : 0m;
        EnsureStoredMoney(document.TotalAmount, "Tổng tiền phiếu");
        EnsureStoredMoney(document.VatAmount, "Tổng VAT phiếu");
        document.SubtotalBeforeVat = PurchasePricingPolicy.RoundMoney(document.TotalAmount - document.VatAmount);
        EnsureStoredMoney(document.SubtotalBeforeVat, "Tổng trước VAT");
        if (!document.HasVat)
        {
            foreach (var line in lines)
            {
                line.TaxRate = 0m;
                line.VatAmount = 0m;
                line.UnitPriceBeforeVat = line.UnitCost;
                line.UnitPriceAfterVat = line.UnitCost;
            }
        }
    }

    private static void EnsureStoredMoney(decimal value, string fieldName)
    {
        if (value < 0m || value > MaximumStoredMoney)
            throw new BusinessRuleException($"{fieldName} vượt giới hạn lưu trữ cho phép.");
    }

    private static int TryParseSequence(string documentNo, string prefix)
    {
        if (string.IsNullOrWhiteSpace(documentNo))
            return 0;

        if (!documentNo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return 0;

        var seqPart = documentNo.Substring(prefix.Length);

        return int.TryParse(seqPart, out var seq) ? seq : 0;
    }

    public async Task UpdateHeaderAsync(UpdateStockDocumentHeaderRequest request, CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetByIdAsync(request.StockDocumentId, ct);
        if (document == null)
            throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");

        EnsureEditable(document.Status);

        if (document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder || document.PurchaseOrderId.HasValue)
            throw new BusinessRuleException("Không thể đổi kho, nhà cung cấp hoặc HKD của phiếu nhập tạo từ đơn đặt hàng.");

        if (!request.WarehouseId.HasValue || request.WarehouseId.Value <= 0)
            throw new BusinessRuleException("Vui lòng chọn kho.");

        if (!request.LegalEntityId.HasValue || request.LegalEntityId.Value <= 0)
            throw new BusinessRuleException("Vui lòng chọn HKD nhập hàng.");

        var warehouse = await _warehouseRepository.GetByIdAsync(request.WarehouseId.Value, ct);
        StockReceiptLegalEntityPolicy.EnsureWarehouseSelectable(
            request.LegalEntityId.Value,
            warehouse);

        if (request.SupplierId.HasValue)
        {
            var supplierExists = await _stockDocumentRepository.SupplierExistsAsync(request.SupplierId.Value, ct);
            if (!supplierExists)
                throw new BusinessRuleException("Nhà cung cấp không tồn tại.");
        }

        document.WarehouseId = request.WarehouseId.Value;
        document.SupplierId = request.SupplierId;
        document.DocumentDate = request.DocumentDate ?? document.DocumentDate;
        document.Note = request.Note?.Trim();
        document.ApprovalNote = request.ApprovalNote?.Trim();

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Resolve barcode snapshot cho line nhập kho.
    ///
    /// Thứ tự ưu tiên:
    /// 1) barcode primary của đúng đơn vị đang nhập
    /// 2) barcode primary của đơn vị mặc định bán
    /// 3) barcode primary của đơn vị gốc
    /// 4) barcode active đầu tiên còn lại
    ///
    /// Đây là barcode snapshot để hiển thị / đối chiếu chứng từ,
    /// không phải cơ chế lookup barcode chính thức.
    /// </summary>
    private static string? ResolveBarcodeSnapshot(ProductVariant? variant, int? preferredUnitId)
    {
        if (variant?.UnitConversions == null || !variant.UnitConversions.Any())
            return null;

        var activeConversions = variant.UnitConversions
            .Where(c => !c.IsDeleted && c.IsActive)
            .ToList();

        if (!activeConversions.Any())
            return null;

        if (preferredUnitId.HasValue && preferredUnitId.Value > 0)
        {
            var preferredBarcode = activeConversions
                .Where(c => c.UnitId == preferredUnitId.Value)
                .SelectMany(c => c.Barcodes ?? Enumerable.Empty<ProductVariantUnitBarcode>())
                .Where(b => !b.IsDeleted && b.IsActive)
                .OrderByDescending(b => b.IsPrimary)
                .ThenBy(b => b.Id)
                .Select(b => b.Barcode)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(preferredBarcode))
                return preferredBarcode;
        }

        var defaultSaleBarcode = activeConversions
            .Where(c => c.IsDefaultForSale)
            .SelectMany(c => c.Barcodes ?? Enumerable.Empty<ProductVariantUnitBarcode>())
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .Select(b => b.Barcode)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(defaultSaleBarcode))
            return defaultSaleBarcode;

        var baseUnitBarcode = activeConversions
            .Where(c => c.IsBaseUnit)
            .SelectMany(c => c.Barcodes ?? Enumerable.Empty<ProductVariantUnitBarcode>())
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .Select(b => b.Barcode)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(baseUnitBarcode))
            return baseUnitBarcode;

        var fallbackBarcode = activeConversions
            .SelectMany(c => c.Barcodes ?? Enumerable.Empty<ProductVariantUnitBarcode>())
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .Select(b => b.Barcode)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(fallbackBarcode) ? null : fallbackBarcode;
    }

    private static decimal NormalizeFactor(decimal factor)
    {
        return factor <= 0 ? 1m : factor;
    }

    private static decimal CalculateDefaultPurchaseUnitCost(decimal baseCostPrice, decimal factor)
    {
        // CostPrice = 1 thường là dữ liệu mặc định lỗi, không lấy làm giá nhập thật.
        if (baseCostPrice <= 1m)
            return 0m;

        return Math.Round(baseCostPrice * factor, 0, MidpointRounding.AwayFromZero);
    }

    private static decimal ResolvePurchaseUnitCost(
        decimal requestUnitCost,
        decimal baseCostPrice,
        decimal factor)
    {
        if (requestUnitCost > 1m)
            return requestUnitCost;

        return CalculateDefaultPurchaseUnitCost(baseCostPrice, factor);
    }

    private static decimal CalculateLineTotal(decimal quantity, decimal unitCost)
    {
        return quantity * unitCost;
    }

    private static decimal CalculateBaseUnitCost(decimal lineTotal, decimal baseQuantity)
    {
        if (baseQuantity <= 0)
            return 0m;

        return lineTotal / baseQuantity;
    }
    private int RequireStoreId()
    {
        var storeId = _tenantContext.StoreId;

        if (!storeId.HasValue || storeId.Value <= 0)
        {
            throw new InvalidOperationException(
                "Current store context is unavailable.");
        }

        return storeId.Value;
    }
    private static string BuildReceiptDocumentNo(DateTime documentDate, int sequence)
    {
        return $"NK-{documentDate:yyyyMMdd}-{sequence:D4}";
    }
    public async Task RequestRevisionAsync(
    int documentId,
    string note,
    CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetByIdAsync(documentId, ct);
        if (document == null)
            throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");

        if (document.Status != StockDocumentStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ phiếu đang chờ duyệt mới được đề nghị sửa.");

        if (document.HasRevisionRequest)
            throw new BusinessRuleException("Phiếu này đã có yêu cầu sửa, vui lòng chờ quản lý xử lý.");

        note = (note ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(note))
            throw new BusinessRuleException("Vui lòng nhập lý do đề nghị sửa.");

        if (note.Length > 1000)
            throw new BusinessRuleException("Lý do đề nghị sửa không được vượt quá 1000 ký tự.");

        document.HasRevisionRequest = true;
        document.RevisionRequestNote = note;
        document.RevisionRequestedAtUtc = DateTime.UtcNow;
        document.RevisionRequestedByUserId = null;

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

    public async Task ResolveRevisionRequestAsync(
        int documentId,
        bool returnToEdit,
        string? approvalNote = null,
        CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetByIdAsync(documentId, ct);
        if (document == null)
            throw new BusinessRuleException("Phiếu nhập kho không tồn tại.");

        if (document.Status != StockDocumentStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ phiếu đang chờ duyệt mới xử lý được yêu cầu sửa.");

        if (!document.HasRevisionRequest)
            throw new BusinessRuleException("Phiếu này chưa có yêu cầu sửa.");

        document.HasRevisionRequest = false;
        document.RevisionResolvedAtUtc = DateTime.UtcNow;
        document.RevisionResolvedByUserId = null;

        if (returnToEdit)
        {
            document.Status = StockDocumentStatus.Rejected;
            document.ApprovalNote = string.IsNullOrWhiteSpace(approvalNote)
                ? document.RevisionRequestNote
                : approvalNote.Trim();
        }

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

}
