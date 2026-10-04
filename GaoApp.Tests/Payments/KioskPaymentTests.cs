using System.Text.Json;
using System.Reflection;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Enums;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Acb;
using GaoApp.Web.Services.Kiosk;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static async Task<KioskStation> AddKiosk(Fixture f)
    {
        var station = new KioskStation { StoreId = 1, TerminalId = 3, Terminal = new POSTerminal { Id = 3, StoreId = 1, Code = "KIOSK", Name = "Quầy tự phục vụ" }, SystemUserId = 7, OrderId = 100, CartTouchedAtUtc = DateTime.UtcNow, CustomerId = 19, CustomerExpiresAtUtc = DateTime.UtcNow.AddMinutes(2) };
        f.Db.Stores.Add(new Store { Id = 1, Name = "Test store", SubDomain = "test", SubDomainNormalized = "TEST" }); f.Db.Add(station); await f.Db.SaveChangesAsync();
        return station;
    }
    [Fact]
    public async Task Kiosk_recovered_bank_payment_finishes_once_then_clears_screen_after_15_seconds()
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f);
        var service = new KioskHarness(f).Service;
        await f.Service.TryCreateAsync(100, default);
        var pending = JsonSerializer.SerializeToElement(await service.PollAsync(station, default));
        Assert.Equal("payment", pending.GetProperty("mode").GetString()); Assert.Equal(0, f.FinalizeCount);
        Assert.Equal(InvoiceIssuanceRoute.Unselected, f.Order.InvoiceIssuanceRoute);
        await Assert.ThrowsAsync<ConflictAppException>(() => service.CommandAsync(station, new KioskCommand { Action = "finish", SessionKey = station.SessionKey, Revision = station.Revision, CommandId = Guid.NewGuid() }, default));
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000); await f.Callback();
        var success = JsonSerializer.SerializeToElement(await service.PollAsync(station, default));
        Assert.Equal("success", success.GetProperty("mode").GetString()); Assert.Equal(1, f.FinalizeCount);
        Assert.Equal(InvoiceIssuanceRoute.Automatic, f.Order.InvoiceIssuanceRoute);
        await service.PollAsync(station, default); Assert.Equal(1, f.FinalizeCount);
        Assert.Single(await f.Db.OrderPayments.Where(x => x.ReferenceCode != null).ToListAsync());
        var oldSession = station.SessionKey; station.CompletedAtUtc = DateTime.UtcNow.AddSeconds(-16);
        var reset = JsonSerializer.SerializeToElement(await service.PollAsync(station, default));
        Assert.Equal("idle", reset.GetProperty("mode").GetString()); Assert.Null(station.OrderId); Assert.Null(station.CustomerId); Assert.NotEqual(oldSession, station.SessionKey);
        Assert.Equal(1, f.FinalizeCount); Assert.Equal(AcbSessionStatus.Completed, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
    }
    [Fact]
    public async Task Kiosk_wrong_bank_amount_keeps_review_state_and_blocks_cart_mutation()
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f);
        var service = new KioskHarness(f).Service;
        await f.Service.TryCreateAsync(100, default); f.Bank.Pay(f.Bank.Orders.Single().Key, 60000); await f.Callback();
        var state = JsonSerializer.SerializeToElement(await service.PollAsync(station, default));
        Assert.Equal("review", state.GetProperty("mode").GetString()); Assert.Equal(0, f.FinalizeCount); Assert.NotNull(station.OrderId); Assert.NotNull(station.HelpRequestedAtUtc);
        await Assert.ThrowsAsync<ConflictAppException>(() => service.CommandAsync(station, new KioskCommand { Action = "cancel", SessionKey = station.SessionKey, Revision = station.Revision, CommandId = Guid.NewGuid() }, default));
    }
    [Fact]
    public async Task Kiosk_retry_after_token_rejection_creates_new_key_but_reuses_uncertain_attempt()
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f); var service = new KioskHarness(f).Service;
        var command = new KioskCommand { Action = "checkout", SessionKey = station.SessionKey, Revision = station.Revision, CommandId = Guid.NewGuid() };
        f.Bank.RejectToken = true; await Assert.ThrowsAsync<AcbApiException>(() => service.CommandAsync(station, command, default));
        var oldKey = station.CheckoutKey; Assert.Equal(AcbSessionStatus.Cancelled, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        f.Bank.RejectToken = false; await service.CommandAsync(station, command, default); Assert.NotEqual(oldKey, station.CheckoutKey);
        Assert.Single(await f.Db.Set<AcbQrSession>().Where(x => x.Status == AcbSessionStatus.Pending).ToListAsync());
        await service.CommandAsync(station, command, default); Assert.Single(f.Bank.Orders);
    }
    private static KioskCommand KioskAction(KioskStation station, string action) => new()
    { Action = action, SessionKey = station.SessionKey, Revision = station.Revision, CommandId = Guid.NewGuid() };

    [Fact]
    public async Task Kiosk_second_order_can_retry_failed_QR_without_reusing_first_payment_or_losing_cart()
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f); var kiosk = new KioskHarness(f);
        await kiosk.Service.CommandAsync(station, KioskAction(station, "checkout"), default);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000); await f.Callback();
        await kiosk.Service.PollAsync(station, default);
        var firstOrder = f.Order; var firstKey = station.CheckoutKey; var firstSession = station.SessionKey;
        await kiosk.Service.CommandAsync(station, KioskAction(station, "finish"), default);
        Assert.Null(station.CheckoutKey); Assert.Null(station.CompletedAtUtc); Assert.NotEqual(firstSession, station.SessionKey);
        // The HTTP/SQL test covers creating this next draft via the real POS service.
        f.Order = new Order { Id = 101, StoreId = 1, POSShift = firstOrder.POSShift, POSShiftId = firstOrder.POSShiftId, GrandTotal = 5000 };
        f.Order.Lines.Add(new OrderLine { StoreId = 1, Variant = firstOrder.Lines.Single().Variant, Quantity = 1, UnitPrice = 5000, LineTotal = 5000 });
        f.Db.Add(f.Order); station.OrderId = f.Order.Id; station.CartTouchedAtUtc = DateTime.UtcNow; await f.Db.SaveChangesAsync();
        f.Bank.InitiateFailureCode = "30020500";
        await Assert.ThrowsAsync<AcbApiException>(() => kiosk.Service.CommandAsync(station, KioskAction(station, "checkout"), default));
        var failed = await f.Db.Set<AcbQrSession>().SingleAsync(x => x.OrderId == 101);
        Assert.Equal(AcbSessionStatus.Creating, failed.Status); Assert.NotEqual(firstKey, station.CheckoutKey);
        var state = JsonSerializer.SerializeToElement(await kiosk.Service.PollAsync(station, default));
        Assert.Equal("payment", state.GetProperty("mode").GetString()); Assert.Equal(0, f.Order.PaidTotal); Assert.Equal(1, f.FinalizeCount);
        f.Bank.InitiateFailureCode = null;
        var retry = KioskAction(station, "retry-payment");
        state = JsonSerializer.SerializeToElement(await kiosk.Service.CommandAsync(station, retry, default));
        Assert.Equal(AcbSessionStatus.Cancelled, failed.Status);
        Assert.Equal(101, state.GetProperty("order").GetProperty("Id").GetInt32());
        Assert.Single(f.Order.Lines); Assert.Equal(5000, f.Order.GrandTotal);
        await kiosk.Service.CommandAsync(station, retry, default); Assert.Equal(2, f.Bank.Orders.Count);
        var active = await f.Db.Set<AcbQrSession>().SingleAsync(x => x.OrderId == 101 && x.Status == AcbSessionStatus.Pending);
        f.Bank.Pay(active.ProviderOrderId, 5000); await f.Callback();
        state = JsonSerializer.SerializeToElement(await kiosk.Service.PollAsync(station, default));
        Assert.Equal("success", state.GetProperty("mode").GetString()); Assert.Equal(2, f.FinalizeCount);
        Assert.Single(firstOrder.Payments, x => x.Provider == "ACB"); Assert.Single(f.Order.Payments, x => x.Provider == "ACB");
        Assert.Equal(5000, f.Order.PaidTotal); Assert.Equal(2, kiosk.InvoiceCalls);
    }

    [Fact]
    public async Task Kiosk_retry_discovers_received_money_and_completes_instead_of_creating_another_QR()
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f); var kiosk = new KioskHarness(f);
        f.Bank.DropInitiateResponse = true;
        await Assert.ThrowsAsync<AcbApiException>(() => kiosk.Service.CommandAsync(station, KioskAction(station, "checkout"), default));
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000); // Receipt exists, but callback has not arrived.
        var result = JsonSerializer.SerializeToElement(await kiosk.Service.CommandAsync(station, KioskAction(station, "retry-payment"), default));
        Assert.Equal("success", result.GetProperty("mode").GetString());
        Assert.Single(f.Bank.Orders); Assert.Equal(0, f.Bank.CancelCalls); Assert.Equal(1, f.FinalizeCount);
        Assert.Single(f.Order.Payments, p => p.Provider == "ACB");
    }

    [Fact]
    public async Task Kiosk_retry_does_not_replace_an_uncertain_attempt_when_bank_lookup_fails()
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f); var kiosk = new KioskHarness(f);
        f.Bank.DropInitiateResponse = true;
        await Assert.ThrowsAsync<AcbApiException>(() => kiosk.Service.CommandAsync(station, KioskAction(station, "checkout"), default));
        var key = station.CheckoutKey; f.Bank.FailRetrieve = true;
        await Assert.ThrowsAsync<ConflictAppException>(() => kiosk.Service.CommandAsync(station, KioskAction(station, "retry-payment"), default));
        Assert.Equal(key, station.CheckoutKey); Assert.Equal(100, station.OrderId); Assert.Single(f.Bank.Orders);
        Assert.Equal(AcbSessionStatus.Creating, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        Assert.Equal(0, f.FinalizeCount); Assert.Equal(0, f.Bank.CancelCalls);
    }

    [Fact]
    public async Task Kiosk_retry_cannot_replace_a_displayed_pending_QR()
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f); var kiosk = new KioskHarness(f);
        await kiosk.Service.CommandAsync(station, KioskAction(station, "checkout"), default);
        await Assert.ThrowsAsync<ConflictAppException>(() => kiosk.Service.CommandAsync(station, KioskAction(station, "retry-payment"), default));
        Assert.Single(f.Bank.Orders); Assert.Equal(0, f.Bank.CancelCalls);
    }

    private sealed class KioskHarness
    {
        public KioskService Service { get; }
        public int InvoiceCalls, CancelCalls;
        public bool FailInvoice;
        public KioskHarness(Fixture f)
        {
            var invoices = DispatchProxy.Create<IInvoiceIssuanceRouteService, StubProxy>();
            ((StubProxy)(object)invoices).Call = (m, a) => m.Name == nameof(IInvoiceIssuanceRouteService.SetInitialRouteAsync)
                ? SaveRoute((int)a![0]!, (InvoiceIssuanceRoute)a[1]!) : throw new NotSupportedException(m.Name);
            var pos = DispatchProxy.Create<IPOSService, StubProxy>();
            ((StubProxy)(object)pos).Call = (m, a) => m.Name == nameof(IPOSService.CancelAsync)
                ? Cancel((int)a![0]!) : throw new NotSupportedException(m.Name);
            Service = new KioskService(f.Db, pos, null!, f.Service, invoices);
            async Task<Result<InvoiceIssuanceRouteDto>> SaveRoute(int id, InvoiceIssuanceRoute route)
            {
                InvoiceCalls++; Assert.Equal(f.Order.Id, id); Assert.Equal(OrderStatus.Completed, f.Order.Status);
                if (FailInvoice) return Result<InvoiceIssuanceRouteDto>.Failure(Error.Conflict("Synthetic route failure"));
                f.Order.InvoiceIssuanceRoute = route;
                await f.Db.SaveChangesAsync();
                return Result<InvoiceIssuanceRouteDto>.Success(new() { OrderId = id, Route = route });
            }
            async Task Cancel(int id)
            {
                Assert.Equal(f.Order.Id, id); Assert.Equal(OrderStatus.Draft, f.Order.Status);
                CancelCalls++; f.Order.Status = OrderStatus.Cancelled; await f.Db.SaveChangesAsync();
            }
        }
    }
    [Fact]
    public async Task Kiosk_cancel_checks_bank_retries_once_and_preserves_late_payment_for_review()
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f); var kiosk = new KioskHarness(f);
        await f.Service.TryCreateAsync(100, default);
        var command = new KioskCommand { Action = "cancel-payment", SessionKey = station.SessionKey, Revision = station.Revision, CommandId = Guid.NewGuid() };
        var result = JsonSerializer.SerializeToElement(await kiosk.Service.CommandAsync(station, command, default));
        Assert.Equal("idle", result.GetProperty("mode").GetString()); Assert.Null(station.OrderId);
        Assert.Equal(OrderStatus.Cancelled, f.Order.Status); Assert.Equal(1, kiosk.CancelCalls); Assert.Equal(1, f.Bank.CancelCalls);
        Assert.Equal(AcbSessionStatus.Cancelled, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        await kiosk.Service.CommandAsync(station, command, default);
        Assert.Equal(1, kiosk.CancelCalls); Assert.Equal(1, f.Bank.CancelCalls); Assert.Equal(0, f.FinalizeCount);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000); await f.Callback();
        Assert.Equal(AcbSessionStatus.ReviewRequired, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        Assert.Equal(OrderStatus.Cancelled, f.Order.Status); Assert.Equal(0, f.FinalizeCount);
    }
    [Theory]
    [InlineData(70000, false)]
    [InlineData(60000, false)]
    [InlineData(0, true)]
    public async Task Kiosk_cancel_keeps_order_when_payment_exists_or_bank_is_unreachable(int received, bool bankFailure)
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f); var kiosk = new KioskHarness(f);
        await f.Service.TryCreateAsync(100, default);
        if (received > 0) f.Bank.Pay(f.Bank.Orders.Single().Key, received); // No callback: cancellation itself must find the receipt.
        f.Bank.FailRetrieve = bankFailure;
        await Assert.ThrowsAsync<ConflictAppException>(() => kiosk.Service.CommandAsync(station,
            new KioskCommand { Action = "cancel-payment", SessionKey = station.SessionKey, Revision = station.Revision, CommandId = Guid.NewGuid() }, default));
        Assert.Equal(100, station.OrderId); Assert.NotNull(station.HelpRequestedAtUtc);
        Assert.Equal(OrderStatus.Draft, f.Order.Status); Assert.Equal(0, kiosk.CancelCalls); Assert.Equal(0, f.Bank.CancelCalls);
        Assert.Equal(0, f.FinalizeCount); Assert.Single(f.Order.Payments);
        Assert.NotEqual(AcbSessionStatus.Cancelled, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
    }
    [Fact]
    public async Task Kiosk_finish_retries_invoice_choice_before_clearing_a_paid_order()
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f); var kiosk = new KioskHarness(f) { FailInvoice = true };
        f.Order.Status = OrderStatus.Completed; await f.Db.SaveChangesAsync();
        var command = new KioskCommand { Action = "finish", SessionKey = station.SessionKey, Revision = station.Revision, CommandId = Guid.NewGuid() };
        await Assert.ThrowsAsync<ConflictAppException>(() => kiosk.Service.CommandAsync(station, command, default));
        Assert.Equal(100, station.OrderId); Assert.Null(station.CompletedAtUtc); Assert.NotNull(station.HelpRequestedAtUtc);
        kiosk.FailInvoice = false; await kiosk.Service.CommandAsync(station, command, default);
        Assert.Null(station.OrderId); Assert.Equal(InvoiceIssuanceRoute.Automatic, f.Order.InvoiceIssuanceRoute);
        Assert.Equal(2, kiosk.InvoiceCalls); Assert.Equal(0, f.FinalizeCount);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Kiosk_callback_and_scheduled_check_share_first_confirmation_and_finalize_once(bool callbackFirst)
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f); var kiosk = new KioskHarness(f);
        await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000);
        if (callbackFirst) await f.Callback();
        var before = f.Bank.RetrieveCalls;
        await kiosk.Service.PollAsync(station, default); // Local callback-state read, not an outbound bank lookup.
        Assert.Equal(before, f.Bank.RetrieveCalls);
        Assert.Equal(callbackFirst ? 1 : 0, f.FinalizeCount);
        await kiosk.Service.PollAsync(station, default, refreshBank: true);
        Assert.Equal(1, f.FinalizeCount); Assert.Equal(1, kiosk.InvoiceCalls);
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        Assert.Equal(callbackFirst ? AcbConfirmationSource.Callback : AcbConfirmationSource.ScheduledCheck, session.ConfirmationSource);
        await f.Callback(); await kiosk.Service.PollAsync(station, default, refreshBank: true);
        Assert.Equal(1, f.FinalizeCount); Assert.Equal(1, kiosk.InvoiceCalls);
        Assert.Single(f.Order.Payments, p => p.Provider == "ACB");
    }
    [Fact]
    public async Task Kiosk_check_reloads_callback_confirmation_saved_by_another_request_before_the_lock()
    {
        await using var f = await Fixture.Create(); var station = await AddKiosk(f); var kiosk = new KioskHarness(f);
        await f.Service.TryCreateAsync(100, default);
        var stale = await f.Db.Set<AcbQrSession>().SingleAsync();
        var confirmedAt = DateTime.UtcNow.AddSeconds(-1);
        await using (var callbackDb = new TestContext((DbContextOptions<TestContext>)f.Db.GetService<IDbContextOptions>(), f.Tenant))
        {
            var received = await callbackDb.Set<AcbQrSession>().SingleAsync();
            received.Status = AcbSessionStatus.Received; received.ConfirmationSource = AcbConfirmationSource.Callback; received.ConfirmedAtUtc = confirmedAt;
            callbackDb.Add(new AcbPaymentTransaction { StoreId = 1, SessionId = received.Id, TransactionNumber = "CALLBACK-FIRST", Amount = 70000, Status = "COMPLETED" });
            await callbackDb.SaveChangesAsync();
        }
        Assert.Equal(AcbSessionStatus.Pending, stale.Status);
        await kiosk.Service.PollAsync(station, default, refreshBank: true);
        Assert.Equal(1, f.FinalizeCount); Assert.Equal(0, f.Bank.RetrieveCalls);
        Assert.Equal(AcbConfirmationSource.Callback, stale.ConfirmationSource); Assert.Equal(confirmedAt, stale.ConfirmedAtUtc);
        Assert.Single(f.Order.Payments, p => p.Provider == "ACB");
    }

}
