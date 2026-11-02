using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Configuration;

internal static class MigrationTestSupport
{
    // Historical feature guards must be exercised after delivering their fixture's
    // audit evidence. Never delete pending evidence or disable the audit Down guard.
    internal static async Task MigrateHistoricalPrefixAsync(InventoryPostingLocalDb database, string target)
    {
        await using (var discovery = database.CreateHostContext())
        {
            var stores = await discovery.AuditOutbox.Where(x => x.ProcessedAtUtc == null)
                .Select(x => x.StoreId).Distinct().ToArrayAsync();
            foreach (var storeId in stores)
            {
                await using var source = database.CreateTenantContext(storeId);
                var processor = new AuditOutboxProcessor(source, new AuditFactory(database.ConnectionString),
                    NullLogger<AuditOutboxProcessor>.Instance);
                while (await source.AuditOutbox.AnyAsync(x => x.ProcessedAtUtc == null))
                    Assert.True(await processor.ProcessPendingAsync(100) > 0, "Fixture audit evidence could not be delivered before downgrade.");
            }
        }
        await database.MigrateAsync(target);
    }

    private sealed class AuditFactory(string connection) : IDbContextFactory<AuditLogDbContext>
    {
        public AuditLogDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AuditLogDbContext>()
            .UseSqlServer(connection).Options);
    }
}
