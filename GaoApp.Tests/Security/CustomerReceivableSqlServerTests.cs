using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class CustomerReceivableSqlServerTests
{
    [Fact]
    public async Task Credit_sale_collection_in_later_shift_is_idempotent_and_does_not_restate_sales()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store, true);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/payments", new { clientRequestId = Guid.NewGuid(), method = 0, amount = 20 });
        var credit = new { clientRequestId = Guid.NewGuid(), expectedCustomerId = customerId, expectedBalance = 40, note = "Ghi nợ không hẹn ngày tại POS" };
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", credit);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", credit);
        int firstShift;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var order = await db.Orders.SingleAsync(x => x.Id == orderId);
            firstShift = order.POSShiftId;
            Assert.Equal(OrderStatus.Completed, order.Status);
            Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
            Assert.Equal(40m, order.BalanceDue);
            Assert.Null(order.CreditDueDate);
            Assert.Single(await db.Set<CustomerReceivableEntry>().ToListAsync());
            Assert.Equal(97m, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 20 });
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var collect = new { clientRequestId = Guid.NewGuid(), customerId, orderId, amount = 10, method = 0 };
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.JsonAsync(HttpMethod.Post, "/admin/customer-debt/collect", collect)));
        Assert.Single(responses.Select(x => x.GetProperty("receiptId").GetInt32()).Distinct());
        using (var excess = await client.Http.PostAsJsonAsync("/admin/customer-debt/collect", new { clientRequestId = Guid.NewGuid(), customerId, orderId, amount = 31, method = 0 }))
            Assert.Equal(HttpStatusCode.BadRequest, excess.StatusCode);
        using (var mismatch = await client.Http.PostAsJsonAsync("/admin/customer-debt/collect", new { collect.clientRequestId, customerId, orderId, amount = 11, method = 0 }))
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        using (var page = await client.Http.GetAsync($"/admin/customer-debt?customerId={customerId}"))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("PTCN-", await page.Content.ReadAsStringAsync());
        }
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using (var crossStore = await foreign.Http.PostAsJsonAsync("/admin/customer-debt/collect", new { clientRequestId = Guid.NewGuid(), customerId, orderId, amount = 1, method = 0 }))
            Assert.Equal(HttpStatusCode.BadRequest, crossStore.StatusCode);
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(30m, (await verify.Orders.SingleAsync(x => x.Id == orderId)).BalanceDue);
        Assert.Equal(30m, await verify.Set<CustomerReceivableEntry>().SumAsync(x => x.Amount));
        Assert.Single(await verify.Set<CustomerDebtReceipt>().ToListAsync());
        Assert.Equal(20m, (await verify.POSShifts.SingleAsync(x => x.Id == firstShift)).CashSalesTotal);
        var current = await verify.POSShifts.SingleAsync(x => x.Status == POSShiftStatus.Open);
        Assert.Equal(0m, current.CashSalesTotal);
        Assert.Equal(10m, current.CashInTotal);
        Assert.Equal(10m, current.ClosingCashExpected);
        Assert.Single(await verify.OrderPayments.Where(x => x.IsDebtCollection).ToListAsync());
    }

    [Fact]
    public async Task Credit_requires_existing_customer_permission_and_matching_customer_and_balance()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store, false);
        var credit = new { clientRequestId = Guid.NewGuid(), expectedCustomerId = customerId, expectedBalance = 60, dueDate = DateTime.UtcNow.AddDays(7).Date };
        using (var blocked = await client.Http.PostAsJsonAsync($"/admin/pos/{orderId}/finalize-credit", credit)) Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        { (await db.Customers.SingleAsync(x => x.Id == customerId)).HaveDebt = true; await db.SaveChangesAsync(); }
        using (var stale = await client.Http.PostAsJsonAsync($"/admin/pos/{orderId}/finalize-credit", new { credit.clientRequestId, expectedCustomerId = customerId + 1, expectedBalance = 60, credit.dueDate })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Order.Finalize, PermissionCodes.CustomerDebt.View));
        using (var denied = await viewer.Http.PostAsJsonAsync($"/admin/pos/{orderId}/finalize-credit", credit)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await viewer.Http.PostAsJsonAsync("/admin/customer-debt/collect", new { clientRequestId = Guid.NewGuid(), customerId, amount = 1, method = 0 })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", credit);
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(60m, await verify.Set<CustomerReceivableEntry>().SumAsync(x => x.Amount));
        Assert.Empty(await verify.OrderPayments.ToListAsync());
        Assert.Equal(0m, (await verify.POSShifts.SingleAsync()).CashSalesTotal);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/orders/{orderId}/void", new { reason = "Hủy đơn nợ chưa thu tiền" });
        verify.ChangeTracker.Clear();
        Assert.Equal(0m, (await verify.Orders.SingleAsync(x => x.Id == orderId)).BalanceDue);
        Assert.Equal(0m, await verify.Set<CustomerReceivableEntry>().SumAsync(x => x.Amount));
        Assert.Equal(100m, (await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
    }

    [Fact]
    public async Task Bank_collection_and_returns_separate_debt_reduction_from_actual_refund()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store, true);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", new {
            clientRequestId = Guid.NewGuid(), expectedCustomerId = customerId, expectedBalance = 60,
            dueDate = DateTime.UtcNow.AddDays(7).Date });
        int bankId, lineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var bank = new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "ACB", AccountNumber = "TEST-123", AccountName = "Test", IsActive = true };
            db.StoreBankAccounts.Add(bank); await db.SaveChangesAsync(); bankId = bank.Id;
            lineId = await db.OrderLines.Where(x => x.OrderId == orderId).Select(x => x.Id).SingleAsync();
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/customer-debt/collect", new {
            clientRequestId = Guid.NewGuid(), customerId, orderId, amount = 20, method = 1, storeBankAccountId = bankId, reference = "BANK-001" });
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/returns", new {
            orderId, type = 3, reason = "Trả hàng trừ công nợ",
            lines = new[] { new { orderLineId = lineId, returnQuantity = 2, refundUnitAmount = 20, action = 0 } }, payments = Array.Empty<object>() });
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(0m, (await db.Orders.SingleAsync(x => x.Id == orderId)).BalanceDue);
            Assert.Equal(-40m, (await db.Set<CustomerReceivableEntry>().SingleAsync(x => x.Kind == "Return")).Amount);
            Assert.Empty(await db.SalesReturnPayments.ToListAsync());
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/returns", new {
            orderId, type = 3, reason = "Trả phần còn lại, hoàn tiền đã thu",
            lines = new[] { new { orderLineId = lineId, returnQuantity = 1, refundUnitAmount = 20, action = 0 } },
            payments = new[] { new { method = 1, amount = 20, referenceCode = "REFUND-001" } } });
        using (var page = await client.Http.GetAsync($"/admin/customer-debt?customerId={customerId}"))
        {
            var html = await page.Content.ReadAsStringAsync();
            Assert.True(page.IsSuccessStatusCode, html);
            Assert.Contains("REFUND-001", html);
        }
        using (var export = await client.Http.GetAsync($"/admin/customer-debt/export?customerId={customerId}"))
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        var shift = await verify.POSShifts.SingleAsync();
        Assert.Equal(0m, shift.CashInTotal);
        Assert.Equal(0m, shift.NonCashSalesTotal);
        Assert.Equal(20m, shift.NonCashRefundTotal);
        Assert.Equal(0m, await verify.Set<CustomerReceivableEntry>().SumAsync(x => x.Amount));
    }

    [Fact]
    public async Task Collection_report_filters_local_dates_exports_all_pages_and_protects_receipts()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var client = await app.LoginAsync(account);
        var (orderId, customerId) = await Start(app, client, store, true);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", new {
            clientRequestId = Guid.NewGuid(), expectedCustomerId = customerId, expectedBalance = 60 });
        var result = await client.JsonAsync(HttpMethod.Post, "/admin/customer-debt/collect", new {
            clientRequestId = Guid.NewGuid(), customerId, amount = 10, method = 0 });
        var receiptId = result.GetProperty("receiptId").GetInt32();
        int shiftId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var receipt = await db.Set<CustomerDebtReceipt>().SingleAsync();
            shiftId = receipt.POSShiftId;
            // Read-model fixtures on a disposable database, including both UTC+7 boundaries.
            var middle = new DateTime(2026, 10, 1, 5, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < 51; i++) db.Set<CustomerDebtReceipt>().Add(new() {
                StoreId = store.StoreId, CustomerId = customerId, POSShiftId = shiftId,
                ClientRequestId = Guid.NewGuid(), Amount = 1, Method = PaymentMethod.Cash, Reference = "REPORT-IN" });
            foreach (var (reference, amount) in new[] { ("REPORT-BANK", 20), ("REPORT-BEFORE", 100), ("REPORT-AFTER", 200) })
                db.Set<CustomerDebtReceipt>().Add(new() { StoreId = store.StoreId, CustomerId = customerId, POSShiftId = shiftId,
                    ClientRequestId = Guid.NewGuid(), Amount = amount, Method = PaymentMethod.BankTransfer, Reference = reference });
            await db.SaveChangesAsync();
            await db.Set<CustomerDebtReceipt>().ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAtUtc, middle));
            await db.Set<CustomerDebtReceipt>().Where(x => x.Id == receiptId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CreatedAtUtc, new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc)));
            await db.Set<CustomerDebtReceipt>().Where(x => x.Reference == "REPORT-BANK").ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CreatedAtUtc, new DateTime(2026, 10, 1, 16, 59, 59, DateTimeKind.Utc)));
            await db.Set<CustomerDebtReceipt>().Where(x => x.Reference == "REPORT-BEFORE").ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CreatedAtUtc, new DateTime(2026, 9, 30, 16, 59, 59, DateTimeKind.Utc)));
            await db.Set<CustomerDebtReceipt>().Where(x => x.Reference == "REPORT-AFTER").ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CreatedAtUtc, new DateTime(2026, 10, 1, 17, 0, 0, DateTimeKind.Utc)));
        }
        var filter = $"from=2026-10-01&to=2026-10-01&customerId={customerId}&shiftId={shiftId}";
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.CustomerDebt.View));
        var html = WebUtility.HtmlDecode(await viewer.Http.GetStringAsync("/admin/customer-debt/collections?" + filter));
        Assert.Contains("53 phiếu", html);
        Assert.Contains("REPORT-BANK", html);
        Assert.DoesNotContain("REPORT-BEFORE", html);
        Assert.DoesNotContain("REPORT-AFTER", html);
        Assert.Contains("PTCN-" + receiptId.ToString("D6"), await viewer.Http.GetStringAsync("/admin/customer-debt/collections?" + filter + "&page=2"));
        using (var export = await viewer.Http.GetAsync("/admin/customer-debt/collections/export?" + filter + "&page=2"))
        {
            export.EnsureSuccessStatusCode();
            using var stream = new MemoryStream(await export.Content.ReadAsByteArrayAsync());
            using var book = new ClosedXML.Excel.XLWorkbook(stream);
            var sheet = book.Worksheet(1);
            Assert.Equal(81m, sheet.Cell(4, 2).GetValue<decimal>());
            Assert.Equal(61m, sheet.Cell(4, 4).GetValue<decimal>());
            Assert.Equal(20m, sheet.Cell(4, 6).GetValue<decimal>());
            Assert.Equal(60, sheet.LastRowUsed()!.RowNumber()); // 7 heading rows + 53 receipts.
        }
        var bankHtml = WebUtility.HtmlDecode(await viewer.Http.GetStringAsync("/admin/customer-debt/collections?" + filter + "&method=1&search=REPORT-BANK"));
        Assert.Contains("1 phiếu", bankHtml);
        Assert.DoesNotContain("REPORT-IN", bankHtml);
        var collectorHtml = WebUtility.HtmlDecode(await viewer.Http.GetStringAsync("/admin/customer-debt/collections?" + filter + $"&userId={account.UserId}"));
        Assert.Contains("PTCN-" + receiptId.ToString("D6"), collectorHtml);
        var detail = WebUtility.HtmlDecode(await viewer.Http.GetStringAsync($"/admin/customer-debt/receipts/{receiptId}"));
        Assert.Contains($"/admin/pos/order-detail/{orderId}", detail);
        Assert.Contains("E2E test user", detail);
        Assert.Contains("10", detail);
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using (var denied = await foreign.Http.GetAsync($"/admin/customer-debt/receipts/{receiptId}")) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var foreignReport = await foreign.Http.GetStringAsync("/admin/customer-debt/collections?from=2026-10-01&to=2026-10-01");
        Assert.DoesNotContain("REPORT-BANK", foreignReport);
        using var noPermission = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        foreach (var path in new[] { "/collections", "/collections/export", $"/receipts/{receiptId}" })
        {
            using var denied = await noPermission.Http.GetAsync("/admin/customer-debt" + path);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        foreach (var invalid in new[] { "from=2026-10-02&to=2026-10-01", "method=99", "from=invalid", "shiftId=-1" })
        {
            using var bad = await viewer.Http.GetAsync("/admin/customer-debt/collections?" + invalid);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        // Customer selection is independent of the list's search term.
        using var customer = await viewer.Http.GetAsync($"/admin/customer-debt?customerId={customerId}&search=no-match");
        Assert.Equal(HttpStatusCode.OK, customer.StatusCode);
        using var list = await viewer.Http.GetAsync("/admin/customer-debt?status=open&page=999");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Customer_totals_cover_all_pages_and_status_filters()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var shift = new POSShift { StoreId = store.StoreId, TerminalId = store.TerminalId,
            WarehouseId = store.WarehouseId, OpenedByUserId = account.UserId };
        var orders = Enumerable.Range(0, 206).Select(i => new Order {
            StoreId = store.StoreId, POSShift = shift, Customer = new Customer {
                StoreId = store.StoreId, Code = $"DEBT-PAGE-{i:D3}", Name = $"Khách phân trang {i:D3}" },
            OrderNumber = $"DEBT-PAGE-{i:D3}", Status = OrderStatus.Completed, IsCreditSale = true,
            GrandTotal = 10, PaidTotal = i == 205 ? 10 : 0, BalanceDue = i == 205 ? 0 : 10,
            CreditDueDate = i == 0 ? DateTime.UtcNow.AddHours(7).Date.AddDays(-1) : null }).ToList();
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync();
        foreach (var order in orders)
            db.Set<CustomerReceivableEntry>().Add(new() { StoreId = store.StoreId,
                CustomerId = order.CustomerId!.Value, OrderId = order.Id, Amount = 10, Kind = "Sale" });
        db.Set<CustomerReceivableEntry>().Add(new() { StoreId = store.StoreId,
            CustomerId = orders[^1].CustomerId!.Value, OrderId = orders[^1].Id, Amount = -10, Kind = "Return" });
        await db.SaveChangesAsync();
        var service = new GaoApp.Infrastructure.Services.Orders.CustomerReceivableService(db, null!, null!);
        var first = await service.GetAsync(null, null, default);
        Assert.Equal(206, first.TotalCustomers);
        Assert.Equal(205, first.OpenCustomers);
        Assert.Equal(1, first.SettledCustomers);
        Assert.Equal(2050m, first.TotalBalance);
        Assert.Equal(10m, first.TotalOverdue);
        Assert.Equal(50, first.Customers.Count);
        var last = await service.GetAsync(null, null, default, 999);
        Assert.Equal(5, last.Page);
        Assert.Equal(6, last.Customers.Count);
        Assert.Equal(2050m, last.TotalBalance);
        Assert.Empty(first.Customers.Select(x => x.Id).Intersect(last.Customers.Select(x => x.Id)));
        var overdue = await service.GetAsync(null, null, default, 1, "overdue");
        Assert.Single(overdue.Customers);
        Assert.Equal(10m, overdue.TotalBalance);
        var settled = await service.GetAsync(null, null, default, 1, "settled");
        Assert.Single(settled.Customers);
        Assert.Equal(0m, settled.TotalBalance);
        var search = await service.GetAsync(null, "DEBT-PAGE-204", default);
        Assert.Single(search.Customers);
        Assert.Equal(10m, search.TotalBalance);
    }

    private static async Task<(int OrderId, int CustomerId)> Start(FullApplicationFixture app, FullApplicationFixture.Client client, FullApplicationFixture.StoreSeed store, bool eligible)
    {
        int customerId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var customer = new Customer { StoreId = store.StoreId, Name = "Khách công nợ", HaveDebt = eligible };
            db.Customers.Add(customer); await db.SaveChangesAsync(); customerId = customer.Id;
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var order = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var id = order.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={store.VariantId}&qty=3");
        var draft = await client.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{customerId}", new { repriceExistingLines = false });
        Assert.Equal(eligible, draft.GetProperty("customerCanBuyOnCredit").GetBoolean());
        return (id, customerId);
    }
}
