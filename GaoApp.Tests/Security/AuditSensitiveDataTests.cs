using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Repositories.Audit;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Services.Audit;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Security;

public sealed class AuditSensitiveDataTests
{
    [Theory]
    [InlineData("{\"PasswordHash\":\"synthetic-secret\",\"Name\":\"visible\"}")]
    [InlineData("{\"Nested\":[{\"CLIENT_SECRET\":\"synthetic-secret\"}],\"Name\":\"visible\"}")]
    [InlineData("{\"Name\":\"visible\",\"api-key\":\"synthetic-secret\"}")]
    public async Task Explicit_writes_and_historical_reads_redact_secrets(string json)
    {
        var repo = new AuditRepository(); var service = new AuditLogService(repo, new Context());
        await service.WriteAsync(new WriteAuditLogRequest { OldValuesJson = json, NewValuesJson = json });
        Assert.DoesNotContain("synthetic-secret", repo.Item!.NewValuesJson);
        Assert.Contains("visible", repo.Item.NewValuesJson);
        // Simulate an unmodified historical record containing a secret.
        repo.Item.OldValuesJson = json; repo.Item.NewValuesJson = json;
        var detail = await service.GetDetailAsync(1);
        Assert.DoesNotContain("synthetic-secret", detail!.OldValuesJson);
        Assert.DoesNotContain("synthetic-secret", detail.NewValuesJson);
        Assert.Equal(json, repo.Item.NewValuesJson); // Read must not silently rewrite stored evidence.
    }

    [Fact]
    public void Malformed_historical_json_fails_closed()
        => Assert.DoesNotContain("synthetic-secret", AuditSensitiveData.SanitizeJson("{bad:synthetic-secret"));

    [Fact]
    public async Task Automatic_audit_excludes_hash_on_create_update_and_delete_but_keeps_business_changes()
    {
        var auditOptions = new DbContextOptionsBuilder<AuditLogDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var interceptor = new AuditSaveChangesInterceptor(new Context(), new AuditFactory(auditOptions), NullLogger<AuditSaveChangesInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<PrimaryDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor).Options;
        await using var db = new PrimaryDb(options);
        var item = new Tracked { StoreId = 7, Name = "before", PasswordHash = "synthetic-secret-old" };
        db.Add(item); await db.SaveChangesAsync();
        item.Name = "after"; item.PasswordHash = "synthetic-secret-new"; await db.SaveChangesAsync();
        db.Remove(item); await db.SaveChangesAsync();
        await using var audit = new AuditLogDbContext(auditOptions);
        var logs = await audit.AuditLogs.ToListAsync();
        Assert.Equal(3, logs.Count);
        foreach (var log in logs)
        {
            var snapshot = (log.OldValuesJson ?? "") + log.NewValuesJson + log.ChangedColumnsJson;
            Assert.DoesNotContain("synthetic-secret", snapshot);
            Assert.DoesNotContain("PasswordHash", snapshot);
            Assert.Contains("Name", snapshot);
        }
    }

    private sealed class Tracked : IAuditTrackedEntity
    {
        public int Id { get; set; }
        public int StoreId { get; set; }
        public string Name { get; set; } = "";
        public string PasswordHash { get; set; } = "";
    }
    private sealed class PrimaryDb(DbContextOptions<PrimaryDb> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Tracked>();
    }
    private sealed class AuditFactory(DbContextOptions<AuditLogDbContext> options) : IDbContextFactory<AuditLogDbContext>
    {
        public AuditLogDbContext CreateDbContext() => new(options);
    }
    private sealed class Context : IAuditExecutionContextAccessor
    {
        public AuditExecutionContextDto GetCurrent() => new() { StoreId = 7, UserId = 1 };
    }
    private sealed class AuditRepository : IAuditLogRepository
    {
        public AuditLog? Item { get; set; }
        public Task AddAsync(AuditLog auditLog, CancellationToken ct = default) { Item = auditLog; return Task.CompletedTask; }
        public Task AddRangeAsync(IEnumerable<AuditLog> auditLogs, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuditLog?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Item);
        public Task<PagedResult<AuditLogListItemDto>> SearchAsync(int? storeId, AuditLogQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
