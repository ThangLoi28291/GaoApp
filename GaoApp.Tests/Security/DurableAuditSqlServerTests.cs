using System.Security.Claims;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Identity;
using GaoApp.Infrastructure.Interceptors;
using GaoApp.Infrastructure.Repositories.Audit;
using GaoApp.Infrastructure.Services.Audit;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Observability;
using GaoApp.Web.Services.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class DurableAuditSqlServerTests
{
    [Fact]
    public async Task Downgrade_refuses_to_discard_pending_evidence_and_preserves_delivered_logs()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync();
        var store = await sql.SeedInventoryCatalogAsync();
        await using (var db = sql.CreateTenantContext(store.StoreId))
            await new AuditLogRepository(db).AddAsync(new AuditLog { StoreId = store.StoreId, Summary = "Downgrade evidence" });
        await Assert.ThrowsAnyAsync<Exception>(() => sql.MigrateAsync("20261014140000_AddBankNotificationBridge"));
        Assert.Equal(1L, await sql.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.AuditOutbox WHERE ProcessedAtUtc IS NULL"));
        Assert.Equal(1, await Process(sql, store.StoreId));
        await sql.MigrateAsync("20261014140000_AddBankNotificationBridge");
        Assert.Equal(1L, await sql.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.AuditLogs"));
    }

    [Fact]
    public async Task Main_write_failure_leaves_no_success_evidence_and_a_later_valid_save_can_succeed()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync();
        var store = await sql.SeedInventoryCatalogAsync();
        await using var db = Audited(sql, store.StoreId);
        var brand = Brand("invalid"); brand.Name = null!;
        db.Brands.Add(brand);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Empty(await db.AuditOutbox.ToListAsync());
        brand.Name = "Recovered valid name";
        await db.SaveChangesAsync();
        Assert.Single(await db.AuditOutbox.ToListAsync());
        Assert.Single(await db.Brands.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Bank_configuration_and_secret_rotation_are_audited_and_foreign_store_evidence_is_rejected()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync();
        var store = await sql.SeedInventoryCatalogAsync();
        var other = await sql.SeedInventoryCatalogAsync();
        await using var db = Audited(sql, store.StoreId);
        var bank = new StoreBankAccount
        {
            BankCode = "ACB", BankName = "Audit test bank", AccountName = "Synthetic merchant", AccountNumber = "000000001",
            CallbackSecret = "synthetic-old-bank-secret"
        };
        db.Add(bank); await db.SaveChangesAsync();
        bank.CallbackSecret = "synthetic-new-bank-secret"; await db.SaveChangesAsync();
        bank.AccountNumber = "000000002"; await db.SaveChangesAsync();
        var events = (await db.AuditOutbox.ToListAsync()).Select(AuditOutboxEvidence.Read).ToList();
        Assert.Equal(3, events.Count);
        Assert.All(events, x => Assert.DoesNotContain("synthetic-", (x.OldValuesJson ?? "") + x.NewValuesJson));
        Assert.Single(events, x => x.ChangedColumnsJson?.Contains("CredentialsChanged") == true);
        Assert.Single(events, x => x.OldValuesJson?.Contains("000000001") == true && x.NewValuesJson?.Contains("000000002") == true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AuditLogRepository(db).AddAsync(new AuditLog { StoreId = other.StoreId }));
        await using var check = sql.CreateTenantContext(other.StoreId);
        Assert.Empty(await check.AuditOutbox.ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Generated_ids_and_evidence_share_the_outer_transaction_for_sync_and_async_saves(bool sync, bool mars)
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync();
        var store = await sql.SeedInventoryCatalogAsync();
        var brand = Brand("committed");
        await using (var db = Audited(sql, store.StoreId, mars))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Brands.Add(brand);
            if (sync) db.SaveChanges(); else await db.SaveChangesAsync();
            var message = Assert.Single(await db.AuditOutbox.ToListAsync());
            var evidence = AuditOutboxEvidence.Read(message);
            Assert.Equal(brand.Id.ToString(), evidence.EntityId);
            Assert.Contains("\"Id\":" + brand.Id, evidence.NewValuesJson);
            Assert.Equal("Brand #" + brand.Id, evidence.EntityDisplay);
            await tx.RollbackAsync();
        }
        await using (var check = sql.CreateTenantContext(store.StoreId))
        {
            Assert.False(await check.Brands.AnyAsync(x => x.Code == brand.Code));
            Assert.Empty(await check.AuditOutbox.ToListAsync());
            Assert.Empty(await check.AuditLogs.ToListAsync());
        }
        await using (var db = Audited(sql, store.StoreId, mars))
        {
            db.Brands.Add(Brand("saved"));
            if (sync) db.SaveChanges(); else await db.SaveChangesAsync();
        }
        Assert.Equal(1, await Process(sql, store.StoreId));
        await using var final = sql.CreateTenantContext(store.StoreId);
        Assert.Single(await final.AuditLogs.ToListAsync());
        Assert.NotNull((await final.AuditOutbox.SingleAsync()).ProcessedAtUtc);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Rejected_evidence_rolls_back_the_primary_write_including_outer_transactions(bool outer, bool mars)
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync();
        var store = await sql.SeedInventoryCatalogAsync();
        await sql.ExecuteAsync("CREATE TRIGGER dbo.Issue10_RejectEvidence ON dbo.AuditOutbox AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE PayloadJson LIKE '%Must roll back%') THROW 51012, 'synthetic-outbox-failure', 1;");
        var code = "reject-" + Guid.NewGuid().ToString("N")[..16];
        await using (var db = Audited(sql, store.StoreId, mars))
        {
            await using var tx = outer ? await db.Database.BeginTransactionAsync() : null;
            if (outer)
            {
                db.Brands.Add(Brand("prior"));
                await db.SaveChangesAsync();
            }
            db.Brands.Add(new Brand { Code = code, Name = "Must roll back" });
            if (outer) Assert.ThrowsAny<Exception>(() => db.SaveChanges());
            else await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
            Assert.Empty(db.ChangeTracker.Entries());
        }
        await using var check = sql.CreateTenantContext(store.StoreId);
        Assert.Empty(await check.Brands.ToListAsync());
        Assert.Empty(await check.AuditOutbox.ToListAsync());
        Assert.Empty(await check.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Sink_failure_survives_context_restart_and_retries_without_replaying_business_or_leaking_secrets()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync();
        var store = await sql.SeedInventoryCatalogAsync();
        var brand = Brand("recovery");
        await using (var db = Audited(sql, store.StoreId))
        {
            db.Brands.Add(brand);
            await db.SaveChangesAsync();
            await new AuditLogRepository(db).AddAsync(new AuditLog
            {
                StoreId = store.StoreId, Summary = "Synthetic independent failed sign-in", IsSuccess = false,
                NewValuesJson = "{\"PasswordHash\":\"synthetic-secret\",\"Visible\":true}"
            });
        }
        await sql.ExecuteAsync("CREATE TRIGGER dbo.Issue10_RejectSink ON dbo.AuditLogs AFTER INSERT AS THROW 51013, 'synthetic-secret-sink', 1;");
        var logger = new GlobalExceptionMiddlewareTests.RecordingLogger<AuditOutboxProcessor>();
        Assert.Equal(0, await Process(sql, store.StoreId, logger));
        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, x => { Assert.DoesNotContain("synthetic-secret", x.Message); Assert.Null(x.Exception); });
        await using (var check = sql.CreateTenantContext(store.StoreId))
        {
            var pending = await check.AuditOutbox.ToListAsync();
            Assert.Equal(2, pending.Count);
            Assert.All(pending, x => { Assert.Null(x.ProcessedAtUtc); Assert.Equal(1, x.AttemptCount); Assert.DoesNotContain("synthetic-secret", x.PayloadJson); Assert.DoesNotContain("synthetic-secret", x.LastErrorType); });
            Assert.Equal(1, await check.Brands.CountAsync(x => x.Code == brand.Code));
        }
        await sql.ExecuteAsync("DROP TRIGGER dbo.Issue10_RejectSink; UPDATE dbo.AuditOutbox SET NextAttemptAtUtc=SYSUTCDATETIME();");
        Assert.Equal(2, await Process(sql, store.StoreId));
        Assert.Equal(0, await Process(sql, store.StoreId));
        await using var final = sql.CreateTenantContext(store.StoreId);
        Assert.Equal(2, await final.AuditLogs.CountAsync());
        Assert.Single(await final.AuditLogs.Where(x => !x.IsSuccess).ToListAsync());
        Assert.Equal(1, await final.Brands.CountAsync(x => x.Code == brand.Code));
    }

    [Fact]
    public async Task Concurrent_workers_and_redelivery_produce_one_log_per_event_and_enforce_store_scope()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync();
        var first = await sql.SeedInventoryCatalogAsync();
        var second = await sql.SeedInventoryCatalogAsync();
        Guid eventId;
        await using (var db = Audited(sql, first.StoreId))
        {
            db.Brands.Add(Brand("parallel")); await db.SaveChangesAsync();
            eventId = (await db.AuditOutbox.SingleAsync()).EventId;
        }
        Assert.Equal(0, await Process(sql, second.StoreId));
        var counts = await Task.WhenAll(Process(sql, first.StoreId), Process(sql, first.StoreId));
        Assert.Equal(1, counts.Sum());
        // Simulate redelivery of an already materialized event after a consumer restart.
        await sql.ExecuteAsync("UPDATE dbo.AuditOutbox SET ProcessedAtUtc=NULL,NextAttemptAtUtc=SYSUTCDATETIME();");
        Assert.Equal(1, await Process(sql, first.StoreId));
        await using (var db = sql.CreateTenantContext(first.StoreId))
        {
            Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EventId == eventId));
            var message = await db.AuditOutbox.SingleAsync();
            message.PayloadJson = "{}";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = sql.CreateTenantContext(second.StoreId))
        {
            Assert.Empty(await db.AuditOutbox.ToListAsync());
            Assert.Empty(await db.AuditLogs.ToListAsync());
        }
        await using var host = sql.CreateHostContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Processor(sql, host).ProcessPendingAsync());
    }

    [Fact]
    public async Task Background_and_credential_only_changes_keep_evidence_without_http_or_credential_values()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync();
        var store = await sql.SeedInventoryCatalogAsync();
        var tenant = new TenantContext(); tenant.SetStore(store.StoreId, "test-background");
        var accessor = new HttpAuditExecutionContextAccessor(new HttpContextAccessor(), tenant);
        int userId;
        await using (var db = Audited(sql, store.StoreId, accessor: accessor))
        {
            var user = new User { UserName = "audit-" + Guid.NewGuid().ToString("N"), PasswordHash = "synthetic-old-secret" };
            db.Users.Add(user); await db.SaveChangesAsync(); userId = user.Id;
            user.PasswordHash = "synthetic-new-secret"; await db.SaveChangesAsync();
            var messages = await db.AuditOutbox.ToListAsync();
            Assert.Equal(2, messages.Count);
            Assert.All(messages, x => Assert.DoesNotContain("synthetic-", x.PayloadJson));
            var evidence = messages.Select(AuditOutboxEvidence.Read).ToArray();
            Assert.All(evidence, x => Assert.Equal("SYSTEM:background", x.ActorUserName));
            Assert.Single(evidence, x => x.ChangedColumnsJson?.Contains("CredentialsChanged") == true && x.EntityId == userId.ToString());
        }
        Assert.Equal(2, await Process(sql, store.StoreId));
    }

    [Fact]
    public async Task Password_change_is_not_committed_if_its_transactional_evidence_cannot_be_saved()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync();
        var store = await sql.SeedInventoryCatalogAsync();
        const string password = "Original password 2026!";
        ClaimsPrincipal principal;
        int userId;
        await using (var db = sql.CreateTenantContext(store.StoreId))
        {
            var user = new User { UserName = "password-" + Guid.NewGuid().ToString("N"), PasswordHash = new PasswordHasher().Hash(password) };
            var member = new UserInStore { StoreId = store.StoreId, User = user,
                Role = new Role { StoreId = store.StoreId, Code = "AUDIT", Name = "Audit test" } };
            db.Add(member); await db.SaveChangesAsync(); userId = user.Id;
            principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("store_id", store.StoreId.ToString()),
                new Claim(AuthSessionStamp.ClaimType, AuthSessionStamp.Create(user, member))
            }, "test"));
        }
        await sql.ExecuteAsync("CREATE TRIGGER dbo.Issue10_RejectPasswordAudit ON dbo.AuditLogs AFTER INSERT AS THROW 51014, 'synthetic-evidence-error', 1;");
        var tenant = new TenantContext(); tenant.SetStore(store.StoreId, "password-test");
        await using (var db = Audited(sql, store.StoreId))
            await Assert.ThrowsAnyAsync<Exception>(() => new EmployeeAccountService(db, tenant, new PasswordHasher())
                .ChangePasswordAsync(principal, password, "Replacement password 2026!", default));
        await using var check = sql.CreateTenantContext(store.StoreId);
        Assert.True(new PasswordHasher().Verify(password, (await check.Users.SingleAsync(x => x.Id == userId)).PasswordHash));
        Assert.Empty(await check.AuditLogs.ToListAsync());
        Assert.Empty(await check.AuditOutbox.ToListAsync());
    }

    [Fact]
    public async Task Cancellation_after_primary_save_rolls_back_before_returning_to_the_caller()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync();
        var store = await sql.SeedInventoryCatalogAsync();
        using var cancel = new CancellationTokenSource();
        var tenant = new TenantContext(); tenant.SetStore(store.StoreId, "cancel-test");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(sql.ConnectionString)
            .AddInterceptors(new CancelAfterPrimary(cancel), new AuditSaveChangesInterceptor(new Context(store.StoreId))).Options;
        await using (var db = new AppDbContext(options, tenant, new Actor()))
        {
            db.Brands.Add(Brand("cancelled"));
            await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync(cancel.Token));
        }
        await using var check = sql.CreateTenantContext(store.StoreId);
        Assert.Empty(await check.Brands.ToListAsync());
        Assert.Empty(await check.AuditOutbox.ToListAsync());
    }

    private static Brand Brand(string prefix) => new() { Code = prefix + "-" + Guid.NewGuid().ToString("N")[..16], Name = "Audit " + prefix };
    private static AppDbContext Audited(InventoryPostingLocalDb sql, int storeId, bool mars = false, IAuditExecutionContextAccessor? accessor = null)
    {
        var tenant = new TenantContext(); tenant.SetStore(storeId, "audit-test");
        var connection = new SqlConnectionStringBuilder(sql.ConnectionString) { MultipleActiveResultSets = mars }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(accessor ?? new Context(storeId))).Options;
        return new(options, tenant, new Actor());
    }
    private static AuditOutboxProcessor Processor(InventoryPostingLocalDb sql, AppDbContext db, ILogger<AuditOutboxProcessor>? logger = null)
        => new(db, new Factory(sql.ConnectionString), logger ?? NullLogger<AuditOutboxProcessor>.Instance);
    private static async Task<int> Process(InventoryPostingLocalDb sql, int storeId, ILogger<AuditOutboxProcessor>? logger = null)
    {
        await using var db = sql.CreateTenantContext(storeId);
        return await Processor(sql, db, logger).ProcessPendingAsync();
    }
    private sealed class Factory(string connection) : IDbContextFactory<AuditLogDbContext>
    {
        public AuditLogDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AuditLogDbContext>().UseSqlServer(connection).Options);
    }
    private sealed class Context(int storeId) : IAuditExecutionContextAccessor
    {
        public AuditExecutionContextDto GetCurrent() => new() { StoreId = storeId, UserId = 42, UserName = "audit-test", TraceId = "issue10" };
    }
    private sealed class Actor : ICurrentUser
    {
        public int? UserId => 42;
        public string? UserName => "audit-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
    private sealed class CancelAfterPrimary(CancellationTokenSource cancel) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { cancel.Cancel(); return ValueTask.FromResult(result); }
    }
}
