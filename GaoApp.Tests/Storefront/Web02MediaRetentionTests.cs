using GaoApp.Application.Common.Options;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Media;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Media;
using GaoApp.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb02"), Trait("Category", "StorefrontWeb02")]
public sealed class Web02MediaRetentionTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(true, false, false)][InlineData(false, false, false)][InlineData(false, true, false)]
    [InlineData(true, false, true)][InlineData(false, false, true)]
    public async Task Website_only_image_or_model_is_used_in_library_and_survives_sweep_even_when_hidden_or_source_inactive(bool published, bool inactive, bool model)
    {
        var c = await fixture.CaseAsync(); var path = "uploads/products/" + Guid.NewGuid().ToString("N") + (model ? ".glb" : ".jpg");
        var asset = await c.AssetAsync(path, model ? "model/gltf-binary" : "image/jpeg"); var files = new RetentionFiles();
        await files.Storage.SaveAsync(new MemoryStream([1, 2, 3, 4]), path); var clock = new RetentionClock();
        await using var db = c.Context(); var request = c.Publication(publish: published);
        request.Media = [new() { MediaAssetId = asset, Kind = model ? StorefrontMediaKind.Model3D : StorefrontMediaKind.Image, AltText = "Website only" }];
        await c.Products(db).SaveAsync(c.ProductId, request); Assert.Empty(await db.ProductImages.ToListAsync());
        if (inactive) await db.Products.ExecuteUpdateAsync(p => p.SetProperty(x => x.IsActive, false));
        // A deadline set by older cleanup must be repaired after adding a live link.
        await db.MediaAssets.Where(x => x.Id == asset).ExecuteUpdateAsync(p => p.SetProperty(x => x.ExpireAtUtc, clock.Utc.AddDays(-1)));
        var library = files.Library(db, clock); var item = await library.GetAsync(asset, default);
        Assert.NotNull(item); Assert.True(item.Used); Assert.Equal("used", item.Status); Assert.Equal(c.ProductId, Assert.Single(item.Products).Id);
        Assert.Equal(asset, Assert.Single((await library.ListAsync(null, "used", 1, default)).Items).Id);
        Assert.Empty((await library.ListAsync(null, "unused", 1, default)).Items);
        Assert.Equal(MediaCleanupOutcome.Kept, await library.ProcessAsync(asset, default));
        var sweep = await library.SweepAsync(0, default); Assert.Equal(0, sweep.Deleted); Assert.Equal(0, sweep.Scheduled); Assert.Equal(0, sweep.Failed);
        db.ChangeTracker.Clear(); var persisted = await db.MediaAssets.SingleAsync(x => x.Id == asset); Assert.Null(persisted.ExpireAtUtc); Assert.False(persisted.IsDeleted);
        Assert.True(File.Exists(files.Paths.Resolve(path))); Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(files.Paths.Resolve(path)));
    }

    [Fact]
    public async Task Removing_the_last_website_link_starts_full_retention_and_then_removes_only_the_fixture_file()
    {
        var c = await fixture.CaseAsync(); var path = "uploads/products/" + Guid.NewGuid().ToString("N") + ".jpg";
        var asset = await c.AssetAsync(path); var files = new RetentionFiles(); await files.Storage.SaveAsync(new MemoryStream([4, 3, 2, 1]), path);
        var clock = new RetentionClock(); await using var db = c.Context(); var request = c.Publication(publish: false);
        request.Media = [new() { MediaAssetId = asset, Kind = StorefrontMediaKind.Image }]; await c.Products(db).SaveAsync(c.ProductId, request);
        var library = files.Library(db, clock); Assert.Equal(MediaCleanupOutcome.Kept, await library.ProcessAsync(asset, default));
        var unlink = await c.SavedAsync(); unlink.Media.Clear(); await c.Products(db).SaveAsync(c.ProductId, unlink);
        Assert.Equal(MediaCleanupOutcome.Scheduled, await library.ProcessAsync(asset, default));
        db.ChangeTracker.Clear(); Assert.Equal(clock.Utc.AddDays(7), (await db.MediaAssets.SingleAsync(x => x.Id == asset)).ExpireAtUtc);
        clock.Utc = clock.Utc.AddDays(6); Assert.Equal(MediaCleanupOutcome.Kept, await library.ProcessAsync(asset, default)); Assert.True(File.Exists(files.Paths.Resolve(path)));
        clock.Utc = clock.Utc.AddDays(1); Assert.Equal(MediaCleanupOutcome.Deleted, await library.ProcessAsync(asset, default)); Assert.False(File.Exists(files.Paths.Resolve(path)));
        db.ChangeTracker.Clear(); Assert.True((await db.MediaAssets.IgnoreQueryFilters().SingleAsync(x => x.Id == asset)).IsDeleted);
        Assert.Equal(MediaCleanupOutcome.Kept, await library.ProcessAsync(asset, default));
    }

    [Theory]
    [InlineData(-1)][InlineData(1)]
    public async Task Explicit_selection_claims_live_final_media_with_a_retention_deadline_and_preserves_existing_bytes(int days)
    {
        var c = await fixture.CaseAsync(); var path = "uploads/products/" + Guid.NewGuid().ToString("N") + ".jpg";
        var asset = await c.AssetAsync(path, expires: DateTime.UtcNow.AddDays(days)); var files = new RetentionFiles();
        await files.Storage.SaveAsync(new MemoryStream([9, 8, 7]), path); await using var db = c.Context();
        Assert.Contains(await c.Products(db).SearchMediaAsync("WEB02"), x => x.Id == asset);
        var request = c.Publication(); request.Media = [new() { MediaAssetId = asset, Kind = StorefrontMediaKind.Image }];
        await c.Products(db).SaveAsync(c.ProductId, request); db.ChangeTracker.Clear();
        var row = await db.MediaAssets.SingleAsync(x => x.Id == asset); Assert.Null(row.ExpireAtUtc); Assert.False(row.IsTemp); Assert.False(row.IsDeleted);
        Assert.Equal(MediaCleanupOutcome.Kept, await files.Library(db, TimeProvider.System).ProcessAsync(asset, default));
        Assert.Equal(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(files.Paths.Resolve(path)));
    }

    [Fact]
    public async Task Failed_media_selection_rolls_back_any_retention_claim_and_creates_no_links()
    {
        var c = await fixture.CaseAsync(); var other = await fixture.CaseAsync(); var deadline = DateTime.UtcNow.AddDays(1);
        var asset = await c.AssetAsync(expires: deadline); var foreign = await other.AssetAsync(); await using var db = c.Context();
        var before = await db.MediaAssets.AsNoTracking().Where(x => x.Id == asset).Select(x => x.ExpireAtUtc).SingleAsync();
        var request = c.Publication(); request.Media = [new() { MediaAssetId = asset, Kind = StorefrontMediaKind.Image }, new() { MediaAssetId = foreign, Kind = StorefrontMediaKind.Image }];
        await Assert.ThrowsAsync<ValidationAppException>(() => c.Products(db).SaveAsync(c.ProductId, request));
        await using var verify = c.Context(); Assert.Equal(before, await verify.MediaAssets.Where(x => x.Id == asset).Select(x => x.ExpireAtUtc).SingleAsync());
        Assert.Empty(await verify.Set<StorefrontMedia>().ToListAsync()); Assert.Empty(await verify.StorefrontProducts.ToListAsync());
    }

    [Fact]
    public async Task Racing_expired_media_cleanup_and_publication_never_leaves_a_link_to_deleted_bytes()
    {
        var c = await fixture.CaseAsync(); var path = "uploads/products/" + Guid.NewGuid().ToString("N") + ".jpg";
        var asset = await c.AssetAsync(path, expires: DateTime.UtcNow.AddDays(-1)); var files = new RetentionFiles(); await files.Storage.SaveAsync(new MemoryStream([5, 5, 5]), path);
        async Task<bool> Publish()
        {
            await using var db = c.Context(); var request = c.Publication(); request.Media = [new() { MediaAssetId = asset, Kind = StorefrontMediaKind.Image }];
            try { await c.Products(db).SaveAsync(c.ProductId, request); return true; }
            catch (ValidationAppException) { return false; } catch (ConflictAppException) { return false; }
        }
        async Task<MediaCleanupOutcome> Cleanup()
        { await using var db = c.Context(); return await files.Library(db, TimeProvider.System).ProcessAsync(asset, default); }
        var publish = Publish(); var cleanup = Cleanup(); await Task.WhenAll((Task)publish, cleanup);
        await using var verify = c.Context(); var row = await verify.MediaAssets.IgnoreQueryFilters().SingleAsync(x => x.Id == asset);
        if (await publish)
        {
            Assert.False(row.IsDeleted); Assert.Null(row.ExpireAtUtc); Assert.True(File.Exists(files.Paths.Resolve(path)));
            Assert.Single(await verify.Set<StorefrontMedia>().Where(x => x.MediaAssetId == asset).ToListAsync()); Assert.Equal(MediaCleanupOutcome.Kept, await cleanup);
        }
        else
        {
            Assert.Empty(await verify.Set<StorefrontMedia>().Where(x => x.MediaAssetId == asset).ToListAsync()); Assert.True(row.IsDeleted);
            Assert.False(File.Exists(files.Paths.Resolve(path))); Assert.Equal(MediaCleanupOutcome.Deleted, await cleanup);
        }
    }

    private sealed class RetentionClock : TimeProvider
    {
        internal DateTime Utc { get; set; } = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Utc);
    }
    private sealed class RetentionFiles
    {
        internal UploadPathResolver Paths { get; }
        internal LocalFileStorageService Storage { get; }
        internal RetentionFiles([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        {
            var directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../.artifacts/storefront-rollout/WEB02/tests/media-fixture", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            Paths = new UploadPathResolver(new RetentionEnvironment { ContentRootPath = directory, WebRootPath = Path.Combine(directory, "wwwroot") },
                Options.Create(new StorageOptions { UploadRoot = Path.Combine(directory, "uploads") }));
            Storage = new LocalFileStorageService(Paths, NullLogger<LocalFileStorageService>.Instance);
        }
        internal MediaLibraryService Library(GaoApp.Infrastructure.Data.AppDbContext db, TimeProvider clock)
            => new(db, Storage, Options.Create(new MediaCleanupOptions()), clock, NullLogger<MediaLibraryService>.Instance);
    }
    private sealed class RetentionEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Web02MediaRetentionTests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "";
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
