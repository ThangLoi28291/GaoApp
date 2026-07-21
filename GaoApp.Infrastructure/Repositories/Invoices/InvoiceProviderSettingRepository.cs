using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public class InvoiceProviderSettingRepository : IInvoiceProviderSettingRepository
{
    private readonly AppDbContext _db;
    private readonly IInvoiceProviderCredentialProtector _credentialProtector;

    public InvoiceProviderSettingRepository(
        AppDbContext db,
        IInvoiceProviderCredentialProtector credentialProtector)
    {
        _db = db;
        _credentialProtector = credentialProtector;
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

    public Task<InvoiceProviderSetting?> GetByIdAsync(
     int id,
     CancellationToken ct = default)
    {
        return _db.InvoiceProviderSettings
            .FirstOrDefaultAsync(
                x => x.Id == id && !x.IsDeleted,
                ct);
    }
    public async Task<InvoiceProviderSetting?> GetByIdWithCredentialAsync(
    int id,
    CancellationToken ct = default)
    {
        var entity = await _db.InvoiceProviderSettings
            .FirstOrDefaultAsync(
                x => x.Id == id && !x.IsDeleted,
                ct);

        return await PrepareCredentialForUseAsync(entity, ct);
    }

    /// <summary>
    /// Bản cũ: lấy Viettel active mới nhất, không lọc StoreId.
    /// Giữ lại để không làm lỗi các service/admin cũ nếu đang dùng.
    /// </summary>
    public async Task<InvoiceProviderSetting?> GetActiveViettelAsync(CancellationToken ct = default)
    {
        var entity = await _db.InvoiceProviderSettings
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.IsActive &&
                x.ProviderCode == "VIETTEL")
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        return await PrepareCredentialForUseAsync(entity, ct);
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

        var entity = await _db.InvoiceProviderSettings
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive &&
                x.ProviderCode == "VIETTEL")
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        return await PrepareCredentialForUseAsync(entity, ct);
    }

    public async Task<InvoiceProviderSetting?> GetForInvoiceAsync(
        int storeId,
        int? invoiceProviderSettingId,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
            return null;

        if (!invoiceProviderSettingId.HasValue)
            return await GetActiveViettelAsync(storeId, ct);

        var entity = await _db.InvoiceProviderSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == invoiceProviderSettingId.Value &&
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive &&
                x.ProviderCode == "VIETTEL",
                ct);

        return await PrepareCredentialForUseAsync(entity, ct);
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
        entity.Password = _credentialProtector.Protect(entity.Password);
        await _db.InvoiceProviderSettings.AddAsync(entity, ct);
    }

    public void Update(InvoiceProviderSetting entity)
    {
        entity.Password = _credentialProtector.Protect(entity.Password);
        _db.InvoiceProviderSettings.Update(entity);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }

    private async Task<InvoiceProviderSetting?> PrepareCredentialForUseAsync(
        InvoiceProviderSetting? entity,
        CancellationToken ct)
    {
        if (entity == null)
            return null;

        var storedValue = entity.Password;
        var plaintext = _credentialProtector.Unprotect(storedValue);

        // Nâng cấp plaintext legacy ngay lần đọc đầu tiên, nhưng vẫn trả plaintext
        // trong memory để các Viettel client hiện tại tiếp tục hoạt động.
        if (!_credentialProtector.IsProtected(storedValue))
        {
            var protectedValue = _credentialProtector.Protect(storedValue);
            var entry = _db.Entry(entity);
            var isTracked = entry.State != EntityState.Detached;

            await _db.InvoiceProviderSettings
                .Where(x => x.Id == entity.Id && x.Password == storedValue)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Password, protectedValue),
                    ct);

            // SQL Server rowversion thay đổi khi nâng cấp secret. Reload entity
            // đang tracked để lần update kế tiếp không báo concurrency giả.
            if (isTracked)
            {
                await entry.ReloadAsync(ct);
                plaintext = _credentialProtector.Unprotect(entity.Password);
            }
        }

        entity.Password = plaintext;
        return entity;
    }
}
