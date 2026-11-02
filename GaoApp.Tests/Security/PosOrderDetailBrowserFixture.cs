using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Data belongs exclusively to FullApplicationFixture's GUID database.
internal static class PosOrderDetailBrowserFixture
{
    internal sealed record CapabilityAccount(string Label, string User, string Password,
        bool CanReturn, bool CanVoid, bool CanReprint, bool CanViewBank, bool CanOpenPaymentRequests, bool CanRestock);
    internal sealed record Seed(int OrderId, int EmptyOrderId, int DeliveryOrderId,
        string ReadOnlyUser, string ReadOnlyPassword, IReadOnlyList<CapabilityAccount> CapabilityAccounts, string BeforeSnapshot);

    internal static async Task<Seed> PrepareAsync(FullApplicationFixture web,
        FullApplicationFixture.StoreSeed store, FullApplicationFixture.Account account)
    {
        await using var db = web.Database.CreateTenantContext(store.StoreId);
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
        var shift = new POSShift { StoreId = store.StoreId, TerminalId = store.TerminalId,
            WarehouseId = store.WarehouseId, OpenedByUserId = account.UserId,
            Status = POSShiftStatus.Open, ShiftCode = "DETAIL-BROWSER" };
        var customer = new Customer { StoreId = store.StoreId, Code = "UI04B",
            Name = "Nguyễn Minh Anh · Cửa hàng thực phẩm và đồ dùng gia đình", Phone = "0901234567" };
        var order = new Order { StoreId = store.StoreId, POSShift = shift, Customer = customer,
            OrderNumber = "POS-20261025-000001", Status = OrderStatus.Completed, PaymentStatus = PaymentStatus.Paid,
            Subtotal = 200000, DiscountTotal = 20000,
            PromotionDiscountTotal = 4000, ComboDiscountTotal = 3000, OrderDiscount = 4000,
            VoucherDiscountTotal = 7000, ComboPromotionName = "Combo gia đình", GrandTotal = 180000,
            DepositAmount = 20000, PaidTotal = 180000, CompletedAtUtc = DateTime.UtcNow,
            Note = "Giao tại quầy.\nKhách cần kiểm tra hạn sử dụng trước khi nhận hàng." };
        order.Lines.Add(new OrderLine { StoreId = store.StoreId, ProductId = variant.ProductId,
            VariantId = variant.Id, ItemName = "Sữa dinh dưỡng nguyên hạt không đường dành cho gia đình · hộp lớn phiên bản đặc biệt",
            Sku = "SKU-THUC-PHAM-NGUYEN-HAT-20261025", Barcode = "8938505974190123456789", UnitName = "Hộp",
            SellingUnitName = "Hộp", Quantity = 2, BaseQuantity = 2, Multiplier = 1, UnitPrice = 100000,
            OriginalUnitPrice = 100000, LineDiscount = 2000, PromotionDiscount = 4000,
            PromotionName = "Ưu đãi thành viên", ComboAllocatedDiscount = 3000, LineTotal = 194000 });
        var empty = new Order { StoreId = store.StoreId, POSShift = shift,
            OrderNumber = "DRAFT-UI04B", Status = OrderStatus.Draft, PaymentStatus = PaymentStatus.Unpaid };
        db.Orders.AddRange(order, empty); await db.SaveChangesAsync();
        db.OrderPayments.AddRange(
            new OrderPayment { StoreId = store.StoreId, OrderId = order.Id, Method = PaymentMethod.Cash, Amount = 80000 },
            new OrderPayment { StoreId = store.StoreId, OrderId = order.Id, Method = PaymentMethod.BankTransfer,
                Amount = 80000, ReferenceCode = "UI04B-SYNTHETIC-TRANSFER", Provider = "Synthetic" });
        await db.SaveChangesAsync();
        using var delivery = await DeliveryBillPresentationSeed.CreateAsync(web, headerDiscounts: true);
        var readOnly = await web.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        using var readClient = await web.LoginAsync(readOnly);
        var readResponse = await readClient.Http.GetAsync($"/admin/pos/order-detail/{order.Id}");
        if (readResponse.StatusCode != HttpStatusCode.OK ||
            (await readResponse.Content.ReadAsStringAsync()).Contains("data-order-legal"))
            throw new InvalidOperationException("View-only order detail must preserve HKD permission boundary.");
        var other = await web.AddAccountAsync(web.Stores[1], PermissionCodes.Pos.Order.View);
        using var otherClient = await web.LoginAsync(other);
        var cross = await otherClient.Http.GetAsync($"/admin/pos/order-detail/{order.Id}");
        using var crossError = JsonDocument.Parse(await cross.Content.ReadAsStringAsync());
        if (cross.StatusCode != HttpStatusCode.BadRequest ||
            crossError.RootElement.GetProperty("message").GetString() != "Không tìm thấy đơn hàng.")
            throw new InvalidOperationException($"Cross-store detail exposed: {cross.StatusCode}");
        var denied = await web.AddAccountAsync(store);
        using var deniedClient = await web.LoginAsync(denied);
        if ((await deniedClient.Http.GetAsync($"/admin/pos/order-detail/{order.Id}")).StatusCode != HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Order.View must still be enforced by the controller.");
        var capabilityAccounts = new List<CapabilityAccount> {
            new("view-only", readOnly.Name, readOnly.Password, false, false, false, false, false, false)
        };
        var combinations = new[] {
            ("order-refund-only", new[]{PermissionCodes.Pos.Order.Refund}, false, false, false, false, false, false),
            ("payment-refund-only", new[]{PermissionCodes.Pos.Payment.Refund}, false, false, false, false, false, false),
            ("return-both", new[]{PermissionCodes.Pos.Order.Refund, PermissionCodes.Pos.Payment.Refund}, true, false, false, false, false, false),
            ("void-only", new[]{PermissionCodes.Pos.Order.Void}, false, true, false, false, false, false),
            ("payment-tools", new[]{PermissionCodes.Pos.Payment.View, PermissionCodes.Pos.Shift.View, PermissionCodes.Pos.Order.Reprint}, false, false, true, true, true, false),
            ("restock-only", new[]{PermissionCodes.Inventory.StockDocument.Approve}, false, false, false, false, false, true)
        };
        foreach (var (label, permissions, canReturn, canVoid, canReprint, canBank, canRequests, canRestock) in combinations)
        {
            var a = await web.AddAccountAsync(store, [PermissionCodes.Pos.Order.View, ..permissions]);
            using var c = await web.LoginAsync(a);
            var html = await c.Http.GetStringAsync($"/admin/pos/order-detail/{order.Id}");
            foreach (var (attribute, expected) in new[] {
                ("return",canReturn),("void",canVoid),("reprint",canReprint),("view-bank",canBank),("open-payment-requests",canRequests) })
                if (!html.Contains($"data-mobile-can-{attribute}=\"{expected.ToString().ToLowerInvariant()}\""))
                    throw new InvalidOperationException($"Phone permission metadata incorrect: {label}/{attribute}");
            // Desktop markup stays available as before. Endpoint authorization remains the enforcement layer.
            if (!html.Contains("data-csp-action=\"order-void\""))
                throw new InvalidOperationException("Phone metadata must not remove desktop controls.");
            if (!canReturn)
            {
                using var refund = await c.Http.PostAsJsonAsync($"/admin/pos/orders/{order.Id}/refund", new {reason="Denied UI04c probe",refundMethod=0});
                using var partial = await c.Http.PostAsJsonAsync("/admin/pos/returns", new {orderId=order.Id,type=1,reason="Denied UI04c probe"});
                if (refund.StatusCode != HttpStatusCode.Forbidden || partial.StatusCode != HttpStatusCode.Forbidden)
                    throw new InvalidOperationException($"Refund pair must be enforced at server: {label}");
            }
            if (!canVoid && (await c.Http.PostAsJsonAsync($"/admin/pos/orders/{order.Id}/void", new {reason="Denied UI04c probe"})).StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"Void must be enforced at server: {label}");
            capabilityAccounts.Add(new(label,a.Name,a.Password,canReturn,canVoid,canReprint,canBank,canRequests,canRestock));
        }
        foreach (var path in new[] { $"/admin/pos/orders/{order.Id}/print?autoPrint=false", $"/admin/acb/payments/orders/{order.Id}",
            "/admin/pos-shift/requests?tab=payment", "/admin/pos/returns/pending-restock" })
            if ((await readClient.Http.GetAsync(path)).StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"View-only destination should remain forbidden: {path}");
        return new(order.Id, empty.Id, delivery.OrderId, readOnly.Name, readOnly.Password, capabilityAccounts,
            await SnapshotAsync(web, store));
    }

    private static async Task<string> SnapshotAsync(FullApplicationFixture web, FullApplicationFixture.StoreSeed store)
    {
        await using var db = web.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {
            Orders = await db.Orders.AsNoTracking().OrderBy(x => x.Id).Select(x => new {
                x.Id, x.RowVersion, x.Status, x.PaymentStatus, x.Subtotal, x.DiscountTotal, x.GrandTotal,
                x.PaidTotal, x.DepositAmount, x.BalanceDue, x.Note }).ToListAsync(),
            Lines = await db.OrderLines.AsNoTracking().OrderBy(x => x.Id).Select(x => new {
                x.Id, x.RowVersion, x.Quantity, x.UnitPrice, x.LineTotal }).ToListAsync(),
            Payments = await db.OrderPayments.AsNoTracking().OrderBy(x => x.Id).Select(x => new {
                x.Id, x.RowVersion, x.Amount, x.Method }).ToListAsync(),
            Menus = await db.AdminMenuItems.AsNoTracking().OrderBy(x => x.Id).Select(x => new {
                x.Id, x.RowVersion }).ToListAsync()
        });
    }

    internal static async Task<string> VerifyAsync(FullApplicationFixture web,
        FullApplicationFixture.StoreSeed store, Seed seed)
    {
        if (seed.BeforeSnapshot != await SnapshotAsync(web, store))
            throw new InvalidOperationException("Read-only UI probe mutated order or configured menus.");
        return "PASS real SQL: order/line/payment/menu snapshots unchanged; view-only, denied and cross-store boundaries enforced; UI-04c seven capability combinations and denied writes/destinations checked.";
    }
}
