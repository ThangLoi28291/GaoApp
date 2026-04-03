using GaoApp.Application.DTOs.Media;

namespace GaoApp.Application.Interfaces.Services.Media;

public interface ITempUploadService
{
    Task<string> UploadAsync(TempUploadRequest req, int storeId, int? userId, CancellationToken ct = default);
    Task<bool> RevertAsync(string tempToken, int storeId, int? userId, CancellationToken ct = default);
}
