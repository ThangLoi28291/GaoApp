using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Orders;

public interface IOrderLegalEntityAllocationReversalRepository
{
    Task<Dictionary<int, decimal>> GetReversedBaseQuantityBySourceEntryIdsAsync(
        IReadOnlyCollection<int> sourceValuationEntryIds,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy toàn bộ reversal của order kèm allocation gốc để đồng bộ lại
    /// InvoiceDetail nháp đúng theo từng HKD/kho đã thực sự nhận hàng trả.
    /// </summary>
    Task<List<OrderLegalEntityAllocationReversal>> GetForOrderAsync(
        int orderId,
        CancellationToken ct = default);

    Task AddRangeAsync(
        IReadOnlyCollection<OrderLegalEntityAllocationReversal> reversals,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
