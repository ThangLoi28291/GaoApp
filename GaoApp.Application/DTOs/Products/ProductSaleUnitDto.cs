namespace GaoApp.Application.DTOs.Products;

/// <summary>Read-only selling prices; deliberately excludes supplier and purchase-cost data.</summary>
public sealed record ProductSaleUnitDto(int VariantId, string Sku, string VariantName,
    string UnitName, decimal Factor, bool IsBaseUnit, bool IsActive,
    decimal? RetailPrice, decimal? WholesalePrice);
