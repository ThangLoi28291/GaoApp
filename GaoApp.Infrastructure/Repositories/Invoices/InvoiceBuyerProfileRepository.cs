using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public class InvoiceBuyerProfileRepository : IInvoiceBuyerProfileRepository
{
    private readonly AppDbContext _db;

    public InvoiceBuyerProfileRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<InvoiceBuyerProfile?> GetBestByTaxCodeAsync(
        int storeId,
        string taxCode,
        string? buyerType = null,
        CancellationToken ct = default)
    {
        taxCode = (taxCode ?? string.Empty).Trim();

        if (storeId <= 0 || string.IsNullOrWhiteSpace(taxCode))
            return null;

        var query = _db.InvoiceBuyerProfiles
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive &&
                x.TaxCode != null &&
                x.TaxCode == taxCode);

        if (!string.IsNullOrWhiteSpace(buyerType))
        {
            var normalizedBuyerType = buyerType.Trim();

            var sameType = await query
                .Where(x => x.BuyerType == normalizedBuyerType)
                .OrderByDescending(x => x.IsVerifiedByUser)
                .ThenByDescending(x => x.LastUsedAtUtc)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (sameType != null)
                return sameType;
        }

        return await query
            .OrderByDescending(x => x.IsVerifiedByUser)
            .ThenByDescending(x => x.LastUsedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAsync(
        InvoiceBuyerProfile profile,
        CancellationToken ct = default)
    {
        await _db.InvoiceBuyerProfiles.AddAsync(profile, ct);
    }

    public void Update(
        InvoiceBuyerProfile profile)
    {
        _db.InvoiceBuyerProfiles.Update(profile);
    }
}