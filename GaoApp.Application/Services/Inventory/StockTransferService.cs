using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Service nghiệp vụ chuyển kho.
///
/// Rule phase hiện tại:
/// - Draft / Rejected / PendingApproval đều cho sửa
/// - Confirmed khóa toàn bộ
/// - Chỉ Confirm mới sinh InventoryTransaction
/// - Confirm chuyển kho luôn ghi 2 movement độc lập:
///   + TransferOut ở kho nguồn
///   + TransferIn ở kho đích
/// - Nếu phiếu Confirmed bị sai thì không sửa transaction cũ,
///   mà phải xử lý bằng reverse/adjustment ở phase sau
///
/// Kiến trúc chuẩn:
/// - Service nghiệp vụ quyết định flow
/// - InventoryMovementFactory build request
/// - InventoryMovementService ghi balance + ledger
///
/// Ghi chú kiến trúc barcode:
/// - ProductVariant không còn Barcode
/// - BarcodeSnapshot của line chuyển kho sẽ được resolve từ:
///   ProductUnitConversion + ProductVariantUnitBarcode
/// </summary>
public class StockTransferService : IStockTransferService
{
    private readonly IStockTransferRepository _stockTransferRepository;
    private readonly IInventoryMovementFactory _inventoryMovementFactory;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly IInventoryUnitResolver _inventoryUnitResolver;

    public StockTransferService(
        IStockTransferRepository stockTransferRepository,
        IInventoryMovementFactory inventoryMovementFactory,
        IInventoryMovementService inventoryMovementService,
        IInventoryUnitResolver inventoryUnitResolver)
    {
        _stockTransferRepository = stockTransferRepository;
        _inventoryMovementFactory = inventoryMovementFactory;
        _inventoryMovementService = inventoryMovementService;
        _inventoryUnitResolver = inventoryUnitResolver;
    }

    public async Task<PagedResult<StockTransferDocumentListItemDto>> GetListAsync(
        int page,
        int pageSize,
        string? keyword,
        int? fromWarehouseId,
        int? toWarehouseId,
        int? status,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct = default)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 20 : pageSize;

        var (items, total) = await _stockTransferRepository.GetListAsync(
            page, pageSize, keyword, fromWarehouseId, toWarehouseId, status, fromDate, toDate, ct);

        var result = items.Select(x => new StockTransferDocumentListItemDto
        {
            Id = x.Id,
            DocumentNo = x.DocumentNo,
            DocumentDate = x.DocumentDate,
            FromWarehouseId = x.FromWarehouseId,
            FromWarehouseName = x.FromWarehouse?.Name ?? string.Empty,
            ToWarehouseId = x.ToWarehouseId,
            ToWarehouseName = x.ToWarehouse?.Name ?? string.Empty,
            Status = x.Status,
            TotalLines = x.Lines?.Count ?? 0,
            Note = x.Note
        }).ToList();

