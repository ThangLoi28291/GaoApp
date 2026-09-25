using GaoApp.Application.Common.Options;
using GaoApp.Application.DTOs.Media;
using GaoApp.Application.Services.Media;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Media;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Services.Media;
using GaoApp.Infrastructure.Storage;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Media;

[Collection("SqlServerConcurrency")]
public sealed class MediaLibrarySqlServerTests
{
    [Fact]
    public async Task Expired_upload_rolls_back_entire_product_creation_including_default_variant_and_barcode()
    {
        await using var database = new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(seed.StoreId);
        var template = await db.Products.AsNoTracking().SingleAsync();
        var productCount = await db.Products.CountAsync();
        var variantCount = await db.ProductVariants.CountAsync();
        var barcodeCount = await db.ProductVariantUnitBarcodes.CountAsync();
        var images = new ProductImageService(new MediaAssetRepository(db), new ProductImageRepository(db), new MediaTestFiles());
        var products = new GaoApp.Application.Services.Products.ProductService(new ProductRepository(db), images,
            new ProductVariantRepository(db), new GaoApp.Infrastructure.Data.AppUnitOfWork(db));
        var request = new GaoApp.Application.DTOs.Products.ProductCreateDto
        {
            Name = "Rollback media test", Alias = "rollback-media-test", BaseUnitId = template.BaseUnitId,
            CategoryId = template.CategoryId, SupplierId = template.SupplierId, TempImageTokens = ["expired-token"]
        };
        var failed = await products.CreateAsync(seed.StoreId, request, null);
        Assert.True(failed.IsFailure);
        db.ChangeTracker.Clear();
        Assert.Equal(productCount, await db.Products.CountAsync());
        Assert.Equal(variantCount, await db.ProductVariants.CountAsync());
        Assert.Equal(barcodeCount, await db.ProductVariantUnitBarcodes.CountAsync());
        request.TempImageTokens = [];
        Assert.True((await products.CreateAsync(seed.StoreId, request, null)).IsSuccess);
        Assert.Equal(productCount + 1, await db.Products.CountAsync());
        Assert.Equal(variantCount + 1, await db.ProductVariants.CountAsync());
    }

