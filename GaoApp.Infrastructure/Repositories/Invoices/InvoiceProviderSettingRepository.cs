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

    /// <summary>
    /// Bản cũ: lấy Viettel active mới nhất, không lọc StoreId.
    /// Giữ lại để không làm lỗi các service/admin cũ nếu đang dùng.
    /// </summary>
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

    /// <summary>
    /// Bản mới: lấy Viettel active theo StoreId.
    /// Dùng khi tạo InvoiceHead để snapshot đúng cấu hình cửa hàng.
    /// </summary>
    public async Task<InvoiceProviderSetting?> GetActiveViettelAsync(
        int storeId,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
            return null;

        return await _db.InvoiceProviderSettings
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
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
        providerCode = (providerCode ?? string.Empty).Trim().ToUpperInvariant();
        supplierTaxCode = (supplierTaxCode ?? string.Empty).Trim();
        templateCode = (templateCode ?? string.Empty).Trim();
        invoiceSeries = (invoiceSeries ?? string.Empty).Trim();

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