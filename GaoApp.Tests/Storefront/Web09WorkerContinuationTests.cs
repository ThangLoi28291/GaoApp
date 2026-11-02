using GaoApp.Application.Common;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb09"), Trait("Category", "StorefrontWeb09")]
public sealed class Web09WorkerContinuationTests
{
    [Fact]
    public async Task Owned_actual_bank_worker_retains_late_money_once_for_cancelled_order_after_genuine_Stop_with_public_release_off()
    {
        var fixture = new Web01Fixture(); Web09RecoverySupport.Plan(fixture.Database, "Isolated WEB09 bank-owner worker only, no AdminMain/live transport");
        try
        {
            await fixture.InitializeAsync(); var pending = await Web05Prepared.CreateAsync(fixture); var old = pending.Case;
            // Prior-stage builders arrange an existing real pending order. They
            // are not evidence of the new operator activation/release path.
            var control = new Web09Case(old.Seed, old.Web); await control.GrantAsync(StorefrontPermissions.ViewSettings, StorefrontPermissions.ManageSettings);
            await control.StopAsync(activationPolicy: false);
            await using (var db = old.Context())
            {
                var order = await db.StorefrontOrders.AsNoTracking().SingleAsync();
                await new StorefrontCancellationService(db, new Web01Actor(null, false), new InventoryBalanceRepository(db), old.Web.Clock).CancelGuestAsync(new()
                {
                    Token = Web04Support.GuestToken(pending.Placed), Reason = "WEB09 real cancellation before late owned bank delivery",
                    ClientRequestId = Guid.NewGuid(), ExpectedRevision = order.Revision, ExpectedVersion = Convert.ToBase64String(order.RowVersion)
                });
            }
            pending.Bank.Pay(pending.Reference, 45000m);
            await using (var db = old.Context())
            {
                var next = (await db.Set<StorefrontQrProcessing>().AsNoTracking().SingleAsync()).NextAttemptAtUtc;
                old.Web.Clock.Utc = DateTime.SpecifyKind(next, DateTimeKind.Utc).AddSeconds(1); // Forward clock only, never rewrite queue/proof rows.
            }
            var services = WorkerServices(old.Seed.Database.ConnectionString);
            services.AddScoped<IStorefrontBankProcessor>(s => new StorefrontBankProcessor(s.GetRequiredService<AppDbContext>(),
                Web05Support.AdminProtocol(old.AdminKeys, pending.Bank), old.Web.Clock));
            await using var provider = services.BuildServiceProvider();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["StorefrontBanking:WorkerEnabled"] = "true", ["StorefrontHost:ReleaseEnabled"] = "false" }).Build();
            using var worker = new StorefrontBankWorker(provider.GetRequiredService<IServiceScopeFactory>(), config, NullLogger<StorefrontBankWorker>.Instance, old.Web.Clock);
            await worker.StartAsync(default);
            try
            {
                var received = false; var deadline = DateTime.UtcNow.AddSeconds(25);
                while (!received && DateTime.UtcNow < deadline)
                {
                    await using var db = old.Context(); received = await db.Set<StorefrontRefundFunding>().AnyAsync(x => x.StorefrontOrderId == pending.OrderId && x.Amount == 45000m);
                    if (!received) await Task.Delay(100);
                }
                Assert.True(received, "Real bank-owner worker must discover and process its due row while public release is off and store sales are paused.");
            }
            finally { await worker.StopAsync(default); }
            await old.ProcessAsync(pending.IntentId, pending.Bank); // Genuine duplicate retrieval.
            await using var verify = old.Context(); var actual = await verify.StorefrontOrders.SingleAsync();
            Assert.Equal(StorefrontOrderPhase.Cancelled, actual.Phase); Assert.Equal(StorefrontRefundState.Requested, actual.RefundState);
            Assert.False((await verify.StorefrontSettings.SingleAsync()).IsEnabled);
            Assert.Equal(45000m, Assert.Single(await verify.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == old.StoreId).ToListAsync()).Amount);
            Assert.Equal(45000m, Assert.Single(await verify.Set<StorefrontRefundFunding>().ToListAsync()).Amount);
            Assert.Single(await verify.Set<StorefrontRefund>().ToListAsync()); Assert.Empty(await verify.Set<StorefrontPaymentAllocation>().ToListAsync());
            Assert.Empty(await verify.OrderPayments.ToListAsync()); Assert.Empty(await verify.Orders.ToListAsync()); Assert.Empty(await verify.InventoryTransactions.ToListAsync());
            Assert.Empty(await verify.Set<StorefrontRefundEvidence>().ToListAsync()); Assert.Empty(await verify.AdminMenuItems.ToListAsync());
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Actual_invoice_owner_worker_completes_previously_committed_work_after_operator_Stop_without_duplicate_sale_stock_or_cash()
    {
        var fixture = new Web01Fixture(); Web09RecoverySupport.Plan(fixture.Database, "Isolated WEB09 invoice owner worker only, no AdminMain/provider transport");
        try
        {
            await fixture.InitializeAsync(); var c = await Web06Case.CreateAsync(fixture);
            await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
            var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 200000m));
            var loss = new LostInvoiceAfterCommit();
            await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal, invoices: loss).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
            Assert.Equal(1, loss.Calls);
            var control = new Web09Case(c.Seed, c.Checkout); await control.GrantAsync(StorefrontPermissions.ViewSettings, StorefrontPermissions.ManageSettings); await control.StopAsync(false);
            var before = await c.Checkout.CountsAsync();
            await using (var db = c.Context()) { Assert.Empty(await db.InvoiceHeads.ToListAsync()); Assert.Null((await db.Set<StorefrontInvoiceWork>().SingleAsync()).CompletedAtUtc); }
            var services = WorkerServices(c.Seed.Database.ConnectionString);
            services.AddScoped<IStorefrontInvoiceProcessor>(s => { var db = s.GetRequiredService<AppDbContext>(); return new StorefrontInvoiceProcessor(db, Web06FinanceSupport.InvoiceService(c, db), TimeProvider.System); });
            await using var provider = services.BuildServiceProvider();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["StorefrontFulfillment:InvoiceWorkerEnabled"] = "true" }).Build();
            using var worker = new StorefrontInvoiceWorker(provider.GetRequiredService<IServiceScopeFactory>(), config, NullLogger<StorefrontInvoiceWorker>.Instance, TimeProvider.System);
            await worker.StartAsync(default);
            try
            {
                var complete = false; var deadline = DateTime.UtcNow.AddSeconds(25);
                while (!complete && DateTime.UtcNow < deadline)
                {
                    await using var db = c.Context(); complete = await db.Set<StorefrontInvoiceWork>().AnyAsync(x => x.StorefrontOrderId == c.OrderId && x.CompletedAtUtc != null);
                    if (!complete) await Task.Delay(100);
                }
                Assert.True(complete, "Actual owner worker must discover durable invoice work through fresh tenant scopes after Stop.");
            }
            finally { await worker.StopAsync(default); }
            Assert.Equal(before, await c.Checkout.CountsAsync());
            await using var verify = c.Context(); Assert.Single(await verify.InvoiceHeads.ToListAsync()); Assert.Single(await verify.Orders.ToListAsync());
            Assert.Equal(200000m, (await verify.POSShifts.SingleAsync()).CashInTotal); Assert.Equal(0m, (await verify.POSShifts.SingleAsync()).CashSalesTotal);
            Assert.False((await verify.StorefrontSettings.SingleAsync()).IsEnabled); Assert.Empty(await verify.AdminMenuItems.ToListAsync());
        }
        finally { await fixture.DisposeAsync(); }
    }
    private static ServiceCollection WorkerServices(string connection)
    {
        var services = new ServiceCollection(); services.AddLogging(x => x.ClearProviders()); services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(x => x.GetRequiredService<TenantContext>()); services.AddScoped<ITenantContextWriter>(x => x.GetRequiredService<TenantContext>());
        services.AddScoped<AppDbContext>(x => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options,
            x.GetRequiredService<ITenantContext>(), new Web01Actor(null, false))); return services;
    }
    private sealed class SimulatedInvoiceProcessLoss : Exception { }
    private sealed class LostInvoiceAfterCommit : IStorefrontInvoiceProcessor
    { internal int Calls; public Task ProcessAsync(int id, CancellationToken ct = default) { Calls++; throw new SimulatedInvoiceProcessLoss(); } }
}
