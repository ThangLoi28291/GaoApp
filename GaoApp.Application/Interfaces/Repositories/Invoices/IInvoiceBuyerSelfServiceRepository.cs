using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceBuyerSelfServiceRepository
{
    // Serializes with invoice issuance/route changes in the current transaction.
    Task LockStoreAsync(int storeId, CancellationToken ct = default);
    Task<List<InvoiceHead>> GetHeadsForAccessAsync(int storeId, int orderId, CancellationToken ct = default);
    Task RevokeForOrderAsync(int storeId, int orderId, DateTime nowUtc, CancellationToken ct = default);
    Task MarkSubmittedAsync(int storeId, int requestId, DateTime nowUtc, CancellationToken ct = default);

    Task<InvoiceBuyerSelfServiceRequest?> GetByTokenHashAsync(
        byte[] tokenHash,
        CancellationToken ct = default);

    Task AddAsync(
        InvoiceBuyerSelfServiceRequest request,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