        return new PagedResult<StockTransferDocumentListItemDto>(page, pageSize, total, result);
    }

    public async Task<int> CreateAsync(CreateStockTransferDocumentRequest request, CancellationToken ct = default)
    {
        ValidateWarehouses(request.FromWarehouseId, request.ToWarehouseId);

        if (!await _stockTransferRepository.WarehouseExistsAsync(request.FromWarehouseId, ct))
            throw new InvalidOperationException("Kho nguồn không tồn tại.");

        if (!await _stockTransferRepository.WarehouseExistsAsync(request.ToWarehouseId, ct))
            throw new InvalidOperationException("Kho đích không tồn tại.");

        var documentNo = await GenerateDocumentNoAsync(request.DocumentDate, ct);

        var document = new StockTransferDocument
        {
            DocumentNo = documentNo,
            DocumentDate = request.DocumentDate.Date,
            FromWarehouseId = request.FromWarehouseId,
            ToWarehouseId = request.ToWarehouseId,
            Status = StockTransferDocumentStatus.Draft,
            Note = request.Note?.Trim()
        };

        await _stockTransferRepository.AddAsync(document, ct);
        await _stockTransferRepository.SaveChangesAsync(ct);

        return document.Id;
    }

    public async Task<StockTransferDocumentDto?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        var document = await _stockTransferRepository.GetDetailAsync(id, ct);
        if (document == null)
            return null;

        return new StockTransferDocumentDto
        {
            Id = document.Id,
            DocumentNo = document.DocumentNo,
            DocumentDate = document.DocumentDate,
            FromWarehouseId = document.FromWarehouseId,
            FromWarehouseName = document.FromWarehouse?.Name ?? string.Empty,
            ToWarehouseId = document.ToWarehouseId,
            ToWarehouseName = document.ToWarehouse?.Name ?? string.Empty,
            Status = document.Status,
            Note = document.Note,
            SubmittedAtUtc = document.SubmittedAtUtc,
            SubmittedByUserId = document.SubmittedByUserId,
            ApprovedAtUtc = document.ApprovedAtUtc,
            ApprovedByUserId = document.ApprovedByUserId,
            ConfirmedAtUtc = document.ConfirmedAtUtc,
            ConfirmedByUserId = document.ConfirmedByUserId,
            TotalLines = document.Lines.Count,
            Lines = document.Lines
                .OrderBy(x => x.LineNo)
                .Select(x => new StockTransferLineDto
                {
                    Id = x.Id,
                    LineNo = x.LineNo,
                    ProductVariantId = x.ProductVariantId,
                    UnitId = x.UnitId,
                    ImageUrl = BuildImageUrl(x.ProductVariant),
                    UnitNameSnapshot = x.UnitNameSnapshot,
                    Factor = x.Factor,
                    Quantity = x.Quantity,
                    BaseQuantity = x.BaseQuantity,
                    ProductNameSnapshot = x.ProductNameSnapshot,
                    SkuSnapshot = x.SkuSnapshot,
                    BarcodeSnapshot = x.BarcodeSnapshot,
                    Note = x.Note
                })
                .ToList()
        };
    }
    private static string? BuildImageUrl(ProductVariant? variant)
    {
        var product = variant?.Product;

        if (product?.ProductImages == null || !product.ProductImages.Any())
            return null;

        var image = product.ProductImages
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .FirstOrDefault();

        var storagePath = image?.MediaAsset?.StoragePath;

        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        storagePath = storagePath.Trim();

        if (storagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            storagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return storagePath;
        }

        return "/" + storagePath.TrimStart('/');
    }
    public async Task UpdateHeaderAsync(int id, UpdateStockTransferHeaderRequest request, CancellationToken ct = default)
    {
        var document = await _stockTransferRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Phiếu chuyển kho không tồn tại.");

        EnsureEditable(document.Status);
        ValidateWarehouses(request.FromWarehouseId, request.ToWarehouseId);

        if (!await _stockTransferRepository.WarehouseExistsAsync(request.FromWarehouseId, ct))
            throw new InvalidOperationException("Kho nguồn không tồn tại.");

        if (!await _stockTransferRepository.WarehouseExistsAsync(request.ToWarehouseId, ct))
            throw new InvalidOperationException("Kho đích không tồn tại.");

        document.DocumentDate = request.DocumentDate.Date;
        document.FromWarehouseId = request.FromWarehouseId;
        document.ToWarehouseId = request.ToWarehouseId;
        document.Note = request.Note?.Trim();

        await _stockTransferRepository.SaveChangesAsync(ct);
    }

    public async Task<int> AddLineAsync(int documentId, AddStockTransferLineRequest request, CancellationToken ct = default)
    {
        var document = await _stockTransferRepository.GetDetailAsync(documentId, ct)
            ?? throw new InvalidOperationException("Phiếu chuyển kho không tồn tại.");

        EnsureEditable(document.Status);

        var variant = await _stockTransferRepository.GetVariantForTransferAsync(request.ProductVariantId, ct)
            ?? throw new InvalidOperationException("Sản phẩm không tồn tại.");

        var resolvedUnit = await _inventoryUnitResolver.ResolveAsync(
            request.ProductVariantId,
            request.UnitId,
            ct);

        var factor = resolvedUnit.Factor <= 0 ? 1m : resolvedUnit.Factor;
        var existingLine = document.Lines.FirstOrDefault(x =>
    x.ProductVariantId == request.ProductVariantId &&
    x.UnitId == resolvedUnit.UnitId);

        if (existingLine != null)
        {
            existingLine.Quantity += request.Quantity;
            existingLine.Factor = factor;
            existingLine.BaseQuantity = existingLine.Quantity * factor;
            existingLine.UnitNameSnapshot = resolvedUnit.UnitName ?? string.Empty;
            existingLine.BarcodeSnapshot = ResolveBarcodeSnapshot(variant, resolvedUnit.UnitId);

            var incomingNote = request.Note?.Trim();

            if (!string.IsNullOrWhiteSpace(incomingNote))
            {
                if (incomingNote.StartsWith("Quét mã:", StringComparison.OrdinalIgnoreCase))
                {
                    existingLine.Note = incomingNote;
                }
                else
                {
                    existingLine.Note = string.IsNullOrWhiteSpace(existingLine.Note)
                        ? incomingNote
                        : $"{existingLine.Note} | {incomingNote}";
                }
            }

            await _stockTransferRepository.SaveChangesAsync(ct);
            return existingLine.Id;
        }
        // QUAN TRỌNG:
        // Không tự lấy từ document.Lines vì có thể đang bị query filter / soft delete che mất.
        // Phải lấy số dòng kế tiếp từ repository để bám đúng dữ liệu DB thực tế.
        var newLineNo = await _stockTransferRepository.GetNextLineNoAsync(document.Id, ct);

        var line = new StockTransferLine
        {
            StockTransferDocumentId = document.Id,
            LineNo = newLineNo,

            ProductVariantId = request.ProductVariantId,
            UnitId = resolvedUnit.UnitId,
            UnitNameSnapshot = resolvedUnit.UnitName ?? string.Empty,
            Factor = factor,
            Quantity = request.Quantity,
            BaseQuantity = request.Quantity * factor,

            ProductNameSnapshot = variant.Product?.Name ?? $"Variant #{variant.Id}",
            SkuSnapshot = variant.Sku,
            BarcodeSnapshot = ResolveBarcodeSnapshot(variant, resolvedUnit.UnitId),

            Note = request.Note?.Trim()
        };

        document.Lines.Add(line);
        await _stockTransferRepository.SaveChangesAsync(ct);

        return line.Id;
    }

    public async Task UpdateLineAsync(int lineId, UpdateStockTransferLineRequest request, CancellationToken ct = default)
    {
        var line = await _stockTransferRepository.GetLineByIdAsync(lineId, ct)
            ?? throw new InvalidOperationException("Dòng chuyển kho không tồn tại.");

        EnsureEditable(line.StockTransferDocument.Status);

        var resolvedUnit = await _inventoryUnitResolver.ResolveAsync(
            line.ProductVariantId,
            request.UnitId,
            ct);

        var factor = resolvedUnit.Factor <= 0 ? 1m : resolvedUnit.Factor;

        line.UnitId = resolvedUnit.UnitId;
        line.UnitNameSnapshot = resolvedUnit.UnitName ?? string.Empty;
        line.Factor = factor;
        line.Quantity = request.Quantity;
        line.BaseQuantity = request.Quantity * factor;
        line.Note = request.Note?.Trim();

        // Nếu đổi đơn vị thì refresh lại barcode snapshot tương ứng
        if (line.ProductVariant != null)
        {
            line.BarcodeSnapshot = ResolveBarcodeSnapshot(line.ProductVariant, resolvedUnit.UnitId);
        }

        await _stockTransferRepository.SaveChangesAsync(ct);
    }

    public async Task DeleteLineAsync(int lineId, CancellationToken ct = default)
    {
        var line = await _stockTransferRepository.GetLineByIdAsync(lineId, ct)
            ?? throw new InvalidOperationException("Dòng chuyển kho không tồn tại.");

        EnsureEditable(line.StockTransferDocument.Status);

        await _stockTransferRepository.RemoveLineAsync(line, ct);
        await _stockTransferRepository.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(int id, CancellationToken ct = default)
    {
        var document = await _stockTransferRepository.GetDetailAsync(id, ct)
            ?? throw new InvalidOperationException("Phiếu chuyển kho không tồn tại.");

        if (document.Status != StockTransferDocumentStatus.Draft &&
            document.Status != StockTransferDocumentStatus.Rejected)
        {
            throw new InvalidOperationException("Chỉ phiếu Draft hoặc Rejected mới được submit.");
        }

        if (document.Lines.Count == 0)
            throw new InvalidOperationException("Phiếu chuyển kho chưa có dòng hàng.");

        document.Status = StockTransferDocumentStatus.PendingApproval;
        document.SubmittedAtUtc = DateTime.UtcNow;
        document.SubmittedByUserId = null; // TODO: thay bằng current user

        await _stockTransferRepository.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(int id, string? reason, CancellationToken ct = default)
    {
        var document = await _stockTransferRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Phiếu chuyển kho không tồn tại.");

        if (document.Status != StockTransferDocumentStatus.PendingApproval)
            throw new InvalidOperationException("Chỉ phiếu PendingApproval mới được reject.");

        document.Status = StockTransferDocumentStatus.Rejected;

        if (!string.IsNullOrWhiteSpace(reason))
        {
            var trimmedReason = reason.Trim();
            document.Note = string.IsNullOrWhiteSpace(document.Note)
                ? $"Từ chối duyệt: {trimmedReason}"
                : $"{document.Note}{Environment.NewLine}Từ chối duyệt: {trimmedReason}";
        }

        await _stockTransferRepository.SaveChangesAsync(ct);
    }

    public async Task ConfirmAsync(int id, CancellationToken ct = default)
    {
        var document = await _stockTransferRepository.GetDetailAsync(id, ct)
            ?? throw new InvalidOperationException("Phiếu chuyển kho không tồn tại.");

        if (document.Status != StockTransferDocumentStatus.PendingApproval)
            throw new InvalidOperationException("Chỉ phiếu PendingApproval mới được confirm.");

        if (document.Lines.Count == 0)
            throw new InvalidOperationException("Phiếu chuyển kho chưa có dòng hàng.");

        await _stockTransferRepository.BeginTransactionAsync(ct);

        try
        {
            foreach (var line in document.Lines.OrderBy(x => x.LineNo))
            {
                // =====================================================
                // 🔥 FIX QUAN TRỌNG NHẤT
                // Nếu chưa có cost snapshot → lấy từ tồn kho hiện tại
                // =====================================================
                if (line.UnitCostSnapshot <= 0)
                {
                    var resolved = await _inventoryMovementService.PeekOutboundUnitCostAsync(
                        document.FromWarehouseId,
                        line.ProductVariantId,
                        line.BaseQuantity,
                        ct);

                    if (resolved <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Không xác định được giá vốn cho dòng #{line.LineNo} (variant {line.ProductVariantId}).");
                    }

                    line.UnitCostSnapshot = resolved;
                }

                var unitCost = line.UnitCostSnapshot;

                // =====================================================
                // Transfer OUT (xuất kho nguồn)
                // =====================================================
                var transferOutRequest = _inventoryMovementFactory.CreateTransferOutRequest(
                    document.FromWarehouseId,
                    line.ProductVariantId,
                    document.Id.ToString(),
                    line.Id,
                    document.DocumentNo,
                    line.LineNo,
                    line.BaseQuantity,
                    unitCost,
                    DateTime.UtcNow);

                await _inventoryMovementService.CreateAsync(transferOutRequest, ct);

                // =====================================================
                // Transfer IN (nhập kho đích)
                // =====================================================
                var transferInRequest = _inventoryMovementFactory.CreateTransferInRequest(
                    document.ToWarehouseId,
                    line.ProductVariantId,
                    document.Id.ToString(),
                    line.Id,
                    document.DocumentNo,
                    line.LineNo,
                    line.BaseQuantity,
                    unitCost,
                    DateTime.UtcNow);

                await _inventoryMovementService.CreateAsync(transferInRequest, ct);
            }

            document.Status = StockTransferDocumentStatus.Confirmed;
            document.ConfirmedAtUtc = DateTime.UtcNow;
            document.ConfirmedByUserId = null;

            await _stockTransferRepository.SaveChangesAsync(ct);
            await _stockTransferRepository.CommitTransactionAsync(ct);
        }
        catch
        {
            await _stockTransferRepository.RollbackTransactionAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Draft / Rejected / PendingApproval đều cho sửa.
    /// Confirmed khóa toàn bộ.
    /// </summary>
    private static void EnsureEditable(StockTransferDocumentStatus status)
    {
        if (status == StockTransferDocumentStatus.Confirmed)
            throw new InvalidOperationException("Phiếu chuyển kho đã xác nhận, không được sửa.");
    }

    /// <summary>
    /// Validate kho nguồn/kho đích.
    /// </summary>
    private static void ValidateWarehouses(int fromWarehouseId, int toWarehouseId)
    {
        if (fromWarehouseId <= 0)
            throw new InvalidOperationException("Kho nguồn không hợp lệ.");

        if (toWarehouseId <= 0)
            throw new InvalidOperationException("Kho đích không hợp lệ.");

        if (fromWarehouseId == toWarehouseId)
            throw new InvalidOperationException("Kho nguồn và kho đích không được trùng nhau.");
    }

    /// <summary>
    /// Resolve barcode snapshot cho line chuyển kho.
    ///
    /// Thứ tự ưu tiên:
    /// 1) barcode primary của đúng đơn vị đang chuyển
    /// 2) barcode primary của đơn vị mặc định bán
    /// 3) barcode primary của đơn vị gốc
    /// 4) barcode active đầu tiên còn lại
    ///
    /// Đây là barcode snapshot để hiển thị / đối chiếu tại thời điểm tạo dòng,
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

        // 1) Ưu tiên đúng đơn vị đang chọn
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

        // 2) Đơn vị mặc định bán
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

        // 3) Đơn vị gốc
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

        // 4) Bất kỳ barcode active đầu tiên còn lại
        var fallbackBarcode = activeConversions
            .SelectMany(c => c.Barcodes ?? Enumerable.Empty<ProductVariantUnitBarcode>())
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .Select(b => b.Barcode)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(fallbackBarcode) ? null : fallbackBarcode;
    }

    /// <summary>
    /// Sinh số phiếu theo format:
    /// CK-yyyyMMdd-0001
    /// </summary>
    private async Task<string> GenerateDocumentNoAsync(DateTime documentDate, CancellationToken ct)
    {
        var lastNo = await _stockTransferRepository.GetLastDocumentNoByDateAsync(documentDate, ct);
        var nextNumber = 1;

        if (!string.IsNullOrWhiteSpace(lastNo))
        {
            var parts = lastNo.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out var lastSeq))
            {
                nextNumber = lastSeq + 1;
            }
        }

        return $"CK-{documentDate:yyyyMMdd}-{nextNumber:D4}";
    }
}