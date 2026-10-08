using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.DTOs.Promotions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Tests.Inventory;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[CollectionDefinition("DeliveryD08", DisableParallelization = true)]
public sealed class DeliveryD08Collection : ICollectionFixture<DeliveryD02Fixture> { }

// Real POS source, picking, export and departure. All finalization calls use the
// fixture's real authenticated Web/POS dependency graph, never a fake finalizer.
internal sealed class DeliveryD08Case(DeliveryD07Case legacy, FullApplicationFixture.Account closer,
    FullApplicationFixture.Client client, int shiftId, int currentCartId) : IDisposable
{
    internal DeliveryD07Case Legacy => legacy;
    internal DeliveryD06Case Trip => legacy.Trip;
    internal DeliveryD06Order Order => legacy.Order();
    internal int Id => legacy.Id();
    internal FullApplicationFixture.Account Closer => closer;
    internal FullApplicationFixture.Client Client => client;
    internal int ShiftId => shiftId;
    internal int CurrentCartId => currentCartId;
    internal string Path => Trip.Path("handover/" + Id);
    internal AppDbContext Context() => legacy.Context();
    internal static decimal Number(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
    internal static string Text(decimal number) => number.ToString("0.####", CultureInfo.InvariantCulture);

    internal static async Task<DeliveryD08Case> CreateAsync(DeliveryD02Fixture fixture,
        decimal? stock = null, string? promotion = null, bool opening = false)
    {
        var first = opening ? await OpeningOrderAsync(fixture) : promotion is null
            ? new DeliveryD06Order(new DeliveryD05Case(await DeliveryD04Case.CreateAsync(fixture,
                quantity: 4, unitPrice: 20000, customerTier: "RETAIL",
                stockLayers: stock is null ? [(4m, 9000m)] : stock > 0 ? [(stock.Value, 5000m)] : [])))
            : await PromotedOrderAsync(fixture, promotion);
        DeliveryD06Order? second = null;
        DeliveryD06Case? trip = null;
        FullApplicationFixture.Client? closingClient = null;
        try
        {
            if (promotion is null) await first.ReadyAsync();
            second = await DeliveryD06Order.CreateAsync(fixture, quantity: 3, cost: 11000, distinctSource: true);
            await second.ReadyAsync();
            var courier = await fixture.Web.AddAccountAsync(first.Account.Store);
            var other = await fixture.Web.AddAccountAsync(first.Account.Store);
            trip = new(fixture.Web, [first, second], courier, other);
            await trip.CreateDraftAsync(); await trip.HandoverAsync(); await trip.DepartAsync();
            // Origin A is genuinely closed. B has a separate current cart and warehouse.
            await first.Client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 0 });
            var closing = await CounterAsync(fixture.Web, first.Account.Store);
            closingClient = closing.Client;
            return new(new(trip), closing.Account, closingClient, closing.ShiftId, closing.CartId);
        }
        catch
        {
            closingClient?.Dispose();
            if (trip is not null) trip.Dispose(); else { first.Dispose(); second?.Dispose(); }
            throw;
        }
    }

    internal static async Task<DeliveryD06Order> OpeningOrderAsync(DeliveryD02Fixture fixture, bool wrongPair = false)
    {
        var order = new DeliveryD06Order(new DeliveryD05Case(await DeliveryD04Case.CreateAsync(fixture,
            quantity: 4, unitPrice: 20000, customerTier: "RETAIL", stockLayers: [])));
        try
        {
            // Coherent historical Opening, then a real legacy FIFO consumption.
            // Original5 remains immutable; the later promoted budget begins at4.
            await order.Inventory.SeedHistoricalFinalSourceAsync(5, 8761, 43805);
            var variant = await order.Inventory.VariantAsync();
            await using var db = order.Inventory.Picking.Source.Context();
            var layer = await db.InventoryCostLayers.SingleAsync(x => x.ProductVariantId == variant);
            var transaction = await db.InventoryTransactions.SingleAsync(x => x.Id == layer.InventoryTransactionId);
            var valuation = await db.InventoryValuationEntries.SingleAsync(x => x.Id == layer.InventoryValuationEntryId);
            transaction.TransactionType = wrongPair ? InventoryTransactionType.PurchaseReceipt : InventoryTransactionType.OpeningBalance;
            valuation.EntryType = InventoryValuationEntryType.Opening;
            transaction.ReferenceType = valuation.ReferenceType = layer.ReferenceType = InventoryReferenceType.None;
            transaction.ReferenceId = valuation.ReferenceId = layer.ReferenceId = "D08-OPENING-" + order.Id;
            await db.SaveChangesAsync();
            if (!wrongPair)
            {
                var prior = await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
                    new InventoryMovementFactory().CreateAdjustmentDecrease(order.Account.Store.WarehouseId,
                        variant, 1, null, "D08 Opening prior FIFO consumption"));
                Assert.True(prior.IsCreated);
                var allocation = await db.InventoryCostLayerAllocations.AsNoTracking().SingleAsync(x => x.InventoryCostLayerId == layer.Id);
                Assert.False(allocation.IsProvisional); Assert.Equal(1m, allocation.Quantity); Assert.Equal(8761m, allocation.Amount);
                await db.Entry(layer).ReloadAsync(); Assert.Equal((byte)0, layer.CostBudgetPolicy);
                Assert.Equal(5m, layer.OriginalQuantity); Assert.Equal(4m, layer.RemainingQuantity);
            }
            return order;
        }
        catch { order.Dispose(); throw; }
    }

    internal static async Task<(FullApplicationFixture.Account Account, FullApplicationFixture.Client Client,
        int ShiftId, int CartId)> CounterAsync(FullApplicationFixture web, FullApplicationFixture.StoreSeed origin)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var store = origin;
        await using (var db = web.Database.CreateTenantContext(store.StoreId))
        {
            var owner = await db.Warehouses.Where(x => x.Id == origin.WarehouseId).Select(x => x.LegalEntityId).SingleAsync();
            var warehouse = new Warehouse { StoreId = store.StoreId, LegalEntityId = owner,
                Code = "D08-B-" + suffix, Name = "D08 closing warehouse " + suffix, IsActive = true };
            var terminal = new POSTerminal { StoreId = store.StoreId, Code = "D08-B-" + suffix, Name = "D08 till " + suffix };
            db.AddRange(warehouse, terminal); await db.SaveChangesAsync();
            store = store with { WarehouseId = warehouse.Id, TerminalId = terminal.Id };
        }
        var account = await web.AddAccountAsync(store, "*");
        var http = await web.LoginAsync(account);
        try
        {
            var shift = await http.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
            var draft = await http.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
            var cart = draft.GetProperty("orderId").GetInt32();
            await http.JsonAsync(HttpMethod.Post, "/admin/pos/" + cart + "/items?variantId=" + store.VariantId + "&qty=1");
            return (account, http, shift.GetProperty("id").GetInt32(), cart);
        }
        catch { http.Dispose(); throw; }
    }

    internal async Task<DeliveryHandoverWorkspaceDto> WorkspaceAsync(FullApplicationFixture.Client? http = null)
        => (await (http ?? client).Http.GetFromJsonAsync<DeliveryHandoverWorkspaceDto>(Trip.Path("handover")))!;
    internal async Task<DeliveryHandoverRequest> RequestAsync(decimal delivered = 4, string payment = "Cash", Guid? key = null)
    {
        var workspace = await WorkspaceAsync();
        var row = workspace.Trip.Orders.Single(x => x.DeliveryOrderId == Id);
        var line = Assert.Single(row.Lines);
        var exported = Number(line.DispatchedBaseQuantityText);
        var lines = new[] { new DeliveryHandoverLineRequest(line.LineId, Text(delivered), Text(exported - delivered)) };
        return await RequestLinesAsync(workspace, lines, payment, key);
    }
    internal async Task<DeliveryHandoverRequest> RequestLinesAsync(DeliveryHandoverWorkspaceDto workspace,
        IReadOnlyList<DeliveryHandoverLineRequest> lines, string payment = "Cash", Guid? key = null)
    {
        var row = workspace.Trip.Orders.Single(x => x.DeliveryOrderId == Id);
        var preview = await PreviewAsync(new(workspace.Trip.Version, row.Version, lines));
        var total = preview.PayableText;
        var delivered = lines.Sum(x => Number(x.DeliveredBaseQuantityText));
        var exported = row.Lines.Sum(x => Number(x.DispatchedBaseQuantityText));
        return new(key ?? Guid.NewGuid(), workspace.Trip.Version, row.Version,
            closer.Store.TerminalId, shiftId, Trip.Courier.UserId, true,
            delivered == exported ? "Full" : delivered == 0 ? "NotDelivered" : "Partial",
            delivered == exported ? null : "Quầy kiểm đếm hàng khách không nhận", null, lines,
            payment, payment == "Cash" ? total : "0", payment == "Cash" ? total : "0",
            payment == "BankTransfer" ? total : "0", payment == "BankTransfer");
    }
    internal async Task<DeliveryHandoverPreviewDto> PreviewAsync(DeliveryHandoverPreviewRequest body)
    {
        using var response = await client.Http.PostAsJsonAsync(Path + "/preview", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DeliveryHandoverPreviewDto>())!;
    }
    internal async Task<DeliveryHandoverAck> ConfirmAsync(DeliveryHandoverRequest body, FullApplicationFixture.Client? http = null)
    {
        using var response = await (http ?? client).Http.PostAsJsonAsync(Path, body);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, (int)response.StatusCode + " " + raw);
        return JsonSerializer.Deserialize<DeliveryHandoverAck>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    internal async Task RejectAsync(object body, HttpStatusCode status, string? code = null,
        FullApplicationFixture.Client? http = null)
    {
        using var response = await (http ?? client).Http.PostAsJsonAsync(Path, body);
        Assert.Equal(status, response.StatusCode);
        if (code is not null) Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
    internal async Task<int> BankAsync()
    {
        await using var db = Context();
        var bank = new StoreBankAccount { StoreId = closer.Store.StoreId, BankCode = "ACB", BankName = "ACB",
            AccountNumber = "D08-" + Guid.NewGuid().ToString("N"), AccountName = "Synthetic closing account", IsActive = true };
        db.StoreBankAccounts.Add(bank); await db.SaveChangesAsync(); return bank.Id;
    }
    internal async Task<int> CustomerAsync(bool eligible)
    {
        await using var db = Context();
        var id = (await db.DeliveryOrders.SingleAsync(x => x.Id == Id)).CustomerId!.Value;
        var customer = await db.Customers.SingleAsync(x => x.Id == id); customer.HaveDebt = eligible;
        await db.SaveChangesAsync(); return id;
    }
    internal async Task<Order> SaleAsync(DeliveryHandoverAck ack)
    {
        Assert.NotNull(ack.Settlement.SaleOrderId);
        await using var db = Context();
        return await db.Orders.AsNoTracking().Include(x => x.Lines).Include(x => x.Payments)
            .SingleAsync(x => x.Id == ack.Settlement.SaleOrderId.Value);
    }
    internal async Task<string> SnapshotAsync()
    {
        await using var db = Context();
        return JsonSerializer.Serialize(new {
            Legacy = await legacy.SnapshotAsync(),
            Lines = await db.OrderLines.AsNoTracking().OrderBy(x => x.Id).Select(x => new {
                x.Id, x.OrderId, x.VariantId, x.Quantity, x.BaseQuantity, x.LineTotal, x.LineCostTotal, x.IsProvisionalCost }).ToArrayAsync(),
            Payments = await db.OrderPayments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Shifts = await db.POSShifts.AsNoTracking().OrderBy(x => x.Id).Select(x => new {
                x.Id, x.Status, x.CurrentOrderId, x.CashSalesTotal, x.NonCashSalesTotal }).ToArrayAsync(),
            Debt = await db.Set<CustomerReceivableEntry>().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Rewards = await db.CustomerRewardLedgers.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Qr = await db.PosPaymentQrRequests.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync() });
    }
    internal async Task<string> DispatchProofAsync()
    {
        await using var db = Context();
        var ids = await db.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == Id).Select(x => x.InventoryTransactionId).Distinct().ToArrayAsync();
        return JsonSerializer.Serialize(new {
            Transactions = await db.InventoryTransactions.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(),
            Fragments = await db.DeliveryDispatchCostFragments.AsNoTracking().Where(x => x.DeliveryOrderId == Id).OrderBy(x => x.Id).ToArrayAsync() });
    }
    internal async Task<string> ClosingCartAsync()
    {
        await using var db = Context();
        return JsonSerializer.Serialize(new {
            Cart = await db.Orders.AsNoTracking().Where(x => x.Id == currentCartId).Select(x => new { x.Id, x.Status, x.GrandTotal, x.RowVersion }).SingleAsync(),
            Lines = await db.OrderLines.AsNoTracking().Where(x => x.OrderId == currentCartId).OrderBy(x => x.Id).ToArrayAsync(),
            Stock = await db.InventoryBalances.AsNoTracking().Where(x => x.WarehouseId == closer.Store.WarehouseId).OrderBy(x => x.Id).ToArrayAsync() });
    }
    public void Dispose() { client.Dispose(); legacy.Dispose(); }

    private static async Task<DeliveryD06Order> PromotedOrderAsync(DeliveryD02Fixture fixture, string kind)
    {
        var source = await fixture.CaseAsync(false);
        try
        {
            int first, second, conversionA, conversionB, productId, customerId, voucherId;
            await using (var db = source.Context())
            {
                var template = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == source.Account.Store.VariantId);
                var suffix = Guid.NewGuid().ToString("N")[..12];
                var product = new Product { StoreId = source.Account.Store.StoreId, Name = "D08 promo " + suffix, Alias = "d08-" + suffix,
                    CategoryId = template.Product.CategoryId, SupplierId = template.Product.SupplierId,
                    BaseUnitId = template.Product.BaseUnitId, BasePrice = 10000, IsActive = true, IsSellable = true };
                db.Products.Add(product); await db.SaveChangesAsync(); productId = product.Id;
                var a = new ProductVariant { StoreId = product.StoreId, ProductId = product.Id, Sku = "D08-A-" + suffix, Price = 10000, IsActive = true };
                var b = new ProductVariant { StoreId = product.StoreId, ProductId = product.Id, Sku = "D08-B-" + suffix, Price = 10000, IsActive = true };
                db.AddRange(a, b); await db.SaveChangesAsync(); first = a.Id; second = b.Id;
                var ca = new ProductUnitConversion { StoreId = product.StoreId, ProductVariantId = first, UnitId = product.BaseUnitId,
                    Factor = 1, Price = 10000, IsBaseUnit = true, IsDefaultForSale = true, IsActive = true };
                var cb = new ProductUnitConversion { StoreId = product.StoreId, ProductVariantId = second, UnitId = product.BaseUnitId,
                    Factor = 1, Price = 10000, IsBaseUnit = true, IsDefaultForSale = true, IsActive = true };
                db.AddRange(ca, cb); await db.SaveChangesAsync(); conversionA = ca.Id; conversionB = cb.Id;
                foreach (var id in new[] { first, second }) await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
                    new InventoryMovementFactory().CreatePurchaseReceipt(source.Account.Store.WarehouseId, id, 10, 5000, "D08-STOCK-" + source.CartId, id, "Synthetic source", id));
                var customer = new Customer { StoreId = product.StoreId, Name = "D08 promo customer " + suffix, PriceTier = "RETAIL" };
                db.Customers.Add(customer); await db.SaveChangesAsync(); customerId = customer.Id;
                var voucher = new CustomerRewardVoucher { StoreId = product.StoreId, CustomerId = customerId, VoucherCode = "D08-" + suffix,
                    Value = 3000, Status = CustomerRewardVoucherStatus.Available, IssuedAtUtc = DateTime.UtcNow, RequiredAmount = 0 };
                db.CustomerRewardVouchers.Add(voucher); await db.SaveChangesAsync(); voucherId = voucher.Id;
            }
            var promo = new SavePromotionRequest { Name = "D08 " + kind + " " + source.CartId,
                Type = kind == "combo" ? PromotionType.ComboFixedPrice : PromotionType.BuyXGetY,
                ComboFixedPrice = kind == "combo" ? 15000 : null, BuyQuantity = kind == "gift" ? 2 : null,
                GetQuantity = kind == "gift" ? 1 : null, RequireGiftQuantityInCart = true, IsActive = true,
                StartAtUtc = DateTime.UtcNow.AddDays(-1), EndAtUtc = DateTime.UtcNow.AddDays(1), Priority = 100 };
            if (kind == "combo") promo.ComboRules = [new() { ProductId = productId, VariantId = first, RequiredQuantity = 1 }, new() { ProductId = productId, VariantId = second, RequiredQuantity = 1 }];
            else promo.Items = [new() { ProductId = productId, VariantId = first, MinQuantity = 1 }];
            var saved = await source.Client.JsonAsync(HttpMethod.Post, "/admin/promotion/save", promo); Assert.True(saved.GetProperty("success").GetBoolean());
            await using (var db = source.Context())
            {
                var line = await db.OrderLines.Where(x => x.OrderId == source.CartId && !x.IsDeleted).Select(x => x.Id).SingleAsync();
                await source.Client.JsonAsync(HttpMethod.Delete, "/admin/pos/lines/" + line);
            }
            // Current POS takes paid quantity and creates the separate free gift line.
            await source.Client.JsonAsync(HttpMethod.Post, $"/admin/pos/{source.CartId}/items?variantId={first}&productUnitConversionId={conversionA}&qty={(kind == "gift" ? 2 : 1)}");
            if (kind == "combo") await source.Client.JsonAsync(HttpMethod.Post, $"/admin/pos/{source.CartId}/items?variantId={second}&productUnitConversionId={conversionB}&qty=1");
            await source.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/customer/" + customerId, new { repriceExistingLines = false });
            await source.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/discount", new { discountAmount = 1000 });
            await source.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/reward-vouchers", new { voucherIds = new[] { voucherId } });
            source.Detail = (await DeliveryD03Support.Create(source)).Delivery;
            var picking = new DeliveryD04Case(source); var detail = await picking.ClaimAsync();
            await picking.CommandAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version,
                detail.Lines.Where(x => x.IsActive).Select(x => new DeliveryPickingReportLine(x.LineId, x.PlannedQuantityText, null)).ToArray()));
            detail = await picking.SubmitAsync(); if (detail.Delivery.State == "AwaitingApproval") await picking.ApproveAsync();
            return new(new DeliveryD05Case(picking));
        }
        catch { source.Dispose(); throw; }
    }
}
