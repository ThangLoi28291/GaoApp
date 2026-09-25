using System.Diagnostics;
using GaoApp.Application.Common.Options;
using GaoApp.Application.DTOs.Media;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Media;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace GaoApp.Tests.Media;

[Collection("SqlServerConcurrency")]
public sealed class MediaLibraryScaleSqlServerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Large_catalog_lists_filters_and_retains_images_within_request_timeout()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(seed.StoreId);
        var template = await db.Products.AsNoTracking().SingleAsync();
        // Reproduce the reported catalog size without copying any store data.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT TOP (32000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N
            INTO #MediaNumbers FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            INSERT INTO Products (StoreId, Name, Alias, CategoryId, SupplierId, BaseUnitId, BasePrice,
                IsActive, IsSellable, CreatedAtUtc, IsDeleted)
            SELECT {seed.StoreId}, CONCAT('Media scale product ', N), CONCAT('media-scale-', N),
                {template.CategoryId}, {template.SupplierId}, {template.BaseUnitId}, 0, 1, 1, SYSUTCDATETIME(), 0
            FROM #MediaNumbers;
            INSERT INTO ProductVariant (StoreId, ProductId, Sku, CostPrice, IsActive, HasInputInvoice, CreatedAtUtc, IsDeleted)
            SELECT StoreId, Id, CONCAT('MEDIA-SCALE-', Id), 0, 1, 0, SYSUTCDATETIME(), 0
            FROM Products WHERE StoreId={seed.StoreId} AND Alias LIKE 'media-scale-%';
            INSERT INTO MediaAssets (StoreId, StoragePath, OriginalFileName, ContentType, SizeBytes, IsTemp, CreatedAtUtc, IsDeleted)
            SELECT {seed.StoreId}, CONCAT('uploads/products/media-scale/', N, '.png'), CONCAT('Media-scale-', N, '.png'),
                'image/png', 100, 0, SYSUTCDATETIME(), 0 FROM #MediaNumbers WHERE N<=16000;
            INSERT INTO ProductImages (StoreId, ProductId, MediaAssetId, IsPrimary, SortOrder, CreatedAtUtc, IsDeleted)
            SELECT a.StoreId, p.Id, a.Id, 1, 0, SYSUTCDATETIME(), 0
            FROM #MediaNumbers n JOIN Products p ON p.StoreId={seed.StoreId} AND p.Alias=CONCAT('media-scale-', n.N)
            JOIN MediaAssets a ON a.StoreId={seed.StoreId} AND a.StoragePath=CONCAT('uploads/products/media-scale/', n.N, '.png')
            WHERE n.N<=16000;
            DROP TABLE #MediaNumbers;
            """);
        var now = DateTime.UtcNow;
        var extras = new[] { "html", "display", "temp", "waiting", "ready", "unused", "deleted" }
            .Select(name => new MediaAsset
            {
                StoreId = seed.StoreId, StoragePath = $"uploads/products/media-scale/{name}.png",
                OriginalFileName = $"Media-scale-{name}.png", SizeBytes = 100, ContentType = "image/png",
                IsTemp = name == "temp", IsDeleted = name == "deleted",
                ExpireAtUtc = name == "temp" ? now.AddHours(6) : name == "waiting" ? now.AddDays(7) : name == "ready" ? now.AddDays(-1) : null
            }).ToList();
        db.MediaAssets.AddRange(extras);
        var htmlProduct = await db.Products.SingleAsync(p => p.Id == template.Id);
        htmlProduct.Content = $"<img src='/{extras[0].StoragePath}'>";
        db.Set<DisplayPromotion>().Add(new DisplayPromotion
            { StoreId = seed.StoreId, Title = "Media scale display", MediaUrl = "/" + extras[1].StoragePath });
        await db.SaveChangesAsync();
        db.MediaAssets.Remove(extras[6]);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Database.SetCommandTimeout(10);
        var files = new MediaTestFiles();
        var library = new MediaLibraryService(db, files, Options.Create(new MediaCleanupOptions()),
            TimeProvider.System, NullLogger<MediaLibraryService>.Instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var elapsed = Stopwatch.StartNew();
        var page = await library.ListAsync(null, null, 1, deadline.Token);
        output.WriteLine($"Large catalog: {page.Summary.Total} live assets, 32001 products; list {elapsed.ElapsedMilliseconds} ms.");
        Assert.Equal(16006, page.Summary.Total);
        Assert.Equal(16002, page.Summary.Used);
        Assert.Equal(1, page.Summary.Temporary);
        Assert.Equal(2, page.Summary.Waiting);
        Assert.Equal(1, page.Summary.Ready);
        Assert.Equal(1600600, page.Summary.Bytes);
        Assert.Equal(24, page.Items.Count);
        Assert.Equal(667, page.Pages);
        Assert.All(page.Items.Where(x => x.Name.EndsWith(".png") && x.Products.Count > 0),
            x => Assert.Single(x.Products));

        var filtered = await library.ListAsync("MEDIA-SCALE-UNUSED", "unused", int.MaxValue, deadline.Token);
        Assert.Equal(extras[5].Id, Assert.Single(filtered.Items).Id);
        Assert.Equal(page.Summary.Total, filtered.Summary.Total);
        Assert.Equal(1, filtered.Page);
        var finalPage = await library.ListAsync(null, "used", int.MaxValue, deadline.Token);
        Assert.Equal(18, finalPage.Items.Count);
        var live = finalPage.Items.First(x => x.Products.Count > 0);
        Assert.Single((await library.GetAsync(live.Id, deadline.Token))!.Products);
        Assert.Equal(MediaCleanupOutcome.Kept, await library.ProcessAsync(live.Id, deadline.Token));
        Assert.Empty(files.Deleted);
        output.WriteLine($"List, search, last page, detail and retention check completed in {elapsed.ElapsedMilliseconds} ms.");
    }
}
