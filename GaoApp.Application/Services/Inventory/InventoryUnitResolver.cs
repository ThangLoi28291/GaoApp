using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Service resolve đơn vị quy đổi dùng chung.
/// 
/// Mục tiêu:
/// - không copy-paste logic giữa StockDocumentService và InventoryAdjustmentService
/// - đảm bảo mọi nghiệp vụ kho dùng cùng 1 cách tính factor
/// - tránh lệch logic theo thời gian
/// </summary>
public class InventoryUnitResolver : IInventoryUnitResolver
{
    private readonly IStockDocumentRepository _stockDocumentRepository;

    public InventoryUnitResolver(IStockDocumentRepository stockDocumentRepository)
    {
        _stockDocumentRepository = stockDocumentRepository;
    }

    public async Task<(int UnitId, string? UnitName, decimal Factor)> ResolveAsync(
        int productVariantId,
        int? unitId,
        CancellationToken ct = default)
    {
        var variant = await _stockDocumentRepository.GetVariantForStockDocumentAsync(productVariantId, ct);
        if (variant == null)
            throw new InvalidOperationException($"Không tìm thấy ProductVariant. ProductVariantId={productVariantId}");

        if (variant.Product == null)
            throw new InvalidOperationException($"ProductVariant chưa load Product. ProductVariantId={productVariantId}");

        // 1. Nếu có truyền unitId -> ưu tiên tìm ProductUnitConversion active
        if (unitId.HasValue)
        {
            var conversion = await _stockDocumentRepository.GetConversionAsync(productVariantId, unitId.Value, ct);
            if (conversion != null)
            {
                return (
                    conversion.UnitId,
                    conversion.Unit?.Name,
                    conversion.Factor <= 0 ? 1m : conversion.Factor
                );
            }

            // 2. Nếu không có conversion nhưng unitId đúng bằng BaseUnit của Product -> fallback đơn vị gốc
            if (unitId.Value == variant.Product.BaseUnitId)
            {
                return (
                    variant.Product.BaseUnitId,
                    variant.Product.BaseUnit?.Name,
                    1m
                );
            }

            throw new InvalidOperationException(
                $"Không tìm thấy cấu hình quy đổi đơn vị. ProductVariantId={productVariantId}, UnitId={unitId.Value}");
        }

        // 3. Không truyền unitId -> ưu tiên base conversion nếu có
        var baseConversion = await _stockDocumentRepository.GetBaseConversionAsync(productVariantId, ct);
        if (baseConversion != null)
        {
            return (
                baseConversion.UnitId,
                baseConversion.Unit?.Name,
                baseConversion.Factor <= 0 ? 1m : baseConversion.Factor
            );
        }

        // 4. Không có ProductUnitConversion nào -> fallback về BaseUnit của Product
        return (
            variant.Product.BaseUnitId,
            variant.Product.BaseUnit?.Name,
            1m
        );
    }
}