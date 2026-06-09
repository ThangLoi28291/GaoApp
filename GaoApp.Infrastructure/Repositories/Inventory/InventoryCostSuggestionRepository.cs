using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

/// <summary>
/// Lấy giá vốn đề xuất.
/// Ưu tiên đơn giản, an toàn:
/// 1. ProductVariant.CostPrice.
/// Phase sau có thể nâng lên lấy cost layer / phiếu nhập gần nhất.
/// </summary>
public class InventoryCostSuggestionRepository : IInventoryCostSuggestionRepository
{
    private readonly AppDbContext _db;

    public InventoryCostSuggestionRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<InventoryCostSuggestionDto> GetSuggestedCostAsync(
        int productVariantId,
        CancellationToken ct = default)
    {
        var variant = await _db.ProductVariants
            .AsNoTracking()
            .Where(x => x.Id == productVariantId)
            .Select(x => new
            {
                x.Id,
                x.CostPrice
            })
            .FirstOrDefaultAsync(ct);

        if (variant == null)
        {
            return new InventoryCostSuggestionDto
            {
                ProductVariantId = productVariantId,
                UnitCost = null,
                SourceType = "NotFound",
                SourceText = "Không tìm thấy sản phẩm."
            };
        }

        if (variant.CostPrice > 0)
        {
            return new InventoryCostSuggestionDto
            {
                ProductVariantId = productVariantId,
                UnitCost = variant.CostPrice,
                SourceType = "ProductVariantCostPrice",
                SourceText = "Giá vốn hiện tại của sản phẩm."
            };
        }

        return new InventoryCostSuggestionDto
        {
            ProductVariantId = productVariantId,
            UnitCost = null,
            SourceType = "MissingCost",
            SourceText = "Sản phẩm chưa có giá vốn hợp lệ."
        };
    }
}