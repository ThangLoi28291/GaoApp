using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Orders.LegalEntityAllocation;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontCheckoutService
{
    private async Task<List<Source>> Sources(StorefrontSettings settings, CancellationToken ct)
    {
        var store = await db.Stores.AsNoTracking().SingleAsync(x => x.Id == Store && x.IsActive, ct);
        if (!store.IsMultiLegalEntityEnabled)
        {
            var warehouse = await db.Warehouses.AsNoTracking().Include(x => x.LegalEntity).SingleOrDefaultAsync(x => x.StoreId == Store &&
                x.Id == settings.SourceWarehouseId && x.IsActive, ct);
            Require(warehouse is not null && warehouse.LegalEntity.StoreId == Store && warehouse.LegalEntity.IsActive &&
                settings.SourceLegalEntityId == warehouse.LegalEntityId, "Kho nguồn và chủ thể của website chưa sẵn sàng.");
            return [new(warehouse!.Id, warehouse.LegalEntityId, 1, Hash(Convert.ToBase64String(warehouse.RowVersion) + "|" + Convert.ToBase64String(warehouse.LegalEntity.RowVersion)))];
        }
        Require(store.MultiLegalEntityActivatedAtUtc.HasValue && store.MultiLegalEntityActivatedAtUtc <= Now, "Chế độ đa chủ thể chưa có thời điểm kích hoạt hợp lệ.");
        var entities = await db.LegalEntities.AsNoTracking().Include(x => x.DefaultWarehouse).Where(x => x.StoreId == Store && x.IsActive && x.SalePriority > 0)
            .OrderBy(x => x.SalePriority).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
        Require(entities.Count is > 0 and <= 100 && entities.All(x => x.DefaultWarehouse is { IsActive: true } w && w.StoreId == Store && w.LegalEntityId == x.Id) &&
            entities.Select(x => x.DefaultWarehouseId).Distinct().Count() == entities.Count && entities.Select(x => x.SalePriority).Distinct().Count() == entities.Count,
            "Các chủ thể bán hàng cần kho mặc định hoạt động riêng và thứ tự ưu tiên không trùng.");
        return entities.Select(x => new Source(x.DefaultWarehouseId!.Value, x.Id, x.SalePriority,
            Hash(Convert.ToBase64String(x.RowVersion) + "|" + Convert.ToBase64String(x.DefaultWarehouse!.RowVersion)))).ToList();
    }

    private static bool Shortage(IReadOnlyList<StorefrontCheckoutLine> lines, IReadOnlyList<Source> sources, IEnumerable<InventoryBalance> inventory)
    {
        var stocks = inventory.ToList();
        var missing = false;
        foreach (var group in lines.GroupBy(x => x.VariantId))
        {
            var demand = group.Sum(x => x.BaseQuantity); StorefrontValues.Quantity(demand, positive: true);
            var available = sources.Sum(source => stocks.Where(x => x.WarehouseId == source.WarehouseId && x.ProductVariantId == group.Key)
                .Sum(x => Math.Max(0, x.OnHandQty - x.ReservedQty)));
            missing |= available < demand;
        }
        return missing;
    }

    private async Task<Dictionary<(int Warehouse, int Variant), InventoryBalance>> LockBalances(Data data, CancellationToken ct)
    {
        var variants = data.Lines.Select(x => x.VariantId).Distinct().Order().ToList();
        var keys = data.Sources.SelectMany(source => variants.Select(variant => new InventoryPostingLockKey(Store, source.WarehouseId, variant))).ToList();
        await balances.LockBatchAsync(keys, ct);
        var result = new Dictionary<(int Warehouse, int Variant), InventoryBalance>();
        var warehouseIds = data.Sources.Select(x => x.WarehouseId).ToList();
        var held = await db.InventoryReservations.AsNoTracking().Where(x => x.StoreId == Store && warehouseIds.Contains(x.WarehouseId) &&
            variants.Contains(x.ProductVariantId) && x.Status == InventoryReservationStatus.Active)
            .GroupBy(x => new { x.WarehouseId, x.ProductVariantId }).Select(group => new { group.Key.WarehouseId, group.Key.ProductVariantId, Quantity = group.Sum(x => x.ReservedQty) }).ToListAsync(ct);
        var heldByBalance = held.ToDictionary(x => (x.WarehouseId, x.ProductVariantId), x => x.Quantity);
        foreach (var key in keys.OrderBy(x => x.WarehouseId).ThenBy(x => x.ProductVariantId))
        {
            var balance = await balances.LockAndGetOrCreateAsync(Store, key.WarehouseId, key.ProductVariantId, ct);
            var evidence = heldByBalance.GetValueOrDefault((key.WarehouseId, key.ProductVariantId));
            Require(evidence == balance.ReservedQty && evidence >= 0, "Bộ đếm giữ hàng chưa khớp sổ giữ chung. Cửa hàng cần đối soát trước khi nhận thêm đơn.");
            result.Add((key.WarehouseId, key.ProductVariantId), balance);
        }
        return result;
    }

    private async Task Reserve(StorefrontOrder order, Data data, Dictionary<(int Warehouse, int Variant), InventoryBalance> locked, CancellationToken ct)
    {
        // This pure allocator receives the actual storefront order identity. No POS repository receives it.
        var plan = AllocationPlan(data.Lines,data.Sources,locked.Values.ToList(),order.Id,data.AllowNegative);
        Require(data.BankBinding is null||plan.Allocations.Select(x=>x.LegalEntityId).Distinct().Order().SequenceEqual(data.BankBinding.SourceLegalEntityIds.Order()),"Nguồn hàng QR vừa thay đổi; hãy xác nhận lại báo giá.");
        Require(plan.IsSuccess && plan.TotalAllocatedBaseQuantity == data.Lines.Sum(x => x.BaseQuantity), "Không đủ tồn khả dụng để phân bổ nguồn hàng cho đơn.");
        var sources = new Dictionary<int, StorefrontFulfillmentSource>();
        foreach (var warehouse in plan.Allocations.Select(x => x.WarehouseId).Distinct().Order())
        {
            var source = data.Sources.Single(x => x.WarehouseId == warehouse);
            var entity = new StorefrontFulfillmentSource { StoreId = Store, StorefrontOrderId = order.Id, SourceWarehouseId = warehouse,
                SourceLegalEntityId = source.LegalEntityId, DispatchPostingKey = $"storefront:{order.Code}:{warehouse}" };
            db.Set<StorefrontFulfillmentSource>().Add(entity); sources.Add(warehouse, entity);
        }
        await db.SaveChangesAsync(ct);
        var lines = new Dictionary<int, StorefrontOrderLine>();
        var invoiceFlags = await db.ProductVariants.AsNoTracking().Where(x => x.StoreId == Store && data.Lines.Select(l => l.VariantId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.HasInputInvoice, ct);
        foreach (var price in data.Lines)
        {
            var allocations = plan.Allocations.Where(x => x.OrderLineId == price.Index).ToList();
            Require(allocations.Sum(x => x.BaseQuantity) == price.BaseQuantity, "Phân bổ lượng giữ không khớp dòng hàng.");
            var entity = new StorefrontOrderLine { StoreId = Store, StorefrontOrderId = order.Id, ProductId = price.ProductId, VariantId = price.VariantId,
                ProductUnitConversionId = price.ProductUnitConversionId, SellingUnitId = price.UnitId, ItemName = price.Name, UnitName = price.UnitName,
                Quantity = price.Quantity, BaseMultiplier = price.BaseMultiplier, BaseQuantity = price.BaseQuantity, UnitPrice = price.UnitPrice,
                HasInputInvoiceSnapshot = invoiceFlags[price.VariantId],
                Discount = price.Discount, RoundingAdjustment = price.RoundingAdjustment, NetAmount = price.NetAmount,
                WeightKgPerSellingUnit = price.WeightKg ?? 0, IsGift = price.IsGift,
                SnapshotJson = Serialize(new { Pricing = price, Allocations = allocations.Select(item => new { FulfillmentSourceId = sources[item.WarehouseId].Id,
                    item.WarehouseId, item.LegalEntityId, item.SalePriority, item.BaseQuantity, item.AllocationSource }) }) };
            db.Set<StorefrontOrderLine>().Add(entity); lines.Add(price.Index, entity);
        }
        await db.SaveChangesAsync(ct);
        var reservations = new List<InventoryReservation>();
        foreach (var item in plan.Allocations.OrderBy(x => x.WarehouseId).ThenBy(x => x.ProductVariantId).ThenBy(x => x.OrderLineId))
        {
            StorefrontValues.Quantity(item.BaseQuantity, positive: true);
            var balance = locked[(item.WarehouseId, item.ProductVariantId)];
            balance.ReservedQty = StorefrontInventoryPolicy.ReserveOwn(balance.OnHandQty, balance.ReservedQty, item.BaseQuantity, data.AllowNegative);
            balance.QuantityPrecisionVersion = 1;
            var reservation = new InventoryReservation { StoreId = Store, WarehouseId = item.WarehouseId, ProductVariantId = item.ProductVariantId,
                SourceLegalEntityId = item.LegalEntityId, ReferenceType = InventoryReferenceType.Storefront,
                ReferenceId = order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ReferenceLineId = lines[item.OrderLineId].Id,
                StorefrontOrderId = order.Id, StorefrontOrderLineId = lines[item.OrderLineId].Id, StorefrontFulfillmentSourceId = sources[item.WarehouseId].Id,
                ReservedQty = item.BaseQuantity, Status = InventoryReservationStatus.Active, ReservedAtUtc = Now, Note = "Giữ hàng từ đơn website; chưa xuất kho." };
            db.InventoryReservations.Add(reservation); reservations.Add(reservation);
        }
        await db.SaveChangesAsync(ct);
        foreach (var reservation in reservations) db.Set<StorefrontReservationLink>().Add(new() { StoreId = Store, StorefrontOrderId = order.Id,
            OrderLineId = reservation.StorefrontOrderLineId!.Value, InventoryReservationId = reservation.Id });
        await db.SaveChangesAsync(ct);
        Require(lines.Values.Sum(x => x.NetAmount) == order.GoodsAmount, "Tổng tiền dòng hàng không khớp đơn đã xác nhận.");
    }
    private OrderLegalEntityAllocationResult AllocationPlan(IReadOnlyList<StorefrontCheckoutLine> priceLines,IReadOnlyList<Source> sourceRows,
        IReadOnlyList<InventoryBalance> stock,int correlationId,bool allowNegative) => allocation.Preview(new OrderLegalEntityAllocationRequest { StoreId = Store, OrderId = correlationId,
            AllowNegativeInventory = allowNegative, Lines = priceLines.Select(line => new OrderLegalEntityAllocationLineInput {
                OrderLineId = line.Index, ProductVariantId = line.VariantId, ProductUnitConversionId = line.ProductUnitConversionId,
                Quantity = line.Quantity, Multiplier = line.BaseMultiplier, BaseQuantity = line.BaseQuantity }).ToList(),
            EligibleSources = sourceRows.Select(source => new LegalEntityAllocationSourceInput { LegalEntityId = source.LegalEntityId,
                WarehouseId = source.WarehouseId, SalePriority = source.Priority }).ToList(),
            Inventory = stock.Select(balance => new LegalEntityInventoryAvailabilityInput { WarehouseId = balance.WarehouseId,
                ProductVariantId = balance.ProductVariantId, OnHandBaseQuantity = balance.OnHandQty, ReservedBaseQuantity = balance.ReservedQty }).ToList() });
}
