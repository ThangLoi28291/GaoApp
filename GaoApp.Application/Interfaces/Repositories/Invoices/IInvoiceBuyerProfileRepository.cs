using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceBuyerProfileRepository
{
    Task<InvoiceBuyerProfile?> GetBestByTaxCodeAsync(
        int storeId,
        string taxCode,
        string? buyerType = null,
        CancellationToken ct = default);

    Task AddAsync(
        InvoiceBuyerProfile profile,
        CancellationToken ct = default);

    void Update(
        InvoiceBuyerProfile profile);
}