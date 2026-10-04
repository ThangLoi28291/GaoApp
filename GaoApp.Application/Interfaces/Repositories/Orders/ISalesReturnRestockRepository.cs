using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Orders;

public interface ISalesReturnRestockRepository
{
    Task EnsureAvailableAsync(CancellationToken ct);
    Task LockOrderAsync(int orderId, CancellationToken ct);
    Task AddRangeAsync(IReadOnlyCollection<SalesReturnRestockFragment> rows, CancellationToken ct);
    Task<List<OrderLegalEntityAllocationReversal>> GetLegalReservationsAsync(int returnId, CancellationToken ct);
    Task<List<SalesReturnRestockFragment>> GetPendingAsync(int? returnId, CancellationToken ct);
    Task<bool> HasFragmentsAsync(int returnId, CancellationToken ct);
    Task<Dictionary<int, decimal>> GetLegacyReservedQuantitiesAsync(IReadOnlyCollection<int> sourceIds, CancellationToken ct);
}
