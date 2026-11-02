using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data.Configurations;
using GaoApp.Infrastructure.Interceptors;
using GaoApp.Infrastructure.Services.Audit;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Observability;

public sealed class AuditSaveChangesInterceptorTests
{
    [Fact]
    public async Task Automatic_audit_stages_evidence_with_the_write_without_calling_a_separate_sink()
    {
        var options = new DbContextOptionsBuilder<PrimaryTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditContext())).Options;
        await using var db = new PrimaryTestDbContext(options);
        var entity = new Tracked { StoreId = 7, Name = "Synthetic brand" };
        db.Add(entity);
        await db.SaveChangesAsync();
        var evidence = AuditOutboxEvidence.Read(Assert.Single(await db.Set<AuditOutboxMessage>().ToListAsync()));
        Assert.Equal(entity.Id.ToString(), evidence.EntityId);
        Assert.Equal("trace-audit", evidence.TraceId);
        Assert.Equal(7, evidence.StoreId);
        Assert.Contains("Synthetic brand", evidence.NewValuesJson);
    }

    private sealed class PrimaryTestDbContext(DbContextOptions<PrimaryTestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        { builder.Entity<Tracked>(); builder.ApplyConfiguration(new AuditOutboxMessageConfiguration()); }
    }
    private sealed class Tracked : IAuditTrackedEntity
    {
        public int Id { get; set; }
        public int StoreId { get; set; }
        public string Name { get; set; } = "";
    }
    private sealed class AuditContext : IAuditExecutionContextAccessor
    {
        public AuditExecutionContextDto GetCurrent() => new() { StoreId = 7, UserId = 1, TraceId = "trace-audit" };
    }
}