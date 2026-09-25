using GaoApp.Application.Common;
using GaoApp.Application.Common.Abstractions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Options;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Media;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using GaoApp.Web.Services.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Media;

public sealed class MediaCleanupWorkerTests
{
    [Fact]
    public async Task Worker_uses_fresh_tenant_scopes_scans_every_batch_and_retries_idempotently()
    {
        var dbOptions = new DbContextOptionsBuilder<InMemoryAppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var files = new MediaTestFiles(); var clock = new MediaTestClock();
        var config = Options.Create(new MediaCleanupOptions { BatchSize = 1 });
        var services = new ServiceCollection();
        services.AddLogging(); services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContextWriter>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<AppDbContext>(sp => new InMemoryAppDbContext(dbOptions, sp.GetRequiredService<TenantContext>(), new WorkerUser()));
        services.AddSingleton<IFileStorageService>(files); services.AddSingleton<TimeProvider>(clock); services.AddSingleton(config);
        services.AddScoped<MediaLibraryService>();
        await using var provider = services.BuildServiceProvider();
        int store1, store2;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = new Store { Name = "First", SubDomain = "first", SubDomainNormalized = "FIRST", IsActive = true };
            var second = new Store { Name = "Second", SubDomain = "second", SubDomainNormalized = "SECOND", IsActive = false };
            db.AddRange(first, second); await db.SaveChangesAsync(); store1 = first.Id; store2 = second.Id;
            db.MediaAssets.AddRange(Enumerable.Range(0, 3).Select(i => new MediaAsset
            {
                StoreId = i == 2 ? store2 : store1, StoragePath = $"uploads/products/test-{i}.png",
                IsTemp = true, ExpireAtUtc = clock.Utc.AddHours(-1)
            }));
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<MediaLibraryService>().SweepAsync(0, default));
        }
        var status = new MediaCleanupStatus();
        var worker = new MediaCleanupWorker(provider.GetRequiredService<IServiceScopeFactory>(), config, status, NullLogger<MediaCleanupWorker>.Instance);
        await worker.RunOnceAsync(default);
        Assert.Equal(2, status.Get(store1)!.Result.Deleted); Assert.Equal(1, status.Get(store2)!.Result.Deleted);
        Assert.Equal(0, status.Get(store1)!.Result.Failed); Assert.Equal(0, status.Get(store2)!.Result.Failed);
        Assert.Equal(3, files.Deleted.Count);
        await worker.RunOnceAsync(default);
        Assert.Equal(0, status.Get(store1)!.Result.Deleted); Assert.Equal(3, files.Deleted.Count);
    }
    private sealed class WorkerUser : ICurrentUser
    {
        public int? UserId => null; public string? UserName => null; public int? TerminalId => null;
        public string? TerminalCode => null; public bool IsAuthenticated => false;
    }
}
