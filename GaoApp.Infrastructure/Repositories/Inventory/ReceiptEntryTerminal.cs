using GaoApp.Application.Interfaces.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

internal static class ReceiptEntryTerminal
{
    internal static async Task CaptureAsync(AppDbContext db, ICurrentPOSContext? pos,
        StockDocument document, CancellationToken ct)
    {
        if (document.Type != StockDocumentType.Receipt) return;
        // Only server-resolved context is authoritative, including on split/PO creation.
        document.EntryTerminalId = null;
        document.EntryTerminalName = null;
        document.EntryTerminalCode = null;
        if (pos?.IsAvailable != true || pos.StoreId != db.CurrentStoreId ||
            (document.StoreId > 0 && document.StoreId != pos.StoreId)) return;
        var terminal = await db.POSTerminals.AsNoTracking()
            .Where(t => t.StoreId == pos.StoreId && t.Id == pos.TerminalId && !t.IsDeleted)
            .Select(t => new { t.Id, t.Name, t.Code }).SingleOrDefaultAsync(ct);
        if (terminal is null) return;
        document.EntryTerminalId = terminal.Id;
        document.EntryTerminalName = terminal.Name;
        document.EntryTerminalCode = terminal.Code;
    }
}
