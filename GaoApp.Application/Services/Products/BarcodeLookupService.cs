using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Products;

/// <summary>
/// Service lookup barcode / search sản phẩm cho:
/// - POS
/// - StockDocument
/// - InventoryAdjustment
///
/// CHỐT KIẾN TRÚC:
/// - Không còn dùng ProductVariant.Barcode
/// - Barcode duy nhất của hệ thống nằm ở ProductVariantUnitBarcode
/// - Mọi lookup barcode đều phải xuất phát từ ProductVariantUnitBarcode
/// </summary>
public class BarcodeLookupService : IBarcodeLookupService
{
    private readonly IProductVariantUnitBarcodeRepository _barcodeRepo;
    private readonly IProductVariantBarcodeHistoryRepository _historyRepository;
    private readonly IProductVariantRepository _variantRepo;
    private readonly ICurrentStore _currentStore;

    public BarcodeLookupService(
        IProductVariantUnitBarcodeRepository barcodeRepo,
        IProductVariantBarcodeHistoryRepository historyRepository,
        IProductVariantRepository variantRepo,
        ICurrentStore currentStore)
    {
        _barcodeRepo = barcodeRepo;
        _variantRepo = variantRepo;
        _currentStore = currentStore;
        _historyRepository = historyRepository;
    }

    /// <summary>
    /// Build ra 1 option đơn vị khi user quét đúng barcode của 1 conversion cụ thể.
    /// Ví dụ:
    /// - quét barcode của "thùng" => trả đúng unit "thùng"
    /// - factor = 24
    /// </summary>
    private VariantLookupUnitOption BuildUnitOptionFromBarcodeMatch(ProductVariantUnitBarcode unitBarcode)
    {
        var conversion = unitBarcode.ProductUnitConversion
            ?? throw new InvalidOperationException("ProductVariantUnitBarcode thiếu ProductUnitConversion.");

        var variant = conversion.ProductVariant
            ?? throw new InvalidOperationException("ProductUnitConversion thiếu ProductVariant.");

        var product = variant.Product
            ?? throw new InvalidOperationException("ProductVariant thiếu Product.");

        return new VariantLookupUnitOption
        {
            ProductUnitConversionId = conversion.Id,
            UnitId = conversion.UnitId,
            UnitName = conversion.Unit?.Name ?? product.BaseUnit?.Name ?? string.Empty,
            Factor = conversion.Factor <= 0 ? 1 : conversion.Factor,
            Price = conversion.Price ?? variant.Price ?? product.BasePrice,
            Barcode = unitBarcode.Barcode,
            IsBaseUnitFallback = false
        };
    }

    /// <summary>
    /// Build toàn bộ danh sách đơn vị bán của 1 variant.
    ///
    /// Ưu tiên:
    /// - lấy từ ProductUnitConversion
    /// - mỗi conversion lấy 1 barcode ưu tiên (primary trước, sau đó barcode bất kỳ còn active)
    ///
    /// Fallback base unit chỉ dùng khi:
    /// - variant chưa có ProductUnitConversion nào
    /// Điều này giúp UI không bị trống trong giai đoạn dữ liệu chưa chuẩn hóa hết.
    /// </summary>
    private List<VariantLookupUnitOption> BuildUnitOptionsForVariant(ProductVariant variant)
    {
        var result = new List<VariantLookupUnitOption>();

        if (variant.UnitConversions != null && variant.UnitConversions.Any())
        {
            foreach (var conversion in variant.UnitConversions
                         .Where(x => !x.IsDeleted && x.IsActive)
                         .OrderByDescending(x => x.IsBaseUnit)
                         .ThenByDescending(x => x.IsDefaultForSale)
                         .ThenBy(x => x.Factor)
                         .ThenBy(x => x.SortOrder)
                         .ThenBy(x => x.Id))
            {
                var barcode = conversion.Barcodes?
                    .Where(x => !x.IsDeleted && x.IsActive)
                    .OrderByDescending(x => x.IsPrimary)
                    .ThenBy(x => x.Id)
                    .Select(x => x.Barcode)
                    .FirstOrDefault();

                result.Add(new VariantLookupUnitOption
                {
                    ProductUnitConversionId = conversion.Id,
                    UnitId = conversion.UnitId,
                    UnitName = conversion.Unit?.Name ?? string.Empty,
                    Factor = conversion.Factor <= 0 ? 1 : conversion.Factor,
                    Price = conversion.Price ?? variant.Price ?? variant.Product.BasePrice,
                    Barcode = barcode,
                    IsBaseUnitFallback = false
                });
            }
        }

        // Fallback chỉ phục vụ keyword search UI.
        if (!result.Any())
        {
            result.Add(new VariantLookupUnitOption
            {
                ProductUnitConversionId = null,
                UnitId = variant.Product.BaseUnitId,
                UnitName = variant.Product.BaseUnit?.Name ?? string.Empty,
                Factor = 1,
                Price = variant.Price ?? variant.Product.BasePrice,
                Barcode = null,
                IsBaseUnitFallback = true
            });
        }

        return result;
    }

