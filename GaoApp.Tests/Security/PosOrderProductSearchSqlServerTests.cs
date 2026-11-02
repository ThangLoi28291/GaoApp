using System.Net;
using System.Diagnostics;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Delivery;
using GaoApp.Tests.Configuration;
using GaoApp.Infrastructure.Repositories.Orders;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PosOrderProductSearchSqlServerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Product_search_matches_sold_and_current_names_skus_barcodes_before_paging_with_tenant_isolation()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var foreign = app.Stores[1];
        var account = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var foreignAccount = await app.AddAccountAsync(foreign, PermissionCodes.Pos.Order.View);
        int firstId, secondId, otherId, foreignId;
        var completedAt = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.Include(v => v.Product).SingleAsync(v => v.Id == store.VariantId);
            variant.Sku = "CURRENT-BOX";
            variant.ProductVariantName = "Tên biến thể hiện tại";
            variant.IsActive = false;
            variant.Product.Name = "Danh mục hiện tại";
            variant.Product.IsActive = false;
            var otherVariant = new ProductVariant { StoreId = store.StoreId, ProductId = variant.ProductId, Sku = "OTHER-ITEM" };
            var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id,
                UnitId = variant.Product.BaseUnitId, Factor = 1, IsActive = false };
            conversion.Barcodes.Add(new ProductVariantUnitBarcode { StoreId = store.StoreId,
                Barcode = "CURRENT-BARCODE", IsActive = false });
            var shift = new POSShift { StoreId = store.StoreId, TerminalId = store.TerminalId,
                WarehouseId = store.WarehouseId, OpenedByUserId = account.UserId };
            var customer = new Customer { StoreId = store.StoreId, Name = "Khách tìm sản phẩm", Code = "KH-PRODUCT" };
            Order NewOrder(string number, OrderStatus status = OrderStatus.Completed) => new()
            {
                StoreId = store.StoreId, POSShift = shift, Customer = customer, OrderNumber = number,
                Status = status, CompletedAtUtc = completedAt, GrandTotal = 100, PaidTotal = 100
            };
            OrderLine Line(string name) => new() { StoreId = store.StoreId, ProductId = variant.ProductId,
                VariantId = variant.Id, ItemName = name, Sku = "SOLD-DAU", Barcode = "8930000000001",
                ScannedBarcode = "SCAN-DAU-01", Quantity = 1, UnitPrice = 100, LineTotal = 100 };
            var first = NewOrder("PRODUCT-ONE"); first.Note = "Ghi chú cần tìm";
            first.Lines.Add(Line("Sữa Đậu Nành"));
            first.Lines.Add(Line("Sữa Đậu Nành hộp lớn")); // Multiple matches still count as one invoice.
            var second = NewOrder("PRODUCT-TWO", OrderStatus.Refunded);
            second.Lines.Add(Line("sữa đậu nành"));
            var other = NewOrder("PRODUCT-OTHER");
            other.Lines.Add(new OrderLine { StoreId = store.StoreId, ProductId = variant.ProductId,
                Variant = otherVariant, ItemName = "Bánh quy", Quantity = 1, LineTotal = 100 });
            var removedLine = Line("Sản phẩm chỉ đã xóa");
            other.Lines.Add(removedLine);
            db.AddRange(first, second, other, conversion);
            await db.SaveChangesAsync();
            firstId = first.Id; secondId = second.Id; otherId = other.Id;
            await db.OrderLines.Where(l => l.Id == removedLine.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.IsDeleted, true));
        }
        await using (var db = app.Database.CreateTenantContext(foreign.StoreId))
        {
            var variant = await db.ProductVariants.SingleAsync(v => v.Id == foreign.VariantId);
            var shift = new POSShift { StoreId = foreign.StoreId, TerminalId = foreign.TerminalId,
                WarehouseId = foreign.WarehouseId, OpenedByUserId = foreignAccount.UserId };
            var order = new Order { StoreId = foreign.StoreId, POSShift = shift, OrderNumber = "PRODUCT-FOREIGN",
                Status = OrderStatus.Completed, CompletedAtUtc = completedAt,
                Lines = [new OrderLine { StoreId = foreign.StoreId, ProductId = variant.ProductId, VariantId = variant.Id,
                    ItemName = "Sữa Đậu Nành", Sku = "SOLD-DAU", Barcode = "8930000000001", Quantity = 1 }] };
            db.Add(order);
            // A malformed foreign child must not make a current-store invoice match.
            db.Add(new OrderLine { StoreId = foreign.StoreId, OrderId = otherId, ProductId = variant.ProductId,
                VariantId = variant.Id, ItemName = "Chỉ cửa hàng khác", Quantity = 1 });
            await db.SaveChangesAsync(); foreignId = order.Id;
        }
        using var client = await app.LoginAsync(account);
        async Task<JsonElement> Search(string term, string extra = "") => await client.JsonAsync(HttpMethod.Get,
            "/admin/pos/orders?product=" + Uri.EscapeDataString(term) + extra);
        async Task<int[]> Ids(string term, string extra = "") => (await Search(term, extra)).GetProperty("items")
            .EnumerateArray().Select(o => o.GetProperty("orderId").GetInt32()).Order().ToArray();
        var expected = new[] { firstId, secondId }.Order().ToArray();
        foreach (var term in new[] { "Sữa", "sua", "Sữa Đậu Nành", "sua dau nanh", " SUA DAU NANH ", "sold-dau",
                     "8930000000001", "scan-dau-01", "CURRENT-BOX", "ten bien the hien tai", "CURRENT-BARCODE" })
            Assert.Equal(expected, await Ids(term));
        Assert.Equal(new[] { firstId, secondId, otherId }.Order(), await Ids("danh muc hien tai"));
        var page1 = await Search("sua dau nanh", "&page=1&pageSize=1");
        var page2 = await Search("sua dau nanh", "&page=2&pageSize=1");
        Assert.Equal(2, page1.GetProperty("totalItems").GetInt32());
        Assert.Equal(2, page2.GetProperty("totalItems").GetInt32());
        Assert.NotEqual(Assert.Single(page1.GetProperty("items").EnumerateArray()).GetProperty("orderId").GetInt32(),
            Assert.Single(page2.GetProperty("items").EnumerateArray()).GetProperty("orderId").GetInt32());
        Assert.Empty(await Ids("sua dau nanh", "&page=3&pageSize=1"));
        Assert.Equal(new[] { firstId }, await Ids("sua dau nanh", $"&keyword=ONE&customer=KH-PRODUCT&employeeId={account.UserId}&terminalId={store.TerminalId}&status=Completed&fromDate=2026-10-10&toDate=2026-10-10"));
        Assert.Empty(await Ids("sua dau nanh", "&customer=does-not-exist"));
        Assert.Empty(await Ids("san pham chi da xoa"));
        Assert.Empty(await Ids("chi cua hang khac"));
        Assert.Empty(await Ids("%")); // Search text is literal, not a LIKE pattern.
        Assert.Equal(3, (await Search("   ")).GetProperty("totalItems").GetInt32());
        using var invalid = await client.Http.GetAsync("/admin/pos/orders?product=" + new string('x', 321));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var outsider = await app.LoginAsync(foreignAccount);
        var foreignResult = await outsider.JsonAsync(HttpMethod.Get, "/admin/pos/orders?product=sua%20dau%20nanh");
        Assert.Equal(foreignId, Assert.Single(foreignResult.GetProperty("items").EnumerateArray()).GetProperty("orderId").GetInt32());
        var html = WebUtility.HtmlDecode(await client.Http.GetStringAsync("/admin/pos/orders-page?product=sua%20dau%20nanh"));
        Assert.Contains("label for=\"product\"", html);
        Assert.Contains("Tên sản phẩm, SKU hoặc mã vạch", html);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            await db.ProductVariants.Where(v => v.Id == store.VariantId).ExecuteUpdateAsync(s => s.SetProperty(v => v.IsDeleted, true));
            await db.Products.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true));
        }
        Assert.Equal(expected, await Ids("sua dau nanh"));
        Assert.Equal(expected, await Ids("SOLD-DAU"));
        Assert.Equal(expected, await Ids("8930000000001"));
    }

    [Fact]
    public async Task Delivery_product_search_uses_actual_sale_lines_after_settlement_and_source_lines_before_settlement()
    {
        await using var app = await FullApplicationFixture.StartDeliveryAsync();
        using var settled = await DeliveryBillPresentationSeed.CreateAsync(app, state: DeliveryState.Settled);
        using var pending = await DeliveryBillPresentationSeed.CreateAsync(app);
        var store = settled.Account.Store;
        int saleId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var source = await db.Orders.SingleAsync(o => o.Id == settled.OrderId);
            var variant = await db.ProductVariants.SingleAsync(v => v.Id == store.VariantId);
            var sale = new Order { StoreId = store.StoreId, POSShiftId = source.POSShiftId, OrderNumber = "PRODUCT-DELIVERED",
                Status = OrderStatus.Completed, CompletedAtUtc = DateTime.UtcNow, GrandTotal = 100,
                Lines = [new OrderLine { StoreId = store.StoreId, ProductId = variant.ProductId, VariantId = variant.Id,
                    ItemName = "Đường thực giao", Sku = "DELIVERED-SKU", Quantity = 1, UnitPrice = 100, LineTotal = 100 }] };
            db.Add(sale); await db.SaveChangesAsync(); saleId = sale.Id;
            var delivery = await db.DeliveryOrders.SingleAsync(d => d.Id == settled.DeliveryId);
            db.Add(new DeliveryJournalEntry { StoreId = store.StoreId, DeliveryOrderId = delivery.Id,
                SourceWarehouseId = delivery.SourceWarehouseId, SourceLegalEntityId = delivery.SourceLegalEntityId,
                Kind = DeliveryJournalKind.Settlement, PostingKey = "product-search:" + delivery.Id, SaleOrderId = saleId, MoneyAmount = 100 });
            await db.SaveChangesAsync();
        }
        async Task<JsonElement> Search(string term) => await settled.Client.JsonAsync(HttpMethod.Get,
            "/admin/pos/orders?deliveryOnly=true&product=" + Uri.EscapeDataString(term));
        var actual = await Search("duong thuc giao");
        Assert.Equal(1, actual.GetProperty("totalItems").GetInt32());
        Assert.Equal(saleId, Assert.Single(actual.GetProperty("items").EnumerateArray()).GetProperty("orderId").GetInt32());
        var quote = await Search("sua doi");
        Assert.Equal(1, quote.GetProperty("totalItems").GetInt32());
        Assert.Equal(pending.OrderId, Assert.Single(quote.GetProperty("items").EnumerateArray()).GetProperty("orderId").GetInt32());
    }

    [Fact]
    public async Task Partial_milk_search_returns_exact_results_with_350000_invoices_1400000_lines_and_32000_variants_within_ui_deadline()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var store = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(store.StoreId);
        db.Database.SetCommandTimeout(180);
        var milk = await db.ProductVariants.SingleAsync(v => v.Id == store.ProductVariantId);
        milk.ProductVariantName = "Sữa tươi hộp 180ml";
        var others = Enumerable.Range(1, 31999).Select(i => new ProductVariant
        {
            StoreId = store.StoreId, ProductId = milk.ProductId, Sku = "BENCH-" + i,
            ProductVariantName = "Hàng thường số " + i
        }).ToArray();
        db.ProductVariants.AddRange(others);
        var terminal = new POSTerminal { StoreId = store.StoreId, Code = "PRODUCT-BENCH", Name = "Product benchmark" };
        var shift = new POSShift { StoreId = store.StoreId, Terminal = terminal, WarehouseId = store.WarehouseId, OpenedByUserId = 1 };
        var seed = new Order { StoreId = store.StoreId, POSShift = shift, OrderNumber = "PRODUCT-BENCH-0",
            Status = OrderStatus.Completed, GrandTotal = 100, CompletedAtUtc = DateTime.UtcNow, Note = new string('x', 450),
            Lines = [new OrderLine { StoreId = store.StoreId, ProductId = milk.ProductId, VariantId = milk.Id,
                ItemName = "Sữa tươi hộp 180ml", Quantity = 1, UnitPrice = 100, LineTotal = 100 }] };
        db.Add(seed); await db.SaveChangesAsync();
        var usedVariants = new[] { milk.Id }.Concat(others.Take(63).Select(v => v.Id)).ToArray();
        var seedLineId = seed.Lines.Single().Id;
        var orderColumns = Columns<Order>(db);
        var orderSelect = orderColumns.Select(c => c == "OrderNumber" ? "CONCAT(N'PRODUCT-BENCH-', n.i)" : $"s.[{c}]");
        // Identifiers come only from EF's trusted model; all seed values are parameters.
        var orderInsertSql = $$"""
            WITH n AS (SELECT TOP (349999) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO dbo.Orders ({{string.Join(",", orderColumns.Select(c => $"[{c}]"))}})
            SELECT {{string.Join(",", orderSelect)}} FROM n CROSS JOIN dbo.Orders s WHERE s.Id = {0};
            """;
        await db.Database.ExecuteSqlRawAsync(orderInsertSql, seed.Id);
        var lineColumns = Columns<OrderLine>(db);
        var lineSelect = lineColumns.Select(c => c switch
        {
            "OrderId" => "o.Id",
            "VariantId" => "CONVERT(int, v.[value])",
            "ItemName" => "CASE WHEN CONVERT(int, v.[key])=0 THEN N'Sữa tươi hộp 180ml' WHEN o.Id%997=0 THEN N'Sữa tươi tên cũ' ELSE N'Hàng thường' END",
            _ => $"s.[{c}]"
        });
        var lineInsertSql = $$"""
            INSERT INTO dbo.OrderLines ({{string.Join(",", lineColumns.Select(c => $"[{c}]"))}})
            SELECT {{string.Join(",", lineSelect)}} FROM dbo.Orders o
            CROSS JOIN (VALUES (0),(1),(2),(3)) g(n)
            JOIN OPENJSON({1}) v ON CONVERT(int, v.[key])=((o.Id-{2})*4+g.n)%64
            CROSS JOIN dbo.OrderLines s WHERE s.Id={0} AND o.StoreId={3};
            """;
        await db.Database.ExecuteSqlRawAsync(lineInsertSql, seedLineId, JsonSerializer.Serialize(usedVariants), seed.Id, store.StoreId);
        db.ChangeTracker.Clear();
        var expected = await db.Database.SqlQuery<int>($"""
            SELECT COUNT(DISTINCT OrderId) AS [Value] FROM dbo.OrderLines
            WHERE StoreId={store.StoreId} AND (VariantId={milk.Id} OR ItemName=N'Sữa tươi tên cũ')
            """).SingleAsync();
        var repo = new OrderRepository(db);
        foreach (var term in new[] { "sữa tươi", "sua tuoi" })
        {
            var watch = Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await repo.QueryOrdersAsync(null, null, null, null, 1, 20, timeout.Token,
                new GaoApp.Application.DTOs.POS.OrderListQueryDto { Product = term });
            output.WriteLine($"350,000 invoices / 1,400,001 lines / 32,000 variants; '{term}': {result.Total} matches in {watch.Elapsed.TotalMilliseconds:F1} ms.");
            Assert.Equal(expected, result.Total); Assert.Equal(20, result.Items.Count);
            Assert.Equal(20, result.Items.Select(o => o.OrderId).Distinct().Count());
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
        }
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static string[] Columns<TEntity>(GaoApp.Infrastructure.Data.AppDbContext db) => db.Model.FindEntityType(typeof(TEntity))!
        .GetProperties().Where(p => p.Name != "Id" && !p.IsConcurrencyToken && p.GetComputedColumnSql() == null)
        .Select(p => p.GetColumnName()).ToArray();
}
