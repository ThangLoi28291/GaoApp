using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// CRUD phiếu kiểm kê.
///
/// Phase 5.8.2:
/// - tạo phiếu
/// - sửa header
/// - thêm / sửa / xóa dòng
/// - xem danh sách / chi tiết
///
/// Phase 5.11:
/// - bổ sung summary cho màn hình chi tiết phiếu kiểm kê
///   gồm:
///   + tổng line
///   + số line lệch
///   + số line tăng
///   + số line giảm
///   + tổng tăng theo đơn vị gốc
///   + tổng giảm theo đơn vị gốc
///
/// NOTE:
/// Phase hiện tại chưa triển khai phân quyền.
/// Tạm thời cho phép chỉnh sửa chứng từ ở trạng thái:
/// - Draft
/// - PendingApproval
/// - Rejected
/// Khi hoàn thiện phân quyền:
/// - PendingApproval chỉ cho Manager/Admin sửa
/// - Confirmed khóa hoàn toàn
///
/// Ghi chú kiến trúc barcode:
/// - ProductVariant không còn Barcode
/// - BarcodeSnapshot của line kiểm kê sẽ được resolve từ:
///   ProductUnitConversion + ProductVariantUnitBarcode
/// </summary>
public class StockCountService : IStockCountService
{
    private readonly IStockCountRepository _stockCountRepository;
    private readonly IInventoryUnitResolver _inventoryUnitResolver;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly IInventoryMovementFactory _inventoryMovementFactory;

    public StockCountService(
        IStockCountRepository stockCountRepository,
        IInventoryUnitResolver inventoryUnitResolver,
        IInventoryMovementService inventoryMovementService,
        IInventoryMovementFactory inventoryMovementFactory)
    {
        _stockCountRepository = stockCountRepository;
        _inventoryUnitResolver = inventoryUnitResolver;
        _inventoryMovementService = inventoryMovementService;
        _inventoryMovementFactory = inventoryMovementFactory;
    }

    public async Task<List<StockCountDocumentListItemDto>> GetListAsync(CancellationToken ct = default)
    {
        var documents = await _stockCountRepository.GetListAsync(ct);

        return documents.Select(x => new StockCountDocumentListItemDto
        {
            Id = x.Id,
            DocumentNo = x.DocumentNo,
            DocumentDate = x.DocumentDate,
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse?.Name ?? string.Empty,
            Status = x.Status,
            Note = x.Note,
            ConfirmedAtUtc = x.ConfirmedAtUtc,
            TotalLines = x.Lines.Count
        }).ToList();
    }

    public async Task<int> CreateAsync(CreateStockCountDocumentRequest request, CancellationToken ct = default)
    {
        var warehouseExists = await _stockCountRepository.WarehouseExistsAsync(request.WarehouseId, ct);
        if (!warehouseExists)
            throw new InvalidOperationException("Kho không tồn tại.");

        var document = new StockCountDocument
        {
            WarehouseId = request.WarehouseId,
            DocumentNo = await GenerateDocumentNoAsync(ct),
            DocumentDate = request.DocumentDate ?? DateTime.Now,
            Status = StockCountDocumentStatus.Draft,
            Note = request.Note?.Trim()
        };

        await _stockCountRepository.AddAsync(document, ct);

        try
        {
            await _stockCountRepository.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Không tạo được phiếu kiểm kê do trùng số phiếu. Vui lòng thử lại.", ex);
        }

        return document.Id;
    }

    /// <summary>
    /// Lấy chi tiết phiếu kiểm kê.
    /// Sau khi map xong danh sách line sẽ tính thêm summary để UI hiển thị nhanh.
    /// </summary>
    public async Task<StockCountDocumentDto?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        var document = await _stockCountRepository.GetDetailAsync(id, ct);
        if (document == null)
            return null;

