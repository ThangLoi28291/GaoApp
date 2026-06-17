using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceProviderSettingRepository
{
    Task<List<InvoiceProviderSetting>> GetAllAsync(CancellationToken ct = default);

    Task<InvoiceProviderSetting?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<InvoiceProviderSetting?> GetActiveViettelAsync(CancellationToken ct = default);

    Task<bool> ExistsDuplicateAsync(
        int id,
        string providerCode,
        string supplierTaxCode,
        string templateCode,
        string invoiceSeries,
        CancellationToken ct = default);

    Task AddAsync(InvoiceProviderSetting entity, CancellationToken ct = default);

    void Update(InvoiceProviderSetting entity);

    Task SaveChangesAsync(CancellationToken ct = default);
}