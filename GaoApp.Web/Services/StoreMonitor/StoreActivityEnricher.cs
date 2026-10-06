using System.Globalization;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Printing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GaoApp.Web.Services.StoreMonitor;

/// <summary>Read-only, store-scoped facts for the activity wall. Never copy notes, photos, customer data or request prices.</summary>
public static class StoreActivityEnricher
{
    public sealed record Item(string Name, decimal Quantity, string? Unit, decimal? DifferenceBase = null, decimal? SavedCost = null);
    public sealed record Snapshot(int Parent, string? Number, string? Location, Item? Line, int LineCount = 0, string? Goods = null, DateTime? Date = null, decimal? Freight = null);
    internal sealed record SavedLine(int Parent, Item Item, int? UnitId = null);
    public static bool NeedsSavedLine(string controller, string action) =>
        controller is "StockDocuments" or "StockCounts" or "StockTransfers" &&
        action is "AddLine" or "AddLineByBarcode" or "UpdateLine";

    /// <summary>Request-local copies taken at SaveChanges, never a later reader's mutable line.</summary>
    public sealed class SavedOperation : IDisposable
    {
        private readonly AppDbContext db;
        private readonly int store;
        private readonly bool includeBefore;
        private sealed record PendingLine(object Entity, SavedLine? Before);
        private PendingLine[] pending = [];
        private readonly Dictionary<(string Controller, int Id), SavedLine> lines = [];
        private readonly Dictionary<(string Controller, int Id), SavedLine> originals = [];
        public SavedOperation(AppDbContext db, int store, bool includeBefore = false)
        {
            this.db = db; this.store = store; this.includeBefore = includeBefore;
            db.SavingChanges += Saving;
            db.SavedChanges += Saved;
            db.SaveChangesFailed += Failed;
        }
        private void Saving(object? sender, SavingChangesEventArgs args)
        {
            pending = db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified &&
                x.Entity is StockDocumentLine or StockCountLine or StockTransferLine)
                .Select(x => new PendingLine(x.Entity, includeBefore && x.State == EntityState.Modified ? Original(x) : null)).ToArray();
        }
        private SavedLine? Original(EntityEntry entry)
        {
            var values = entry.OriginalValues;
            return entry.Entity switch
            {
                StockDocumentLine line when line.StockDocument?.StoreId == store =>
                    new(values.GetValue<int>(nameof(StockDocumentLine.StockDocumentId)),
                        new(values.GetValue<string>(nameof(StockDocumentLine.ProductNameSnapshot)),
                            values.GetValue<decimal>(nameof(StockDocumentLine.Quantity)),
                            values.GetValue<string?>(nameof(StockDocumentLine.UnitNameSnapshot)), null,
                            values.GetValue<decimal>(nameof(StockDocumentLine.UnitCost))),
                        values.GetValue<int?>(nameof(StockDocumentLine.UnitId))),
                StockCountLine line when line.StockCountDocument?.StoreId == store && values.GetValue<int>(nameof(StockCountLine.StoreId)) == store =>
                    new(values.GetValue<int>(nameof(StockCountLine.StockCountDocumentId)),
                        new(values.GetValue<string>(nameof(StockCountLine.ProductNameSnapshot)),
                            values.GetValue<decimal>(nameof(StockCountLine.CountedQty)),
                            values.GetValue<string?>(nameof(StockCountLine.UnitNameSnapshot)),
                            values.GetValue<decimal>(nameof(StockCountLine.DifferenceQtyBase))),
                        values.GetValue<int>(nameof(StockCountLine.UnitId))),
                StockTransferLine line when line.StockTransferDocument?.StoreId == store && values.GetValue<int>(nameof(StockTransferLine.StoreId)) == store =>
                    new(values.GetValue<int>(nameof(StockTransferLine.StockTransferDocumentId)),
                        new(values.GetValue<string>(nameof(StockTransferLine.ProductNameSnapshot)),
                            values.GetValue<decimal>(nameof(StockTransferLine.Quantity)),
                            values.GetValue<string?>(nameof(StockTransferLine.UnitNameSnapshot))),
                        values.GetValue<int>(nameof(StockTransferLine.UnitId))),
                _ => null
            };
        }
        private void Remember(string controller, int id, SavedLine after, SavedLine? before)
        {
            var key = (controller, id);
            // Multiple successful saves belong to one action: retain its first original and last saved value.
            if (!lines.ContainsKey(key) && before is not null && before.Parent == after.Parent) originals[key] = before;
            lines[key] = after;
        }
        private void Saved(object? sender, SavedChangesEventArgs args)
        {
            foreach (var change in pending)
            {
                switch (change.Entity)
                {
                    case StockDocumentLine line when !line.IsDeleted && line.StockDocument?.StoreId == store:
                        Remember("StockDocuments", line.Id, new(line.StockDocumentId, new(line.ProductNameSnapshot,
                            line.Quantity, line.UnitNameSnapshot, null, line.UnitCost), line.UnitId), change.Before);
                        break;
                    case StockCountLine line when !line.IsDeleted && line.StoreId == store && line.StockCountDocument?.StoreId == store:
                        Remember("StockCounts", line.Id, new(line.StockCountDocumentId, new(line.ProductNameSnapshot,
                            line.CountedQty, line.UnitNameSnapshot, line.DifferenceQtyBase), line.UnitId), change.Before);
                        break;
                    case StockTransferLine line when !line.IsDeleted && line.StoreId == store && line.StockTransferDocument?.StoreId == store:
                        Remember("StockTransfers", line.Id, new(line.StockTransferDocumentId, new(line.ProductNameSnapshot,
                            line.Quantity, line.UnitNameSnapshot), line.UnitId), change.Before);
                        break;
                }
            }
            pending = [];
        }
        private void Failed(object? sender, SaveChangesFailedEventArgs args) => pending = [];
        internal SavedLine? Read(string controller, int id, int? parent) =>
            lines.TryGetValue((controller, id), out var line) && (!parent.HasValue || line.Parent == parent) ? line : null;
        public async Task<Item?> Before(string controller, int id, int parent, CancellationToken ct)
        {
            if (Read(controller, id, parent) is null || !originals.TryGetValue((controller, id), out var line) || line.Parent != parent) return null;
            return (await ResolveUnit(db, store, line, ct))?.Item;
        }
        public void Dispose()
        {
            db.SavingChanges -= Saving; db.SavedChanges -= Saved; db.SaveChangesFailed -= Failed;
            pending = [];
        }
    }
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static string N(decimal value) => value.ToString("#,0.###", Vietnamese);
    private static string Q(Item item) => N(item.Quantity) + (string.IsNullOrWhiteSpace(item.Unit) ? "" : " " + item.Unit);
    private static string Name(string? value) => string.IsNullOrWhiteSpace(value) ? "mặt hàng chưa có tên" : value.Length > 65 ? value[..64] + "…" : value;
    private static object? P(object? value, string name) => StoreActivityDetails.Property(value, name);
    private static int? Id(object? value) => value is int i && i > 0 ? i : null;
    private static int? Arg(IDictionary<string, object?> args, string name) => args.TryGetValue(name, out var value) ? Id(value) : null;
    private static async Task<SavedLine?> ResolveUnit(AppDbContext db, int store, SavedLine? saved, CancellationToken ct)
    {
        if (saved?.UnitId is { } unitId && string.IsNullOrEmpty(saved.Item.Unit))
            saved = saved with { Item = saved.Item with { Unit = await db.Units.AsNoTracking()
                .Where(x => x.Id == unitId && x.StoreId == store).Select(x => x.Name).SingleOrDefaultAsync(ct) } };
        return saved;
    }
    public static object? Request(IDictionary<string, object?> args) => args.TryGetValue("request", out var request) ? request : args.TryGetValue("dto", out var dto) ? dto : null;
    public static bool NeedsBefore(string controller, string action) => controller is "StockDocuments" or "StockCounts" or "StockTransfers"
        ? action is "UpdateLine" or "DeleteLine" or "UpdateHeader" : controller == "StockDocumentManagement" && action == "UpdateHeader" || controller == "ReceiptIntake" && action is "Quantity" or "Remove" or "Review";

    public static async Task<Snapshot?> Read(AppDbContext db, int store, string controller, string action,
        IDictionary<string, object?> args, object? payload, Snapshot? before, CancellationToken ct, SavedOperation? operation = null)
    {
        if (controller is not ("StockDocuments" or "StockDocumentManagement" or "WarehouseReceiving" or "ReceiptIntake" or "StockCounts" or "StockTransfers")) return null;
        var request = Request(args);
        var parent = Arg(args, "documentId") ?? Arg(args, "stockDocumentId") ?? Arg(args, "stockCountDocumentId") ?? Arg(args, "id") ??
            Id(P(request, "stockDocumentId")) ?? Id(P(request, "stockCountDocumentId")) ?? Id(P(payload, "stockDocumentId")) ?? before?.Parent;
        if (action is "Create" or "CreateReceipt") parent = Id(P(payload, "id"));
        var lineId = action is "AddLine" or "AddLineByBarcode" ? Id(P(payload, "lineId")) ?? Id(P(payload, "id")) : Arg(args, "lineId");
        SavedLine? saved = null;
        if (operation is not null && NeedsSavedLine(controller, action))
        {
            saved = lineId is { } savedId ? operation.Read(controller, savedId, parent) : null;
            // Legacy lines may lack the unit-name snapshot. Resolve only the captured unit ID,
            // never a later line's quantity, unit selection, name or price.
            saved = await ResolveUnit(db, store, saved, ct);
        }
        else if (controller == "StockDocuments" && lineId is { } receiptLine)
            saved = await db.StockDocumentLines.AsNoTracking().Where(x => x.Id == receiptLine && x.StockDocument.StoreId == store && (!parent.HasValue || x.StockDocumentId == parent))
                .Select(x => new SavedLine(x.StockDocumentId, new Item(x.ProductNameSnapshot, x.Quantity,
                    !string.IsNullOrEmpty(x.UnitNameSnapshot) ? x.UnitNameSnapshot : x.UnitId != null ? x.Unit!.Name : x.Factor == 1 ? x.ProductVariant.Product.BaseUnit.Name : null, null, x.UnitCost), null)).SingleOrDefaultAsync(ct);
        if (operation is null && controller == "StockCounts" && lineId is { } countLine)
            saved = await db.StockCountLines.AsNoTracking().Where(x => x.Id == countLine && x.StoreId == store && x.StockCountDocument.StoreId == store && (!parent.HasValue || x.StockCountDocumentId == parent))
                .Select(x => new SavedLine(x.StockCountDocumentId, new Item(x.ProductNameSnapshot, x.CountedQty, !string.IsNullOrEmpty(x.UnitNameSnapshot) ? x.UnitNameSnapshot : x.Unit.Name, x.DifferenceQtyBase, null), null)).SingleOrDefaultAsync(ct);
        if (operation is null && controller == "StockTransfers" && lineId is { } transferLine)
            saved = await db.StockTransferLines.AsNoTracking().Where(x => x.Id == transferLine && x.StoreId == store && x.StockTransferDocument.StoreId == store && (!parent.HasValue || x.StockTransferDocumentId == parent))
                .Select(x => new SavedLine(x.StockTransferDocumentId, new Item(x.ProductNameSnapshot, x.Quantity,
                    !string.IsNullOrEmpty(x.UnitNameSnapshot) ? x.UnitNameSnapshot : db.Units.Where(u => u.Id == x.UnitId && u.StoreId == store).Select(u => u.Name).FirstOrDefault(), null, null), null)).SingleOrDefaultAsync(ct);
        if (controller == "ReceiptIntake" && Arg(args, "itemId") is { } itemId)
            saved = await db.StockDocumentProvisionalItems.AsNoTracking().Where(x => x.Id == itemId && x.StoreId == store && x.StockDocument.StoreId == store && (!parent.HasValue || x.StockDocumentId == parent))
                .Select(x => new SavedLine(x.StockDocumentId, new Item(x.NameSnapshot, x.Quantity, x.UnitNameSnapshot, null, null), null)).SingleOrDefaultAsync(ct);
        parent = saved?.Parent ?? parent;
        if (parent is not { } documentId) return null;
        // A missing line is never resolved through a caller-supplied product name or variant ID.
        Item? item = saved?.Item;
        if (controller == "ReceiptIntake" && payload is ProvisionalReceivingStateDto state && state.StockDocumentId == documentId)
        {
            if (action is "Capture" or "Known" && P(request, "commandId") is Guid command)
            {
                var recent = state.RecentReceipts?.FirstOrDefault(x => x.CommandId == command);
                if (recent is not null) item = new(recent.Name, recent.CurrentQuantity, recent.UnitName);
            }
            else if (Arg(args, "itemId") is { } knownItem)
            {
                var row = state.Items.FirstOrDefault(x => x.Id == knownItem);
                if (row is not null) item = new(row.Name, row.Quantity, row.UnitName);
            }
        }
        if (controller is "StockDocuments" or "StockDocumentManagement" or "WarehouseReceiving" or "ReceiptIntake")
        {
            var header = await db.StockDocuments.AsNoTracking().Where(x => x.Id == documentId && x.StoreId == store)
                .Select(x => new { x.DocumentNo, x.DocumentDate, x.FreightTotal, Location = x.Warehouse.StoreId == store ? x.Warehouse.Name : null }).SingleOrDefaultAsync(ct);
            if (header is null) return null;
            var lines = db.StockDocumentLines.AsNoTracking().Where(x => x.StockDocumentId == documentId && x.StockDocument.StoreId == store);
            if (action == "SavePriceDraft" && payload is ReceiptPriceDraftResult prices)
            {
                var savedIds = prices.LineVersions.Keys.ToArray();
                lines = lines.Where(x => savedIds.Contains(x.Id));
            }
            return new(documentId, header.DocumentNo, header.Location, item, await lines.CountAsync(ct), Goods(await lines.OrderBy(x => x.LineNo).Select(x => x.ProductNameSnapshot).Take(2).ToArrayAsync(ct)), header.DocumentDate, header.FreightTotal);
        }
        if (controller == "StockCounts")
        {
            var header = await db.StockCountDocuments.AsNoTracking().Where(x => x.Id == documentId && x.StoreId == store)
                .Select(x => new { x.DocumentNo, x.DocumentDate, Location = x.Warehouse.StoreId == store ? x.Warehouse.Name : null }).SingleOrDefaultAsync(ct);
            if (header is null) return null;
            var lines = db.StockCountLines.AsNoTracking().Where(x => x.StockCountDocumentId == documentId && x.StoreId == store);
            return new(documentId, header.DocumentNo, header.Location, item, await lines.CountAsync(ct), Goods(await lines.OrderBy(x => x.LineNo).Select(x => x.ProductNameSnapshot).Take(2).ToArrayAsync(ct)), header.DocumentDate);
        }
        var transfer = await db.StockTransferDocuments.AsNoTracking().Where(x => x.Id == documentId && x.StoreId == store)
            .Select(x => new { x.DocumentNo, x.DocumentDate, From = x.FromWarehouse.StoreId == store ? x.FromWarehouse.Name : null, To = x.ToWarehouse.StoreId == store ? x.ToWarehouse.Name : null }).SingleOrDefaultAsync(ct);
        if (transfer is null) return null;
        var transferLines = db.StockTransferLines.AsNoTracking().Where(x => x.StockTransferDocumentId == documentId && x.StoreId == store);
        return new(documentId, transfer.DocumentNo, $"{transfer.From} → {transfer.To}", item, await transferLines.CountAsync(ct), Goods(await transferLines.OrderBy(x => x.LineNo).Select(x => x.ProductNameSnapshot).Take(2).ToArrayAsync(ct)), transfer.DocumentDate);
    }
    private static string? Goods(IEnumerable<string> names) => string.Join(", ", names.Select(Name));

    public static StoreActivityDetails.Description Describe(string controller, string action, Snapshot after, Snapshot? before = null, object? request = null)
    {
        var kind = controller switch { "StockCounts" => "count", "StockTransfers" => "transfer", _ => "receipt" };
        var caption = kind switch { "count" => "Kiểm kê", "transfer" => "Chuyển kho", _ => "Phiếu nhập" };
        var item = action is "DeleteLine" or "Remove" ? before?.Line : after.Line;
        string? text = null; var parts = new List<string>();
        if (item is not null)
        {
            var verb = (controller, action) switch
            {
                ("StockCounts", "AddLine" or "UpdateLine") => "vừa đếm",
                ("StockCounts", "DeleteLine") => "vừa bỏ khỏi kiểm kê",
                ("StockTransfers", "AddLine") => "vừa thêm hàng chuyển kho",
                ("StockTransfers", "UpdateLine") => "vừa sửa lượng chuyển kho",
                ("StockTransfers", "DeleteLine") => "vừa bỏ hàng chuyển kho",
                (_, "DeleteLine" or "Remove") => "vừa bỏ khỏi phiếu nhập",
                (_, "AddLineByBarcode" or "Known") => "vừa quét nhập",
                (_, "AddLine" or "Capture") => "vừa thêm vào phiếu nhập",
                ("ReceiptIntake", "Review") when P(request, "saveDraftOnly") is true => "vừa lưu thông tin chờ duyệt",
                ("ReceiptIntake", "Review") when P(request, "approve") is true => "vừa duyệt mặt hàng",
                ("ReceiptIntake", "Review") => "vừa bổ sung thông tin mặt hàng",
                (_, "UpdateLine" or "Quantity") => "vừa sửa dòng nhập",
                _ => null
            };
            if (verb is not null) text = (kind, action) switch
            {
                ("receipt", "AddLine" or "Capture") => $"vừa thêm {Name(item.Name)} vào phiếu nhập",
                ("receipt", "DeleteLine" or "Remove") => $"vừa bỏ {Name(item.Name)} khỏi phiếu nhập",
                ("count", "DeleteLine") => $"vừa bỏ {Name(item.Name)} khỏi kiểm kê",
                ("transfer", "AddLine") => $"vừa thêm {Name(item.Name)} vào phiếu chuyển kho",
                ("transfer", "DeleteLine") => $"vừa bỏ {Name(item.Name)} khỏi phiếu chuyển kho",
                _ => $"{verb} {Name(item.Name)}"
            };
            var label = kind == "count" ? "Đã đếm" : action is "DeleteLine" or "Remove" ? "Đã bỏ" : "Số lượng";
            parts.Add(before?.Line is { } old && action is "UpdateLine" or "Quantity"
                ? $"{label} {Q(old)} → {Q(item)}" : $"{label} {Q(item)}");
            if (action == "UpdateLine" && before?.Line?.SavedCost is { } oldCost && item.SavedCost is { } newCost && oldCost != newCost)
                parts.Add("Giá nhập đã thay đổi");
            if (kind == "count" && action != "DeleteLine" && item.DifferenceBase is { } difference)
                parts.Add($"Chênh lệch {N(difference)} ĐV gốc");
        }
        else
        {
            var verb = action switch
            {
                "SavePriceDraft" => "vừa lưu giá nháp",
                "SavePricingAllocation" => "vừa lưu phân bổ giá",
                "ApplyPricingAllocation" => "vừa áp dụng phân bổ giá",
                "UpdateHeader" => "vừa sửa thông tin",
                "UpdateFreight" => "vừa lưu cước vận chuyển",
                "Create" or "CreateReceipt" => "vừa tạo",
                "SubmitApproval" or "SubmitForApproval" or "Submit" => "vừa gửi chờ duyệt",
                "Approve" or "ApproveCommercial" => "vừa duyệt",
                "Confirm" => "vừa xác nhận",
                "Reject" => "vừa từ chối",
                _ => null
            };
            if (verb is not null) text = $"{verb} {caption.ToLower(Vietnamese)} {Name(string.IsNullOrWhiteSpace(after.Number) ? $"#{after.Parent}" : after.Number)}";
            parts.Add($"{after.LineCount} mặt hàng" + (after.LineCount > 0 && !string.IsNullOrEmpty(after.Goods) ? $": {after.Goods}" + (after.LineCount > 2 ? ", …" : "") : ""));
            if (action == "UpdateHeader" && before is not null)
            {
                if (before.Location != after.Location) parts.Insert(0, $"Đổi kho: {before.Location} → {after.Location}");
                if (before.Date?.Date != after.Date?.Date) parts.Insert(0, $"Ngày phiếu: {before.Date:dd/MM/yyyy} → {after.Date:dd/MM/yyyy}");
            }
            if (action == "UpdateFreight" && after.Freight is { } freight) parts.Insert(0, $"Cước {N(freight)} ₫");
        }
        if (!string.IsNullOrWhiteSpace(after.Location)) parts.Add(after.Location);
        if (!string.IsNullOrWhiteSpace(after.Number)) parts.Add(after.Number);
        return new($"{caption} #{after.Parent}", string.Join(" · ", parts), $"{kind}:{after.Parent}", text);
    }

    public static async Task<StoreActivityDetails.Description?> Label(AppDbContext db, int store, string action, IDictionary<string, object?> args, object? payload, CancellationToken ct)
    {
        var id = action == "Print" ? Id(P(payload, "id")) : Arg(args, "id");
        if (id is null) return null;
        if (action is "Print" or "Confirm" or "Cancel")
        {
            var job = await db.Set<ProductLabelJob>().AsNoTracking().Where(x => x.Id == id && x.StoreId == store)
                .Select(x => new { x.TaskId, x.PayloadJson, x.ResultJson }).SingleOrDefaultAsync(ct);
            if (job is null) return null;
            return DescribeLabelJob(action, id.Value, job.TaskId, LabelJson.Read<LabelPrintPayload>(job.PayloadJson).Items, LabelJson.Read<List<LabelQuantity>>(job.ResultJson));
        }
        var task = await db.Set<ProductLabelTask>().AsNoTracking().Where(x => x.Id == id && x.StoreId == store)
            .Select(x => new { x.DocumentNo, x.LinesJson }).SingleOrDefaultAsync(ct);
        if (task is null) return null;
        var lines = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson).Where(x => !x.Removed).ToArray();
        var required = lines.Sum(x => (long)x.Required); var printed = lines.Sum(x => (long)x.Printed);
        var text = action == "Complete" ? "vừa hoàn tất phiếu tem" : "vừa lập kế hoạch in tem";
        var perItem = string.Join(", ", lines.Take(2).Select(x => action == "Complete" ? $"{Name(x.Product.Name)}: {x.Printed}/{x.Required} tem" : $"{Name(x.Product.Name)}: {x.Required} tem"));
        return new($"Phiếu tem #{id}", $"{lines.Length} mặt hàng · {perItem}" + (lines.Length > 2 ? ", …" : "") + $" · Kế hoạch {required} tem · Đã xác nhận {printed} tem · {task.DocumentNo}", $"label:{id}", text);
    }
    public static StoreActivityDetails.Description DescribeLabelJob(string action, int id, int? taskId, IReadOnlyList<LabelPrintItem> items, IReadOnlyList<LabelQuantity> results)
    {
        var planned = items.Sum(x => (long)x.Quantity);
        // ResultJson is the accepted confirmation, including partial or zero prints, not the original plan.
        var actual = results.Sum(x => (long)x.Quantity);
        var verb = action switch { "Confirm" => $"vừa xác nhận in {actual}/{planned} tem", "Cancel" => $"vừa hủy lệnh {planned} tem", _ => $"vừa gửi in {planned} tem" };
        var details = action == "Confirm" ? results.Select(x => new { Name = items.FirstOrDefault(i => i.Product.VariantId == x.VariantId && i.Product.UnitId == x.UnitId)?.Product.Name, x.Quantity })
            : items.Select(x => new { Name = (string?)x.Product.Name, x.Quantity });
        return new($"Lệnh in #{id}", $"{items.Count} mặt hàng · " + string.Join(", ", details.Take(2).Select(x => $"{Name(x.Name)}: {x.Quantity} tem")) + (items.Count > 2 ? ", …" : ""),
            taskId is { } parent ? $"label:{parent}" : $"label-job:{id}", $"{verb} · {Goods(items.Select(x => x.Product.Name).Take(2))}");
    }
}
