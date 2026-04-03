using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Interfaces.Services.Media;

public interface IProductImageService
{
    Task CommitTempImagesAsync(
        int storeId,
        int productId,
        List<string> tempTokens,
        string? primaryToken,
        int? userId,
        CancellationToken ct = default);

    Task SyncEditAsync(
        int storeId,
        int productId,
        List<ProductImageStateDto> orderedItems,
        string? primaryKey,
        int? userId,
        CancellationToken ct = default);
    Task SetPrimaryAsync(
    int storeId,
    int productId,
    string? primaryKey,
    CancellationToken ct = default);
    Task ClearImageTrackingAsync(
      int storeId,
      int productId);
    Task<List<ProductImageListItemDto>> GetImagesForVariantAsync(
    int storeId,
    int productId,
    CancellationToken ct = default);
}
