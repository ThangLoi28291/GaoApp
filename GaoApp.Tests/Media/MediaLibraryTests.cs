using GaoApp.Application.Common.Abstractions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Options;
using GaoApp.Application.DTOs.Media;
using GaoApp.Application.Services.Media;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Media;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Services.Media;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GaoApp.Infrastructure.Storage;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

namespace GaoApp.Tests.Media;

public sealed class MediaLibraryTests
{
    [Theory]
    [InlineData("uploads/legacy-data/images/imported.jpg", "image/jpeg")]
    [InlineData("uploads/legacy-data/files/imported.png", "image/png")]
    [InlineData("uploads/products/current.webp", "image/webp")]
    [InlineData("uploads/_temp/upload.gif", "image/gif")]
    [InlineData("uploads/legacy-data/images/imported.svg", null)]
    [InlineData("uploads/legacy-data/private/photo.jpg", null)]
    [InlineData("uploads/legacy-data/images/../private/photo.jpg", null)]
    public async Task Preview_serves_supported_images_but_rejects_other_stores_and_unsafe_paths(string path, string? mime)
    {
        await using var f = new Fixture();
        var asset = await f.Add(path: path);
        var otherStoreAsset = await f.Add(store: 2, path: path);
        var root = Path.Combine(Path.GetTempPath(), "gao-media-preview-" + Guid.NewGuid().ToString("N"));
        var uploadRoot = Path.Combine(root, "uploads");
        var env = new PreviewEnvironment { ContentRootPath = root, WebRootPath = Path.Combine(root, "wwwroot") };
        var paths = new UploadPathResolver(env, Options.Create(new StorageOptions { UploadRoot = uploadRoot }));
        var tenant = new TenantContext();
        tenant.SetStore(1, "one");
        var controller = new MediaLibraryController(f.Service, tenant, null!, Options.Create(new MediaCleanupOptions()), null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        // Even rejected paths point to an existing file; a missing file must not mask the guard.
        var physicalPath = Path.GetFullPath(Path.Combine(uploadRoot, path[8..]));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await File.WriteAllBytesAsync(physicalPath, [1, 2, 3]);
        try
        {
            var result = await controller.Preview(asset.Id, paths, default);
            if (mime == null)
                Assert.IsType<NotFoundResult>(result);
            else
            {
                var file = Assert.IsType<PhysicalFileResult>(result);
                Assert.Equal(physicalPath, file.FileName);
                Assert.Equal(mime, file.ContentType);
                Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions.ToString());
            }
            Assert.IsType<NotFoundResult>(await controller.Preview(otherStoreAsset.Id, paths, default));
            File.Delete(physicalPath);
            Assert.IsType<NotFoundResult>(await controller.Preview(asset.Id, paths, default));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class PreviewEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MediaPreviewTests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "";
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task Expired_temp_is_deleted_once_and_recent_upload_survives()
    {
        await using var f = new Fixture();
        var expired = await f.Add(temp: true, deadline: f.Clock.Utc.AddSeconds(-1));
        var recent = await f.Add(temp: true, deadline: f.Clock.Utc.AddHours(1));
        Assert.Equal(MediaCleanupOutcome.Deleted, await f.Service.ProcessAsync(expired.Id, default));
        Assert.Equal(MediaCleanupOutcome.Kept, await f.Service.ProcessAsync(expired.Id, default));
        Assert.Equal(MediaCleanupOutcome.Kept, await f.Service.ProcessAsync(recent.Id, default));
        Assert.Single(f.Files.Deleted);
        Assert.Equal(expired.StoragePath, f.Files.Deleted[0]);
    }

    [Fact]
    public async Task Old_unused_image_gets_full_grace_from_discovery_and_is_then_deleted()
    {
        await using var f = new Fixture();
        var asset = await f.Add();
        Assert.Equal(MediaCleanupOutcome.Scheduled, await f.Service.ProcessAsync(asset.Id, default));
        Assert.Equal(f.Clock.Utc.AddDays(7), (await f.Asset(asset.Id)).ExpireAtUtc);
        f.Clock.Utc = f.Clock.Utc.AddDays(6);
        Assert.Equal(MediaCleanupOutcome.Kept, await f.Service.ProcessAsync(asset.Id, default));
        f.Clock.Utc = f.Clock.Utc.AddDays(1);
        Assert.Equal(MediaCleanupOutcome.Deleted, await f.Service.ProcessAsync(asset.Id, default));
    }

    [Fact]
    public async Task Inactive_product_keeps_image_and_cancels_pending_deadline()
    {
        await using var f = new Fixture();
        var asset = await f.Add(deadline: f.Clock.Utc.AddDays(-1));
        await f.Link(asset, inactive: true);
        Assert.Equal(MediaCleanupOutcome.Kept, await f.Service.ProcessAsync(asset.Id, default));
        Assert.Null((await f.Asset(asset.Id)).ExpireAtUtc);
        Assert.Empty(f.Files.Deleted);
    }

    [Fact]
    public async Task Live_variant_keeps_soft_deleted_product_image()
    {
        await using var f = new Fixture();
        var asset = await f.Add(deadline: f.Clock.Utc.AddDays(-1));
        var image = await f.Link(asset, variant: true);
        image.IsDeleted = true; image.DeletedAtUtc = f.Clock.Utc; await f.Db.SaveChangesAsync();
        Assert.Equal(image.Id, (await f.Db.ProductVariants.SingleAsync()).PrimaryProductImageId);
        Assert.Equal(MediaCleanupOutcome.Kept, await f.Service.ProcessAsync(asset.Id, default));
        Assert.Empty(f.Files.Deleted);
        var page = await f.Service.ListAsync(null, "used", 1, default);
        Assert.Equal(image.ProductId, Assert.Single(Assert.Single(page.Items).Products).Id);
    }

    [Fact]
    public async Task Deleted_product_starts_new_retention_instead_of_deleting_immediately()
    {
        await using var f = new Fixture();
        var asset = await f.Add(); var image = await f.Link(asset);
        f.Db.Products.Remove(image.Product); await f.Db.SaveChangesAsync();
        Assert.Equal(MediaCleanupOutcome.Scheduled, await f.Service.ProcessAsync(asset.Id, default));
        Assert.Empty(f.Files.Deleted);
    }

    [Fact]
    public async Task Product_edit_unlinks_only_after_save_and_never_deletes_file_inline()
    {
        await using var f = new Fixture();
        var asset = await f.Add(); var image = await f.Link(asset);
        var images = new ProductImageService(new MediaAssetRepository(f.Db), new ProductImageRepository(f.Db), f.Files);
        await images.SyncEditAsync(1, image.ProductId, [], null, 99);
        Assert.Empty(f.Files.Deleted);
        await f.Db.SaveChangesAsync();
        Assert.True((await f.Db.ProductImages.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        Assert.False((await f.Asset(asset.Id)).IsDeleted);
        Assert.Equal(MediaCleanupOutcome.Scheduled, await f.Service.ProcessAsync(asset.Id, default));
    }

    [Fact]
    public async Task Delete_failure_leaves_durable_claim_for_retry_and_token_cannot_be_reused()
    {
        await using var f = new Fixture();
        var asset = await f.Add(temp: true, deadline: f.Clock.Utc.AddHours(-1));
        f.Files.FailDelete = true;
        Assert.Equal(MediaCleanupOutcome.Failed, await f.Service.ProcessAsync(asset.Id, default));
        var pending = await f.Asset(asset.Id);
        Assert.True(pending.IsDeleted); Assert.NotNull(pending.ExpireAtUtc); Assert.Null(pending.TempToken);
        f.Files.FailDelete = false;
        Assert.Equal(MediaCleanupOutcome.Deleted, await f.Service.ProcessAsync(asset.Id, default));
        Assert.Null((await f.Asset(asset.Id)).ExpireAtUtc);
    }

    [Fact]
    public async Task Shared_legacy_path_is_not_deleted_while_another_store_uses_it()
    {
        await using var f = new Fixture();
        var own = await f.Add(deadline: f.Clock.Utc.AddHours(-1));
        var alias = await f.Add(store: 2, path: own.StoragePath);
        await f.Link(alias);
        Assert.Equal(MediaCleanupOutcome.Kept, await f.Service.ProcessAsync(own.Id, default));
        Assert.Empty(f.Files.Deleted);
        Assert.Single((await f.Service.ListAsync(null, null, 1, default)).Items);
        Assert.Equal(MediaCleanupOutcome.Missing, await f.Service.ProcessAsync(alias.Id, default));
        Assert.False(await f.Service.CancelTempAsync(alias.Id, default));
        Assert.Null(await f.Service.GetPreviewPathAsync(alias.Id, default));
    }

    [Theory]
    [InlineData("uploads/invoices/private.png")]
    [InlineData("uploads/products/../../private.png")]
    [InlineData("C:/other/photo.png")]
    public async Task Unmanaged_or_unsafe_paths_are_never_deleted(string path)
    {
        await using var f = new Fixture();
        var asset = await f.Add(path: path, deadline: f.Clock.Utc.AddHours(-1));
        Assert.Equal(MediaCleanupOutcome.Kept, await f.Service.ProcessAsync(asset.Id, default));
        Assert.Empty(f.Files.Deleted);
    }

    [Fact]
    public async Task Product_html_and_display_promotion_references_retain_files()
    {
        await using var f = new Fixture();
        var asset = await f.Add(deadline: f.Clock.Utc.AddHours(-1));
        f.Db.Products.Add(new Product { StoreId = 1, Name = "HTML", Alias = "html", Content = "<img src='/" + asset.StoragePath + "'>" });
        var display = await f.Add(deadline: f.Clock.Utc.AddHours(-1));
        f.Db.Set<DisplayPromotion>().Add(new DisplayPromotion { StoreId = 1, Title = "Promo", MediaUrl = "/" + display.StoragePath });
        await f.Db.SaveChangesAsync();
        Assert.Equal(MediaCleanupOutcome.Kept, await f.Service.ProcessAsync(asset.Id, default));
        Assert.Equal(MediaCleanupOutcome.Kept, await f.Service.ProcessAsync(display.Id, default));
        Assert.Empty(f.Files.Deleted);
    }

    [Fact]
    public async Task Uploads_with_same_filename_have_distinct_immutable_paths_and_revert_is_retryable()
    {
        await using var f = new Fixture();
        var repo = new MediaAssetRepository(f.Db);
        var uploads = new TempUploadService(repo, f.Files, new MediaCleanupOptions());
        var first = await uploads.UploadAsync(new TempUploadRequest { FileName = "same.png", ContentType = "image/png", SizeBytes = 1, Content = new MemoryStream([1]) }, 1, 99);
        var second = await uploads.UploadAsync(new TempUploadRequest { FileName = "same.png", ContentType = "image/png", SizeBytes = 1, Content = new MemoryStream([2]) }, 1, 99);
        var files = await f.Db.MediaAssets.ToListAsync();
        Assert.Equal(2, files.Select(x => x.StoragePath).Distinct().Count());
        Assert.All(files, x => Assert.StartsWith("uploads/products/1/", x.StoragePath));
        Assert.True(await uploads.RevertAsync(first, 1, 99));
        Assert.Empty(await repo.GetTempsByTokensAsync([first], 1));
        Assert.Single(await repo.GetTempsByTokensAsync([second], 1));
        Assert.Empty(f.Files.Deleted);
    }

    [Fact]
    public async Task Filters_summary_and_keyset_batches_do_not_skip_assets()
    {
        await using var f = new Fixture(batchSize: 2);
        for (var i = 0; i < 5; i++) await f.Add(temp: true, deadline: f.Clock.Utc.AddHours(-1));
        var page = await f.Service.ListAsync("photo", "ready", 500, default);
        Assert.Equal(5, page.Summary.Ready); Assert.Equal(5, page.Items.Count); Assert.Equal(1, page.Page);
        var first = await f.Service.SweepAsync(0, default);
        var second = await f.Service.SweepAsync(first.LastId, default);
        var third = await f.Service.SweepAsync(second.LastId, default);
        Assert.Equal(5, first.Deleted + second.Deleted + third.Deleted);
        Assert.Equal(5, (await f.Service.ListAsync(null, "deleted", 1, default)).FilteredCount);
        Assert.Empty((await f.Service.ListAsync(null, null, 1, default)).Items);
    }

    [Fact]
    public async Task Failed_upload_remains_registered_and_is_not_claimable_until_ready()
    {
        await using var f = new Fixture();
        f.Files.FailSave = true;
        var uploads = new TempUploadService(new MediaAssetRepository(f.Db), f.Files, new MediaCleanupOptions());
        await Assert.ThrowsAsync<IOException>(() => uploads.UploadAsync(new TempUploadRequest
            { FileName = "interrupted.png", ContentType = "image/png", SizeBytes = 1, Content = new MemoryStream([1]) }, 1, 99));
        var registered = await f.Db.MediaAssets.SingleAsync();
        Assert.Null(registered.TempToken); Assert.NotNull(registered.ExpireAtUtc);
        Assert.False(await f.Service.CancelTempAsync(registered.Id, default));
        Assert.Equal(MediaCleanupOutcome.Deleted, await f.Service.ProcessAsync(registered.Id, default));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public InMemoryAppDbContext Db { get; }
        private readonly TenantContext tenant = new();
        public MediaTestClock Clock { get; } = new();
        public MediaTestFiles Files { get; } = new();
        public MediaLibraryService Service { get; }
        public Fixture(int batchSize = 100)
        {
            tenant.SetStore(1, "one");
            Db = new(new DbContextOptionsBuilder<InMemoryAppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant, new User());
            Service = new(Db, Files, Options.Create(new MediaCleanupOptions { BatchSize = batchSize }), Clock, NullLogger<MediaLibraryService>.Instance);
        }
        public async Task<MediaAsset> Add(bool temp = false, DateTime? deadline = null, int store = 1, string? path = null)
        {
            tenant.SetStore(store, "seed");
            var asset = new MediaAsset { StoreId = store, StoragePath = path ?? $"uploads/products/{Guid.NewGuid():N}/photo.png",
                OriginalFileName = "photo.png", ContentType = "image/png", SizeBytes = 100, IsTemp = temp,
                TempToken = temp ? Guid.NewGuid().ToString("N") : null, ExpireAtUtc = deadline };
            Db.Add(asset); await Db.SaveChangesAsync(); tenant.SetStore(1, "one"); return asset;
        }
        public async Task<ProductImage> Link(MediaAsset asset, bool inactive = false, bool variant = false)
        {
            tenant.SetStore(asset.StoreId, "seed");
            var product = new Product { StoreId = asset.StoreId, Name = "Test product", Alias = Guid.NewGuid().ToString("N"), IsActive = !inactive };
            var image = new ProductImage { StoreId = asset.StoreId, Product = product, MediaAssetId = asset.Id };
            Db.Add(image);
            if (variant) Db.Add(new ProductVariant { StoreId = asset.StoreId, Product = product, PrimaryProductImage = image, Sku = "test", ProductVariantName = "Variant" });
            await Db.SaveChangesAsync(); tenant.SetStore(1, "one"); return image;
        }
        public Task<MediaAsset> Asset(int id) { Db.ChangeTracker.Clear(); return Db.MediaAssets.IgnoreQueryFilters().SingleAsync(a => a.Id == id); }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class User : ICurrentUser
    {
        public int? UserId => 99; public string? UserName => "media-test"; public int? TerminalId => null;
        public string? TerminalCode => null; public bool IsAuthenticated => true;
    }
}

internal sealed class MediaTestClock : TimeProvider
{
    public DateTime Utc { get; set; } = DateTime.UtcNow.AddDays(1);
    public override DateTimeOffset GetUtcNow() => new(Utc, TimeSpan.Zero);
}
internal sealed class MediaTestFiles : IFileStorageService
{
    public List<string> Deleted { get; } = [];
    public bool FailDelete { get; set; }
    public bool FailSave { get; set; }
    public Task<string> SaveAsync(Stream content, string path, CancellationToken ct = default) => FailSave ? throw new IOException("Synthetic upload interruption") : Task.FromResult(path);
    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        if (FailDelete) throw new IOException("Synthetic file lock");
        Deleted.Add(path); return Task.CompletedTask;
    }
    public Task MoveAsync(string from, string to, CancellationToken ct = default) => throw new InvalidOperationException("Image promotion must not move files.");
    public string ToPublicUrl(string path) => "/" + path;
}