    /// <summary>
    /// Add 1 dòng kết quả lookup vào danh sách Select2.
    /// Có chống trùng để tránh 1 variant/unit/barcode bị add nhiều lần.
    /// </summary>
    private void AddLookupResult(
        List<StockDocumentLookupSelect2ItemDto> results,
        HashSet<string> addedKeys,
        ProductVariant variant,
        VariantLookupUnitOption unit,
        string? barcode,
        string sourceType)
    {
        var normalizedBarcode = string.IsNullOrWhiteSpace(barcode) ? unit.Barcode : barcode;
        var key = $"{variant.Id}_{unit.ProductUnitConversionId}_{unit.UnitId}_{normalizedBarcode}";

        if (!addedKeys.Add(key))
            return;

        results.Add(new StockDocumentLookupSelect2ItemDto
        {
            ProductVariantId = variant.Id,
            ProductUnitConversionId = unit.ProductUnitConversionId,
            UnitId = unit.UnitId,
            ProductName = variant.Product.Name,
            Sku = variant.Sku,
            Barcode = normalizedBarcode,
            UnitName = unit.UnitName,
            Factor = unit.Factor,
            IsBaseUnitFallback = unit.IsBaseUnitFallback,
            SourceType = sourceType,
            Text = BuildLookupText(
                variant.Product.Name,
                variant.Sku,
                unit.UnitName,
                unit.Factor,
                normalizedBarcode,
                unit.IsBaseUnitFallback)
        });
    }

