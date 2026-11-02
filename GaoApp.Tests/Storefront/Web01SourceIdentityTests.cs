using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Delivery;
using GaoApp.Tests.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb01"), Trait("Category", "StorefrontWeb01")]
public sealed class Web01SourceIdentityTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delivery_link_requires_exact_warehouse_and_owner_even_inside_same_store(bool anotherOwner)
    {
        var c = await fixture.CaseAsync(); var commerce = await c.CommerceAsync(); int foreignWarehouseId;
        await using (var setup = c.Context())
        {
            var ownerId = c.LegalEntityId;
            if (anotherOwner)
            {
                var owner = new LegalEntity { StoreId = c.StoreId, Code = "W01-OTHER", Name = "Synthetic second owner", LegalName = "Synthetic second owner", SalePriority = 2 };
                setup.Add(owner); await setup.SaveChangesAsync(); ownerId = owner.Id;
            }
            var warehouse = new Warehouse { StoreId = c.StoreId, LegalEntityId = ownerId, Code = "W01-OTHER", Name = "Synthetic second warehouse", IsActive = true };
            setup.Add(warehouse); await setup.SaveChangesAsync(); foreignWarehouseId = warehouse.Id;
        }
        var wrongDeliveryId = await CreateActualLegacyDelivery(c, foreignWarehouseId);
        await using (var attempt = c.Context())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => attempt.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE StorefrontFulfillmentSources SET DeliveryOrderId={wrongDeliveryId} WHERE Id={commerce.FulfillmentId}"));
            Assert.Equal(547, error.Number);
        }
        var matchingDeliveryId = await CreateActualLegacyDelivery(c, c.WarehouseId);
        await using var db = c.Context();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE StorefrontFulfillmentSources SET DeliveryOrderId={matchingDeliveryId} WHERE Id={commerce.FulfillmentId}");
        Assert.Equal(matchingDeliveryId, (await db.Set<GaoApp.Domain.Storefront.StorefrontFulfillmentSource>().SingleAsync()).DeliveryOrderId);
        var anotherMatching = await CreateActualLegacyDelivery(c, c.WarehouseId);
        var rebound = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE StorefrontFulfillmentSources SET DeliveryOrderId={anotherMatching} WHERE Id={commerce.FulfillmentId}"));
        Assert.Equal(51090, rebound.Number);
        var cleared = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE StorefrontFulfillmentSources SET DeliveryOrderId=NULL WHERE Id={commerce.FulfillmentId}"));
        Assert.Equal(51090, cleared.Number);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_inbound_stock_cannot_be_claimed_as_dispatch_evidence_even_inside_same_store(bool wrongSkuAndWarehouse)
    {
        var c = await fixture.CaseAsync(); var commerce = await c.CommerceAsync();
        var delivery = await CreateActualLegacyDelivery(c, c.WarehouseId);
        await using var db = c.Context();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE StorefrontFulfillmentSources SET DeliveryOrderId={delivery} WHERE Id={commerce.FulfillmentId}");
        var variantId = c.VariantId; var warehouseId = c.WarehouseId;
        if (wrongSkuAndWarehouse)
        {
            var variant = new ProductVariant { StoreId = c.StoreId, ProductId = c.ProductId, Sku = "W01-OTHER-" + Guid.NewGuid().ToString("N"), Price = 20, CostPrice = 10 };
            var warehouse = new Warehouse { StoreId = c.StoreId, LegalEntityId = c.LegalEntityId, Code = "W01-OTHER", Name = "Synthetic other warehouse" };
            db.AddRange(variant, warehouse); await db.SaveChangesAsync(); variantId = variant.Id; warehouseId = warehouse.Id;
        }
        var movement = await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
            new InventoryMovementFactory().CreateAdjustmentIncrease(warehouseId, variantId, 1, 10, "Synthetic WEB01 inbound"));
        Assert.NotNull(movement.InventoryTransactionId);
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO StorefrontLineSources (StoreId,OrderLineId,FulfillmentSourceId,StorefrontOrderId,BaseQuantity,InventoryTransactionId,RecordedCost,IsProvisionalCost,IsDeleted) VALUES ({c.StoreId},{commerce.LineId},{commerce.FulfillmentId},{commerce.OrderId},1,{movement.InventoryTransactionId},10,0,0)"));
        Assert.Equal(51092, error.Number);
    }

    // These are actual synthetic users, terminals, shifts, POS drafts and delivery
    // records in the GUID fixture; no invented ID is used to bypass an origin FK.
    private static async Task<int> CreateActualLegacyDelivery(Web01Case c, int warehouseId)
    {
        await using var db = c.Context();
        var terminal = new POSTerminal { StoreId = c.StoreId, Code = "W01-" + Guid.NewGuid().ToString("N")[..12], Name = "Synthetic delivery origin" };
        db.Add(terminal); await db.SaveChangesAsync();
        var shift = new POSShift { StoreId = c.StoreId, TerminalId = terminal.Id, WarehouseId = warehouseId, OpenedByUserId = c.UserId };
        db.Add(shift); await db.SaveChangesAsync();
        var cart = new Order { StoreId = c.StoreId, POSShiftId = shift.Id, Subtotal = 20, GrandTotal = 20,
            Lines = [new OrderLine { StoreId = c.StoreId, ProductId = c.ProductId, VariantId = c.VariantId, ItemName = "Synthetic delivery line",
                UnitName = "Unit", BaseUnitName = "Unit", Quantity = 1, Multiplier = 1, BaseQuantity = 1, UnitPrice = 20, LineTotal = 20 }] };
        db.Add(cart); await db.SaveChangesAsync();
        var service = new DeliveryFoundationService(db, new Web01Actor(c.UserId, terminalId: terminal.Id),
            new Web01Permissions(c.StoreId, c.UserId, [PermissionCodes.Delivery.View, PermissionCodes.Delivery.Create]));
        await using var tx = await db.Database.BeginTransactionAsync();
        var result = await service.CreateInTransactionAsync(new DeliveryFoundationCreate(Guid.NewGuid(), cart.Id,
            "Synthetic recipient", "0901234567", "Synthetic test address", null));
        await tx.CommitAsync(); return result.Id;
    }
}
