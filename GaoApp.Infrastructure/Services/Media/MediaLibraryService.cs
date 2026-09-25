using System.Data;
using GaoApp.Application.Common.Abstractions;
using GaoApp.Application.Common.Options;
using GaoApp.Application.DTOs.Media;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Services.Media;

/// <summary>
/// ExpireAtUtc is the durable cleanup deadline. For final assets it starts when
/// disuse is first observed, never from upload age. IsDeleted + a deadline is a
/// deletion claim awaiting file removal; IsDeleted + null means completed.
/// No unmanaged disk file is inferred to be garbage merely from its age.
/// </summary>
public sealed class MediaLibraryService(AppDbContext db, IFileStorageService storage,
    IOptions<MediaCleanupOptions> options, TimeProvider clock, ILogger<MediaLibraryService> logger)
{
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value
        : throw new InvalidOperationException("Cần chọn cửa hàng để quản lý hình ảnh.");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // Ignore filters deliberately: even a deleted ProductImage can still be a
    // live variant's primary image. Inactive products/variants also retain files.
    private IQueryable<int> UsedAssetIds()
    {
        var direct = from pi in db.ProductImages.IgnoreQueryFilters()
                     join p in db.Products.IgnoreQueryFilters() on pi.ProductId equals p.Id
                     where !pi.IsDeleted && !p.IsDeleted
                     select pi.MediaAssetId;
        var variants = from v in db.ProductVariants.IgnoreQueryFilters()
                       join pi in db.ProductImages.IgnoreQueryFilters() on v.PrimaryProductImageId equals pi.Id
                       join p in db.Products.IgnoreQueryFilters() on v.ProductId equals p.Id
                       where !v.IsDeleted && !p.IsDeleted
                       select pi.MediaAssetId;
        return direct.Union(variants);
    }

    private async Task<List<MediaLibraryItem>> LoadItemsAsync(int? id, CancellationToken ct)
    {
        var storeId = StoreId;
        var now = Now;
        var assets = db.MediaAssets.IgnoreQueryFilters().AsNoTracking().Where(a => a.StoreId == storeId);
        if (id.HasValue) assets = assets.Where(a => a.Id == id.Value);
        // Resolve references once, outside the status projection. A correlated
        // CASE/EXISTS in each COUNT/SUM repeatedly scanned the whole catalog and
        // timed out on a store with 16k images. Only compact metadata is loaded;
        // product names/links are fetched for the requested page below.
        var items = await assets.Select(a => new MediaLibraryItem
            {
                Id = a.Id, Name = a.OriginalFileName ?? "Ảnh", StoragePath = a.StoragePath,
                SizeBytes = a.SizeBytes, CreatedAtUtc = a.CreatedAtUtc, ExpireAtUtc = a.ExpireAtUtc,
                IsTemp = a.IsTemp, IsDeleted = a.IsDeleted
            }).ToListAsync(ct);
        if (items.Count == 0) return items;
        var usedIds = (await UsedAssetIds().Where(usedId => assets.Any(a => a.Id == usedId)).ToListAsync(ct)).ToHashSet();
        var remainingIds = items.Where(a => !usedIds.Contains(a.Id)).Select(a => a.Id).ToList();
        if (remainingIds.Count > 0)
        {
            // Keep legacy HTML/advertisement references, including cross-store
            // references for safety, without loading their content into memory
            // or scanning it for images already known to be linked.
            var remaining = assets.Where(a => remainingIds.Contains(a.Id));
            var htmlReferences = from a in remaining
                                 from p in db.Products.IgnoreQueryFilters()
                                 where !p.IsDeleted && p.Content != null && p.Content.Contains(a.StoragePath)
                                 select a.Id;
            usedIds.UnionWith(await htmlReferences.Distinct().ToListAsync(ct));
            var displayReferences = from a in remaining
                                    from p in db.Set<DisplayPromotion>().IgnoreQueryFilters()
                                    where !p.IsDeleted && p.MediaUrl != null && p.MediaUrl.Contains(a.StoragePath)
                                    select a.Id;
            usedIds.UnionWith(await displayReferences.Distinct().ToListAsync(ct));
        }
        foreach (var item in items)
        {
            item.Used = usedIds.Contains(item.Id);
            item.Status = item.Used ? "used" : item.IsDeleted && item.ExpireAtUtc == null ? "deleted" :
                item.ExpireAtUtc <= now ? "ready" : item.IsTemp ? "temp" :
                item.ExpireAtUtc != null ? "waiting" : "unused";
        }
        return items;
    }

    public async Task<MediaLibraryPage> ListAsync(string? search, string? status, int page, CancellationToken ct)
    {
        var allItems = await LoadItemsAsync(null, ct);
        var summary = new MediaLibrarySummary
        {
            Total = allItems.Count(x => x.Status != "deleted"),
            Used = allItems.Count(x => x.Used),
            Temporary = allItems.Count(x => x.Status == "temp"),
            Waiting = allItems.Count(x => x.Status == "waiting" || x.Status == "unused"),
            Ready = allItems.Count(x => x.Status == "ready"),
            Bytes = allItems.Where(x => x.Status != "deleted").Sum(x => x.SizeBytes),
            ReadyBytes = allItems.Where(x => x.Status == "ready").Sum(x => x.SizeBytes)
        };
        search = (search ?? "").Trim();
        if (search.Length > 150) search = search[..150];
        status = new[] { "used", "temp", "waiting", "unused", "ready", "deleted" }.Contains(status) ? status! : "all";
        var query = status == "all" ? allItems.Where(x => x.Status != "deleted") : allItems.Where(x => x.Status == status);
        if (search.Length > 0) query = query.Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        var count = query.Count();
        const int pageSize = 24;
        var pages = Math.Max(1, (count + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, pages);
        var items = query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();
        await LoadProductLinksAsync(items, ct);
        return new(items, summary, search, status, page, pages, count);
    }

    public async Task<MediaLibraryItem?> GetAsync(int id, CancellationToken ct)
    {
        var item = (await LoadItemsAsync(id, ct)).SingleOrDefault();
        if (item != null) await LoadProductLinksAsync([item], ct);
        return item;
    }

    private async Task LoadProductLinksAsync(List<MediaLibraryItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return;
        var ids = items.Select(x => x.Id).ToList();
        var storeId = StoreId;
        var direct = from pi in db.ProductImages.IgnoreQueryFilters().AsNoTracking()
                     join p in db.Products.IgnoreQueryFilters().AsNoTracking() on pi.ProductId equals p.Id
                     where pi.StoreId == storeId && p.StoreId == storeId && !pi.IsDeleted && !p.IsDeleted && ids.Contains(pi.MediaAssetId)
                     select new { pi.MediaAssetId, p.Id, p.Name };
        var variants = from v in db.ProductVariants.IgnoreQueryFilters().AsNoTracking()
                       join pi in db.ProductImages.IgnoreQueryFilters().AsNoTracking() on v.PrimaryProductImageId equals pi.Id
                       join p in db.Products.IgnoreQueryFilters().AsNoTracking() on v.ProductId equals p.Id
                       where v.StoreId == storeId && pi.StoreId == storeId && p.StoreId == storeId &&
                           !v.IsDeleted && !p.IsDeleted && ids.Contains(pi.MediaAssetId)
                       select new { pi.MediaAssetId, p.Id, p.Name };
        var links = await direct.Union(variants).ToListAsync(ct);
        foreach (var item in items)
            item.Products = links.Where(x => x.MediaAssetId == item.Id).Select(x => new MediaProductLink(x.Id, x.Name)).ToList();
    }

    public async Task<MediaCleanupResult> SweepAsync(int afterId, CancellationToken ct)
    {
        var storeId = StoreId;
        var ids = await db.MediaAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.StoreId == storeId && a.Id > afterId && (!a.IsDeleted || a.ExpireAtUtc != null))
            .OrderBy(a => a.Id).Select(a => a.Id).Take(options.Value.BatchSize).ToListAsync(ct);
        int scheduled = 0, deleted = 0, failed = 0, kept = 0;
        foreach (var id in ids)
        {
            try
            {
                var result = await ProcessAsync(id, ct);
                switch (result)
                {
                    case MediaCleanupOutcome.Scheduled: scheduled++; break;
                    case MediaCleanupOutcome.Deleted: deleted++; break;
                    case MediaCleanupOutcome.Failed: failed++; break;
                    default: kept++; break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "Media cleanup failed for Store {StoreId}, Asset {AssetId}; retry on next sweep.", storeId, id);
            }
            finally { db.ChangeTracker.Clear(); }
        }
        return new(ids.Count, scheduled, deleted, failed, kept, ids.LastOrDefault(afterId));
    }

    public Task<string?> GetPreviewPathAsync(int id, CancellationToken ct)
    {
        var storeId = StoreId;
        return db.MediaAssets.IgnoreQueryFilters().Where(a => a.StoreId == storeId && a.Id == id &&
            (!a.IsDeleted || a.ExpireAtUtc != null)).Select(a => a.StoragePath).SingleOrDefaultAsync(ct);
    }

    public async Task<bool> CancelTempAsync(int id, CancellationToken ct)
    {
        var storeId = StoreId;
        await using var tx = await BeginAsync(ct);
        var asset = await db.MediaAssets.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.StoreId == storeId && a.Id == id, ct);
        if (asset == null || asset.IsDeleted || !asset.IsTemp || asset.TempToken == null || await IsUsedAsync(asset, ct)) return false;
        asset.TempToken = null;
        asset.ExpireAtUtc = Now;
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return true;
    }

    public async Task<MediaCleanupOutcome> ProcessAsync(int id, CancellationToken ct)
    {
        var storeId = StoreId;
        // Phase 1 commits a tombstone before disk I/O. A stale product upload
        // cannot resurrect the asset: the committed SQL rowversion has changed.
        await using (var tx = await BeginAsync(ct))
        {
            var asset = await db.MediaAssets.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.StoreId == storeId && a.Id == id, ct);
            if (asset == null) return MediaCleanupOutcome.Missing;
            if (!ManagedPath(asset.StoragePath)) return MediaCleanupOutcome.Kept;
            if (await IsUsedAsync(asset, ct))
            {
                if (!asset.IsDeleted && !asset.IsTemp && asset.ExpireAtUtc != null)
                {
                    asset.ExpireAtUtc = null;
                    await db.SaveChangesAsync(ct);
                }
                if (tx != null) await tx.CommitAsync(ct);
                return MediaCleanupOutcome.Kept;
            }
            if (asset.IsDeleted && asset.ExpireAtUtc == null) return MediaCleanupOutcome.Kept;
            if (asset.ExpireAtUtc == null)
            {
                // Legacy unlinked assets get a FULL grace period on discovery.
                asset.ExpireAtUtc = asset.IsTemp ? Now.AddHours(options.Value.TempLifetimeHours) : Now.AddDays(options.Value.UnusedRetentionDays);
                await db.SaveChangesAsync(ct);
                if (tx != null) await tx.CommitAsync(ct);
                return MediaCleanupOutcome.Scheduled;
            }
            if (asset.ExpireAtUtc > Now) return MediaCleanupOutcome.Kept;
            if (!asset.IsDeleted)
            {
                asset.IsDeleted = true;
                asset.DeletedAtUtc = Now;
                asset.DeletedBy = db.CurrentUserId;
                asset.TempToken = null;
                await db.SaveChangesAsync(ct);
            }
            if (tx != null) await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();

        // Phase 2 rechecks references, including legacy aliases of the SAME
        // physical path across tenants. Never reveal those tenants to the UI.
        await using var deletion = await BeginAsync(ct);
        var claimed = await db.MediaAssets.IgnoreQueryFilters().SingleAsync(a => a.StoreId == storeId && a.Id == id, ct);
        if (!claimed.IsDeleted || claimed.ExpireAtUtc == null || await IsUsedAsync(claimed, ct)) return MediaCleanupOutcome.Kept;
        var upperPath = claimed.StoragePath.ToUpperInvariant();
        var aliases = await db.MediaAssets.IgnoreQueryFilters().Where(a => a.Id != id && a.StoragePath.ToUpper() == upperPath).ToListAsync(ct);
        foreach (var alias in aliases)
            if (!alias.IsDeleted || await IsUsedAsync(alias, ct)) return MediaCleanupOutcome.Kept;
        try { await storage.DeleteAsync(claimed.StoragePath, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Media file deletion pending for Store {StoreId}, Asset {AssetId}.", storeId, id);
            return MediaCleanupOutcome.Failed;
        }
        claimed.ExpireAtUtc = null;
        await db.SaveChangesAsync(ct);
        if (deletion != null) await deletion.CommitAsync(ct);
        logger.LogInformation("Media cleanup completed for Store {StoreId}, Asset {AssetId}.", storeId, id);
        return MediaCleanupOutcome.Deleted;
    }

    private async Task<bool> IsUsedAsync(MediaAsset asset, CancellationToken ct)
    {
        if (await UsedAssetIds().AnyAsync(id => id == asset.Id, ct)) return true;
        var path = asset.StoragePath;
        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => !p.IsDeleted && p.Content != null && p.Content.Contains(path), ct)) return true;
        return await db.Set<DisplayPromotion>().IgnoreQueryFilters()
            .AnyAsync(p => !p.IsDeleted && p.MediaUrl != null && p.MediaUrl.Contains(path), ct);
    }

    private static bool ManagedPath(string path)
    {
        try
        {
            if (UploadPathResolver.Normalize(path) != path) return false;
            return path.StartsWith("uploads/products/", StringComparison.Ordinal) || path.StartsWith("uploads/_temp/", StringComparison.Ordinal);
        }
        catch (InvalidOperationException) { return false; }
    }

    private async Task<IDbContextTransaction?> BeginAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlRawAsync("""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock @Resource=N'gao-media-cleanup', @LockMode='Exclusive',
                        @LockOwner='Transaction', @LockTimeout=10000;
                    IF @result < 0 THROW 51001, 'Media cleanup is busy.', 1;
                    """, ct);
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }
}
