using GaoApp.Application.DTOs.Products.BarcodeVerification;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Products;

public interface IProductBarcodeVerificationRepository
{
    Task<List<MissingBarcodeUnitDto>> GetMissingUnitsForStockDocumentAsync(
        int stockDocumentId,
        int storeId,
        CancellationToken ct = default);

    Task<List<ProductUnitConversion>> GetValidConversionsForRequestAsync(
        int stockDocumentId,
        int storeId,
        List<int> productUnitConversionIds,
        CancellationToken ct = default);

    Task<List<int>> GetHandledConversionIdsAsync(
        int storeId,
        List<int> productUnitConversionIds,
        CancellationToken ct = default);

    Task<bool> BarcodeExistsAsync(
        int storeId,
        string barcode,
        CancellationToken ct = default);

    Task AddRequestsAsync(
        List<ProductBarcodeVerificationRequest> requests,
        CancellationToken ct = default);

    Task<List<BarcodeVerificationManagementDto>> GetManagementListAsync(
        int storeId,
        BarcodeVerificationRequestStatus? status,
        string? keyword,
        CancellationToken ct = default);

    Task<ProductBarcodeVerificationRequest?> GetRequestForUpdateAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<ProductUnitConversion?> GetConversionForBarcodeCreateAsync(
        int storeId,
        int productUnitConversionId,
        CancellationToken ct = default);

    Task AddBarcodeAsync(
        ProductVariantUnitBarcode barcode,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}