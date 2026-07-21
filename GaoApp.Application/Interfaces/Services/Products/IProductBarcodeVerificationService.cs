using GaoApp.Application.DTOs.Products.BarcodeVerification;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Products;

public interface IProductBarcodeVerificationService
{
    Task<List<MissingBarcodeUnitDto>> GetMissingUnitsForStockDocumentAsync(
        int stockDocumentId,
        int storeId,
        CancellationToken ct = default);

    Task<int> SubmitRequestsAsync(
        int stockDocumentId,
        int storeId,
        int userId,
        SubmitBarcodeVerificationRequest request,
        CancellationToken ct = default);
    Task<List<BarcodeVerificationManagementDto>> GetManagementListAsync(
       int storeId,
       BarcodeVerificationRequestStatus? status,
       string? keyword,
       CancellationToken ct = default);

    Task ApproveAsync(
        int storeId,
        int requestId,
        int managerUserId,
        string? managerNote,
        CancellationToken ct = default);

    Task RejectAsync(
        int storeId,
        int requestId,
        int managerUserId,
        string? managerNote,
        CancellationToken ct = default);

    Task ConfirmNoBarcodeAsync(
        int storeId,
        int requestId,
        int managerUserId,
        string? managerNote,
        CancellationToken ct = default);
}