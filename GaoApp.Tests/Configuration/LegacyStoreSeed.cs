using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Configuration;

internal static class LegacyStoreSeed
{
    // Migration-prefix tests must only write baseline columns, even when the current Store model grows.
    internal static async Task InsertAsync(AppDbContext db, Store store, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Stores] ([Name], [SubDomain], [SubDomainNormalized], [IsActive], [CreatedAtUtc], [IsDeleted])
            VALUES ({store.Name}, {store.SubDomain}, {store.SubDomainNormalized}, {store.IsActive}, SYSUTCDATETIME(), 0);
            """, ct);
        store.Id = await db.Database.SqlQuery<int>($"""
            SELECT [Id] AS [Value] FROM [Stores] WHERE [SubDomainNormalized] = {store.SubDomainNormalized}
            """).SingleAsync(ct);
    }
}
