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

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05WorkerTests
{
    [Fact]
    public async Task Trusted_bank_worker_is_off_by_default_before_creating_any_database_scope()
    {
        var scopes = new ForbiddenScopes(); using var worker = new StorefrontBankWorker(scopes, new ConfigurationBuilder().Build(), NullLogger<StorefrontBankWorker>.Instance);
        await worker.StartAsync(default); await worker.StopAsync(default); Assert.Equal(0, scopes.Calls);
    }

    [Fact]
    public async Task Actual_hosted_worker_uses_fresh_tenant_scopes_and_persistent_admin_keys_to_resume_intent_after_restart_without_POS_or_guest_page()
    {
        // Isolate discovery from all other draft/Review cases in the shared
        // collection, so this worker can see only its own single synthetic store.
        var isolated = new Web01Fixture(); await isolated.InitializeAsync();
        try
        {
            var c = await isolated.Web05Async(); var placed = await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); int intentId; string reference; string account;
            await using (var db = c.Context()) { var intent = await db.Set<StorefrontPaymentIntent>().SingleAsync(); intentId = intent.Id; reference = intent.ProviderReference; account = (await db.StoreBankAccounts.SingleAsync()).AccountNumber; }
            var bank = new Web05BankTransport(account, c.Web.Clock); var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["StorefrontBanking:WorkerEnabled"] = "true" }).Build();
            ServiceProvider Provider()
            {
                var services = new ServiceCollection(); services.AddScoped<TenantContext>(); services.AddScoped<ITenantContextWriter>(s => s.GetRequiredService<TenantContext>());
                services.AddScoped<AppDbContext>(s => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(c.Seed.Database.ConnectionString).Options, s.GetRequiredService<TenantContext>(), new Web01Actor(null, false)));
                services.AddScoped<IStorefrontBankProcessor>(s => new StorefrontBankProcessor(s.GetRequiredService<AppDbContext>(), Web05Support.AdminProtocol(c.AdminKeys, bank), c.Web.Clock)); return services.BuildServiceProvider();
            }
            await using (var first = Provider())
            {
                using var worker = new StorefrontBankWorker(first.GetRequiredService<IServiceScopeFactory>(), config, NullLogger<StorefrontBankWorker>.Instance); await worker.StartAsync(default);
                try { await Until(async () => { await using var db = c.Context(); return (await db.Set<StorefrontQrProcessing>().SingleAsync()).State == (int)StorefrontQrState.Pending; }); }
                finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await worker.StopAsync(stop.Token); }
            }
            Assert.Equal(1, bank.InitiateCalls); bank.Pay(reference, 45_000m);
            await using (var due = c.Context()) await due.Set<StorefrontQrProcessing>().ExecuteUpdateAsync(x => x.SetProperty(s => s.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
            await using (var restarted = Provider())
            {
                using var worker = new StorefrontBankWorker(restarted.GetRequiredService<IServiceScopeFactory>(), config, NullLogger<StorefrontBankWorker>.Instance); await worker.StartAsync(default);
                try { await Until(async () => { await using var db = c.Context(); return (await db.StorefrontOrders.SingleAsync()).PaymentState == StorefrontPaymentState.Confirmed; }); }
                finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await worker.StopAsync(stop.Token); }
            }
            await using var check = c.Context(); Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Equal(reference, (await check.Set<StorefrontPaymentIntent>().SingleAsync()).ProviderReference);
            Assert.Equal(intentId, (await check.Set<StorefrontPaymentIntent>().SingleAsync()).Id); Assert.Equal(1, bank.InitiateCalls); Assert.Single(bank.Orders);
            Assert.Empty(await check.POSShifts.ToListAsync()); Assert.Empty(await check.POSTerminals.ToListAsync()); Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync()); Assert.Empty(await check.AdminMenuItems.ToListAsync());
        }
        finally { await isolated.DisposeAsync(); }
    }
    private static async Task Until(Func<Task<bool>> predicate)
    { var end = DateTime.UtcNow.AddSeconds(35); while (DateTime.UtcNow < end) { if (await predicate()) return; await Task.Delay(100); } Assert.Fail("Owned hosted bank worker did not reach required durable state within35seconds."); }
    private sealed class ForbiddenScopes : IServiceScopeFactory
    { internal int Calls; public IServiceScope CreateScope() { Calls++; throw new InvalidOperationException("Disabled worker must not create scopes."); } }
}