        var dto = new StockCountDocumentDto
        {
            Id = document.Id,
            DocumentNo = document.DocumentNo,
            DocumentDate = document.DocumentDate,
            WarehouseId = document.WarehouseId,
            WarehouseName = document.Warehouse?.Name ?? string.Empty,
            Status = document.Status,
            Note = document.Note,
            ConfirmedAtUtc = document.ConfirmedAtUtc,
            ConfirmedByUserId = document.ConfirmedByUserId,
            Lines = document.Lines
                .OrderBy(x => x.LineNo)
              .Select(x => new StockCountLineDto
              {
                  Id = x.Id,
                  LineNo = x.LineNo,

                  ProductVariantId = x.ProductVariantId,

                  UnitId = x.UnitId,
                  UnitName = x.UnitNameSnapshot,

                  Factor = x.Factor,

                  SystemQtyBase = x.SystemQtyBase,

                  CountedQty = x.CountedQty,
                  CountedQtyBase = x.CountedQtyBase,

                  DifferenceQtyBase = x.DifferenceQtyBase,

                  ProductNameSnapshot = x.ProductNameSnapshot,
                  SkuSnapshot = x.SkuSnapshot,
                  BarcodeSnapshot = x.BarcodeSnapshot,

                  // NEW
                  ImageUrl = BuildImageUrl(x.ProductVariant),

                  Note = x.Note
              })
                .ToList()
        };

        ApplySummary(dto);
        return dto;
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
    public async Task UpdateHeaderAsync(UpdateStockCountDocumentHeaderRequest request, CancellationToken ct = default)
    {
        var document = await _stockCountRepository.GetByIdAsync(request.StockCountDocumentId, ct);
        if (document == null)
            throw new InvalidOperationException("Không tìm thấy phiếu kiểm kê.");

        EnsureEditable(document.Status);

        if (!request.WarehouseId.HasValue || request.WarehouseId.Value <= 0)
            throw new InvalidOperationException("Vui lòng chọn kho.");

        var warehouseExists = await _stockCountRepository.WarehouseExistsAsync(request.WarehouseId.Value, ct);
        if (!warehouseExists)
            throw new InvalidOperationException("Kho không tồn tại.");

        document.WarehouseId = request.WarehouseId.Value;
        document.DocumentDate = request.DocumentDate ?? document.DocumentDate;
        document.Note = request.Note?.Trim();

        await _stockCountRepository.SaveChangesAsync(ct);
    }

