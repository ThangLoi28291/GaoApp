using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Tests.Security;
using GaoApp.Web.Services.StoreMonitor;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

// Cross-module regression: actual POS pricing, SQL inventory, authenticated HTTP
// picking/counter commands, unified POS history and the office projection together.
[Collection("DeliveryD02"), Trait("Category", "DeliveryD11")]
public sealed class DeliveryD11WorkflowTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData("line", "Cash", 20, 2, 16000)]
    [InlineData("line", "BankTransfer", 20, 2, 16000)]
    [InlineData("line", "CustomerCredit", 20, 2, 16000)]
    [InlineData("none", "Cash", 0, 2, 20000)]
    [InlineData("line", "Cash", 20, 0, 0)]
    public async Task POS_quote_to_other_counter_preserves_prices_stock_single_history_and_office_completion(
        string promotion, string payment, int initialStock, int delivered, int payable)
    {
        using var priced = await DeliveryPriceStockCase.PrepareAsync(fixture, promotion, stock: initialStock);
        var source = priced.Source;
        await using (var seed = source.Context())
        {
            var customer = new Customer { StoreId = source.Account.Store.StoreId,
                Name = "D11 customer " + source.CartId, Phone = "0901234567", PriceTier = "RETAIL" };
            seed.Customers.Add(customer); await seed.SaveChangesAsync();
            await source.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/customer/" + customer.Id,
                new { repriceExistingLines = false });
        }
        await priced.ReloadPriceAsync();
        var officeAccount = await fixture.Web.AddAccountAsync(source.Account.Store, PermissionCodes.Admin.StoreMonitorView);
        using var office = await fixture.Web.LoginAsync(officeAccount);
        var createRequest = await DeliveryD03Support.Request(source);
        var created = await priced.CreateAsync(createRequest);
        Assert.Equal(priced.PricedOrder.GrandTotal, created.Delivery.QuotedTotal);
        Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(await priced.CreateAsync(createRequest)));
        await OfficeStateAsync(office, created.Delivery.Id, "Created");
        var bill = WebUtility.HtmlDecode(await source.Client.Http.GetStringAsync(created.BillUrl));
        Assert.Contains("deliveryQr", bill); Assert.Contains("Phiếu giao A5", bill);
        Assert.Contains("30.000", bill);
        if (promotion == "line") { Assert.Contains("Khuyến mãi sản phẩm", bill); Assert.Contains("6.000", bill); Assert.Contains("24.000", bill); }
        await using (var db = source.Context())
        {
            var pending = Assert.Single((await new OrderRepository(db).QueryOrdersAsync(null, null, null,
                created.Delivery.Code, 1, 20)).Items);
            Assert.Equal(source.CartId, pending.OrderId); Assert.NotEqual("Cancelled", pending.DisplayStatus);
            Assert.Empty(await db.OrderPayments.Where(x => x.OrderId == source.CartId).ToArrayAsync());
            Assert.Equal(initialStock, await StockAsync(priced));
        }
        var financeBefore = await priced.Inventory.FinanceAsync();
        await priced.ReadyAsync();
        await OfficeStateAsync(office, created.Delivery.Id, "ReadyForHandover");
        Assert.Equal(initialStock, await StockAsync(priced));
        Assert.Equal(financeBefore, await priced.Inventory.FinanceAsync());

        var courier = await fixture.Web.AddAccountAsync(source.Account.Store);
        var otherCourier = await fixture.Web.AddAccountAsync(source.Account.Store);
        using var trip = new DeliveryD06Case(fixture.Web, [new(priced.Inventory)], courier, otherCourier);
        await trip.CreateDraftAsync();
        var handover = await trip.HandoverRequestAsync();
        await PostTripAsync(source.Client, trip.Path("handover"), handover);
        await OfficeStateAsync(office, created.Delivery.Id, "HandedOver");
        Assert.Equal(initialStock - 3, await StockAsync(priced));
        var departure = new DeliveryTripDepartureRequest(Guid.NewGuid(), (await trip.DetailAsync()).Version,
            await trip.SelectionsAsync());
        await PostTripAsync(source.Client, trip.Path("depart"), departure);
        await OfficeStateAsync(office, created.Delivery.Id, "Delivering");
        Assert.Equal(financeBefore, await priced.Inventory.FinanceAsync());

        // Origin A is closed. The one merged confirmation is performed by B with
        // its own open shift and an unrelated current cart/warehouse.
        await source.Client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 0 });
        var closing = await DeliveryD08Case.CounterAsync(fixture.Web, source.Account.Store);
        using var counter = new DeliveryD08Case(new(trip), closing.Account, closing.Client, closing.ShiftId, closing.CartId);
        var cartBefore = await counter.ClosingCartAsync();
        var dispatchBefore = await counter.DispatchProofAsync();
        var workspace = await counter.WorkspaceAsync();
        Assert.Null(workspace.Trip.ReturnToStore);
        Assert.Null(Assert.Single(workspace.Trip.Orders).Outcome);
        var request = await counter.RequestAsync(delivered, payment);
        if (payment == "BankTransfer") request = request with {
            BankAccountId = await counter.BankAsync(), TransferReference = "D11-verified-" + counter.Id };
        if (payment == "CustomerCredit") await counter.CustomerAsync(true);
        await using var check = source.Context();
        var qrBefore = await check.PosPaymentQrRequests.CountAsync();
        var financeAtCounter = await priced.Inventory.FinanceAsync();
        var ack = await counter.ConfirmAsync(request);
        Assert.Equal("Settled", ack.State); Assert.Equal(DeliveryD08Case.Text(payable), ack.Settlement.PayableText);
        Assert.Equal(closing.Account.Store.TerminalId, ack.Settlement.TerminalId);
        Assert.Equal(closing.ShiftId, ack.Settlement.ShiftId);
        Assert.Equal(source.Account.Store.WarehouseId, ack.Settlement.SourceWarehouseId);
        Assert.Equal(initialStock - delivered, await StockAsync(priced));
        Assert.Equal(cartBefore, await counter.ClosingCartAsync());
        Assert.Equal(dispatchBefore, await counter.DispatchProofAsync());
        Assert.Equal(qrBefore, await check.PosPaymentQrRequests.CountAsync());
        var origin = await check.POSShifts.AsNoTracking().SingleAsync(x => x.TerminalId == source.Account.Store.TerminalId);
        Assert.Equal(POSShiftStatus.Closed, origin.Status); Assert.Equal(0, origin.CashSalesTotal);
        var shift = await check.POSShifts.AsNoTracking().SingleAsync(x => x.Id == closing.ShiftId);
        Assert.Equal(payment == "Cash" ? payable : 0, shift.CashSalesTotal);
        Assert.Equal(payment == "BankTransfer" ? payable : 0, shift.NonCashSalesTotal);
        var returns = await check.DeliveryReturnedCostFragments.AsNoTracking().Where(x => x.DeliveryOrderId == counter.Id).ToArrayAsync();
        Assert.Equal(3 - delivered, returns.Sum(x => x.BaseQuantity));
        var dispatchCost = await check.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == counter.Id).SumAsync(x => x.CostAmount);
        var history = Assert.Single((await new OrderRepository(check).QueryOrdersAsync(null, null, null,
            created.Delivery.Code, 1, 20)).Items);
        Assert.Equal("DeliverySettled", history.DisplayStatus);
        if (payable > 0)
        {
            var sale = await counter.SaleAsync(ack);
            Assert.Equal(sale.Id, history.OrderId); Assert.NotEqual(source.CartId, history.OrderId);
            Assert.True(history.Delivery!.IsSettlementSale); Assert.Equal(source.CartId, history.Delivery.SourceOrderId);
            Assert.Equal(payable, sale.GrandTotal); Assert.Equal(delivered, sale.Lines.Sum(x => x.BaseQuantity));
            Assert.Equal(dispatchCost - returns.Sum(x => x.CostAmount), sale.Lines.Sum(x => x.LineCostTotal));
            Assert.All(sale.Lines, x => Assert.Equal(initialStock == 0, x.IsProvisionalCost));
            Assert.Empty(await check.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Order &&
                x.ReferenceId == sale.Id.ToString()).ToArrayAsync());
            if (payment == "CustomerCredit")
            {
                Assert.Empty(sale.Payments); Assert.Equal(0, sale.PaidTotal); Assert.Equal(payable, sale.BalanceDue);
                Assert.Equal(payable, Assert.Single(await check.Set<CustomerReceivableEntry>()
                    .Where(x => x.OrderId == sale.Id).ToArrayAsync()).Amount);
            }
            else
            {
                var paid = Assert.Single(sale.Payments); Assert.Equal(payable, paid.Amount);
                Assert.Equal(payment == "Cash" ? PaymentMethod.Cash : PaymentMethod.BankTransfer, paid.Method);
                Assert.Equal(payable, sale.PaidTotal); Assert.Equal(0, sale.BalanceDue);
            }
            var receipt = await closing.Client.Http.GetFromJsonAsync<OrderReceiptDto>("/admin/pos/orders/" + sale.Id);
            Assert.NotNull(receipt); Assert.Equal(payable, receipt.GrandTotal);
            Assert.True(receipt.Delivery!.IsSettlementSale);
            Assert.Equal(delivered * 10000, receipt.Subtotal);
            Assert.Equal(delivered * 10000 - payable, receipt.DiscountTotal);
        }
        else
        {
            Assert.Null(ack.Settlement.SaleOrderId); Assert.Equal(source.CartId, history.OrderId);
            Assert.Equal(financeAtCounter, await priced.Inventory.FinanceAsync());
        }
        var completed = await OfficeStateAsync(office, counter.Id, "Settled");
        Assert.Equal(payable, completed.GetProperty("settledTotal").GetDecimal());
        var completedAt = completed.GetProperty("completedAtUtc").GetDateTimeOffset();
        var beforeReads = await counter.SnapshotAsync();
        var expired = await new StoreDeliveryMonitor(check, new Clock(completedAt.AddSeconds(60)))
            .ReadAsync(source.Account.Store.StoreId, default);
        Assert.DoesNotContain(expired.Orders, x => x.OrderId == counter.Id);
        Assert.Equal("Settled", (await source.Service(check).GetAsync(counter.Id)).State);
        // A lost response/reload can resend exactly the old body, including its
        // stale versions, without another return, payment, sale or office event.
        var replay = await counter.ConfirmAsync(request); Assert.True(replay.Replayed);
        Assert.Equal(ack.Settlement, replay.Settlement);
        await closing.Client.Http.GetStringAsync(created.BillUrl);
        Assert.Equal(beforeReads, await counter.SnapshotAsync());
    }

    private static async Task<decimal> StockAsync(DeliveryPriceStockCase c)
    {
        await using var db = c.Source.Context();
        return await db.InventoryBalances.Where(x => x.WarehouseId == c.Source.Account.Store.WarehouseId &&
            x.ProductVariantId == c.FirstVariant).Select(x => (decimal?)x.OnHandQty).SingleOrDefaultAsync() ?? 0;
    }
    private static async Task<JsonElement> OfficeStateAsync(FullApplicationFixture.Client office, int id, string state)
    {
        JsonElement card = default;
        JsonElement[] matches = [];
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            var snapshot = await office.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot");
            matches = snapshot.GetProperty("delivery").GetProperty("orders").EnumerateArray()
                .Where(x => x.GetProperty("orderId").GetInt32() == id).ToArray();
            if (matches.Length > 0)
            {
                card = Assert.Single(matches);
                if (card.GetProperty("state").GetString() == state) break;
            }
            await Task.Delay(150);
        } while (DateTime.UtcNow < deadline);
        card = Assert.Single(matches);
        Assert.Equal(state, card.GetProperty("state").GetString()); return card;
    }
    private static async Task PostTripAsync(FullApplicationFixture.Client client, string path, object request)
    {
        using var response = await client.Http.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, (int)response.StatusCode + " " + await response.Content.ReadAsStringAsync());
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