    /// <summary>
    /// Chuỗi text hiển thị cho Select2.
    /// </summary>
    private static string BuildLookupText(
        string productName,
        string sku,
        string unitName,
        decimal factor,
        string? barcode,
        bool isBaseUnitFallback)
    {
        var parts = new List<string>
        {
            productName,
            $"SKU: {sku}",
            $"ĐV: {unitName}",
            $"x{factor}"
        };

        if (!string.IsNullOrWhiteSpace(barcode))
        {
            parts.Add($"BC: {barcode}");
        }

        if (isBaseUnitFallback)
        {
            parts.Add("Đơn vị gốc (fallback)");
        }

        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Class nội bộ để biểu diễn 1 option đơn vị bán khi build kết quả lookup.
    /// </summary>
    private sealed class VariantLookupUnitOption
    {
        public int? ProductUnitConversionId { get; set; }
        public int? UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal Factor { get; set; }
        public decimal? Price { get; set; }
        public string? Barcode { get; set; }
        public bool IsBaseUnitFallback { get; set; }
    }

    /// <summary>
    /// Lookup barcode chính thức.
    ///
    /// CHỐT:
    /// - Chỉ lookup từ ProductVariantUnitBarcode
    /// - Không fallback ProductVariant.Barcode nữa
    /// </summary>
    public async Task<BarcodeLookupResultDto?> FindAsync(string barcode, CancellationToken ct = default)
    {
        barcode = (barcode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(barcode))
            return null;

        var storeId = _currentStore.StoreId;
        var entity = await _barcodeRepo.FindByBarcodeAsync(storeId, barcode, ct);
        if (entity == null)
            return null;

        var conversion = entity.ProductUnitConversion
            ?? throw new InvalidOperationException("ProductVariantUnitBarcode thiếu ProductUnitConversion.");

        var variant = conversion.ProductVariant
            ?? throw new InvalidOperationException("ProductUnitConversion thiếu ProductVariant.");

        var product = variant.Product
            ?? throw new InvalidOperationException("ProductVariant thiếu Product.");

        var unit = conversion.Unit
            ?? throw new InvalidOperationException("ProductUnitConversion thiếu Unit.");

        var baseUnit = product.BaseUnit
            ?? throw new InvalidOperationException("Product thiếu BaseUnit.");

        return new BarcodeLookupResultDto
        {
            ProductId = product.Id,
            ProductName = product.Name,

            ProductVariantId = variant.Id,
            VariantSku = variant.Sku,

            ProductUnitConversionId = conversion.Id,

            UnitId = unit.Id,
            UnitName = unit.Name,

            BaseUnitId = product.BaseUnitId,
            BaseUnitName = baseUnit.Name,

            Factor = conversion.Factor <= 0 ? 1 : conversion.Factor,
            IsBaseUnit = conversion.IsBaseUnit,
            IsDefaultForSale = conversion.IsDefaultForSale,

            CostPrice = variant.CostPrice,
            SellPrice = conversion.Price ?? variant.Price ?? product.BasePrice,

            Barcode = entity.Barcode,
            SourceType = "UnitBarcode"
        };
    }

    /// <summary>
    /// Search Select2 cho màn hình StockDocument.
    ///
    /// Thứ tự xử lý:
    /// 1. Nếu keyword đúng barcode đơn vị -> trả đúng unit đó trước
    /// 2. Nếu barcode history có match -> trả variant tương ứng
    /// 3. Search keyword theo tên/SKU/... -> bung toàn bộ đơn vị của variant
    /// </summary>
    public async Task<List<StockDocumentLookupSelect2ItemDto>> SearchForStockDocumentSelect2Async(
        string keyword,
        int take = 20,
        CancellationToken ct = default)
    {
        keyword = (keyword ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return new List<StockDocumentLookupSelect2ItemDto>();

        var results = new List<StockDocumentLookupSelect2ItemDto>();
        var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var storeId = _currentStore.StoreId;

        // 1) Barcode đơn vị quy đổi -> ưu tiên trả đúng unit đó
        var unitBarcode = await _barcodeRepo.FindByBarcodeAsync(storeId, keyword, ct);
        if (unitBarcode != null)
        {
            var conversion = unitBarcode.ProductUnitConversion
                ?? throw new InvalidOperationException("ProductVariantUnitBarcode thiếu ProductUnitConversion.");

            var variant = conversion.ProductVariant
                ?? throw new InvalidOperationException("ProductUnitConversion thiếu ProductVariant.");

            var unitOption = BuildUnitOptionFromBarcodeMatch(unitBarcode);
            AddLookupResult(results, addedKeys, variant, unitOption, keyword, "UnitBarcode");
        }

        // 2) Barcode history
        // Tạm giữ logic cũ nếu history vẫn map ra variant-level.
        var historyVariantId = await _variantRepo.ResolveVariantIdByBarcodeHistoryAsync(keyword, ct);
        if (historyVariantId.HasValue)
        {
            var variantFromHistory = await _variantRepo.GetActiveWithProductAsync(historyVariantId.Value, ct);
            if (variantFromHistory != null)
            {
                var unitOptions = BuildUnitOptionsForVariant(variantFromHistory);

                var fallbackBaseUnit = unitOptions.FirstOrDefault(x => x.IsBaseUnitFallback);
                if (fallbackBaseUnit != null)
                {
                    AddLookupResult(results, addedKeys, variantFromHistory, fallbackBaseUnit, keyword, "BarcodeHistory");
                }
                else
                {
                    var firstUnit = unitOptions.FirstOrDefault();
                    if (firstUnit != null)
                    {
                        AddLookupResult(results, addedKeys, variantFromHistory, firstUnit, keyword, "BarcodeHistory");
                    }
                }
            }
        }

        // 3) Search theo keyword -> bung ra toàn bộ đơn vị
        var keywordMatches = await _variantRepo.SearchForPOSAsync(keyword, take, ct);

        foreach (var variant in keywordMatches)
        {
            var unitOptions = BuildUnitOptionsForVariant(variant);

            foreach (var unit in unitOptions)
            {
                AddLookupResult(results, addedKeys, variant, unit, unit.Barcode, "Keyword");
            }
        }

        return results.Take(take).ToList();
    }

    /// <summary>
    /// Search Select2 cho màn hình điều chỉnh kho.
    /// Dùng lại logic của StockDocument để tránh lệch nghiệp vụ.
    /// </summary>
    public async Task<List<InventoryAdjustmentLookupSelect2ItemDto>> SearchForInventoryAdjustmentSelect2Async(
        string keyword,
        int take = 20,
        CancellationToken ct = default)
    {
        var items = await SearchForStockDocumentSelect2Async(keyword, take, ct);

        return items.Select(x => new InventoryAdjustmentLookupSelect2ItemDto
        {
            ProductVariantId = x.ProductVariantId,
            ProductUnitConversionId = x.ProductUnitConversionId,
            UnitId = x.UnitId,
            ProductName = x.ProductName,
            Sku = x.Sku,
            Barcode = x.Barcode,
            UnitName = x.UnitName,
            Factor = x.Factor,
            IsBaseUnitFallback = x.IsBaseUnitFallback,
            SourceType = x.SourceType,
            Text = x.Text
        }).ToList();
    }
    public async Task<BarcodeLookupResultDto> LookupAsync(string barcode, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var inputBarcode = NormalizeBarcode(barcode);

        if (string.IsNullOrWhiteSpace(inputBarcode))
        {
            return new BarcodeLookupResultDto
            {
                Found = false,
                InputBarcode = inputBarcode
            };
        }

        // 1. Lookup active trước
        var active = await _barcodeRepo.GetActiveByBarcodeAsync(storeId, inputBarcode, ct);
        if (active != null)
        {
            return MapFromActive(active, inputBarcode);
        }

        // 2. Không có active -> lookup history
        var history = await _historyRepository.FindByHistoricalBarcodeAsync(storeId, inputBarcode, ct);
        if (history == null)
        {
            return new BarcodeLookupResultDto
            {
                Found = false,
                InputBarcode = inputBarcode
            };
        }

        // 3. Resolve barcode active hiện tại của cùng conversion qua repository
        var currentActive = await _barcodeRepo.GetCurrentActiveForConversionForLookupAsync(
            storeId,
            history.ProductUnitConversionId,
            ct);

        if (currentActive == null)
        {
            return new BarcodeLookupResultDto
            {
                Found = true,
                InputBarcode = inputBarcode,
                ProductVariantId = history.ProductVariantId,
                ProductUnitConversionId = history.ProductUnitConversionId,
                MatchedBarcode = inputBarcode,
                IsCurrentBarcode = false,
                IsHistoricalBarcode = true,
                WarningMessage = "Đây là mã cũ, nhưng hiện chưa có barcode active cho đơn vị quy đổi này.",
                SourceType = "BarcodeHistory"
            };
        }

        var result = MapFromActive(currentActive, inputBarcode);
        result.IsCurrentBarcode = false;
        result.IsHistoricalBarcode = true;
        result.WarningMessage = $"Đây là mã cũ. Hệ thống đang dùng mã mới: {currentActive.Barcode}";
        result.SourceType = "BarcodeHistory";
        result.MatchedBarcode = inputBarcode;
        result.CurrentActiveBarcode = currentActive.Barcode;

        return result;
    }

    private static BarcodeLookupResultDto MapFromActive(
        ProductVariantUnitBarcode active,
        string inputBarcode)
    {
        var conversion = active.ProductUnitConversion;
        var variant = conversion.ProductVariant;
        var product = variant.Product;

        return new BarcodeLookupResultDto
        {
            Found = true,
            InputBarcode = inputBarcode,

            ProductId = product.Id,
            ProductName = product.Name,

            ProductVariantId = variant.Id,
            VariantSku = variant.Sku,
            //VariantName = null,

            ProductUnitConversionId = conversion.Id,
            BarcodeRecordId = active.Id,

            UnitId = conversion.UnitId,
            UnitName = conversion.Unit?.Name,

            BaseUnitId = product.BaseUnitId,
            BaseUnitName = product.BaseUnit?.Name,

            Factor = conversion.Factor,
            IsBaseUnit = conversion.IsBaseUnit,
            IsDefaultForSale = conversion.IsDefaultForSale,

            CostPrice = variant.CostPrice,
            SellPrice = conversion.Price ?? variant.Price ?? product.BasePrice,

            Barcode = active.Barcode,
            MatchedBarcode = inputBarcode,
            CurrentActiveBarcode = active.Barcode,

            IsCurrentBarcode = true,
            IsHistoricalBarcode = false,
            WarningMessage = null,
            SourceType = "UnitBarcode"
        };
    }

    private static string NormalizeBarcode(string barcode)
    {
        return string.IsNullOrWhiteSpace(barcode)
            ? string.Empty
            : barcode.Trim();
    }
}