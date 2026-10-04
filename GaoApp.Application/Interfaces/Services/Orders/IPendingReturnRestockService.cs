using GaoApp.Application.DTOs.Returns;

namespace GaoApp.Application.Interfaces.Services.Orders;

public interface IPendingReturnRestockService
{
    Task<List<PendingReturnRestockDto>> GetPendingAsync(CancellationToken ct = default);
    Task CompleteAsync(int returnId, CancellationToken ct = default);
}
