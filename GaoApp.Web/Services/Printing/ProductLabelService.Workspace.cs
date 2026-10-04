using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Printing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Printing;

public sealed record LabelLineResolution(string RowVersion, Guid RequestId, List<int> VariantIds, bool Restore, string? Reason);
public sealed record LabelProgress(int Total, int Handled, int Skipped, int Pending);

public sealed partial class ProductLabelService
{
    public async Task<object> TaskState(int id, CancellationToken ct)
    {
        var version = await db.Set<ProductLabelTask>().AsNoTracking().Where(x => x.StoreId == StoreId && x.Id == id)
            .Select(x => x.RowVersion).SingleOrDefaultAsync(ct) ?? throw new NotFoundAppException("Không tìm thấy phiếu in tem.");
        var jobs = await db.Set<ProductLabelJob>().AsNoTracking().Where(x => x.StoreId == StoreId && x.TaskId == id)
            .OrderByDescending(x => x.Id).Select(x => new { x.Id, x.Status, x.RowVersion }).ToListAsync(ct);
        return new { rowVersion = Convert.ToBase64String(version), jobs };
    }

    private static LabelProgress Progress(List<LabelTaskLine> lines)
    {
        var current = lines.Where(x => !x.Removed).ToList();
        var handled = current.Count(x => x.Done);
        var skipped = current.Count(x => !x.Done && x.Skipped);
        return new(current.Count, handled, skipped, current.Count - handled - skipped);
    }

    private sealed record ReceiptInfo(string? DocumentTitle, string? SupplierName);
    private async Task<ReceiptInfo> ReceiptMetadata(int id, CancellationToken ct) =>
        await db.Set<StockDocument>().AsNoTracking().Where(x => x.StoreId == StoreId && x.Id == id)
            .Select(x => new ReceiptInfo(x.DocumentTitle, x.Supplier != null && x.Supplier.StoreId == StoreId ? x.Supplier.Name : null))
            .SingleOrDefaultAsync(ct) ?? new(null, null);

