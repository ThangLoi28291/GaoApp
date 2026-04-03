using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IReturnableValuationFragmentService
{
    Task<List<ReturnableValuationFragmentDto>> GetForOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default);

}