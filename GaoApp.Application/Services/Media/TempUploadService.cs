using GaoApp.Application.Common.Abstractions;
using GaoApp.Application.DTOs.Media;
using GaoApp.Application.Interfaces.Repositories.Media;
using GaoApp.Application.Interfaces.Services.Media;
using System.Security.Cryptography;

namespace GaoApp.Application.Services.Media;

public sealed class TempUploadService : ITempUploadService
{
    private readonly IMediaAssetRepository _repo;
    private readonly IFileStorageService _storage;

    public TempUploadService(IMediaAssetRepository repo, IFileStorageService storage)
    {
        _repo = repo;
        _storage = storage;
    }

    public async Task<string> UploadAsync(TempUploadRequest req, int storeId, int? userId, CancellationToken ct = default)
    {
        if (req.SizeBytes <= 0) throw new InvalidOperationException("File rỗng.");

        var token = NewToken();
        var safeName = SafeFileName(req.FileName);
        var now = DateTime.UtcNow;

        var relativePath = $"uploads/_temp/{now:yyyy/MM/dd}/{token}/{safeName}";

        // ✅ đúng signature của bạn: (content, path)
        await _storage.SaveAsync(req.Content, relativePath, ct);

        var asset = new Domain.Entities.MediaAsset
        {
            StoreId = storeId,
            IsTemp = true,
            TempToken = token,
            StoragePath = relativePath,
            OriginalFileName = req.FileName,
            ContentType = req.ContentType,
            SizeBytes = req.SizeBytes,
            CreatedAtUtc = DateTime.UtcNow,
            ExpireAtUtc = DateTime.UtcNow.AddHours(6),
            CreatedBy = userId
        };

        await _repo.AddAsync(asset, ct);
        await _repo.SaveChangesAsync(ct);

        return token;
    }

    public async Task<bool> RevertAsync(string tempToken, int storeId, int? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tempToken)) return false;

        var asset = await _repo.GetTempByTokenAsync(tempToken.Trim(), storeId, ct);
        if (asset == null) return false;

        await _storage.DeleteAsync(asset.StoragePath, ct);

        _repo.Remove(asset);
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
