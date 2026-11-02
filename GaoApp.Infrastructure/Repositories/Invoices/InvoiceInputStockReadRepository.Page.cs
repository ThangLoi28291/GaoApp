using System.Globalization;
using GaoApp.Application.DTOs.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public sealed partial class InvoiceInputStockReadRepository
{
    public async Task HydratePageAsync(int storeId, InvoiceInputStockPage page, CancellationToken ct = default)
    {
        var rows = page.Movements.Where(x => x.Key.StartsWith("supplemental-", StringComparison.Ordinal)).ToArray();
        if (rows.Length == 0) return;
        var ids = rows.Select(x => int.Parse(x.Key["supplemental-".Length..], CultureInfo.InvariantCulture)).ToArray();
        var notes = await db.InvoiceInputStockSupplementalMovements.AsNoTracking()
            .Where(x => x.StoreId == storeId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Note, x.SourcePeriod, x.LegacyOrderId, x.MovementType })
            .ToDictionaryAsync(x => x.Id, ct);
        var orders = notes.Values.Where(x => x.LegacyOrderId.HasValue).Select(x => x.LegacyOrderId!.Value).Distinct().ToArray();
        var heads = orders.Length == 0 ? new Dictionary<long, int>() : await db.InvoiceHeads.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.LegacySourceId.HasValue && orders.Contains(x.LegacySourceId.Value))
            .Select(x => new { SourceId = x.LegacySourceId!.Value, x.Id }).ToDictionaryAsync(x => x.SourceId, x => x.Id, ct);
        foreach (var row in rows)
        {
            var id = int.Parse(row.Key["supplemental-".Length..], CultureInfo.InvariantCulture);
            if (!notes.TryGetValue(id, out var note)) continue;
            var label = SupplementalLabel(note.MovementType);
            row.Note = string.IsNullOrWhiteSpace(note.Note) ? label ?? "" : $"{label}: {note.Note}";
            if (!string.IsNullOrWhiteSpace(note.SourcePeriod)) row.Note += $" (kỳ {note.SourcePeriod})";
            if (note.LegacyOrderId.HasValue && heads.TryGetValue(note.LegacyOrderId.Value, out var head)) row.InvoiceHeadId = head;
        }
    }
}
