namespace GaoApp.Application.Interfaces.Services.Inventory;

/// <summary>
/// Service dùng chung để resolve đơn vị + hệ số quy đổi của ProductVariant.
/// Dùng cho:
/// - StockDocument
/// - InventoryAdjustment
/// - Transfer
/// - StockCount
/// và các nghiệp vụ kho khác về sau.
/// </summary>
public interface IInventoryUnitResolver
{
    /// <summary>
    /// Resolve đơn vị và factor của ProductVariant.
    /// Nếu unitId = null thì service sẽ tự fallback theo cấu hình hợp lệ.
    /// </summary>
    Task<(int UnitId, string? UnitName, decimal Factor)> ResolveAsync(
        int productVariantId,
        int? unitId,
        CancellationToken ct = default);
}