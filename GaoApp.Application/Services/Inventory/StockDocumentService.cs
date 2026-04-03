using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public class StockDocumentService : IStockDocumentService
{
    private readonly IStockDocumentRepository _stockDocumentRepository;
    private readonly IBarcodeLookupService _barcodeLookupService;
    private readonly IInventoryUnitResolver _inventoryUnitResolver;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly IInventoryMovementFactory _inventoryMovementFactory;
    private readonly IInventoryRevaluationService _inventoryRevaluationService;
    private readonly IDocumentNumberSequenceRepository _documentNumberSequenceRepository;
    private readonly IInventoryValuationEntryRepository _valuationRepository;
    private readonly ITenantContext _tenantContext;

    public StockDocumentService(
        IStockDocumentRepository stockDocumentRepository,
        IBarcodeLookupService barcodeLookupService,
        IInventoryUnitResolver inventoryUnitResolver,
        IInventoryMovementService inventoryMovementService,
        IInventoryMovementFactory inventoryMovementFactory,
        IInventoryRevaluationService inventoryRevaluationService,
        IDocumentNumberSequenceRepository documentNumberSequenceRepository,
        ITenantContext tenantContext,
        IInventoryValuationEntryRepository valuationRepository)
    {
        _stockDocumentRepository = stockDocumentRepository;
        _barcodeLookupService = barcodeLookupService;
        _inventoryUnitResolver = inventoryUnitResolver;
        _inventoryMovementService = inventoryMovementService;
        _inventoryMovementFactory = inventoryMovementFactory;
        _inventoryRevaluationService = inventoryRevaluationService;
        _documentNumberSequenceRepository = documentNumberSequenceRepository;
        _tenantContext = tenantContext;
        _valuationRepository = valuationRepository;
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
            DocumentDate = x.DocumentDate,
            WarehouseName = x.Warehouse?.Name ?? string.Empty,
            SupplierName = x.Supplier?.Name,
            Status = x.Status,
            TotalAmount = x.TotalAmount,
            SubmittedAtUtc = x.SubmittedAtUtc,
            ApprovedAtUtc = x.ApprovedAtUtc
        }).ToList();
    }

    public async Task<int> CreateReceiptAsync(CreateStockDocumentRequest request, CancellationToken ct = default)
    {
        var warehouseExists = await _stockDocumentRepository.WarehouseExistsAsync(request.WarehouseId, ct);
        if (!warehouseExists)
            throw new InvalidOperationException("Kho không tồn tại.");

        if (request.SupplierId.HasValue)
        {
            var supplierExists = await _stockDocumentRepository.SupplierExistsAsync(request.SupplierId.Value, ct);
            if (!supplierExists)
                throw new InvalidOperationException("Nhà cung cấp không tồn tại.");
        }

        var documentDate = request.DocumentDate ?? DateTime.UtcNow;

        var nextNumber = await _documentNumberSequenceRepository.GetNextNumberAsync(
            storeId: _tenantContext.StoreId.Value,
            sequenceType: DocumentNumberSequenceType.StockReceipt,
            sequenceDate: documentDate,
            ct: ct);

        var documentNo = BuildReceiptDocumentNo(documentDate, nextNumber);

        var document = new StockDocument
        {
            DocumentNo = documentNo,
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Draft,
            WarehouseId = request.WarehouseId,
            SupplierId = request.SupplierId,
            DocumentDate = documentDate,
            Note = request.Note,
            TotalAmount = 0
        };

        await _stockDocumentRepository.AddAsync(document, ct);
        await _stockDocumentRepository.SaveChangesAsync(ct);

        return document.Id;
    }

    public async Task<StockDocumentDto?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetDetailAsync(id, ct);
        if (document == null) return null;

        return new StockDocumentDto
        {
            Id = document.Id,
            DocumentNo = document.DocumentNo,
            Type = document.Type,
            Status = document.Status,
            DocumentDate = document.DocumentDate,
            WarehouseId = document.WarehouseId,
            WarehouseName = document.Warehouse?.Name ?? string.Empty,
            SupplierId = document.SupplierId,
            SupplierName = document.Supplier?.Name,
            Note = document.Note,
            TotalAmount = document.TotalAmount,
            SubmittedAtUtc = document.SubmittedAtUtc,
            SubmittedByUserId = document.SubmittedByUserId,
            ApprovedAtUtc = document.ApprovedAtUtc,
            ApprovedByUserId = document.ApprovedByUserId,
            ApprovalNote = document.ApprovalNote,
            ConfirmedAtUtc = document.ConfirmedAtUtc,
            ConfirmedByUserId = document.ConfirmedByUserId,
            Lines = document.Lines
                .OrderBy(x => x.LineNo)
                .Select(x => new StockDocumentLineDto
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
                    ProductNameSnapshot = x.ProductNameSnapshot,
                    SkuSnapshot = x.SkuSnapshot,
                    BarcodeSnapshot = x.BarcodeSnapshot,
                    Note = x.Note
                })
                .ToList()
        };
    }

    public async Task<int> AddLineAsync(int documentId, AddStockDocumentLineRequest request, CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetDetailAsync(documentId, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu nhập kho không tồn tại.");

        EnsureEditable(document.Status);

        var variant = await _stockDocumentRepository.GetVariantForStockDocumentAsync(request.ProductVariantId, ct);
        if (variant == null)
            throw new InvalidOperationException("Sản phẩm không tồn tại.");

        var conversion = await _inventoryUnitResolver.ResolveAsync(request.ProductVariantId, request.UnitId, ct);

        var unitCost = variant.CostPrice;

        var line = new StockDocumentLine
        {
            StockDocumentId = document.Id,
            LineNo = await _stockDocumentRepository.GetNextLineNoAsync(document.Id, ct),
            ProductVariantId = request.ProductVariantId,
            UnitId = conversion.UnitId,
            UnitNameSnapshot = conversion.UnitName,
            Factor = conversion.Factor,
            Quantity = request.Quantity,
            BaseQuantity = request.Quantity * conversion.Factor,
            UnitCost = unitCost,
            LineTotal = (request.Quantity * conversion.Factor) * unitCost,
            ProductNameSnapshot = variant.Product?.Name ?? $"Variant #{variant.Id}",
            SkuSnapshot = variant.Sku,
            BarcodeSnapshot = ResolveBarcodeSnapshot(variant, conversion.UnitId),
            Note = request.Note
        };

        document.Lines.Add(line);
        document.TotalAmount += line.LineTotal;

        await _stockDocumentRepository.SaveChangesAsync(ct);

        return line.Id;
    }

    public async Task<int> AddLineByBarcodeAsync(int documentId, AddStockDocumentLineByBarcodeRequest request, CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetDetailAsync(documentId, ct);
        if (document == null)
            throw new InvalidOperationException($"Phiếu nhập kho không tồn tại. documentId={documentId}");

        EnsureEditable(document.Status);

        var barcode = (request.Barcode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(barcode))
            throw new InvalidOperationException("Barcode không được để trống.");

        if (request.Quantity <= 0)
            throw new InvalidOperationException("Số lượng phải lớn hơn 0.");

        var lookup = await _barcodeLookupService.FindAsync(barcode, ct);
        if (lookup == null)
            throw new InvalidOperationException($"Không tìm thấy sản phẩm theo barcode: {barcode}");

        var variantEntity = await _stockDocumentRepository.GetVariantForStockDocumentAsync(lookup.ProductVariantId, ct);
        if (variantEntity == null)
            throw new InvalidOperationException($"Không tìm thấy ProductVariant. ProductVariantId={lookup.ProductVariantId}");

        var factor = lookup.Factor <= 0 ? 1m : lookup.Factor;
        var lineTotal = request.Quantity * request.UnitCost;
        var baseQuantity = request.Quantity * factor;

        var existing = document.Lines.FirstOrDefault(x =>
            !x.IsDeleted &&
            x.ProductVariantId == lookup.ProductVariantId &&
            x.UnitId == lookup.UnitId &&
            x.UnitCost == request.UnitCost);

        if (existing != null)
        {
            existing.Quantity += request.Quantity;
            existing.BaseQuantity += baseQuantity;
            existing.LineTotal += lineTotal;
            existing.Factor = factor;
            existing.UnitNameSnapshot = lookup.UnitName;
            existing.BarcodeSnapshot = lookup.Barcode;
            existing.Note = request.Note;

            document.TotalAmount = document.Lines
                .Where(x => !x.IsDeleted)
                .Sum(x => x.LineTotal);

            await _stockDocumentRepository.SaveChangesAsync(ct);
            return existing.Id;
        }

        var line = new StockDocumentLine
        {
            StockDocumentId = document.Id,
            LineNo = await _stockDocumentRepository.GetNextLineNoAsync(document.Id, ct),
            ProductVariantId = lookup.ProductVariantId,
            UnitId = lookup.UnitId,
            UnitNameSnapshot = lookup.UnitName,
            Factor = factor,
            Quantity = request.Quantity,
            BaseQuantity = baseQuantity,
            UnitCost = request.UnitCost,
            LineTotal = lineTotal,
            ProductNameSnapshot = lookup.ProductName,
            SkuSnapshot = lookup.VariantSku,
            BarcodeSnapshot = lookup.Barcode,
            Note = request.Note
        };

        document.Lines.Add(line);
        document.TotalAmount += line.LineTotal;

        await _stockDocumentRepository.SaveChangesAsync(ct);
        return line.Id;
    }

    public async Task UpdateLineAsync(int lineId, UpdateStockDocumentLineRequest request, CancellationToken ct = default)
    {
        var line = await _stockDocumentRepository.GetLineByIdAsync(lineId, ct);
        if (line == null)
            throw new InvalidOperationException("Dòng phiếu nhập không tồn tại.");

        EnsureEditable(line.StockDocument.Status);

        var conversion = await _inventoryUnitResolver.ResolveAsync(line.ProductVariantId, request.UnitId, ct);

        line.UnitId = conversion.UnitId;
        line.UnitNameSnapshot = conversion.UnitName;
        line.Factor = conversion.Factor;
        line.Quantity = request.Quantity;
        line.BaseQuantity = request.Quantity * conversion.Factor;
        line.UnitCost = request.UnitCost;
        line.LineTotal = line.BaseQuantity * line.UnitCost;
        line.Note = request.Note;

        if (line.ProductVariant != null)
        {
            line.BarcodeSnapshot = ResolveBarcodeSnapshot(line.ProductVariant, conversion.UnitId);
        }

        var document = await _stockDocumentRepository.GetDetailAsync(line.StockDocumentId, ct);
        if (document != null)
            document.TotalAmount = document.Lines.Sum(x => x.LineTotal);

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

    public async Task DeleteLineAsync(int lineId, CancellationToken ct = default)
    {
        var line = await _stockDocumentRepository.GetLineByIdAsync(lineId, ct);
        if (line == null)
            throw new InvalidOperationException("Dòng phiếu nhập không tồn tại.");

        EnsureEditable(line.StockDocument.Status);

        var document = await _stockDocumentRepository.GetDetailAsync(line.StockDocumentId, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu nhập kho không tồn tại.");

        await _stockDocumentRepository.RemoveLineAsync(line, ct);

        document.TotalAmount -= line.LineTotal;
        if (document.TotalAmount < 0)
            document.TotalAmount = 0;

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

    public async Task SubmitForApprovalAsync(int documentId, string? approvalNote = null, CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetForConfirmAsync(documentId, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu nhập kho không tồn tại.");

        if (document.Status != StockDocumentStatus.Draft &&
            document.Status != StockDocumentStatus.Rejected)
        {
            throw new InvalidOperationException("Chỉ phiếu nháp hoặc phiếu bị từ chối mới được gửi duyệt.");
        }

        if (!document.Lines.Any())
            throw new InvalidOperationException("Phiếu nhập kho chưa có dòng chi tiết.");

        document.Status = StockDocumentStatus.PendingApproval;
        document.SubmittedAtUtc = DateTime.UtcNow;
        document.SubmittedByUserId = null;
        document.ApprovalNote = approvalNote;

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

    public async Task ApproveAsync(int documentId, string? approvalNote = null, CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetForConfirmAsync(documentId, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu nhập kho không tồn tại.");

        if (document.Type != StockDocumentType.Receipt)
            throw new InvalidOperationException("Chứng từ này không phải phiếu nhập kho.");

        if (document.Status != StockDocumentStatus.PendingApproval)
            throw new InvalidOperationException("Chỉ phiếu đang chờ duyệt mới được duyệt nhập kho.");

        var activeLines = document.Lines
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.LineNo)
            .ToList();

        if (!activeLines.Any())
            throw new InvalidOperationException("Phiếu nhập kho chưa có dòng chi tiết hợp lệ.");

        foreach (var line in activeLines)
        {
            if (line.ProductVariantId <= 0)
            {
                throw new InvalidOperationException(
                    $"Dòng {line.LineNo} có ProductVariant không hợp lệ.");
            }

            if (line.BaseQuantity <= 0)
            {
                throw new InvalidOperationException(
                    $"Dòng {line.LineNo} có số lượng quy đổi không hợp lệ.");
            }

            if (line.UnitCost <= 0)
            {
                throw new InvalidOperationException(
                    $"Dòng {line.LineNo} chưa có đơn giá nhập hợp lệ.");
            }
        }

        await _stockDocumentRepository.BeginTransactionAsync(ct);

        try
        {
            var occurredAtUtc = DateTime.UtcNow;

            foreach (var line in activeLines)
            {
                var movementRequest = _inventoryMovementFactory.CreatePurchaseReceipt(
                    warehouseId: document.WarehouseId,
                    productVariantId: line.ProductVariantId,
                    qtyBase: line.BaseQuantity,
                    unitCost: line.UnitCost,
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

            document.Status = StockDocumentStatus.Confirmed;
            document.ApprovedAtUtc = occurredAtUtc;
            document.ApprovedByUserId = null;
            document.ConfirmedAtUtc = occurredAtUtc;
            document.ConfirmedByUserId = null;
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

    public async Task RejectAsync(int documentId, string? approvalNote = null, CancellationToken ct = default)
    {
        var document = await _stockDocumentRepository.GetByIdAsync(documentId, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu nhập kho không tồn tại.");

        if (document.Status != StockDocumentStatus.PendingApproval)
            throw new InvalidOperationException("Chỉ phiếu đang chờ duyệt mới được từ chối.");

        document.Status = StockDocumentStatus.Rejected;
        document.ApprovalNote = approvalNote;

        await _stockDocumentRepository.SaveChangesAsync(ct);
    }

    private void EnsureEditable(StockDocumentStatus status)
    {
        if (status != StockDocumentStatus.Draft &&
            status != StockDocumentStatus.PendingApproval &&
            status != StockDocumentStatus.Rejected)
        {
            throw new InvalidOperationException("Phiếu hiện tại không được phép chỉnh sửa.");
        }
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
            throw new InvalidOperationException("Không tìm thấy phiếu nhập kho.");

        EnsureEditable(document.Status);

        if (!request.WarehouseId.HasValue || request.WarehouseId.Value <= 0)
            throw new InvalidOperationException("Vui lòng chọn kho.");

        var warehouseExists = await _stockDocumentRepository.WarehouseExistsAsync(request.WarehouseId.Value, ct);
        if (!warehouseExists)
            throw new InvalidOperationException("Kho không tồn tại.");

        if (request.SupplierId.HasValue)
        {
            var supplierExists = await _stockDocumentRepository.SupplierExistsAsync(request.SupplierId.Value, ct);
            if (!supplierExists)
                throw new InvalidOperationException("Nhà cung cấp không tồn tại.");
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

    private static string BuildReceiptDocumentNo(DateTime documentDate, int sequence)
    {
        return $"NK-{documentDate:yyyyMMdd}-{sequence:D4}";
    }
}