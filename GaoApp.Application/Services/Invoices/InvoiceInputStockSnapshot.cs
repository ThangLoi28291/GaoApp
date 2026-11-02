using System.Globalization;
using System.Text;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Services.Invoices;

/// <summary>Read-only aggregates and chronological index for one repository snapshot.</summary>
internal sealed class InvoiceInputStockSnapshot
{
    private readonly Entry[] entries;
    private readonly InvoiceInputStockBalance[] balances;
    private readonly int[] balanceOrder;
    private readonly InvoiceInputStockWarehouse[] warehouses;
    private readonly Lazy<SearchText[]> search;
    private readonly int increases, decreases, holds, negatives;

    public InvoiceInputStockSnapshot(IReadOnlyList<InvoiceInputStockMovement> source)
    {
        entries = new Entry[source.Count];
        var groupIds = new Dictionary<(int Warehouse, int Product), int>();
        var warehouseNames = new Dictionary<int, string>();
        var totals = new List<InvoiceInputStockBalance>();
        for (var i = 0; i < source.Count; i++)
        {
            var row = source[i];
            var key = (row.WarehouseId, row.ProductVariantId);
            if (!groupIds.TryGetValue(key, out var group))
            {
                group = totals.Count;
                groupIds.Add(key, group);
                totals.Add(new()
                {
                    WarehouseId = row.WarehouseId, ProductVariantId = row.ProductVariantId,
                    ProductName = row.ProductName, Code = row.Code, BaseUnit = row.BaseUnit,
                    WarehouseName = row.WarehouseName, LegalEntityName = row.LegalEntityName
                });
            }
            var balance = totals[group];
            if (row.IsOpening) balance.Opening += row.Change;
            else if (row.Change > 0) balance.Received += row.Change;
            else if (row.Change < 0) balance.Issued -= row.Change;
            balance.Held += row.Held;
            if (row.Kind == "increase") increases++;
            if (row.Kind == "decrease") decreases++;
            if (row.Kind == "hold") holds++;
            warehouseNames.TryAdd(row.WarehouseId, row.WarehouseName);
            entries[i] = new(row, group, i);
        }
        balances = totals.ToArray();
        negatives = balances.Count(x => x.Available < 0);
        balanceOrder = Enumerable.Range(0, balances.Length).OrderBy(i => balances[i].ProductName)
            .ThenBy(i => balances[i].WarehouseId).ToArray();
        warehouses = warehouseNames.Select(x => new InvoiceInputStockWarehouse(x.Key, x.Value)).OrderBy(x => x.Name).ToArray();
        // Sort once and calculate running stock before any date/type filters.
        // Repository movements stay untouched across concurrent requests.
        Array.Sort(entries, (left, right) =>
        {
            var order = left.Row.DateUtc.CompareTo(right.Row.DateUtc);
            if (order == 0) order = (left.Row.Kind == "increase" ? 0 : 1).CompareTo(right.Row.Kind == "increase" ? 0 : 1);
            return order != 0 ? order : StringComparer.Ordinal.Compare(left.Row.Key, right.Row.Key);
        });
        var running = new decimal[balances.Length];
        foreach (var entry in entries)
        {
            entry.Before = running[entry.Group];
            running[entry.Group] += entry.Row.Change;
            entry.After = running[entry.Group];
        }
        // Search normalization is deferred until the first keyword and then reused.
        search = new(PrepareSearch);
    }

    private SearchText[] PrepareSearch()
    {
        // Product names repeat across thousands of movements. Fold their
        // Vietnamese accents once, preserving spaces and the original combined-field search.
        var products = new Dictionary<(string Name, string Code), string>();
        var texts = new SearchText[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var row = entries[i].Row;
            var key = (row.ProductName, row.Code);
            if (!products.TryGetValue(key, out var product))
            {
                product = Normalize($"{row.ProductName} {row.Code}", trim: false);
                products.Add(key, product);
            }
            var reference = Normalize($"{row.SourceCode} {row.XmlSeries} · {row.XmlNumber} {row.LegacySourceKey}", trim: false);
            var xml = string.IsNullOrEmpty(row.XmlSeries) && string.IsNullOrEmpty(row.XmlNumber)
                ? "" : Compact(Normalize($"{row.XmlSeries}{row.XmlNumber}"));
            texts[i] = new((product + " " + reference).Trim(), xml);
        }
        return texts;
    }

