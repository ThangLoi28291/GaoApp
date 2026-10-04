using System.Globalization;
using System.Text;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;

namespace GaoApp.Application.Services.Invoices;

public sealed class InvoiceInputStockReadService(IInvoiceInputStockReadRepository repository, ICurrentStore currentStore)
{
    public async Task<InvoiceInputStockPage> GetPageAsync(InvoiceInputStockQuery request, CancellationToken ct = default)
    {
        // A drill-down is normally opened for one product and one warehouse.
        // Push those predicates into SQL so the ledger does not load the full
        // store history (which can contain hundreds of thousands of rows).
        var products = request.ProductVariantId is > 0
            ? new[] { request.ProductVariantId.Value }
            : null;
        var warehouses = request.WarehouseId is > 0
            ? new[] { request.WarehouseId.Value }
            : null;
        var source = products is null && warehouses is null
            ? await repository.GetMovementsAsync(currentStore.StoreId, ct)
            : await repository.GetMovementsAsync(currentStore.StoreId, products, warehouses, ct);
        return BuildPage(source, request);
    }

    public static InvoiceInputStockPage BuildPage(IReadOnlyList<InvoiceInputStockMovement> source, InvoiceInputStockQuery request)
    {
        // Compute running balances using complete history BEFORE applying date/type filters.
        // Reuse each product/warehouse history; migration can contribute hundreds of thousands of rows.
        var histories = source.GroupBy(x => (x.WarehouseId, x.ProductVariantId))
            .ToDictionary(x => x.Key, x => x.ToList());
        foreach (var group in histories.Values)
        {
            decimal balance = 0;
            foreach (var row in group.OrderBy(x => x.DateUtc).ThenBy(x => x.Kind == "increase" ? 0 : 1).ThenBy(x => x.Key, StringComparer.Ordinal))
            {
                row.Before = balance;
                balance += row.Change;
                row.After = balance;
            }
        }
        var keyword = Normalize(request.Keyword);
        var hasRowFilter = request.WarehouseId.HasValue || request.ProductVariantId.HasValue || keyword.Length > 0;
        // Do not copy the complete snapshot for the common first page. The
        // snapshot is already scoped by the repository when an id filter is
        // present; reusing it avoids another 200k+ item allocation.
        var selected = hasRowFilter
            ? source.Where(x =>
                (!request.WarehouseId.HasValue || x.WarehouseId == request.WarehouseId) &&
                (!request.ProductVariantId.HasValue || x.ProductVariantId == request.ProductVariantId) &&
                (keyword.Length == 0 || Normalize($"{x.ProductName} {x.Code} {x.SourceCode} {x.XmlNumber} {x.LegacySourceKey}").Contains(keyword)))
                .ToList()
            : source;
        var balances = selected.GroupBy(x => (x.WarehouseId, x.ProductVariantId)).Select(group =>
        {
            // A source keyword must not produce a misleading partial product balance.
            var all = histories[group.Key];
            var first = group.First();
            return new InvoiceInputStockBalance
            {
                WarehouseId = first.WarehouseId, ProductVariantId = first.ProductVariantId,
                ProductName = first.ProductName, Code = first.Code, BaseUnit = first.BaseUnit,
                WarehouseName = first.WarehouseName, LegalEntityName = first.LegalEntityName,
                Opening = all.Where(x => x.IsOpening).Sum(x => x.Change),
                Received = all.Where(x => !x.IsOpening && x.Change > 0).Sum(x => x.Change),
                Issued = -all.Where(x => !x.IsOpening && x.Change < 0).Sum(x => x.Change), Held = all.Sum(x => x.Held)
            };
        }).OrderBy(x => x.ProductName).ThenBy(x => x.WarehouseId).ToList();

        // HTML dates are store-local (Asia/Bangkok/Vietnam, UTC+7), not server-local.
        var from = request.FromDate?.Date.AddHours(-7);
        var until = request.ToDate?.Date.AddDays(1).AddHours(-7);
        var movements = selected.Where(x => (!from.HasValue || x.DateUtc >= from) && (!until.HasValue || x.DateUtc < until)).ToList();
        var result = new InvoiceInputStockPage
        {
            ProductCount = balances.Count, IncreaseCount = movements.Count(x => x.Kind == "increase"),
            DecreaseCount = movements.Count(x => x.Kind == "decrease"), HoldCount = movements.Count(x => x.Kind == "hold"),
            NegativeCount = balances.Count(x => x.Available < 0),
            Warehouses = source.GroupBy(x => x.WarehouseId).Select(x => new InvoiceInputStockWarehouse(x.Key, x.First().WarehouseName)).OrderBy(x => x.Name).ToList(),
            PageSize = new[] { 10, 20, 50, 100 }.Contains(request.PageSize) ? request.PageSize : 20
        };
        if (request.View == "ledger")
        {
            movements = movements.Where(x => string.IsNullOrEmpty(request.Kind) || x.Kind == request.Kind)
                .OrderByDescending(x => x.DateUtc).ThenByDescending(x => x.Kind == "increase" ? 0 : 1)
                .ThenByDescending(x => x.Key, StringComparer.Ordinal).ToList();
            result.TotalItems = movements.Count;
            result.Page = Math.Clamp(request.Page, 1, result.TotalPages);
            result.Movements = movements.Skip((result.Page - 1) * result.PageSize).Take(result.PageSize).ToList();
        }
        else
        {
            if (request.Kind == "negative") balances = balances.Where(x => x.Available < 0).ToList();
            if (request.Kind == "hold") balances = balances.Where(x => x.Held > 0).ToList();
            result.TotalItems = balances.Count;
            result.Page = Math.Clamp(request.Page, 1, result.TotalPages);
            result.Balances = balances.Skip((result.Page - 1) * result.PageSize).Take(result.PageSize).ToList();
        }
        return result;
    }

    private static string Normalize(string? value)
        => string.Concat((value ?? "").Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
}
