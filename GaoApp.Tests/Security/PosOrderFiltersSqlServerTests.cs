using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PosOrderFiltersSqlServerTests
{
    [Fact]
    public async Task Active_autocomplete_is_scoped_and_selected_ids_and_accent_insensitive_names_filter_in_sql()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0]; var foreign = app.Stores[1];
        var first = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var second = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var inactiveUser = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var inactiveMembership = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var removed = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var foreignUser = await app.AddAccountAsync(foreign, PermissionCodes.Pos.Order.View);
        int secondTerminalId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            await db.Users.Where(u => new[] { first.UserId, second.UserId, inactiveUser.UserId, inactiveMembership.UserId, removed.UserId, foreignUser.UserId }.Contains(u.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.FullName, "Nguyễn Thu Hà"));
            await db.Users.Where(u => u.Id == inactiveUser.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
            await db.UserInStores.Where(m => m.UserId == inactiveMembership.UserId).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsActive, false));
            await db.UserInStores.Where(m => m.UserId == removed.UserId).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDeleted, true));
            var terminal = await db.POSTerminals.SingleAsync(t => t.Id == store.TerminalId);
            terminal.Name = "Quầy chính"; terminal.Code = "POS01";
            var secondTerminal = new POSTerminal { StoreId = store.StoreId, Name = "Quầy chính", Code = "POS02" };
            var deletedTerminal = new POSTerminal { StoreId = store.StoreId, Name = "Quầy xóa", Code = "DEL" };
            db.AddRange(secondTerminal, deletedTerminal,
                new POSTerminal { StoreId = store.StoreId, Name = "Quầy tắt", Code = "OFF", IsActive = false },
                new POSTerminal { StoreId = store.StoreId, Name = "Quầy bảo trì", Code = "FIX", Status = POSTerminalStatus.Maintenance },
                new POSTerminal { StoreId = store.StoreId, Name = "Quầy ngừng", Code = "STOP", Status = POSTerminalStatus.Inactive });
            var shift = new POSShift { StoreId = store.StoreId, Terminal = terminal, WarehouseId = store.WarehouseId, OpenedByUserId = first.UserId };
            var otherShift = new POSShift { StoreId = store.StoreId, Terminal = secondTerminal, WarehouseId = store.WarehouseId, OpenedByUserId = second.UserId };
            foreach (var name in new[] { "nguyễn", "nguyen", "Nguyễn", "Nguyen", "Đặng Ánh" })
                db.Add(new Order { StoreId = store.StoreId, POSShift = shift, Status = OrderStatus.Completed, OrderNumber = "ACCENT-" + Guid.NewGuid().ToString("N")[..10],
                    Customer = new Customer { StoreId = store.StoreId, Name = name } });
            db.Add(new Order { StoreId = store.StoreId, POSShift = otherShift, Status = OrderStatus.Completed, OrderNumber = "OTHER-CASHIER" });
            await db.SaveChangesAsync(); secondTerminalId = secondTerminal.Id;
            await db.POSTerminals.Where(t => t.Id == deletedTerminal.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
        }
        using var client = await app.LoginAsync(first);
        async Task<JsonElement> Get(string path) => await client.JsonAsync(HttpMethod.Get, "/admin/pos/orders" + path);
        var staff = await Get("/filter-options?kind=employee&term=nguyen");
        Assert.Equal(new[] { first.UserId, second.UserId }.Order(), staff.EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).Order());
        var terminals = await Get("/filter-options?kind=terminal&term=QUAY");
        Assert.Equal(new[] { store.TerminalId, secondTerminalId }.Order(), terminals.EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).Order());
        foreach (var term in new[] { "nguyen", "NGUYEN", "Nguyễn", "nguyễn" })
        {
            var result = await Get("?customer=" + Uri.EscapeDataString(term) + "&pageSize=1&page=2");
            Assert.Equal(4, result.GetProperty("totalItems").GetInt32()); Assert.Single(result.GetProperty("items").EnumerateArray());
        }
        Assert.Equal(1, (await Get("?customer=dang%20anh")).GetProperty("totalItems").GetInt32());
        Assert.Equal(1, (await Get($"?employeeId={second.UserId}&terminalId={secondTerminalId}&employee=ignored-label&terminal=ignored-label")).GetProperty("totalItems").GetInt32());
        Assert.Equal(0, (await Get($"?employeeId={foreignUser.UserId}")).GetProperty("totalItems").GetInt32());
        Assert.Equal(0, (await Get($"?terminalId={foreign.TerminalId}")).GetProperty("totalItems").GetInt32());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            await db.POSTerminals.Where(t => t.Id == secondTerminalId).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false));
            await db.UserInStores.Where(m => m.UserId == second.UserId).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsActive, false));
        }
        Assert.Equal(0, (await Get($"?employeeId={second.UserId}")).GetProperty("totalItems").GetInt32());
        Assert.Equal(0, (await Get($"?terminalId={secondTerminalId}")).GetProperty("totalItems").GetInt32());
        Assert.Equal(6, (await Get("?")).GetProperty("totalItems").GetInt32()); // Historical rows stay visible without these filters.
    }

    [Fact]
    public async Task Combined_filters_use_real_payments_and_store_scoped_identities_before_paging()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var employee = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var otherEmployee = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var foreign = app.Stores[1];
        var foreignEmployee = await app.AddAccountAsync(foreign, PermissionCodes.Pos.Order.View);
        int cashId, mixedId, transferId, creditId, settledId, qrId, noPaymentId, foreignId;
        var date = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.Users.SingleAsync(u => u.Id == employee.UserId)).FullName = "Nguyễn Thu Ngân";
            (await db.Users.SingleAsync(u => u.Id == otherEmployee.UserId)).FullName = "Lê Thu Hà";
            var terminal = await db.POSTerminals.SingleAsync(t => t.Id == store.TerminalId);
            terminal.Name = "Quầy cửa trước"; terminal.Code = "POS01";
            var secondTerminal = new POSTerminal { StoreId = store.StoreId, Name = "Quầy phía sau", Code = "POS02" };
            var first = new POSShift { StoreId = store.StoreId, OpenedByUserId = employee.UserId, Terminal = terminal, WarehouseId = store.WarehouseId };
            var second = new POSShift { StoreId = store.StoreId, OpenedByUserId = otherEmployee.UserId, Terminal = secondTerminal, WarehouseId = store.WarehouseId };
            var customer = new Customer { StoreId = store.StoreId, Name = "Trần Thanh Mai", Phone = "0912345678", Code = "KH-MAI" };
            Order Order(string number, POSShift shift) => new() { StoreId = store.StoreId, POSShift = shift, Customer = customer,
                OrderNumber = number, Status = OrderStatus.Completed, PaymentStatus = PaymentStatus.Paid,
                GrandTotal = 100, PaidTotal = 100, CompletedAtUtc = date };
            var cash = Order("FILTER-CASH", first);
            var mixed = Order("FILTER-MIXED", first);
            var transfer = Order("FILTER-TRANSFER", second);
            var credit = Order("FILTER-CREDIT", first); credit.IsCreditSale = true; credit.PaidTotal = 40; credit.BalanceDue = 60; credit.PaymentStatus = PaymentStatus.PartiallyPaid;
            var settled = Order("FILTER-CREDIT-SETTLED", second); settled.IsCreditSale = true;
            var qr = Order("FILTER-QR-ONLY", first); qr.Status = OrderStatus.OnHold; qr.PaidTotal = 0; qr.PaymentStatus = PaymentStatus.Unpaid;
            var none = Order("FILTER-NO-PAYMENT", first); none.Customer = null; none.PaidTotal = 0; none.BalanceDue = 100; none.PaymentStatus = PaymentStatus.Unpaid;
            var orders = new[] { cash, mixed, transfer, credit, settled, qr, none };
            db.AddRange(orders); await db.SaveChangesAsync();
            cashId = cash.Id; mixedId = mixed.Id; transferId = transfer.Id; creditId = credit.Id; settledId = settled.Id; qrId = qr.Id; noPaymentId = none.Id;
            void Payment(Order order, PaymentMethod method, decimal amount) =>
                db.Add(new OrderPayment { StoreId = store.StoreId, OrderId = order.Id, Method = method, Amount = amount });
            Payment(cash, PaymentMethod.Cash, 100);
            Payment(cash, PaymentMethod.BankTransfer, 0); // zero/deleted rows must not label this as mixed or transfer
            Payment(cash, PaymentMethod.BankTransfer, 100);
            Payment(mixed, PaymentMethod.Cash, 40); Payment(mixed, PaymentMethod.BankTransfer, 60);
            Payment(transfer, PaymentMethod.BankTransfer, 100);
            Payment(credit, PaymentMethod.Cash, 40);
            Payment(settled, PaymentMethod.BankTransfer, 100);
            var bank = new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "Test", AccountNumber = "123456789", AccountName = "Test" };
            db.Add(new PosPaymentQrRequest { StoreId = store.StoreId, OrderId = qr.Id, BankAccount = bank,
                Amount = 100, RequestCode = "FILTER-QR", Status = PosPaymentQrStatus.Pending });
            await db.SaveChangesAsync();
            await db.OrderPayments.Where(p => p.OrderId == cashId && p.Method == PaymentMethod.BankTransfer && p.Amount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true));
        }
        await using (var db = app.Database.CreateTenantContext(foreign.StoreId))
        {
            var shift = new POSShift { StoreId = foreign.StoreId, OpenedByUserId = foreignEmployee.UserId, TerminalId = foreign.TerminalId, WarehouseId = foreign.WarehouseId };
            var order = new Order { StoreId = foreign.StoreId, POSShift = shift, OrderNumber = "FILTER-FOREIGN", Status = OrderStatus.Completed, CompletedAtUtc = date };
            db.Add(order); await db.SaveChangesAsync(); foreignId = order.Id;
            // Adversarial child row must not classify a current-store order as a card sale.
            db.Add(new OrderPayment { StoreId = foreign.StoreId, OrderId = cashId, Method = PaymentMethod.Card, Amount = 100 });
            await db.SaveChangesAsync();
        }
        using var client = await app.LoginAsync(employee);
        async Task<JsonElement> Search(string query = "") => await client.JsonAsync(HttpMethod.Get, "/admin/pos/orders?" + query);
        async Task<int[]> Ids(string query) => (await Search(query)).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("orderId").GetInt32()).Order().ToArray();
        Assert.Equal(new[] { cashId, mixedId, creditId }.Order(), await Ids("settlement=Cash"));
        Assert.Equal(new[] { mixedId, transferId, settledId }.Order(), await Ids("settlement=BankTransfer"));
        Assert.Equal(new[] { creditId, settledId }.Order(), await Ids("settlement=Credit"));
        Assert.Equal(new[] { creditId }, await Ids("settlement=OutstandingCredit"));
        Assert.Equal(new[] { mixedId }, await Ids("settlement=Mixed"));
        Assert.Empty(await Ids("settlement=Card"));
        Assert.Equal(5, (await Ids("employee=" + Uri.EscapeDataString("Thu Ngân"))).Length);
        Assert.Equal(5, (await Ids("employee=" + employee.Name)).Length);
        Assert.Equal(new[] { transferId, settledId }.Order(), await Ids("terminal=POS02"));
        Assert.Equal(5, (await Ids("terminal=" + Uri.EscapeDataString("cửa trước"))).Length);
        foreach (var term in new[] { "Thanh Mai", "0912345678", "KH-MAI" })
            Assert.Equal(6, (await Ids("customer=" + Uri.EscapeDataString(term))).Length);
        var combined = await Search("employee=" + employee.Name + "&customer=0912345678&terminal=POS01&settlement=Cash&status=Completed&pageSize=1&page=2");
        Assert.Equal(3, combined.GetProperty("totalItems").GetInt32()); Assert.Single(combined.GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { mixedId }, await Ids("employee=" + employee.Name + "&customer=KH-MAI&terminal=POS01&settlement=BankTransfer&keyword=MIXED&fromDate=2026-09-20&toDate=2026-09-20"));
        Assert.Empty(await Ids("employee=" + foreignEmployee.Name));
        var all = await Search();
        Assert.Equal(7, all.GetProperty("totalItems").GetInt32());
        var items = all.GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("orderId").GetInt32());
        Assert.DoesNotContain(foreignId, items.Keys);
        Assert.Equal("Nguyễn Thu Ngân", items[cashId].GetProperty("cashierName").GetString());
        Assert.Equal("Quầy cửa trước", items[cashId].GetProperty("terminalName").GetString());
        Assert.Equal("Trần Thanh Mai", items[cashId].GetProperty("customerName").GetString());
        Assert.Equal("0912345678", items[cashId].GetProperty("customerPhone").GetString());
        Assert.Equal("Cash", Assert.Single(items[cashId].GetProperty("paymentMethods").EnumerateArray()).GetString());
        Assert.Empty(items[qrId].GetProperty("paymentMethods").EnumerateArray());
        Assert.True(items[qrId].GetProperty("hasBankTransfer").GetBoolean()); // QR history still available in reconciliation action
        Assert.Equal("Khách lẻ", items[noPaymentId].GetProperty("customerName").GetString());
        using var invalid = await client.Http.GetAsync("/admin/pos/orders?settlement=999");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var outsider = await app.LoginAsync(foreignEmployee);
        var isolated = await outsider.JsonAsync(HttpMethod.Get, "/admin/pos/orders?customer=0912345678&settlement=Cash");
        Assert.Equal(0, isolated.GetProperty("totalItems").GetInt32());
        // Missing dimensions must not silently remove history from the list.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var shiftId = await db.Orders.Where(o => o.Id == cashId).Select(o => o.POSShiftId).SingleAsync();
            await db.POSShifts.Where(s => s.Id == shiftId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
            await db.Customers.Where(c => c.Code == "KH-MAI").ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
        }
        var missing = await Search("keyword=FILTER-CASH");
        Assert.Equal(1, missing.GetProperty("totalItems").GetInt32());
        var missingItem = Assert.Single(missing.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, missingItem.GetProperty("cashierName").ValueKind);
        Assert.Equal(JsonValueKind.Null, missingItem.GetProperty("terminalName").ValueKind);
        Assert.Contains("không còn thông tin", missingItem.GetProperty("customerName").GetString());
    }
}

