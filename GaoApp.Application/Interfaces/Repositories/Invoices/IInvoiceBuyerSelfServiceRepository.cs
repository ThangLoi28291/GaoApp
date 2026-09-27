using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceBuyerSelfServiceRepository
{
    Task<InvoiceBuyerSelfServiceRequest?> GetByTokenHashAsync(
        byte[] tokenHash,
        CancellationToken ct = default);

    Task AddAsync(
        InvoiceBuyerSelfServiceRequest request,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}