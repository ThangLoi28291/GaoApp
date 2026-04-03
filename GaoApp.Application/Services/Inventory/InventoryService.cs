using GaoApp.Application.Common;
using GaoApp.Application.Common.Extensions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Service đọc dữ liệu kho.
///
/// CHỐT KIẾN TRÚC BARCODE:
/// - Không còn dùng ProductVariant.Barcode
/// - Nếu UI cần hiển thị barcode ở các màn tồn kho / ledger,
///   service sẽ resolve "barcode đại diện" từ:
///   1) đơn vị mặc định bán
///   2) đơn vị gốc
///   3) barcode active đầu tiên còn lại
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly IInventoryBalanceRepository _inventoryBalanceRepository;
    private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
    private readonly INegativeInventoryLogRepository _negativeInventoryLogRepository;

    public InventoryService(
        IInventoryBalanceRepository inventoryBalanceRepository,
        IInventoryTransactionRepository inventoryTransactionRepository,
        INegativeInventoryLogRepository negativeInventoryLogRepository)
    {
        _inventoryBalanceRepository = inventoryBalanceRepository;
        _inventoryTransactionRepository = inventoryTransactionRepository;
        _negativeInventoryLogRepository = negativeInventoryLogRepository;
    }

    public async Task<List<InventoryBalanceDto>> GetBalancesByVariantAsync(int productVariantId, CancellationToken ct = default)
    {
        var items = await _inventoryBalanceRepository.GetByVariantAsync(productVariantId, ct);

        return items.Select(x => new InventoryBalanceDto
        {
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse?.Name ?? string.Empty,
            ProductVariantId = x.ProductVariantId,
            ProductVariantName = x.ProductVariant.GetDisplayName(),
            OnHandQty = x.OnHandQty,
            ReservedQty = x.ReservedQty,
            AvailableQty = x.AvailableQty
        }).ToList();
    }

    public async Task<List<InventoryTransactionDto>> GetTransactionsByVariantAsync(int productVariantId, CancellationToken ct = default)
    {
        var items = await _inventoryTransactionRepository.GetByVariantAsync(productVariantId, ct);

        return items.Select(x => new InventoryTransactionDto
        {
            Id = x.Id,
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse?.Name ?? string.Empty,
            ProductVariantId = x.ProductVariantId,
            ProductVariantName = x.ProductVariant.GetDisplayName(),
            TransactionType = x.TransactionType,
            ReferenceType = x.ReferenceType,
            ReferenceId = x.ReferenceId,
            QuantityChange = x.QuantityChange,
            BeforeQty = x.BeforeQty,
            AfterQty = x.AfterQty,
            OccurredAtUtc = x.OccurredAtUtc,
            Note = x.Note
        }).ToList();
    }

    public async Task<List<NegativeInventoryItemDto>> GetNegativeBalancesAsync(CancellationToken ct = default)
    {
        var items = await _inventoryBalanceRepository.GetNegativeBalancesAsync(ct);

        return items.Select(x => new NegativeInventoryItemDto
        {
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse?.Name ?? string.Empty,
            ProductVariantId = x.ProductVariantId,
            ProductVariantName = x.ProductVariant.GetDisplayName(),
            OnHandQty = x.OnHandQty,
            ReservedQty = x.ReservedQty,
            AvailableQty = x.AvailableQty
        }).ToList();
    }

    public async Task<List<NegativeInventoryLogDto>> GetNegativeLogsAsync(CancellationToken ct = default)
    {
        var items = await _negativeInventoryLogRepository.GetAllAsync(ct);

        return items.Select(x => new NegativeInventoryLogDto
        {
            Id = x.Id,
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse?.Name ?? string.Empty,
            ProductVariantId = x.ProductVariantId,
            ProductVariantName = x.ProductVariant.GetDisplayName(),
            BeforeQty = x.BeforeQty,
            QuantityChange = x.QuantityChange,
            AfterQty = x.AfterQty,
            TransactionType = x.TransactionType,
            ReferenceType = x.ReferenceType,
            ReferenceId = x.ReferenceId,
            Note = x.Note,
            OccurredAtUtc = x.OccurredAtUtc
        }).ToList();
    }

    public async Task<List<StockAdjustmentHistoryItemDto>> GetAdjustmentHistoryAsync(CancellationToken ct = default)
    {
        var items = await _inventoryTransactionRepository.GetAdjustmentTransactionsAsync(ct);

        return items.Select(x => new StockAdjustmentHistoryItemDto
        {
            Id = x.Id,
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse?.Name ?? string.Empty,
            ProductVariantId = x.ProductVariantId,
            ProductVariantName = x.ProductVariant.GetDisplayName(),
            TransactionType = x.TransactionType,
            QuantityChange = x.QuantityChange,
            BeforeQty = x.BeforeQty,
            AfterQty = x.AfterQty,
            ReferenceId = x.ReferenceId,
            Note = x.Note,
            OccurredAtUtc = x.OccurredAtUtc
        }).ToList();
    }

    public async Task<NegativeInventoryItemDto?> GetBalanceItemAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default)
    {
        var item = await _inventoryBalanceRepository
            .GetDetailByWarehouseAndVariantAsync(warehouseId, productVariantId, ct);

        // Nếu chưa có balance thì vẫn trả dữ liệu mặc định để form hiển thị được.
        if (item == null)
        {
            return new NegativeInventoryItemDto
            {
                WarehouseId = warehouseId,
                ProductVariantId = productVariantId,
                ProductVariantName = string.Empty,
                OnHandQty = 0,
                ReservedQty = 0,
                AvailableQty = 0,
                AllowNegativeInventory = false
            };
        }

        return new NegativeInventoryItemDto
        {
            WarehouseId = item.WarehouseId,
            WarehouseName = item.Warehouse?.Name ?? string.Empty,
            AllowNegativeInventory = item.Warehouse?.AllowNegativeInventory ?? false,
            ProductVariantId = item.ProductVariantId,
            ProductVariantName = item.ProductVariant.GetDisplayName(),
            OnHandQty = item.OnHandQty,
            ReservedQty = item.ReservedQty,
            AvailableQty = item.AvailableQty
        };
    }

    /// <summary>
    /// Lấy danh sách tồn kho hiện tại có filter, sort và phân trang.
    /// </summary>
    public async Task<PagedResult<InventoryBalanceListItemDto>> GetCurrentBalancesAsync(
        InventoryBalanceQueryRequest request,
        CancellationToken ct = default)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        // Chuẩn hóa request trước khi xuống repository.
        request.Page = page;
        request.PageSize = pageSize;

        request.Keyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim();

        request.SortBy = string.IsNullOrWhiteSpace(request.SortBy)
            ? "ProductName"
            : request.SortBy.Trim();

        request.SortDirection = string.IsNullOrWhiteSpace(request.SortDirection)
            ? "asc"
            : request.SortDirection.Trim();

        var (items, totalItems) = await _inventoryBalanceRepository.QueryCurrentBalancesAsync(request, ct);

        var dtoItems = items.Select(x => new InventoryBalanceListItemDto
        {
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse?.Name ?? string.Empty,
            ProductVariantId = x.ProductVariantId,
            ProductVariantName = x.ProductVariant.GetDisplayName(),
            Sku = x.ProductVariant.Sku,

            // Không còn dùng ProductVariant.Barcode.
            // Lấy barcode đại diện từ danh sách conversion + barcode.
            Barcode = ResolveRepresentativeBarcode(x.ProductVariant),

            OnHandQty = x.OnHandQty,
            ReservedQty = x.ReservedQty,
            AvailableQty = x.AvailableQty,
            IsNegative = x.OnHandQty < 0
        }).ToList();

        return new PagedResult<InventoryBalanceListItemDto>
        {
            Items = dtoItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };
    }

    /// <summary>
    /// Lấy thẻ kho / ledger kho có filter, sort và phân trang.
    /// Repository đã nhận trực tiếp request nên service chỉ cần:
    /// - chuẩn hóa dữ liệu đầu vào
    /// - gọi repository
    /// - map sang DTO trả về cho API/UI
    /// </summary>
    public async Task<PagedResult<InventoryLedgerItemDto>> GetLedgerAsync(
        InventoryLedgerQueryRequest request,
        CancellationToken ct = default)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        // Chuẩn hóa request để repository xử lý gọn hơn.
        request.Page = page;
        request.PageSize = pageSize;

        request.Keyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim();

        request.ReferenceId = string.IsNullOrWhiteSpace(request.ReferenceId)
            ? null
            : request.ReferenceId.Trim();

        request.SortBy = string.IsNullOrWhiteSpace(request.SortBy)
            ? "TransactionDate"
            : request.SortBy.Trim();

        request.SortDirection = string.IsNullOrWhiteSpace(request.SortDirection)
            ? "desc"
            : request.SortDirection.Trim();

        var (items, totalItems) = await _inventoryTransactionRepository.QueryLedgerAsync(request, ct);

        var dtoItems = items.Select(x => new InventoryLedgerItemDto
        {
            Id = x.Id,
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse?.Name ?? string.Empty,
            ProductVariantId = x.ProductVariantId,
            ProductVariantName = x.ProductVariant.GetDisplayName(),
            Sku = x.ProductVariant.Sku,

            // Không còn dùng ProductVariant.Barcode.
            // Lấy barcode đại diện từ conversion/barcode.
            Barcode = ResolveRepresentativeBarcode(x.ProductVariant),

            TransactionType = x.TransactionType,
            TransactionTypeName = x.TransactionType.ToString(),
            ReferenceType = x.ReferenceType,
            ReferenceTypeName = x.ReferenceType.ToString(),
            ReferenceId = x.ReferenceId,
            ReferenceLineId = x.ReferenceLineId,
            QuantityChange = x.QuantityChange,
            BeforeQty = x.BeforeQty,
            AfterQty = x.AfterQty,
            IsNegativeAfterTransaction = x.AfterQty < 0,
            OccurredAtUtc = x.OccurredAtUtc,
            Note = x.Note
        }).ToList();

        return new PagedResult<InventoryLedgerItemDto>
        {
            Items = dtoItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };
    }

    /// <summary>
    /// Resolve 1 barcode đại diện để hiển thị ở các màn danh sách kho / ledger.
    ///
    /// Thứ tự ưu tiên:
    /// 1) barcode primary của đơn vị mặc định bán
    /// 2) barcode primary của đơn vị gốc
    /// 3) barcode active đầu tiên còn lại
    ///
    /// Ghi chú:
    /// - Đây chỉ là barcode "hiển thị đại diện", không phải barcode lookup chính thức.
    /// - Barcode lookup chính thức phải đi qua IBarcodeLookupService / ProductVariantUnitBarcodeRepository.
    /// </summary>
    private static string? ResolveRepresentativeBarcode(ProductVariant? variant)
    {
        if (variant == null || variant.UnitConversions == null || !variant.UnitConversions.Any())
            return null;

        var activeConversions = variant.UnitConversions
            .Where(c => !c.IsDeleted && c.IsActive)
            .ToList();

        if (!activeConversions.Any())
            return null;

        // 1) Ưu tiên đơn vị mặc định bán
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

        // 2) Nếu không có thì lấy ở đơn vị gốc
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

        // 3) Cuối cùng lấy barcode active đầu tiên của bất kỳ conversion nào
        var fallbackBarcode = activeConversions
            .SelectMany(c => c.Barcodes ?? Enumerable.Empty<ProductVariantUnitBarcode>())
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .Select(b => b.Barcode)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(fallbackBarcode) ? null : fallbackBarcode;
    }
}