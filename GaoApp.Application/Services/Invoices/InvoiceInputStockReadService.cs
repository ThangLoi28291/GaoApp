using System.Runtime.CompilerServices;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;

namespace GaoApp.Application.Services.Invoices;

public sealed class InvoiceInputStockReadService(IInvoiceInputStockReadRepository repository, ICurrentStore currentStore)
{
    // Weak keys release prepared indexes with the repository snapshot. Lazy
    // prevents two simultaneous tab requests from preparing it twice.
    private static readonly ConditionalWeakTable<IReadOnlyList<InvoiceInputStockMovement>, Lazy<InvoiceInputStockSnapshot>> Snapshots = new();

    public async Task<InvoiceInputStockPage> GetPageAsync(InvoiceInputStockQuery request, CancellationToken ct = default)
    {
        IReadOnlyCollection<int>? products = request.ProductVariantId is > 0 ? new[] { request.ProductVariantId.Value } : null;
        var warehouses = request.WarehouseId is > 0 ? new[] { request.WarehouseId.Value } : null;
        var source = await repository.GetPageMovementsAsync(currentStore.StoreId, products, warehouses, ct);
        ct.ThrowIfCancellationRequested();
        var snapshot = Snapshots.GetValue(source, rows => new(() => new InvoiceInputStockSnapshot(rows))).Value;
        var page = snapshot.GetPage(request, ct);
        await repository.HydratePageAsync(currentStore.StoreId, page, ct);
        return page;
    }

    public static InvoiceInputStockPage BuildPage(IReadOnlyList<InvoiceInputStockMovement> source, InvoiceInputStockQuery request)
        => new InvoiceInputStockSnapshot(source).GetPage(request);
}
