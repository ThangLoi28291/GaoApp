using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Domain.Common;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Observability;

public sealed class AuditSaveChangesInterceptorTests
{
    [Fact]
    public void Audit_failure_after_save_does_not_turn_committed_primary_write_into_failure()
    {
        const string secret = "Server=private;Password=synthetic-audit-secret";
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<AuditSaveChangesInterceptor>();
        var interceptor = new AuditSaveChangesInterceptor(
            new FakeAuditContextAccessor(),
            new ThrowingAuditContextFactory(new IOException(secret)),
            logger);
        var options = new DbContextOptionsBuilder<PrimaryTestDbContext>()
            .UseInMemoryDatabase($"r1.6-audit-{Guid.NewGuid():N}")
            .AddInterceptors(interceptor)
            .Options;

        using var db = new PrimaryTestDbContext(options);
        db.Entities.Add(new AuditTrackedTestEntity
        {
            StoreId = 7,
            Name = "Synthetic brand"
        });

        var affected = db.SaveChanges();

        Assert.Equal(1, affected);
        Assert.Single(db.Entities);
        var log = Assert.Single(
            logger.Entries,
            x => x.Level == LogLevel.Error);
        Assert.Contains(nameof(IOException), log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, log.Message, StringComparison.Ordinal);
    }

    private sealed class PrimaryTestDbContext(
        DbContextOptions<PrimaryTestDbContext> options) : DbContext(options)
    {
        public DbSet<AuditTrackedTestEntity> Entities => Set<AuditTrackedTestEntity>();
    }

    private sealed class AuditTrackedTestEntity : IAuditTrackedEntity
    {
        public int Id { get; set; }
        public int StoreId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class FakeAuditContextAccessor : IAuditExecutionContextAccessor
    {
        public AuditExecutionContextDto GetCurrent() => new()
        {
            StoreId = 7,
            UserId = 1,
            TraceId = "trace-audit-r1.6",
            Path = "/synthetic"
        };
    }

    private sealed class ThrowingAuditContextFactory(Exception exception)
        : IDbContextFactory<AuditLogDbContext>
    {
        public AuditLogDbContext CreateDbContext() => throw exception;
    }
}
