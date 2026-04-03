using System.Text.Json;
using GaoApp.Application.Common.Abstractions;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Media;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Media;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Media;

public sealed class ProductImageService : IProductImageService
{
    private readonly IMediaAssetRepository _mediaRepo;
    private readonly IProductImageRepository _imgRepo;
    private readonly IFileStorageService _storage;

    public ProductImageService(
        IMediaAssetRepository mediaRepo,
        IProductImageRepository imgRepo,
        IFileStorageService storage)
    {
        _mediaRepo = mediaRepo;
        _imgRepo = imgRepo;
        _storage = storage;
    }

    /// <summary>
    /// Task 3: Commit ảnh temp khi Create (MVP)
    /// - tempTokens: list token đã upload temp
    /// - primaryToken: token ảnh chính (có thể null)
    /// </summary>
    public async Task CommitTempImagesAsync(
        int storeId,
        int productId,
        List<string> tempTokens,
        string? primaryToken,
        int? userId,
        CancellationToken ct = default)
    {
        tempTokens ??= new();

        // remove empty + distinct
        tempTokens = tempTokens
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tempTokens.Count == 0) return;

        // load temp assets by tokens
        var tempAssets = await _mediaRepo.GetTempsByTokensAsync(tempTokens, storeId, ct);
        if (tempAssets.Count == 0) return;

        var now = DateTime.UtcNow;

        // primary token normalize
        primaryToken = string.IsNullOrWhiteSpace(primaryToken) ? null : primaryToken.Trim();

        // create ProductImage entities in order of token list
        var imagesToAdd = new List<ProductImage>();
        var sort = await _imgRepo.GetNextSortOrderAsync(storeId, productId, ct);


