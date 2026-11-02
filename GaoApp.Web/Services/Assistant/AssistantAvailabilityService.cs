using System.Globalization;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Assistant;

public sealed class AssistantAvailabilityService(AppDbContext db, TimeProvider clock)
{
    public async Task<AssistantAvailabilityResult> ReadAsync(
        AssistantIdentity identity, CatalogVariant variant, CancellationToken ct)
    {
        var storeId = AssistantAccess.Id(identity.Scope.StoreId);
        if (!int.TryParse(variant.Product.VariantId, NumberStyles.None, CultureInfo.InvariantCulture, out var variantId)
            || variantId <= 0)
            throw AssistantException.Invalid();

        // Only warehouses used by an active POS terminal are treated as customer-facing.
        // Warehouse assignment is recorded on POS shifts, not directly on the terminal.
        // Internal, inactive and other-store warehouses are deliberately omitted.
        var warehouses = await (
            from terminal in db.POSTerminals.IgnoreQueryFilters().AsNoTracking()
            join shift in db.POSShifts.IgnoreQueryFilters().AsNoTracking()
                on new { terminal.StoreId, TerminalId = terminal.Id }
                equals new { shift.StoreId, shift.TerminalId }
            join warehouse in db.Warehouses.IgnoreQueryFilters().AsNoTracking()
                on new { shift.StoreId, WarehouseId = shift.WarehouseId }
                equals new { warehouse.StoreId, WarehouseId = warehouse.Id }
            where warehouse.StoreId == storeId && !warehouse.IsDeleted && warehouse.IsActive
                && terminal.StoreId == storeId && !terminal.IsDeleted && terminal.IsActive && !shift.IsDeleted
            select new { warehouse.Id, warehouse.Name })
            .Distinct()
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Take(101)
            .ToListAsync(ct);

        if (warehouses.Count > 100)
            throw new AssistantException(503, "availability_scope_too_large", "Chưa thể kiểm tra tồn cho phạm vi cửa hàng hiện tại.");

        var warehouseIds = warehouses.Select(x => x.Id).ToArray();
        var balances = warehouseIds.Length == 0
            ? []
            : await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && !x.IsDeleted && x.ProductVariantId == variantId
                    && warehouseIds.Contains(x.WarehouseId))
                .Select(x => new { x.WarehouseId, x.OnHandQty, x.ReservedQty })
                .ToListAsync(ct);
        var byWarehouse = balances.ToDictionary(x => x.WarehouseId);
        var sellableUnits = variant.Units.Where(x => x.Unit.CanSell).Select(x => x.Unit)
            .Select(unit => new { Unit = unit, Factor = decimal.TryParse(unit.Factor,
                NumberStyles.Number, CultureInfo.InvariantCulture, out var factor) && factor > 0 ? factor : 0m })
            .Where(x => x.Factor > 0).ToArray();
        var now = clock.GetUtcNow();

        var result = warehouses.Select(warehouse =>
        {
            if (!byWarehouse.TryGetValue(warehouse.Id, out var balance))
                return new AssistantWarehouseAvailability(warehouse.Id, warehouse.Name, "not_recorded", null, null, []);

            var available = balance.OnHandQty - balance.ReservedQty;
            var status = available < 0 ? "review_required" : available == 0 ? "out_of_stock" : "available";
            var safeBaseQty = available > 0 ? available : 0m;
            var units = status is "available" or "out_of_stock"
                ? sellableUnits.Select(x => new AssistantUnitAvailability(x.Unit.UnitId, x.Unit.Name,
                    x.Factor, safeBaseQty / x.Factor)).ToArray()
                : [];
            return new AssistantWarehouseAvailability(warehouse.Id, warehouse.Name, status,
                status is "available" or "out_of_stock" ? safeBaseQty : null,
                variant.BaseUnit.Name, units);
        }).ToArray();

        return new AssistantAvailabilityResult(variant.Product.VariantId, "gaoapp",
            now.UtcTicks.ToString(CultureInfo.InvariantCulture), now, result);
    }
}

public sealed record AssistantAvailabilityResult(string VariantId, string Source, string Revision,
    DateTimeOffset VerifiedAt, AssistantWarehouseAvailability[] Warehouses);

public sealed record AssistantWarehouseAvailability(int WarehouseId, string WarehouseName, string Status,
    decimal? AvailableQuantity, string? BaseUnitName, AssistantUnitAvailability[] Units);

public sealed record AssistantUnitAvailability(string UnitId, string UnitName, decimal Factor,
    decimal AvailableQuantity);
