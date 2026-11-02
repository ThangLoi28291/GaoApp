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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06InvoiceWorkerTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Real_worker_recovers_committed_native_invoice_work_without_customer_page_POS_runtime_or_new_sales_enabled()
    {
        var c = await Web06Case.CreateAsync(fixture); await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 200_000m));
        var command = await c.RequestAsync<StorefrontOperationRequest>();
        var fault = new LostAfterCommit();
        await using (var db = c.Context())
        {
            var ack = await Web06FinanceSupport.Settlement(c, db, terminal, invoices: fault).PostSaleAsync(c.OrderId, command);
            Assert.Equal(c.OrderId, ack.OrderId); Assert.False(ack.Replayed); Assert.NotEmpty(ack.Version); Assert.Equal(1, fault.Calls);
        }
        await using (var db = c.Context()) { Assert.Single(await db.Orders.ToListAsync()); Assert.Empty(await db.InvoiceHeads.ToListAsync()); Assert.Null((await db.Set<StorefrontInvoiceWork>().SingleAsync()).CompletedAtUtc); await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, false)); }
        Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog);
        var services = new ServiceCollection(); services.AddLogging(builder => builder.ClearProviders()); services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(s => s.GetRequiredService<TenantContext>()); services.AddScoped<ITenantContextWriter>(s => s.GetRequiredService<TenantContext>());
        services.AddScoped<AppDbContext>(s => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(c.Seed.Database.ConnectionString).Options, s.GetRequiredService<ITenantContext>(), new Web01Actor(null, false)));
        services.AddScoped<IStorefrontInvoiceProcessor>(s => { var db = s.GetRequiredService<AppDbContext>(); return new StorefrontInvoiceProcessor(db, Web06FinanceSupport.InvoiceService(c, db), TimeProvider.System); });
        await using var provider = services.BuildServiceProvider(); var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["StorefrontFulfillment:InvoiceWorkerEnabled"] = "true" }).Build();
        using var worker = new StorefrontInvoiceWorker(provider.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<StorefrontInvoiceWorker>.Instance, TimeProvider.System);
        await worker.StartAsync(default); var complete = false;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (!complete && DateTime.UtcNow < deadline) { await using var db = c.Context(); complete = await db.Set<StorefrontInvoiceWork>().AnyAsync(x => x.StorefrontOrderId == c.OrderId && x.CompletedAtUtc != null); if (!complete) await Task.Delay(100); }
            Assert.True(complete, "Actual worker must discover the durable row through its real StoreId navigation and a fresh tenant scope.");
        }
        finally { await worker.StopAsync(default); }
        await using var check = c.Context(); Assert.Single(await check.Orders.ToListAsync()); Assert.Single(await check.InvoiceHeads.ToListAsync()); Assert.Single(await check.Set<StorefrontInvoiceWork>().ToListAsync());
        Assert.Equal(1, await check.InventoryTransactions.CountAsync(x => x.ReferenceType == GaoApp.Domain.Enums.InventoryReferenceType.Delivery));
        Assert.Equal(200_000m, (await check.POSShifts.SingleAsync()).CashInTotal); Assert.Equal(0m, (await check.POSShifts.SingleAsync()).CashSalesTotal); Assert.Empty(await check.AdminMenuItems.ToListAsync());
    }
    private sealed class SimulatedProcessLoss : Exception { }
    private sealed class LostAfterCommit : IStorefrontInvoiceProcessor { internal int Calls; public Task ProcessAsync(int id, CancellationToken ct = default) { Calls++; throw new SimulatedProcessLoss(); } }
}
