using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.StoreMonitor;

public sealed record StorePosScan(int LineId, string ItemName, string? UnitName,
    decimal AddedQuantity, decimal Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>Request-local saved scan facts, published only after the POS transaction succeeds.</summary>
public sealed class StoreCheckoutScanCapture : IDisposable
{
    private readonly AppDbContext db;
    private readonly int store;
    private readonly Dictionary<OrderLine, decimal> before = [];
    private readonly Dictionary<OrderLine, Saved> saved = [];
    private OrderLine[] pending = [];
    private bool failed;
    private sealed record Saved(int Order, int Line, int Variant, int? Conversion, string? Barcode,
        string Name, string? Unit, decimal Quantity, decimal Price, decimal Total);
    public static bool Applies(string controller, string action) => controller == "POS" &&
        action is "AddItem" or "AddByBarcode" or "ScanToCurrentCart";
    public StoreCheckoutScanCapture(AppDbContext db, int store)
    {
        this.db = db; this.store = store;
        db.SavingChanges += Saving; db.SavedChanges += SavedChanges; db.SaveChangesFailed += Failed;
    }
    private void Saving(object? sender, SavingChangesEventArgs args)
    {
        pending = db.ChangeTracker.Entries<OrderLine>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified && e.Entity.StoreId == store && !e.Entity.IsPromotionGift)
            .Select(e =>
            {
                before.TryAdd(e.Entity, e.State == EntityState.Added ? 0 : e.OriginalValues.GetValue<decimal>(nameof(OrderLine.Quantity)));
                return e.Entity;
            }).ToArray();
    }
    private void SavedChanges(object? sender, SavedChangesEventArgs args)
    {
        foreach (var line in pending)
            if (!line.IsDeleted && !line.IsPromotionGift && line.Id > 0 && line.StoreId == store)
                saved[line] = new(line.OrderId, line.Id, line.VariantId, line.ProductUnitConversionId,
                    line.ScannedBarcode, line.ItemName, line.SellingUnitName ?? line.UnitName, line.Quantity, line.UnitPrice, line.LineTotal);
        pending = [];
    }
    private void Failed(object? sender, SaveChangesFailedEventArgs args) { failed = true; pending = []; saved.Clear(); }
    public StorePosScan? Read(IDictionary<string, object?> arguments, object? payload)
    {
        if (failed || (StoreActivityDetails.Property(payload, "draft") ?? payload) is not OrderDraftDto draft) return null;
        object? Arg(string name) => arguments.TryGetValue(name, out var value) ? value : null;
        var variant = Arg("variantId") as int?;
        var conversion = Arg("productUnitConversionId") as int?;
        var barcode = (Arg("barcode") as string ?? StoreActivityDetails.Property(Arg("request"), "barcode") as string)?.Trim();
        var matches = saved.Where(x => x.Value.Order == draft.OrderId && x.Value.Quantity > before[x.Key] &&
            (!variant.HasValue || x.Value.Variant == variant) && (!conversion.HasValue || x.Value.Conversion == conversion) &&
            (barcode is null || x.Value.Barcode == barcode) && draft.Lines.Any(l => l.LineId == x.Value.Line && !l.IsPromotionGift)).ToArray();
        if (matches.Length != 1) return null; // Never guess from cart row order or a gift/repricing line.
        var value = matches[0].Value;
        return new(value.Line, Clean(value.Name, 160)!, Clean(value.Unit, 30), value.Quantity - before[matches[0].Key],
            value.Quantity, value.Price, value.Total);
    }
    private static string? Clean(string? value, int limit) => value is null ? null :
        new string(value.Where(c => !char.IsControl(c)).Take(limit).ToArray());
    public void Dispose()
    {
        db.SavingChanges -= Saving; db.SavedChanges -= SavedChanges; db.SaveChangesFailed -= Failed;
    }
}
