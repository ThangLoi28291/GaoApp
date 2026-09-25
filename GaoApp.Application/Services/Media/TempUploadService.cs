using GaoApp.Application.Common.Abstractions;
using GaoApp.Application.DTOs.Media;
using GaoApp.Application.Interfaces.Repositories.Media;
using GaoApp.Application.Interfaces.Services.Media;
using System.Security.Cryptography;
using GaoApp.Application.Common.Options;

namespace GaoApp.Application.Services.Media;

public sealed class TempUploadService : ITempUploadService
{
    private readonly IMediaAssetRepository _repo;
    private readonly IFileStorageService _storage;
    private readonly MediaCleanupOptions _options;

    public TempUploadService(IMediaAssetRepository repo, IFileStorageService storage, MediaCleanupOptions options)
    {
        _repo = repo;
        _storage = storage;
        _options = options;
    }

    public async Task<string> UploadAsync(TempUploadRequest req, int storeId, int? userId, CancellationToken ct = default)
    {
        if (req.SizeBytes <= 0) throw new InvalidOperationException("File rỗng.");

        var token = NewToken();
        var safeName = SafeFileName(req.FileName);
        var now = DateTime.UtcNow;

        // Immutable, unique path: promoting an upload changes database state only.
        // A failed/stale product save must never move or overwrite another image.
        var relativePath = $"uploads/products/{storeId}/{now:yyyy/MM/dd}/{token}/{safeName}";

        var asset = new Domain.Entities.MediaAsset
        {
            StoreId = storeId,
            IsTemp = true,
            TempToken = null, // Not claimable/cancellable until the file is ready.
            StoragePath = relativePath,
            OriginalFileName = req.FileName,
            ContentType = req.ContentType,
            SizeBytes = req.SizeBytes,
            CreatedAtUtc = DateTime.UtcNow,
            ExpireAtUtc = now.AddHours(_options.TempLifetimeHours),
            CreatedBy = userId
        };

        await _repo.AddAsync(asset, ct);
        await _repo.SaveChangesAsync(ct);

        // Register first: an interrupted upload always has an expiring record.
        await _storage.SaveAsync(req.Content, relativePath, ct);
        asset.TempToken = token;
        _repo.Update(asset);
        await _repo.SaveChangesAsync(ct);

        return token;
    }

    public async Task<bool> RevertAsync(string tempToken, int storeId, int? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tempToken)) return false;

        var asset = await _repo.GetTempByTokenAsync(tempToken.Trim(), storeId, ct);
        if (asset == null) return false;

        // Invalidate the token first; physical deletion is retried by the cleanup worker.
        asset.TempToken = null;
        asset.ExpireAtUtc = DateTime.UtcNow;
        _repo.Update(asset);
        await _repo.SaveChangesAsync(ct);

        return true;
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(18);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '-');

        name = name.Replace(' ', '-');              // ⭐ FIX NGAY
        name = System.Text.RegularExpressions.Regex
            .Replace(name, @"-+", "-")              // gộp ---
            .Trim('-');

        return name;
    }

}
