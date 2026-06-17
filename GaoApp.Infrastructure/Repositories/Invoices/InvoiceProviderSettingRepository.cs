using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public class InvoiceProviderSettingRepository : IInvoiceProviderSettingRepository
{
    private readonly AppDbContext _db;

    public InvoiceProviderSettingRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<InvoiceProviderSetting>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.InvoiceProviderSettings
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.ProviderCode)
            .ThenBy(x => x.SupplierTaxCode)
            .ThenBy(x => x.TemplateCode)
            .ThenBy(x => x.InvoiceSeries)
            .ToListAsync(ct);
    }

    public async Task<InvoiceProviderSetting?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.InvoiceProviderSettings
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    }

    public async Task<InvoiceProviderSetting?> GetActiveViettelAsync(CancellationToken ct = default)
    {
        return await _db.InvoiceProviderSettings
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.IsActive &&
                x.ProviderCode == "VIETTEL")
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> ExistsDuplicateAsync(
        int id,
        string providerCode,
        string supplierTaxCode,
        string templateCode,
        string invoiceSeries,
        CancellationToken ct = default)
    {
        providerCode = providerCode.Trim().ToUpperInvariant();
        supplierTaxCode = supplierTaxCode.Trim();
        templateCode = templateCode.Trim();
        invoiceSeries = invoiceSeries.Trim();

        return await _db.InvoiceProviderSettings.AnyAsync(x =>
            !x.IsDeleted &&
            x.Id != id &&
            x.ProviderCode == providerCode &&
            x.SupplierTaxCode == supplierTaxCode &&
            x.TemplateCode == templateCode &&
            x.InvoiceSeries == invoiceSeries,
            ct);
    }

    public async Task AddAsync(InvoiceProviderSetting entity, CancellationToken ct = default)
    {
        await _db.InvoiceProviderSettings.AddAsync(entity, ct);
    }

    public void Update(InvoiceProviderSetting entity)
    {
        _db.InvoiceProviderSettings.Update(entity);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
}