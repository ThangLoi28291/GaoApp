using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosSkuCapacitySqlServerTests
{
    // The first pending scan in the reported client journal has this valid 53-character SKU.
    private const string ReportedSku = "nuoc-giat-xa-power-huong-nuoc-hoa-phap-dam-me-tui-3kg";

    [Theory]
    [InlineData(ReportedSku)]
    [InlineData("123456789012345678901234567890123456789012345678901234567890")]
    public async Task Offline_scan_preserves_full_catalog_sku_and_retries_without_duplicate_lines(string sku)
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var orderId = (await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/ensure", new { })).GetProperty("orderId").GetInt32();
        int shiftId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
            variant.Sku = sku;
            var product = await db.Products.SingleAsync(x => x.Id == variant.ProductId);
            var conversion = new ProductUnitConversion
            {
                StoreId = store.StoreId, ProductVariantId = variant.Id, UnitId = product.BaseUnitId,
                Factor = 1, IsBaseUnit = true, IsDefaultForSale = true, Price = 20,
                Barcodes = [new ProductVariantUnitBarcode { StoreId = store.StoreId, Barcode = "8934988560459" }]
            };
            db.Add(conversion);
            await db.SaveChangesAsync();
            shiftId = (await db.POSShifts.SingleAsync()).Id;
        }

        var operationId = Guid.NewGuid();
        for (var retry = 0; retry < 2; retry++)
        {
            var (status, result) = await Scan(client, orderId, shiftId, operationId);
            Assert.True(status == HttpStatusCode.OK, $"Scan returned {(int)status}: {result}");
            var line = Assert.Single(result.GetProperty("lines").EnumerateArray());
            Assert.Equal(sku, line.GetProperty("sku").GetString());
            Assert.Equal(1m, line.GetProperty("quantity").GetDecimal());
        }

        await using var check = app.Database.CreateTenantContext(store.StoreId);
        var saved = await check.OrderLines.SingleAsync(x => x.OrderId == orderId);
        Assert.Equal(sku, saved.Sku);
        Assert.Equal(1m, saved.Quantity);
        Assert.Single(await check.Set<PosOperationReceipt>().Where(x => x.OperationId == operationId).ToListAsync());
        Assert.Empty(await check.OrderPayments.Where(x => x.OrderId == orderId).ToListAsync());
        Assert.Equal(OrderStatus.Draft, (await check.Orders.SingleAsync(x => x.Id == orderId)).Status);
        Assert.Equal(100m, (await check.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
    }

    [Fact]
    public async Task Widening_migration_preserves_sales_data_and_refuses_a_lossy_downgrade()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync("20261008180000_AllowDeliveryOpeningCostSources");
        var catalog = await database.SeedInventoryCatalogAsync();
        await using (var db = database.CreateTenantContext(catalog.StoreId))
        {
            var terminal = new POSTerminal { StoreId = catalog.StoreId, Code = "SKU-TEST", Name = "SKU test" };
            db.Add(terminal); await db.SaveChangesAsync();
            var shift = new POSShift { StoreId = catalog.StoreId, TerminalId = terminal.Id,
                WarehouseId = catalog.WarehouseId, OpenedByUserId = 1, Status = POSShiftStatus.Open };
            db.Add(shift); await db.SaveChangesAsync();
            var order = new Order { StoreId = catalog.StoreId, POSShiftId = shift.Id, Status = OrderStatus.Draft,
                Lines = [new OrderLine { StoreId = catalog.StoreId, VariantId = catalog.ProductVariantId,
                    ItemName = "Existing sale", Sku = "EXISTING-SKU", Quantity = 1, UnitPrice = 20, LineTotal = 20 }] };
            db.Add(order); await db.SaveChangesAsync();
            db.Add(new OrderLine { StoreId = catalog.StoreId, OrderId = order.Id, VariantId = catalog.ProductVariantId,
                ItemName = "Reported scan", Sku = ReportedSku, Quantity = 1, UnitPrice = 20, LineTotal = 20 });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var sqlError = Assert.IsType<SqlException>(failure.InnerException);
            Assert.Contains(sqlError.Errors.Cast<SqlError>(), x => x.Number is 8152 or 2628);
        }

        await database.MigrateAsync("20261009060000_WidenOrderLineSku");
        await using (var db = database.CreateTenantContext(catalog.StoreId))
        {
            var line = await db.OrderLines.SingleAsync();
            Assert.Equal("EXISTING-SKU", line.Sku);
            Assert.Equal(1m, line.Quantity);
            Assert.Equal(20m, line.LineTotal);
            line.Sku = ReportedSku;
            await db.SaveChangesAsync();
        }
        var downgrade = await Assert.ThrowsAsync<SqlException>(() => database.MigrateAsync("20261008180000_AllowDeliveryOpeningCostSources"));
        Assert.Equal(51030, downgrade.Number);
        await using var check = database.CreateTenantContext(catalog.StoreId);
        Assert.Equal(ReportedSku, (await check.OrderLines.SingleAsync()).Sku);
        Assert.Contains("20261009060000_WidenOrderLineSku", await check.Database.GetAppliedMigrationsAsync());
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Scan(
        FullApplicationFixture.Client client, int orderId, int shiftId, Guid operationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/pos/cart/current/scan")
        {
            Content = JsonContent.Create(new { barcode = "8934988560459", quantity = 1 })
        };
        request.Headers.Add("X-POS-Operation-Id", operationId.ToString());
        request.Headers.Add("X-POS-Shift-Id", shiftId.ToString());
        request.Headers.Add("X-POS-Expected-Order-Id", orderId.ToString());
        request.Headers.Add("X-POS-Offline", "1");
        request.Headers.Add("X-POS-Occurred-At", DateTime.UtcNow.AddMinutes(-5).ToString("O"));
        using var response = await client.Http.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }
}