        foreach (var token in tempTokens)
        {
            var asset = tempAssets.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.TempToken) &&
                string.Equals(a.TempToken, token, StringComparison.OrdinalIgnoreCase));

            if (asset == null) continue;

            // move file: _temp -> final
            var fileName = Path.GetFileName(asset.StoragePath);
            var finalPath = $"uploads/products/{now:yyyy/MM/dd}/{productId}/{fileName}";

            await _storage.MoveAsync(asset.StoragePath, finalPath, ct);

            // update asset
            asset.StoragePath = finalPath;
            asset.IsTemp = false;
            asset.TempToken = null;
            asset.ExpireAtUtc = null;
            _mediaRepo.Update(asset);

            var isPrimary = primaryToken != null &&
                            string.Equals(primaryToken, token, StringComparison.OrdinalIgnoreCase);

            imagesToAdd.Add(new ProductImage
            {
                StoreId = storeId,
                ProductId = productId,
                MediaAssetId = asset.Id,
                SortOrder = sort++,
                IsPrimary = isPrimary
            });
        }

        // if user didn't pick primary => set first as primary (if any)
        if (imagesToAdd.Count > 0 && imagesToAdd.All(x => !x.IsPrimary))
            imagesToAdd[0].IsPrimary = true;

        // ✅ nếu batch này có primary => gỡ primary cũ của product trước
        if (imagesToAdd.Any(x => x.IsPrimary))
        {
            await _imgRepo.UnsetPrimaryAsync(storeId, productId, ct);
        }

        // AddRange (repo-style)
        await _imgRepo.AddRangeAsync(imagesToAdd, ct);



    }

    /// <summary>
    /// Task 4: Sync ảnh khi Edit:
    /// - orderedItems: danh sách theo thứ tự UI (ExistingProductImageId hoặc TempToken)
    /// - primaryKey: "id:123" hoặc "temp:token"
    /// </summary>
    /// 
    public async Task SetPrimaryAsync(
    int storeId,
    int productId,
    string? primaryKey,
    CancellationToken ct = default)
    {
        // 0) Không có ảnh thì thôi
        var anyImageId = await _imgRepo.GetFirstActiveIdAsync(storeId, productId, ct);
        if (anyImageId == null) return;

        // 1) Nếu user KHÔNG chọn primary => GIỮ nguyên primary (đúng yêu cầu “thêm ảnh không đổi”)
        // Nhưng nếu hiện tại không còn primary (ví dụ vừa xóa primary) => fallback ảnh đầu
        if (string.IsNullOrWhiteSpace(primaryKey))
        {
            // kiểm tra còn primary không (nhanh gọn: dùng list)
            var imgs = await _imgRepo.GetByProductIdAsync(productId, storeId, ct);
            if (imgs.Any(x => !x.IsDeleted && x.IsPrimary)) return;

            await _imgRepo.UnsetPrimaryAsync(storeId, productId, ct);
            await _imgRepo.SetPrimaryByIdAsync(storeId, productId, anyImageId.Value, ct);
            return;
        }

        // 2) Parse id:xxx (nếu user chọn ảnh chính)
        int? targetId = null;
        var pk = primaryKey.Trim();

        if (pk.StartsWith("id:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(pk[3..], out var id))
        {
            targetId = id;
        }

        // 3) Nếu user chọn id nhưng ảnh đó đã bị xóa trong cùng lần save -> fallback ảnh đầu
        targetId ??= anyImageId;

        // 4) ATOMIC: clear -> set 1
        await _imgRepo.UnsetPrimaryAsync(storeId, productId, ct);

        var ok = await _imgRepo.SetPrimaryByIdAsync(storeId, productId, targetId.Value, ct);
        if (!ok && anyImageId != null)
        {
            // nếu id không tồn tại (bị xóa) thì set fallback
            await _imgRepo.SetPrimaryByIdAsync(storeId, productId, anyImageId.Value, ct);
        }

        await _imgRepo.MoveToFirstAsync(storeId, productId, targetId.Value, ct);
    }





    public async Task SyncEditAsync(
     int storeId,
     int productId,
     List<ProductImageStateDto> orderedItems,
     string? primaryKey,
     int? userId,
     CancellationToken ct = default)
    {
        orderedItems ??= new();

        // 1) Load existing (tracked)
        var existing = await _imgRepo.GetByProductIdAsync(productId, storeId, ct);
        var existingMap = existing.ToDictionary(x => x.Id);

        // ✅ KHÔNG ĐỤNG IsPrimary ở đây (giữ nguyên ảnh chính cũ)

        // 2) UI order cho ảnh cũ
        var existingIdsInOrder = orderedItems
            .Where(x => x.ExistingProductImageId.HasValue)
            .Select(x => x.ExistingProductImageId!.Value)
            .ToList();

        // 3) temp tokens (ảnh mới) — sẽ append sau
        var tempTokensInOrder = orderedItems
            .Where(x => !string.IsNullOrWhiteSpace(x.TempToken))
            .Select(x => x.TempToken!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 4) Apply sort cho ảnh cũ theo UI
        var keepExistingIds = new HashSet<int>();
        var sort = 0;

        foreach (var id in existingIdsInOrder)
        {
            if (!existingMap.TryGetValue(id, out var pi)) continue;
            pi.SortOrder = sort++;
            keepExistingIds.Add(pi.Id);
        }

        // 5) Load temp assets
        var tempAssets = tempTokensInOrder.Count == 0
            ? new List<MediaAsset>()
            : await _mediaRepo.GetTempsByTokensAsync(tempTokensInOrder, storeId, ct);

        var tempMap = tempAssets
            .Where(a => !string.IsNullOrWhiteSpace(a.TempToken))
            .ToDictionary(a => a.TempToken!, a => a, StringComparer.OrdinalIgnoreCase);

        // 6) Append ảnh mới SAU CÙNG (không đổi primary)
        var now = DateTime.UtcNow;
        var added = new List<ProductImage>();

        foreach (var token in tempTokensInOrder)
        {
            if (!tempMap.TryGetValue(token, out var asset)) continue;

            var fileName = Path.GetFileName(asset.StoragePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"{Guid.NewGuid():N}.jpg";

            var finalPath = $"uploads/products/{now:yyyy/MM/dd}/{productId}/{fileName}";
            await _storage.MoveAsync(asset.StoragePath, finalPath, ct);

            asset.StoragePath = finalPath;
            asset.IsTemp = false;
            asset.TempToken = null;
            asset.ExpireAtUtc = null;
            _mediaRepo.Update(asset);

            added.Add(new ProductImage
            {
                StoreId = storeId,
                ProductId = productId,
                MediaAssetId = asset.Id,
                SortOrder = sort++,     // ✅ luôn nằm sau ảnh cũ
                IsPrimary = false       // ✅ ảnh mới không được thành primary
            });
        }

        // 7) Remove ảnh cũ bị xóa
        var removed = existing.Where(x => !keepExistingIds.Contains(x.Id)).ToList();
        foreach (var pi in removed)
        {
            if (pi.MediaAsset?.StoragePath != null)
                await _storage.DeleteAsync(pi.MediaAsset.StoragePath, ct);
        }

        if (removed.Count > 0) _imgRepo.RemoveRange(removed);
        if (added.Count > 0) await _imgRepo.AddRangeAsync(added, ct);

        // ✅ không SaveChanges ở đây
    }


    public Task ClearImageTrackingAsync(int storeId, int productId)
    {
        _imgRepo.ClearTracking(storeId, productId);
        return Task.CompletedTask;
    }

    public async Task<List<ProductImageListItemDto>> GetImagesForVariantAsync(
    int storeId,
    int productId,
    CancellationToken ct = default)
    {
        var images = await _imgRepo.GetByProductIdAsync(productId, storeId, ct);

        return images
            .Where(x => !x.IsDeleted && x.MediaAsset != null)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.SortOrder)
            .Select(x => new ProductImageListItemDto
            {
                Id = x.Id,
                Url = "/" + x.MediaAsset!.StoragePath, // dùng đúng chuẩn của bạn
                IsPrimary = x.IsPrimary
            })
            .ToList();
    }


}
