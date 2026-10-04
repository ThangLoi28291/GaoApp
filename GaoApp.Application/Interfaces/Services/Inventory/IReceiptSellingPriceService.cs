using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IReceiptSellingPriceService
{
    Task<ReceiptSellingPriceDto> GetAsync(int documentId, int lineId, CancellationToken ct);
    Task<ReceiptPriceReviewsDto> GetReviewsAsync(int documentId, CancellationToken ct);
    Task<List<ReceiptRetailComparisonDto>> GetRetailComparisonAsync(int documentId, CancellationToken ct);
    Task<ReceiptSellingPriceDto> UpdateAsync(int documentId, int lineId,
        UpdateReceiptSellingPricesRequest request, CancellationToken ct);
}