    [Fact]
    public async Task Sql_queries_and_cleanup_preserve_live_references_and_reject_stale_upload_commit()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        using var files = new HostFiles();
        var storage = new LocalFileStorageService(files.Paths, NullLogger<LocalFileStorageService>.Instance);
        var clock = new MediaTestClock();
        int tempId, liveId, productId;
        string tempPath;
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            productId = await db.ProductVariants.Where(v => v.Id == seed.ProductVariantId).Select(v => v.ProductId).SingleAsync();
            var uploads = new TempUploadService(new MediaAssetRepository(db), storage, new MediaCleanupOptions());
            var token = await uploads.UploadAsync(new TempUploadRequest { FileName = "photo.png", ContentType = "image/png", SizeBytes = 3, Content = new MemoryStream([1, 2, 3]) }, seed.StoreId, null);
            var temp = await db.MediaAssets.SingleAsync(a => a.TempToken == token);
            tempId = temp.Id; tempPath = temp.StoragePath;
            var live = new MediaAsset { StoreId = seed.StoreId, StoragePath = "uploads/products/live.png", OriginalFileName = "live.png", SizeBytes = 3, ExpireAtUtc = clock.Utc.AddDays(-1) };
            db.MediaAssets.Add(live); await db.SaveChangesAsync(); liveId = live.Id;
            var image = new ProductImage { StoreId = seed.StoreId, MediaAssetId = live.Id, ProductId = productId, IsDeleted = false };
            db.ProductImages.Add(image); await db.SaveChangesAsync();
            var variant = await db.ProductVariants.SingleAsync(v => v.Id == seed.ProductVariantId);
            variant.PrimaryProductImageId = image.Id; await db.SaveChangesAsync();
            image.IsDeleted = true; await db.SaveChangesAsync();
            await storage.SaveAsync(new MemoryStream([4, 5, 6]), live.StoragePath);
        }
        await using var stale = database.CreateTenantContext(seed.StoreId);
        var staleTemp = await stale.MediaAssets.SingleAsync(a => a.Id == tempId);
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var library = new MediaLibraryService(db, storage, Options.Create(new MediaCleanupOptions()), clock, NullLogger<MediaLibraryService>.Instance);
            var page = await library.ListAsync(null, null, 1, default);
            Assert.Equal(2, page.Summary.Total); Assert.Equal(1, page.Summary.Used); Assert.Equal(1, page.Summary.Ready);
            Assert.Equal(productId, Assert.Single(page.Items.Single(x => x.Id == liveId).Products).Id);
            var detail = await library.GetAsync(liveId, default);
            Assert.NotNull(detail);
            Assert.Equal("used", detail.Status);
            Assert.Equal(productId, Assert.Single(detail.Products).Id);
            Assert.Equal(MediaCleanupOutcome.Kept, await library.ProcessAsync(liveId, default));
            Assert.Equal(MediaCleanupOutcome.Deleted, await library.ProcessAsync(tempId, default));
            Assert.True(File.Exists(files.Paths.Resolve("uploads/products/live.png")));
            Assert.False(File.Exists(files.Paths.Resolve(tempPath)));
        }
        staleTemp.IsTemp = false; staleTemp.TempToken = null; staleTemp.ExpireAtUtc = null;
        stale.ProductImages.Add(new ProductImage { StoreId = seed.StoreId, ProductId = productId, MediaAssetId = tempId });
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var verify = database.CreateTenantContext(seed.StoreId);
        Assert.False(await verify.ProductImages.AnyAsync(i => i.MediaAssetId == tempId));
        var uploadRepo = new MediaAssetRepository(verify);
        var newUploads = new TempUploadService(uploadRepo, storage, new MediaCleanupOptions());
        var newTokens = new List<string>();
        foreach (var value in new byte[] { 7, 8 })
            newTokens.Add(await newUploads.UploadAsync(new TempUploadRequest { FileName = "same.png", ContentType = "image/png", SizeBytes = 1, Content = new MemoryStream([value]) }, seed.StoreId, null));
        var newAssets = await uploadRepo.GetTempsByTokensAsync(newTokens, seed.StoreId);
        var originalPaths = newAssets.Select(a => a.StoragePath).ToArray();
        await new ProductImageService(uploadRepo, new ProductImageRepository(verify), storage)
            .CommitTempImagesAsync(seed.StoreId, productId, newTokens, newTokens[1], null);
        await verify.SaveChangesAsync();
        Assert.Equal(2, originalPaths.Distinct().Count());
        Assert.Equal(originalPaths, newAssets.Select(a => a.StoragePath));
        Assert.All(newAssets, a => { Assert.False(a.IsTemp); Assert.True(File.Exists(files.Paths.Resolve(a.StoragePath))); });
        Assert.Equal(newAssets[1].Id, (await verify.ProductImages.SingleAsync(i => i.IsPrimary)).MediaAssetId);
    }

    [Fact]
    public async Task Concurrent_workers_delete_once_and_legacy_public_copy_is_removed()
    {
        await using var database = new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        using var files = new HostFiles();
        var storage = new LocalFileStorageService(files.Paths, NullLogger<LocalFileStorageService>.Instance);
        const string path = "uploads/products/legacy.png";
        await storage.SaveAsync(new MemoryStream([1, 2, 3]), path);
        var legacy = Path.Combine(files.Environment.WebRootPath, "uploads", "products", "legacy.png");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!); await File.WriteAllBytesAsync(legacy, [4, 5, 6]);
        int id;
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var asset = new MediaAsset { StoreId = seed.StoreId, StoragePath = path, IsTemp = true, TempToken = "expired", ExpireAtUtc = DateTime.UtcNow.AddHours(-1) };
            db.Add(asset); await db.SaveChangesAsync(); id = asset.Id;
        }
        async Task<MediaCleanupOutcome> Run()
        {
            await using var db = database.CreateTenantContext(seed.StoreId);
            return await new MediaLibraryService(db, storage, Options.Create(new MediaCleanupOptions()), TimeProvider.System,
                NullLogger<MediaLibraryService>.Instance).ProcessAsync(id, default);
        }
        var outcomes = await Task.WhenAll(Run(), Run());
        Assert.Single(outcomes, x => x == MediaCleanupOutcome.Deleted);
        Assert.False(File.Exists(legacy)); Assert.False(File.Exists(files.Paths.Resolve(path)));
    }
}
