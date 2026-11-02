using GaoApp.Domain.Entities;
using GaoApp.Application.Common;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07WorkerTests
{
    [Fact]
    public async Task Actual_hosted_worker_expires_then_after_restart_and_new_sales_closed_retains_late_money_and_one_refund_without_any_POS_or_guest_browser()
    {
        // Separate GUID database: a discovery worker must never process unrelated
        // scenarios from the shared collection or any preexisting store.
        var isolated = new Web01Fixture();
        try
        {
            await isolated.InitializeAsync(); var session = await Web07BankSession.CreateAsync(isolated); var c = session.Case;
            c.SynchronizeCommandClockWithActualLegacyTime = false;
            await using (var db = c.Context()) c.Checkout.Clock.Utc = DateTime.SpecifyKind((await db.Set<StorefrontPaymentIntent>().SingleAsync()).ExpiresAtUtc, DateTimeKind.Utc).AddMinutes(1);
            using var transport = new Web07PagedTransport(session.Transport, session.Reference, 45_000m);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["StorefrontBanking:WorkerEnabled"] = "true" }).Build();
            ServiceProvider Provider()
            {
                var services = new ServiceCollection(); services.AddScoped<TenantContext>(); services.AddScoped<ITenantContextWriter>(s => s.GetRequiredService<TenantContext>());
                services.AddScoped<AppDbContext>(s => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(c.Seed.Database.ConnectionString).Options,
                    s.GetRequiredService<TenantContext>(), new Web01Actor(null, false)));
                services.AddScoped<IStorefrontBankProcessor>(s => new StorefrontBankProcessor(s.GetRequiredService<AppDbContext>(),
                    Web05Support.AdminProtocol(c.Banking!.AdminKeys, transport), c.Clock)); return services.BuildServiceProvider();
            }
            async Task ScheduleNow()
            {
                // Mutable server queue scheduling only, never payment/closure/
                // bank-time/confidence proof or another case's work.
                await using var db = c.Context(); await db.Set<StorefrontQrProcessing>().Where(x => x.PaymentIntentId == session.IntentId)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1)));
            }
            async Task RunUntil(Func<Task<bool>> predicate)
            {
                await using var provider = Provider(); using var worker = new StorefrontBankWorker(provider.GetRequiredService<IServiceScopeFactory>(), config, NullLogger<StorefrontBankWorker>.Instance);
                await worker.StartAsync(default);
                try
                {
                    var end = DateTime.UtcNow.AddSeconds(35);
                    while (!await predicate())
                    {
                        if (DateTime.UtcNow >= end) await session.CaptureDiagnosticsAsync("hosted-worker-timeout", transport);
                        Assert.True(DateTime.UtcNow < end, "Actual owned hosted worker did not reach the required durable result."); await Task.Delay(100);
                    }
                }
                finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await worker.StopAsync(stop.Token); }
            }
            await ScheduleNow(); await RunUntil(async () => { await using var db = c.Context(); return await db.StorefrontOrders.AnyAsync(x => x.Id == c.OrderId && x.Phase == StorefrontOrderPhase.Cancelled); });
            int closureId;
            await using (var db = c.Context())
            {
                closureId = Assert.Single(await db.Set<StorefrontQrClosureProof>().ToListAsync()).Id;
                Assert.Empty(await db.Set<StorefrontRefundFunding>().ToListAsync()); Assert.Equal(0m, (await db.InventoryBalances.SingleAsync()).ReservedQty);
                await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, false));
            }
            c.Checkout.Clock.Utc = c.Checkout.Clock.Utc.AddMinutes(1); session.Transport.Pay(session.Reference, 45_000m);
            await ScheduleNow(); await RunUntil(async () => { await using var db = c.Context(); return await db.Set<StorefrontRefundFunding>().AnyAsync(x => x.StorefrontOrderId == c.OrderId && x.Amount == 45_000m); });
            await using var check = c.Context(); Assert.Equal(closureId, Assert.Single(await check.Set<StorefrontQrClosureProof>().ToListAsync()).Id);
            Assert.Equal(StorefrontOrderPhase.Cancelled, (await check.StorefrontOrders.SingleAsync()).Phase); Assert.False((await check.StorefrontSettings.SingleAsync()).IsEnabled);
            Assert.Equal(45_000m, Assert.Single(await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync()).Amount);
            Assert.Equal(45_000m, Assert.Single(await check.Set<StorefrontRefundFunding>().ToListAsync()).Amount); Assert.Single(await check.Set<StorefrontRefund>().ToListAsync());
            Assert.Empty(await check.Set<StorefrontRefundEvidence>().ToListAsync()); Assert.Empty(await check.POSShifts.ToListAsync()); Assert.Empty(await check.POSTerminals.ToListAsync());
            Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync()); Assert.Empty(await check.AdminMenuItems.ToListAsync()); Assert.Equal(1, session.Transport.InitiateCalls);
        }
        finally { await isolated.DisposeAsync(); }
    }
}