    private async Task<List<LabelTaskLine>> WithImages(List<LabelTaskLine> lines, CancellationToken ct)
    {
        var ids = lines.Select(x => x.Product.VariantId).ToList();
        var variants = await db.ProductVariants.AsNoTracking().Where(x => x.StoreId == StoreId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.ProductId }).ToListAsync(ct);
        var products = variants.Select(x => x.ProductId).ToList();
        var images = await db.Set<ProductImage>().AsNoTracking().Where(x => x.StoreId == StoreId && products.Contains(x.ProductId) && x.MediaAsset.StoreId == StoreId && !x.MediaAsset.IsDeleted)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.ProductId, x.MediaAsset.StoragePath }).ToListAsync(ct);
        return lines.Select(l => l with { Product = l.Product with { ImageUrl = images.FirstOrDefault(x => x.ProductId == variants.FirstOrDefault(v => v.Id == l.Product.VariantId)?.ProductId)?.StoragePath is { } path ? "/" + path.TrimStart('/') : null } }).ToList();
    }

    public async Task<object> Workspace(string? q, string? state, int page, CancellationToken ct)
    {
        q = q?.Trim().Replace("Đ", "D").Replace("đ", "d");
        if (q?.Length > 150) throw new ValidationAppException("Tìm kiếm tối đa 150 ký tự.");
        var query = db.Set<ProductLabelTask>().AsNoTracking().Where(x => x.StoreId == StoreId);
        if (!string.IsNullOrEmpty(q)) query = query.Where(x =>
            EF.Functions.Collate(x.DocumentNo, "Latin1_General_100_CI_AI").Contains(q) ||
            EF.Functions.Collate((x.StockDocument.DocumentTitle ?? "").Replace("Đ", "D").Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains(q) ||
            x.StockDocument.Supplier != null && x.StockDocument.Supplier.StoreId == StoreId && EF.Functions.Collate(x.StockDocument.Supplier.Name.Replace("Đ", "D").Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains(q));
        // The compact task snapshots supply product progress; no print payloads or catalog queries per row.
        var data = await query.OrderByDescending(x => x.Id).Select(x => new
        {
            x.Id, x.StockDocumentId, x.DocumentNo, x.CreatedAtUtc, x.LinesJson,
            x.StockDocument.DocumentTitle,
            supplierName = x.StockDocument.Supplier != null && x.StockDocument.Supplier.StoreId == StoreId ? x.StockDocument.Supplier.Name : null,
            sending = db.Set<ProductLabelJob>().Any(j => j.StoreId == StoreId && j.TaskId == x.Id && (j.Status == ProductLabelJobStatus.Queued || j.Status == ProductLabelJobStatus.Sending)),
            attention = db.Set<ProductLabelJob>().Any(j => j.StoreId == StoreId && j.TaskId == x.Id && (j.Status == ProductLabelJobStatus.NeedsAttention || j.Status == ProductLabelJobStatus.AwaitingConfirmation))
        }).ToListAsync(ct);
        var rows = data.Select(x =>
        {
            var progress = Progress(LabelJson.Read<List<LabelTaskLine>>(x.LinesJson));
            var status = x.attention ? "attention" : x.sending ? "sending" : progress.Total > 0 && progress.Pending == 0 ? "completed" : "pending";
            return new { x.Id, x.StockDocumentId, x.DocumentNo, x.DocumentTitle, x.supplierName, x.CreatedAtUtc, progress, state = status };
        }).ToList();
        var stats = new { total = rows.Count, pending = rows.Count(x => x.state != "completed"), sending = rows.Count(x => x.state == "sending"), completed = rows.Count(x => x.state == "completed"), attention = rows.Count(x => x.state == "attention") };
        var filtered = rows.Where(x => state == "all" || state is null or "pending" && x.state != "completed" || x.state == state).ToList();
        const int pageSize = 30;
        page = Math.Clamp(page, 1, Math.Max(1, (filtered.Count + pageSize - 1) / pageSize));
        return new { items = filtered.Skip((page - 1) * pageSize).Take(pageSize), stats, page, pageSize, total = filtered.Count };
    }

    public async Task<object> ResolveLines(int id, LabelLineResolution request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.VariantIds is null || request.VariantIds.Count is 0 or > 500 || request.VariantIds.Distinct().Count() != request.VariantIds.Count)
            throw new ValidationAppException("Chọn các sản phẩm cần xử lý.");
        var reason = request.Reason?.Trim() ?? "";
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock($"task:{id}", ct);
        var task = await TaskEntity(id, ct);
        var lines = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson);
        var previous = lines.SelectMany(l => l.Actions.Where(a => a.RequestId == request.RequestId).Select(a => new { l.Product.VariantId, Action = a })).ToList();
        if (previous.Count > 0)
        {
            if (previous.Count != request.VariantIds.Count || previous.Any(x => !request.VariantIds.Contains(x.VariantId) || x.Action.Action != (request.Restore ? "restore" : "skip") || x.Action.Reason != reason))
                throw new ConflictAppException("Mã yêu cầu đã dùng cho nội dung khác.");
            return await Detail(id, ct);
        }
        // A reused command ID is a conflict even when its changed payload is invalid.
        // Check idempotency under the task lock before validating a fresh command.
        if (reason.Length > 300 || !request.Restore && reason.Length == 0) throw new ValidationAppException("Chọn lý do bỏ qua, tối đa 300 ký tự.");
        if (!request.Restore && string.Equals(reason, "Khác", StringComparison.OrdinalIgnoreCase)) throw new ValidationAppException("Nhập ghi chú cụ thể khi chọn lý do Khác.");
        await Editable(task, request.RowVersion, ct);
        var source = await Source(task.StockDocumentId, ct);
        if (source.Hash != task.SourceHash || source.ProvisionalCount > 0) throw new ConflictAppException("Hoàn thiện sản phẩm và cập nhật từ phiếu nhập trước khi xử lý.");
        foreach (var variant in request.VariantIds)
        {
            var index = lines.FindIndex(x => x.Product.VariantId == variant && !x.Removed);
            if (index < 0) throw new ValidationAppException("Sản phẩm không thuộc phiếu hiện tại.");
            var line = lines[index];
            if (request.Restore ? !line.Skipped : line.Done || line.Skipped) throw new ConflictAppException("Trạng thái sản phẩm đã thay đổi. Tải lại phiếu.");
            var action = new LabelLineAction(request.RequestId, request.Restore ? "restore" : "skip", reason, db.CurrentUserId, db.CurrentUserName ?? "Nhân viên", DateTime.UtcNow, line.Product.Name);
            lines[index] = line with { Skipped = !request.Restore, Actions = [..line.Actions, action] };
        }
        task.LinesJson = LabelJson.Write(lines); LabelTaskProgress.CompleteIfHandled(task, db.CurrentUserId);
        await Save(ct); await tx.CommitAsync(ct); return await Detail(id, ct);
    }
}