    public InvoiceInputStockPage GetPage(InvoiceInputStockQuery request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var keyword = Normalize(request.Keyword);
        var compactKeyword = Compact(keyword);
        var filtered = request.WarehouseId.HasValue || request.ProductVariantId.HasValue || keyword.Length > 0;
        var from = request.FromDate?.Date.AddHours(-7);
        var until = request.ToDate?.Date.AddDays(1).AddHours(-7);
        var dated = from.HasValue || until.HasValue;
        var first = from.HasValue ? LowerBound(from.Value) : 0;
        var end = until.HasValue ? LowerBound(until.Value) : entries.Length;
        var pageSize = request.PageSize is 10 or 20 or 50 or 100 ? request.PageSize : 20;
        var ledger = request.View == "ledger";
        var page = new InvoiceInputStockPage { PageSize = pageSize, Warehouses = warehouses };
        bool[]? selectedGroups = filtered ? new bool[balances.Length] : null;
        bool[]? selectedEntries = filtered ? new bool[entries.Length] : null;
        var displayEntries = filtered ? Enumerable.Repeat(-1, balances.Length).ToArray() : null;
        var texts = keyword.Length > 0 ? search.Value : null;

        if (!filtered && !dated)
        {
            page.ProductCount = balances.Length; page.NegativeCount = negatives;
            page.IncreaseCount = increases; page.DecreaseCount = decreases; page.HoldCount = holds;
        }
        else
        {
            // Searching a document selects full product balances. Dates/kinds
            // affect ledger rows only, not the balances that precede them.
            for (var i = 0; i < entries.Length; i++)
            {
                if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                var row = entries[i].Row;
                if (filtered && ((request.WarehouseId.HasValue && row.WarehouseId != request.WarehouseId)
                    || (request.ProductVariantId.HasValue && row.ProductVariantId != request.ProductVariantId)
                    || (texts != null && !texts[i].Text.Contains(keyword, StringComparison.Ordinal)
                        && (compactKeyword.Length == 0 || !texts[i].Xml.Contains(compactKeyword, StringComparison.Ordinal)))))
                    continue;
                if (filtered)
                {
                    selectedEntries![i] = true;
                    selectedGroups![entries[i].Group] = true;
                    var group = entries[i].Group;
                    if (displayEntries![group] < 0 || entries[i].SourceIndex < entries[displayEntries[group]].SourceIndex)
                        displayEntries[group] = i;
                }
                if (i < first || i >= end) continue;
                if (row.Kind == "increase") page.IncreaseCount++;
                if (row.Kind == "decrease") page.DecreaseCount++;
                if (row.Kind == "hold") page.HoldCount++;
            }
            page.ProductCount = filtered ? selectedGroups!.Count(x => x) : balances.Length;
            page.NegativeCount = filtered
                ? Enumerable.Range(0, balances.Length).Count(i => selectedGroups![i] && balances[i].Available < 0)
                : negatives;
        }

        if (ledger)
        {
            bool Included(int index) => (selectedEntries == null || selectedEntries[index])
                && (string.IsNullOrEmpty(request.Kind) || entries[index].Row.Kind == request.Kind);
            if (!filtered && string.IsNullOrEmpty(request.Kind)) page.TotalItems = Math.Max(0, end - first);
            else for (var i = first; i < end; i++) if (Included(i)) page.TotalItems++;
            page.Page = Math.Clamp(request.Page, 1, page.TotalPages);
            var skip = (page.Page - 1) * pageSize;
            var rows = new List<InvoiceInputStockMovement>(pageSize);
            var start = end - 1;
            if (!filtered && string.IsNullOrEmpty(request.Kind)) { start -= skip; skip = 0; }
            for (var i = start; i >= first && rows.Count < pageSize; i--)
            {
                if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                if (!Included(i)) continue;
                if (skip > 0) { skip--; continue; }
                var entry = entries[i];
                rows.Add(entry.Row.WithBalance(entry.Before, entry.After));
            }
            page.Movements = rows;
        }
        else
        {
            bool Included(int group) => (selectedGroups == null || selectedGroups[group])
                && (request.Kind != "negative" || balances[group].Available < 0)
                && (request.Kind != "hold" || balances[group].Held > 0);
            var visibleBalances = displayEntries == null ? balanceOrder.Where(Included)
                : balanceOrder.Where(Included).OrderBy(i => entries[displayEntries[i]].Row.ProductName)
                    .ThenBy(i => balances[i].WarehouseId).ThenBy(i => entries[displayEntries[i]].SourceIndex);
            page.TotalItems = visibleBalances.Count();
            page.Page = Math.Clamp(request.Page, 1, page.TotalPages);
            page.Balances = visibleBalances.Skip((page.Page - 1) * pageSize).Take(pageSize)
                .Select(i => balances[i].Copy(displayEntries == null ? null : entries[displayEntries[i]].Row)).ToArray();
        }
        return page;
    }

    private int LowerBound(DateTime date)
    {
        var low = 0; var high = entries.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (entries[middle].Row.DateUtc < date) low = middle + 1;
            else high = middle;
        }
        return low;
    }
    private static string Compact(string value) => value.Replace(" ", "").Replace("·", "").Replace("/", "");
    private static string Normalize(string? value, bool trim = true)
    {
        var text = (trim ? (value ?? "").Trim() : value ?? "").ToLowerInvariant().Replace('đ', 'd');
        var ascii = true;
        foreach (var character in text) if (character > 127) { ascii = false; break; }
        if (ascii) return text;
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        return builder.ToString();
    }
    private sealed class Entry(InvoiceInputStockMovement row, int group, int sourceIndex)
    {
        public InvoiceInputStockMovement Row { get; } = row;
        public int Group { get; } = group;
        public int SourceIndex { get; } = sourceIndex;
        public decimal Before { get; set; }
        public decimal After { get; set; }
    }
    private sealed record SearchText(string Text, string Xml);
}