    public async Task<int> AddLineAsync(int stockCountDocumentId, AddStockCountLineRequest request, CancellationToken ct = default)
    {
        var document = await _stockCountRepository.GetDetailAsync(stockCountDocumentId, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu kiểm kê không tồn tại.");

        EnsureEditable(document.Status);

        var variant = await _stockCountRepository.GetVariantForStockCountAsync(request.ProductVariantId, ct);
        if (variant == null)
            throw new InvalidOperationException("Sản phẩm không tồn tại.");

        var unitInfo = await _inventoryUnitResolver.ResolveAsync(
            request.ProductVariantId,
            request.UnitId,
            ct);

        var factor = unitInfo.Factor <= 0 ? 1m : unitInfo.Factor;

        var balance = await _stockCountRepository.GetInventoryBalanceAsync(
            document.WarehouseId,
            request.ProductVariantId,
            ct);

        var systemQtyBase = balance?.OnHandQty ?? 0m;

        // Nếu đã có line cùng variant + unit thì cộng dồn vào line cũ
        // để tránh trùng dòng không cần thiết.
        var existingLine = await _stockCountRepository.FindExistingEditableLineAsync(
            document.Id,
            request.ProductVariantId,
            unitInfo.UnitId,
            ct);

        if (existingLine != null)
        {
            existingLine.CountedQty += request.CountedQty;
            existingLine.UnitNameSnapshot = unitInfo.UnitName;
            existingLine.Factor = factor;
            existingLine.SystemQtyBase = systemQtyBase;
            existingLine.CountedQtyBase = existingLine.CountedQty * factor;
            existingLine.DifferenceQtyBase = existingLine.CountedQtyBase - systemQtyBase;

            // Cập nhật lại barcode snapshot để phản ánh đơn vị hiện tại rõ hơn
            existingLine.BarcodeSnapshot = ResolveBarcodeSnapshot(variant, unitInfo.UnitId);

            var incomingNote = request.Note?.Trim();

            // NOTE RULE:
            // - Nếu là note quét mã -> chỉ giữ 1 note chuẩn, không nối lặp
            // - Nếu là note thường -> nối thêm để giữ lịch sử nhập
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

            await _stockCountRepository.SaveChangesAsync(ct);
            return existingLine.Id;
        }

        var countedQty = request.CountedQty;
        var countedQtyBase = countedQty * factor;
        var differenceQtyBase = countedQtyBase - systemQtyBase;

        var line = new StockCountLine
        {
            StockCountDocumentId = document.Id,
            LineNo = await _stockCountRepository.GetNextLineNoAsync(document.Id, ct),
            ProductVariantId = request.ProductVariantId,
            UnitId = unitInfo.UnitId,
            UnitNameSnapshot = unitInfo.UnitName,
            Factor = factor,
            SystemQtyBase = systemQtyBase,
            CountedQty = countedQty,
            CountedQtyBase = countedQtyBase,
            DifferenceQtyBase = differenceQtyBase,
            ProductNameSnapshot = variant.Product?.Name ?? $"Variant #{variant.Id}",
            SkuSnapshot = variant.Sku,

            // Không còn dùng variant.Barcode.
            // Snapshot barcode được resolve từ conversion/barcode entity.
            BarcodeSnapshot = ResolveBarcodeSnapshot(variant, unitInfo.UnitId),

            Note = request.Note?.Trim()
        };

        document.Lines.Add(line);
        await _stockCountRepository.SaveChangesAsync(ct);

        return line.Id;
    }

    public async Task UpdateLineAsync(int lineId, UpdateStockCountLineRequest request, CancellationToken ct = default)
    {
        var line = await _stockCountRepository.GetLineByIdAsync(lineId, ct);
        if (line == null)
            throw new InvalidOperationException("Dòng kiểm kê không tồn tại.");

        EnsureEditable(line.StockCountDocument.Status);

        var unitInfo = await _inventoryUnitResolver.ResolveAsync(
            line.ProductVariantId,
            request.UnitId,
            ct);

        var factor = unitInfo.Factor <= 0 ? 1m : unitInfo.Factor;
        var countedQty = request.CountedQty;
        var countedQtyBase = countedQty * factor;
        var differenceQtyBase = countedQtyBase - line.SystemQtyBase;

        line.UnitId = unitInfo.UnitId;
        line.UnitNameSnapshot = unitInfo.UnitName;
        line.Factor = factor;
        line.CountedQty = countedQty;
        line.CountedQtyBase = countedQtyBase;
        line.DifferenceQtyBase = differenceQtyBase;
        line.Note = request.Note?.Trim();

        // Nếu đổi đơn vị khi sửa line thì cũng refresh lại barcode snapshot tương ứng
        if (line.ProductVariant != null)
        {
            line.BarcodeSnapshot = ResolveBarcodeSnapshot(line.ProductVariant, unitInfo.UnitId);
        }

        await _stockCountRepository.SaveChangesAsync(ct);
    }

    public async Task DeleteLineAsync(int lineId, CancellationToken ct = default)
    {
        var line = await _stockCountRepository.GetLineByIdAsync(lineId, ct);
        if (line == null)
            throw new InvalidOperationException("Dòng kiểm kê không tồn tại.");

        EnsureEditable(line.StockCountDocument.Status);

        await _stockCountRepository.RemoveLineAsync(line, ct);
        await _stockCountRepository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Rule chỉnh sửa hiện tại:
    /// - Draft: được sửa
    /// - Rejected: được sửa
    /// - PendingApproval: hiện đang khóa theo code hiện tại
    /// - Confirmed: khóa hoàn toàn
    ///
    /// Nếu sau này muốn mở PendingApproval theo phân quyền,
    /// chỉ cần sửa rule tại đây.
    /// </summary>
    private static void EnsureEditable(StockCountDocumentStatus status)
    {
        if (status != StockCountDocumentStatus.Draft &&
            status != StockCountDocumentStatus.Rejected)
        {
            throw new InvalidOperationException("Chỉ phiếu kiểm kê ở trạng thái Draft hoặc Rejected mới được chỉnh sửa.");
        }
    }

    private async Task<string> GenerateDocumentNoAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var prefix = $"KK-{now:yyyyMMdd}-";

        var lastDocumentNo = await _stockCountRepository.GetLastDocumentNoByDateAsync(now.Date, ct);

        var nextNumber = 1;

        if (!string.IsNullOrWhiteSpace(lastDocumentNo) &&
            lastDocumentNo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var seqText = lastDocumentNo.Substring(prefix.Length);

            if (int.TryParse(seqText, out var seq))
            {
                nextNumber = seq + 1;
            }
        }

        return $"{prefix}{nextNumber:D4}";
    }

    public async Task ConfirmAsync(int stockCountDocumentId, CancellationToken ct = default)
    {
        var document = await _stockCountRepository.GetDetailAsync(stockCountDocumentId, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu kiểm kê không tồn tại.");

        if (document.Status != StockCountDocumentStatus.PendingApproval)
            throw new InvalidOperationException("Chỉ phiếu kiểm kê đang chờ duyệt mới được xác nhận.");

        if (!document.Lines.Any())
            throw new InvalidOperationException("Phiếu kiểm kê chưa có dòng chi tiết.");

        await _stockCountRepository.BeginTransactionAsync(ct);

        try
        {
            foreach (var line in document.Lines.OrderBy(x => x.LineNo))
            {
                if (line.DifferenceQtyBase == 0)
                    continue;

                CreateInventoryMovementRequest movementRequest;

                if (line.DifferenceQtyBase > 0)
                {
                    // =========================================================
                    // Phase 5.15 mức 2:
                    // Count gain phải có cost để tạo inbound layer.
                    //
                    // Thứ tự resolve cost:
                    // 1) UnitCostSnapshot đã có trên line
                    // 2) Peek outbound cost từ costing engine hiện tại
                    // 3) LastInboundUnitCost của đúng kho
                    // 4) Variant.CostPrice làm fallback cuối cùng
                    // =========================================================
                    if (line.UnitCostSnapshot <= 0)
                    {
                        decimal resolvedCost = 0m;

                        // 1) thử peek từ costing engine hiện tại
                        resolvedCost = await _inventoryMovementService.PeekOutboundUnitCostAsync(
                            document.WarehouseId,
                            line.ProductVariantId,
                            line.DifferenceQtyBase,
                            ct);

                        // 2) fallback LastInboundUnitCost của đúng warehouse
                        if (resolvedCost <= 0)
                        {
                            var balance = await _stockCountRepository.GetInventoryBalanceAsync(
                                document.WarehouseId,
                                line.ProductVariantId,
                                ct);

                            if (balance?.LastInboundUnitCost.HasValue == true &&
                                balance.LastInboundUnitCost.Value > 0)
                            {
                                resolvedCost = balance.LastInboundUnitCost.Value;
                            }
                        }

                        // 3) fallback cuối cùng: CostPrice nền của variant
                        if (resolvedCost <= 0)
                        {
                            var variant = await _stockCountRepository.GetVariantForStockCountAsync(
                                line.ProductVariantId,
                                ct);

                            if (variant?.CostPrice > 0)
                            {
                                resolvedCost = variant.CostPrice;
                            }
                        }

                        if (resolvedCost <= 0)
                        {
                            throw new InvalidOperationException(
                                $"Không xác định được giá vốn cho dòng kiểm kê #{line.LineNo} (variant {line.ProductVariantId}).");
                        }

                        line.UnitCostSnapshot = resolvedCost;
                    }

                    var unitCost = line.UnitCostSnapshot;

                    movementRequest = _inventoryMovementFactory.CreateStockCountGain(
                        document.WarehouseId,
                        line.ProductVariantId,
                        document.Id.ToString(),
                        line.Id,
                        document.DocumentNo,
                        line.LineNo,
                        line.DifferenceQtyBase,
                        unitCost,
                        DateTime.UtcNow);
                }
                else
                {
                    // loss phải truyền trị tuyệt đối
                    var lossQty = Math.Abs(line.DifferenceQtyBase);

                    decimal? provisionalUnitCost = line.UnitCostSnapshot > 0
                        ? line.UnitCostSnapshot
                        : null;

                    movementRequest = _inventoryMovementFactory.CreateStockCountLoss(
                        document.WarehouseId,
                        line.ProductVariantId,
                        document.Id.ToString(),
                        line.Id,
                        document.DocumentNo,
                        line.LineNo,
                        lossQty,
                        provisionalUnitCost,
                        DateTime.UtcNow);
                }

                await _inventoryMovementService.CreateAsync(movementRequest, ct);
            }

            document.Status = StockCountDocumentStatus.Confirmed;
            document.ConfirmedAtUtc = DateTime.UtcNow;
            document.ConfirmedByUserId = null;

            await _stockCountRepository.SaveChangesAsync(ct);
            await _stockCountRepository.CommitTransactionAsync(ct);
        }
        catch
        {
            await _stockCountRepository.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task SubmitForApprovalAsync(int stockCountDocumentId, CancellationToken ct = default)
    {
        var document = await _stockCountRepository.GetDetailAsync(stockCountDocumentId, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu kiểm kê không tồn tại.");

        if (document.Status != StockCountDocumentStatus.Draft &&
            document.Status != StockCountDocumentStatus.Rejected)
        {
            throw new InvalidOperationException("Chỉ phiếu Draft hoặc Rejected mới được gửi duyệt.");
        }

        if (!document.Lines.Any())
            throw new InvalidOperationException("Phiếu kiểm kê chưa có dòng chi tiết.");

        document.Status = StockCountDocumentStatus.PendingApproval;
        await _stockCountRepository.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(int stockCountDocumentId, CancellationToken ct = default)
    {
        var document = await _stockCountRepository.GetByIdAsync(stockCountDocumentId, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu kiểm kê không tồn tại.");

        if (document.Status != StockCountDocumentStatus.PendingApproval)
            throw new InvalidOperationException("Chỉ phiếu đang chờ duyệt mới được từ chối.");

        document.Status = StockCountDocumentStatus.Rejected;
        await _stockCountRepository.SaveChangesAsync(ct);
    }

    public async Task RefreshSystemQtyAsync(int stockCountDocumentId, CancellationToken ct = default)
    {
        var document = await _stockCountRepository.GetDetailAsync(stockCountDocumentId, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu kiểm kê không tồn tại.");

        EnsureEditable(document.Status);

        foreach (var line in document.Lines)
        {
            var balance = await _stockCountRepository.GetInventoryBalanceAsync(
                document.WarehouseId,
                line.ProductVariantId,
                ct);

            var systemQtyBase = balance?.OnHandQty ?? 0m;

            line.SystemQtyBase = systemQtyBase;
            line.DifferenceQtyBase = line.CountedQtyBase - systemQtyBase;
        }

        await _stockCountRepository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Resolve barcode snapshot cho line kiểm kê.
    ///
    /// Thứ tự ưu tiên:
    /// 1) barcode primary của đúng đơn vị đang kiểm kê
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

        // 1) Ưu tiên đúng đơn vị đang chọn ở line kiểm kê
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
    /// Tính summary cho phiếu kiểm kê dựa trên DifferenceQtyBase của từng line.
    /// Tất cả đều tính theo đơn vị gốc để đảm bảo cộng tổng đúng nghiệp vụ.
    /// </summary>
    private static void ApplySummary(StockCountDocumentDto dto)
    {
        var lines = dto.Lines ?? new List<StockCountLineDto>();

        // 1. Tổng số line của phiếu
        dto.TotalLines = lines.Count;

        // 2. Số line có chênh lệch
        dto.DifferenceLineCount = lines.Count(x => x.DifferenceQtyBase != 0);

        // 3. Số line tăng tồn
        dto.GainLineCount = lines.Count(x => x.DifferenceQtyBase > 0);

        // 4. Số line giảm tồn
        dto.LossLineCount = lines.Count(x => x.DifferenceQtyBase < 0);

        // 5. Tổng số lượng tăng theo đơn vị gốc
        dto.TotalGainQtyBase = lines
            .Where(x => x.DifferenceQtyBase > 0)
            .Sum(x => x.DifferenceQtyBase);

        // 6. Tổng số lượng giảm theo đơn vị gốc
        // Dùng Abs để trả về số dương cho UI/report dễ đọc.
        dto.TotalLossQtyBase = lines
            .Where(x => x.DifferenceQtyBase < 0)
            .Sum(x => Math.Abs(x.DifferenceQtyBase));
    }
}