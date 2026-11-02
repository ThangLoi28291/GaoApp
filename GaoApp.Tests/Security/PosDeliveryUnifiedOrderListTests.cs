using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Tests.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PosDeliveryUnifiedOrderListTests
{
    [Fact]
    public async Task Settled_delivery_has_one_financial_row_with_exact_paging_filters_documents_and_receipts()
    {
        await using var app = await FullApplicationFixture.StartDeliveryAsync();
        using var settled = await DeliveryBillPresentationSeed.CreateAsync(app, state: DeliveryState.Settled);
        using var pending = await DeliveryBillPresentationSeed.CreateAsync(app);
        using var noSale = await DeliveryBillPresentationSeed.CreateAsync(app, state: DeliveryState.Settled);
        using var foreign = await DeliveryBillPresentationSeed.CreateAsync(app, state: DeliveryState.Settled, storeIndex: 1);
        var store = settled.Account.Store;
        var sourceBefore = await DeliveryBillPresentationSeed.SnapshotAsync(app, settled);
        var completedAt = new DateTime(2026, 10, 9, 10, 30, 0, DateTimeKind.Utc);
        int saleId, terminalId;
        await using (var seed = app.Database.CreateTenantContext(store.StoreId))
        {
            var original = await seed.Orders.SingleAsync(x => x.Id == settled.OrderId);
            // Actual handover was finalized at another till, on a later date,
            // for fewer goods than the immutable original quote.
            var terminal = new POSTerminal { StoreId = store.StoreId, Code = "UNIFIED-B", Name = "Quầy chốt B" };
            var shift = new POSShift { StoreId = store.StoreId, Terminal = terminal, WarehouseId = store.WarehouseId,
                OpenedByUserId = settled.Account.UserId, Status = POSShiftStatus.Open, ShiftCode = "UNIFIED-B" };
            var sale = new Order { StoreId = store.StoreId, POSShift = shift, OrderNumber = "POS-UNIFIED-23500",
                Status = OrderStatus.Completed, PaymentStatus = PaymentStatus.Paid, Subtotal = 30000, DiscountTotal = 6500,
                GrandTotal = 23500, PaidTotal = 23500, BalanceDue = 0, CompletedAtUtc = completedAt };
            sale.Payments.Add(new OrderPayment { StoreId = store.StoreId, Method = PaymentMethod.Cash, Amount = 23500 });
            seed.Add(sale); await seed.SaveChangesAsync(); saleId = sale.Id; terminalId = terminal.Id;
            var delivery = await seed.DeliveryOrders.SingleAsync(x => x.Id == settled.DeliveryId);
            seed.Add(new DeliveryJournalEntry { StoreId = store.StoreId, DeliveryOrderId = settled.DeliveryId,
                SourceWarehouseId = delivery.SourceWarehouseId, SourceLegalEntityId = delivery.SourceLegalEntityId,
                Kind = DeliveryJournalKind.Settlement, PostingKey = "unified:" + settled.DeliveryId, SaleOrderId = sale.Id, MoneyAmount = 23500 });
            // Zero-delivery settlement deliberately has no fake sale order.
            seed.Add(new DeliveryJournalEntry { StoreId = store.StoreId, DeliveryOrderId = noSale.DeliveryId,
                SourceWarehouseId = delivery.SourceWarehouseId, SourceLegalEntityId = delivery.SourceLegalEntityId,
                Kind = DeliveryJournalKind.Settlement, PostingKey = "unified:" + noSale.DeliveryId, MoneyAmount = 0 });
            await seed.SaveChangesAsync();
        }
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var repo = new OrderRepository(db);
        var all = await repo.QueryOrdersAsync(null, null, null, "PRES-", 1, 20);
        Assert.Equal(3, all.Total); Assert.Equal(3, all.Items.Count);
        Assert.DoesNotContain(all.Items, x => x.OrderId == settled.OrderId || x.OrderId == foreign.OrderId);
        var item = Assert.Single(all.Items, x => x.OrderId == saleId);
        Assert.Equal(23500, item.GrandTotal); Assert.Equal(23500, item.PaidTotal); Assert.Equal(0, item.BalanceDue);
        Assert.Equal(new[] { "Cash" }, item.PaymentMethods); Assert.Equal("Quầy chốt B", item.TerminalName);
        Assert.Equal("DeliverySettled", item.DisplayStatus); Assert.True(item.Delivery!.IsSettlementSale);
        Assert.Equal(settled.Code, item.Delivery.Code); Assert.Equal(settled.OrderId, item.Delivery.SourceOrderId);
        Assert.Equal(saleId, item.Delivery.SaleOrderId); Assert.Equal("POS-UNIFIED-23500", item.Delivery.SaleOrderNumber);
        Assert.False(Assert.Single(all.Items, x => x.OrderId == pending.OrderId).Delivery!.IsSettlementSale);
        Assert.Contains(all.Items, x => x.OrderId == noSale.OrderId);

        var deliveryOnly = new OrderListQueryDto { DeliveryOnly = true };
        var ids = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await repo.QueryOrdersAsync(null, null, null, "PRES-", page, 1, filters: deliveryOnly);
            Assert.Equal(3, result.Total); ids.Add(Assert.Single(result.Items).OrderId);
        }
        Assert.Equal(3, ids.Distinct().Count()); Assert.Contains(saleId, ids);
        Assert.Empty((await repo.QueryOrdersAsync(null, null, null, "PRES-", 4, 1, filters: deliveryOnly)).Items);
        foreach (var keyword in new[] { settled.Code, settled.Number, "#" + settled.OrderId, "POS-UNIFIED-23500" })
            Assert.Equal(saleId, Assert.Single((await repo.QueryOrdersAsync(null, null, null, keyword, 1, 20)).Items).OrderId);
        Assert.Equal(saleId, Assert.Single((await repo.QueryOrdersAsync(completedAt.Date, completedAt.Date.AddDays(1),
            OrderStatus.Completed, null, 1, 20, filters: deliveryOnly)).Items).OrderId);
        Assert.Equal(saleId, Assert.Single((await repo.QueryOrdersAsync(null, null, null, null, 1, 20,
            filters: new() { DeliveryOnly = true, TerminalId = terminalId, Settlement = OrderSettlementFilter.Cash })).Items).OrderId);
        Assert.DoesNotContain((await repo.QueryOrdersAsync(null, null, OrderStatus.Cancelled, null, 1, 20)).Items,
            x => x.OrderId == settled.OrderId || x.OrderId == pending.OrderId || x.OrderId == noSale.OrderId);
        var sourceLink = await repo.GetDeliveryInfoAsync(settled.OrderId);
        Assert.NotNull(sourceLink); Assert.False(sourceLink.IsSettlementSale); Assert.Equal(saleId, sourceLink.SaleOrderId);
        Assert.Null(await repo.GetDeliveryInfoAsync(foreign.OrderId));

        var receipt = await settled.Client.Http.GetFromJsonAsync<OrderReceiptDto>($"/admin/pos/orders/{saleId}");
        Assert.NotNull(receipt); Assert.True(receipt.Delivery!.IsSettlementSale);
        Assert.Equal(23500, receipt.GrandTotal); Assert.Equal(23500, receipt.PaidTotal);
        var detail = WebUtility.HtmlDecode(await settled.Client.Http.GetStringAsync($"/admin/pos/order-detail/{saleId}"));
        Assert.Contains("Tổng tiền thực giao", detail); Assert.Contains("23.500", detail);
        Assert.Contains($"/admin/pos/order-detail/{settled.OrderId}", detail); Assert.Contains(settled.BillUrl, detail);
        using var printSale = await settled.Client.Http.GetAsync($"/admin/pos/orders/{saleId}/print?autoPrint=false");
        Assert.Equal(HttpStatusCode.OK, printSale.StatusCode); Assert.Null(printSale.Headers.Location);
        using var printSource = await settled.Client.Http.GetAsync($"/admin/pos/orders/{settled.OrderId}/print?autoPrint=false");
        Assert.Equal(HttpStatusCode.Redirect, printSource.StatusCode); Assert.Equal(settled.BillUrl, printSource.Headers.Location?.OriginalString);
        Assert.Equal(sourceBefore, await DeliveryBillPresentationSeed.SnapshotAsync(app, settled));
        Assert.Equal(4, await db.Orders.CountAsync()); Assert.Equal(1, await db.OrderPayments.CountAsync());

        // Missing/deleted invoices must not silently erase the source history.
        await db.Orders.Where(x => x.Id == saleId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDeleted, true));
        var fallback = await repo.QueryOrdersAsync(null, null, null, settled.Number, 1, 20);
        Assert.Equal(settled.OrderId, Assert.Single(fallback.Items).OrderId);
        Assert.False((await repo.GetDeliveryInfoAsync(settled.OrderId))!.IsSettlementSale);
    }
}
