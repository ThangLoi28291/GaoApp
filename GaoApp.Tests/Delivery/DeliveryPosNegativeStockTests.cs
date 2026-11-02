using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD05"), Trait("Category", "DeliveryPosNegativeStock")]
public sealed class DeliveryPosNegativeStockTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Paid_POS_cart_at_zero_stock_finalizes_once_and_keeps_delivery_holds(bool warehouseAllowsNegative)
    {
        using var delivery = await DeliveryD05Case.CreateAsync(fixture, ordered: 2, stock: []);
        await delivery.Picking.ClaimAsync(); await delivery.Picking.ReportAsync("2");
        var variant = await delivery.VariantAsync();
        string heldBefore;
        await using (var db = delivery.Picking.Source.Context())
        {
            var warehouse = await db.Warehouses.SingleAsync(x => x.Id == delivery.Picking.Account.Store.WarehouseId);
            warehouse.AllowNegativeInventory = warehouseAllowsNegative; await db.SaveChangesAsync();
            heldBefore = await Holds(db, delivery.Id);
            var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal(0m, balance.OnHandQty); Assert.Equal(2m, balance.ReservedQty);
        }
        using var sale = await fixture.CaseAsync(false);
        await using (var db = sale.Context())
        {
            var oldLine = await db.OrderLines.Where(x => x.OrderId == sale.CartId && !x.IsDeleted).Select(x => x.Id).SingleAsync();
            await sale.Client.JsonAsync(HttpMethod.Delete, "/admin/pos/lines/" + oldLine);
        }
        var draft = await sale.Client.JsonAsync(HttpMethod.Post, $"/admin/pos/{sale.CartId}/items?variantId={variant}&qty=1");
        var total = draft.GetProperty("grandTotal").GetDecimal(); Assert.True(total > 0);
        await sale.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payments", new
        {
            orderId = sale.CartId, clientRequestId = Guid.NewGuid(), method = 0, amount = total
        });
        await sale.Client.JsonAsync(HttpMethod.Post, $"/admin/pos/{sale.CartId}/finalize");
        using (var retry = await sale.Client.Http.PostAsJsonAsync($"/admin/pos/{sale.CartId}/finalize", new { }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
            Assert.Equal("POS_CART_CURRENT_INVALID_STATUS", (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        }
        await using var verify = sale.Context();
        var order = await verify.Orders.SingleAsync(x => x.Id == sale.CartId);
        Assert.Equal(OrderStatus.Completed, order.Status); Assert.Equal(total, order.PaidTotal); Assert.Equal(0m, order.BalanceDue);
        Assert.Single(await verify.OrderPayments.Where(x => x.OrderId == sale.CartId && !x.IsDeleted).ToArrayAsync());
        var stock = await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        Assert.Equal(-1m, stock.OnHandQty); Assert.Equal(2m, stock.ReservedQty);
        Assert.Equal(heldBefore, await Holds(verify, delivery.Id));
        var issue = Assert.Single(await verify.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Order &&
            x.ReferenceId == sale.CartId.ToString() && x.TransactionType == InventoryTransactionType.SaleIssue).ToArrayAsync());
        Assert.Equal(-1m, issue.QuantityChange); Assert.True(issue.IsProvisionalCost);
        Assert.Empty(await verify.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == delivery.Id).ToArrayAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Legacy_balance_uses_the_same_negative_policy_without_releasing_delivery_holds(bool requestAllowsNegative, bool warehouseAllowsNegative)
    {
        using var delivery = await DeliveryD05Case.CreateAsync(fixture, ordered: 8, stock: [(10m, 10m)]);
        await delivery.Picking.ClaimAsync();
        var variant = await delivery.VariantAsync();
        await using (var db = delivery.Picking.Source.Context())
        {
            var source = await db.DeliveryOrders.SingleAsync(x => x.Id == delivery.Id);
            var line = await db.DeliveryOrderLines.SingleAsync(x => x.DeliveryOrderId == delivery.Id);
            var picking = await db.DeliveryPickingLines.SingleAsync(x => x.DeliveryOrderId == delivery.Id && x.DeliveryOrderLineId == line.Id);
            var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal(0, balance.QuantityPrecisionVersion);
            var warehouse = await db.Warehouses.SingleAsync(x => x.Id == source.SourceWarehouseId);
            warehouse.AllowNegativeInventory = warehouseAllowsNegative;
            picking.ReportedQuantity = 8m / line.BaseMultiplier;
            picking.ReporterUserId = delivery.Picking.Account.UserId; picking.ReportedAtUtc = DateTime.UtcNow;
            picking.ReportFactKind = "picker-report"; picking.ShortageReason = "Persisted legacy report fixture";
            await db.SaveChangesAsync();
            // A persisted legacy hold graph; reporting in the current workflow promotes the balance to policy 1.
            db.InventoryReservations.Add(new InventoryReservation
            {
                StoreId = source.StoreId, WarehouseId = source.SourceWarehouseId, ProductVariantId = variant,
                ReferenceType = InventoryReferenceType.Delivery, ReferenceId = source.Id.ToString(), ReferenceLineId = line.Id,
                DeliveryOrderId = source.Id, DeliveryOrderLineId = line.Id, SourceLegalEntityId = source.SourceLegalEntityId,
                DeliveryRevision = source.Revision, ReportedByUserId = delivery.Picking.Account.UserId,
                ReportedAtUtc = picking.ReportedAtUtc, ReservedQty = 8, Status = InventoryReservationStatus.Active
            });
            balance.ReservedQty = 8; await db.SaveChangesAsync();
        }
        await using var movementDb = delivery.Picking.Source.Context();
        var request = new InventoryMovementFactory().CreateSaleFinalize(delivery.Picking.Account.Store.WarehouseId,
            variant, 99501, 1, "Legacy POS with held delivery stock", 3m, 10m);
        request.AllowNegativeBalance = requestAllowsNegative;
        if (requestAllowsNegative || warehouseAllowsNegative)
            Assert.True((await InventoryPosPostingContractTests.CreateRealMovementService(movementDb).CreateAsync(request)).IsCreated);
        else
            Assert.Contains("DELIVERY_STOCK_PROTECTED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                InventoryPosPostingContractTests.CreateRealMovementService(movementDb).CreateAsync(request))).Message);
        await using var verify = delivery.Picking.Source.Context();
        var result = await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        Assert.Equal(requestAllowsNegative || warehouseAllowsNegative ? 7m : 10m, result.OnHandQty);
        Assert.Equal(8m, result.ReservedQty);
        var hold = await verify.InventoryReservations.SingleAsync(x => x.DeliveryOrderId == delivery.Id);
        Assert.Equal(InventoryReservationStatus.Active, hold.Status); Assert.Equal(8m, hold.ReservedQty); Assert.Null(hold.ReleasedAtUtc);
    }

    private static async Task<string> Holds(GaoApp.Infrastructure.Data.AppDbContext db, int deliveryId)
        => JsonSerializer.Serialize(await db.InventoryReservations.AsNoTracking().Where(x => x.DeliveryOrderId == deliveryId)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.ReservedQty, x.Status, x.ReleasedAtUtc, x.ReleaseNote }).ToArrayAsync());
}
