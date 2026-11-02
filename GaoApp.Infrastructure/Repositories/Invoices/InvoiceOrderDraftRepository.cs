using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public sealed class InvoiceOrderDraftRepository(AppDbContext db) : IInvoiceOrderDraftRepository
{
    public Task<List<InvoiceOrderDraftOrderDto>> SearchAsync(int storeId, string term, CancellationToken ct)
    {
        var numericId = int.TryParse(term.TrimStart('#'), out var id) ? id : 0;
        var query = db.Orders.AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted && x.Status == OrderStatus.Completed);
        query = numericId > 0
            ? query.Where(x => x.Id == numericId || x.OrderNumber == term)
            : query.Where(x => x.OrderNumber != null && x.OrderNumber.Contains(term));
        return query
            .OrderByDescending(x => x.Id).Take(20)
            .Select(x => new InvoiceOrderDraftOrderDto
            {
                OrderId = x.Id, OrderNumber = x.OrderNumber, CompletedAtUtc = x.CompletedAtUtc,
                GrandTotal = x.GrandTotal,
                HasInvoice = db.InvoiceHeads.Any(h => h.StoreId == storeId && !h.IsDeleted && h.OrderId == x.Id)
            }).ToListAsync(ct);
    }

    public async Task<Order?> GetOrderAsync(int storeId, int orderId, bool lockForCreate, CancellationToken ct)
    {
        // Serialize creation with finalization/void and other creators, within the caller's transaction.
        var query = lockForCreate && db.Database.IsRelational()
            ? db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [StoreId] = {storeId} AND [Id] = {orderId} AND [IsDeleted] = 0")
            : db.Orders.Where(x => x.StoreId == storeId && x.Id == orderId && !x.IsDeleted);
        return await query.AsNoTracking().SingleOrDefaultAsync(ct);
    }

    public Task<List<InvoiceOrderDraftReferenceDto>> GetExistingAsync(int storeId, int orderId, CancellationToken ct)
        => db.InvoiceHeads.AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted && x.OrderId == orderId)
            .OrderBy(x => x.Id)
            .Select(x => new InvoiceOrderDraftReferenceDto(x.Id, x.LegalEntity == null ? null : x.LegalEntity.Name))
            .ToListAsync(ct);

    public Task<InvoiceProviderSetting?> GetProviderMetadataAsync(int storeId, int? settingId, CancellationToken ct)
        => db.InvoiceProviderSettings.AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted && x.IsActive && x.ProviderCode == "VIETTEL" &&
                (!settingId.HasValue || x.Id == settingId.Value))
            .OrderByDescending(x => x.Id)
            .Select(x => new InvoiceProviderSetting
            {
                Id = x.Id, StoreId = x.StoreId, ProviderCode = x.ProviderCode,
                SupplierTaxCode = x.SupplierTaxCode, InvoiceType = x.InvoiceType,
                TemplateCode = x.TemplateCode, InvoiceSeries = x.InvoiceSeries, IsActive = x.IsActive
            }).FirstOrDefaultAsync(ct);
}
