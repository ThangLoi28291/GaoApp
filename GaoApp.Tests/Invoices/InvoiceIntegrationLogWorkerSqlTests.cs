using System.Net;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Services.Invoices;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

[Collection("R1FinalDatabasePreflight")]
public sealed class InvoiceIntegrationLogWorkerSqlTests
{
    [Theory]
    [InlineData(InvoiceIntegrationActionType.IssueInvoice)]
    [InlineData(InvoiceIntegrationActionType.SearchByTransactionUuid)]
    public async Task Worker_log_derives_store_from_invoice_and_preserves_following_heartbeat_save(InvoiceIntegrationActionType action)
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateHostContext();
        var invoice = new InvoiceHead { StoreId = seed.StoreId, IsAutoInvoiceGroup = true };
        db.Add(invoice); await db.SaveChangesAsync();
        Assert.Null(db.CurrentStoreId);
        var repo = new InvoiceIntegrationLogRepository(db);
        var log = new InvoiceIntegrationLog { InvoiceHeadId = invoice.Id, ActionType = action, IsSuccess = true };

        await repo.AddAsync(log);
        await repo.SaveChangesAsync();
        var state = new AutoInvoiceWorkerState { StoreId = seed.StoreId, WorkerName = "new-worker-log-test", LastResult = "after provider" };
        db.Add(state); await db.SaveChangesAsync();

        await using var check = database.CreateHostContext();
        Assert.Equal(seed.StoreId, (await check.InvoiceIntegrationLogs.SingleAsync()).StoreId);
        Assert.Equal("after provider", (await check.AutoInvoiceWorkerStates.SingleAsync()).LastResult);
    }

    [Fact]
    public async Task Worker_log_real_lookup_client_writes_valid_store_without_web_tenant()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateHostContext();
        var invoice = new InvoiceHead { StoreId = seed.StoreId, IsAutoInvoiceGroup = true };
        db.Add(invoice); await db.SaveChangesAsync();
        using var http = new HttpClient(new FailedProviderResponse());
        var client = new ViettelInvoiceLookupClient(http, new InvoiceIntegrationLogRepository(db));

        var result = await client.SearchByTransactionUuidAsync(invoice.Id, "https://provider.invalid", "synthetic", "synthetic",
            InvoiceProviderAuthMode.BasicAuth, "0000000000", "synthetic-uuid");

        Assert.False(result.IsSuccess);
        Assert.Equal("HTTP_503", result.Error!.Code);
        var log = await db.InvoiceIntegrationLogs.SingleAsync();
        Assert.Equal(seed.StoreId, log.StoreId);
        Assert.Equal("HTTP_503", log.ErrorCode);
        Assert.DoesNotContain(db.ChangeTracker.Entries<InvoiceIntegrationLog>(), x => x.State == EntityState.Added);
    }

    [Fact]
    public async Task Worker_log_rejects_wrong_store_missing_invoice_and_foreign_tenant_before_tracking()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateHostContext();
        var invoice = new InvoiceHead { StoreId = seed.StoreId, IsAutoInvoiceGroup = true };
        db.Add(invoice); await db.SaveChangesAsync();
        var repo = new InvoiceIntegrationLogRepository(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AddAsync(new InvoiceIntegrationLog
            { InvoiceHeadId = invoice.Id, StoreId = seed.StoreId + 1000 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AddAsync(new InvoiceIntegrationLog
            { InvoiceHeadId = int.MaxValue }));
        Assert.Empty(db.ChangeTracker.Entries<InvoiceIntegrationLog>());
        await using var foreign = database.CreateTenantContext(seed.StoreId + 1000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new InvoiceIntegrationLogRepository(foreign)
            .AddAsync(new InvoiceIntegrationLog { InvoiceHeadId = invoice.Id }));
        Assert.Empty(foreign.ChangeTracker.Entries<InvoiceIntegrationLog>());
    }

    [Fact]
    public async Task Worker_log_failed_insert_is_detached_without_losing_pending_business_changes()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateHostContext();
        var invoice = new InvoiceHead { StoreId = seed.StoreId, IsAutoInvoiceGroup = true };
        db.Add(invoice); await db.SaveChangesAsync();
        var repo = new InvoiceIntegrationLogRepository(db);
        var log = new InvoiceIntegrationLog { InvoiceHeadId = invoice.Id, ErrorCode = new string('x', 101) };
        await repo.AddAsync(log);
        invoice.LastErrorCode = "RETAIN_BUSINESS_STATE";

        await Assert.ThrowsAsync<DbUpdateException>(() => repo.SaveChangesAsync());

        Assert.Equal(EntityState.Detached, db.Entry(log).State);
        Assert.Equal(EntityState.Modified, db.Entry(invoice).State);
        await db.SaveChangesAsync();
        await using var check = database.CreateHostContext();
        Assert.Equal("RETAIN_BUSINESS_STATE", (await check.InvoiceHeads.SingleAsync()).LastErrorCode);
        Assert.Empty(await check.InvoiceIntegrationLogs.ToListAsync());
    }

    private sealed class FailedProviderResponse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("synthetic unavailable") });
    }
}
