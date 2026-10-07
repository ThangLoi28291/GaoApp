using GaoApp.Application.DTOs.Delivery;

namespace GaoApp.Application.Interfaces.Services.Delivery;

public interface IDeliveryFoundationService
{
    Task<IReadOnlyList<DeliverySummaryDto>> ListAsync(int take, CancellationToken ct = default);
    Task<DeliveryDetailDto> GetAsync(int id, CancellationToken ct = default);
    Task<DeliveryDetailDto> LookupAsync(string tokenOrCode, CancellationToken ct = default);
    Task<IReadOnlyList<DeliveryHistoryDto>> HistoryAsync(int id, CancellationToken ct = default);
    Task<DeliveryDetailDto> UpdateRecipientAsync(int id, DeliveryRecipientRequest request, CancellationToken ct = default);
    Task<DeliveryDetailDto> CreateInTransactionAsync(DeliveryFoundationCreate request, CancellationToken ct = default);
}
