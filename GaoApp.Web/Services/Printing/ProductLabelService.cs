using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Printing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Printing;

public sealed record SaveLabelTemplate(ProductLabelDesign Design, string? RowVersion);
public sealed record SaveLabelPrinter
{
    [Required, StringLength(100)] public string Name { get; init; } = "";
    [Required, StringLength(220)] public string WindowsPrinterName { get; init; } = "";
    public int Dpi { get; init; } = 203;
    [Range(20, 108)] public decimal PrintableWidthMm { get; init; } = 108;
    [Range(-5, 5)] public decimal OffsetXmm { get; init; }
    [Range(-5, 5)] public decimal OffsetYmm { get; init; }
    public bool Enabled { get; init; } = true;
    public string? RowVersion { get; init; }
}
public sealed record LabelVersion(string RowVersion);
public sealed record LabelPlanRequest(int TemplateId, List<LabelQuantity> Lines, string RowVersion);
public sealed record LabelJobRequest(int? TaskId, int TemplateId, int PrinterId, List<LabelQuantity> Lines,
    Guid RequestId, string? RowVersion, string? TemplateVersion, bool IsReprint = false, string? Reason = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ProductProgress { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<QuickLabelSelection>? QuickLines { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<LabelQuantity>? PlanLines { get; init; }
}
public sealed record LabelConfirmRequest(string RowVersion, List<LabelQuantity> Lines, string? Note);

public sealed partial class ProductLabelService(AppDbContext db)
{
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ConflictAppException("Chưa xác định cửa hàng.");
    private static string Version(BaseEntity x) => Convert.ToBase64String(x.RowVersion);
    private static string Hash<T>(T x) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LabelJson.Write(x))));
    private static void CheckVersion(BaseEntity entity, string? version)
    {
        if (Version(entity) != version) throw new ConflictAppException("Dữ liệu đã thay đổi ở máy khác. Tải lại trước khi tiếp tục.");
    }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Dữ liệu vừa thay đổi. Tải lại trước khi tiếp tục."); }
    }
    private async Task Lock(string key, CancellationToken ct)
    {
        string resource = $"gao-label:{StoreId}:{key}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @r int;
            EXEC @r = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @r < 0 THROW 51001, 'Label operation is busy. Please retry.', 1;
            """, ct);
    }

    public async Task<object> Templates(CancellationToken ct) => (await db.Set<ProductLabelTemplate>().AsNoTracking()
        .Where(x => x.StoreId == StoreId).OrderBy(x => x.Name).ToListAsync(ct)).Select(TemplateDto).ToArray();
    // Apply the current policy to templates, never rewrite an existing job's immutable payload.
    private static ProductLabelDesign CurrentDesign(ProductLabelTemplate x) => LabelJson.Read<ProductLabelDesign>(x.DefinitionJson) with { BarcodeFormat = "AUTO" };
    private static object TemplateDto(ProductLabelTemplate x)
    {
        var design = CurrentDesign(x);
        return new { x.Id, design, layoutName = ProductLabelLayouts.All.FirstOrDefault(l => l.Key == design.Layout)?.Name, rowVersion = Version(x) };
    }
    private async Task<ProductLabelTemplate> Template(int id, CancellationToken ct) =>
        await db.Set<ProductLabelTemplate>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == id, ct)
        ?? throw new NotFoundAppException("Không tìm thấy mẫu tem.");
    public async Task<object> SaveTemplate(int? id, SaveLabelTemplate request, CancellationToken ct)
    {
        if (request.Design is null) throw new ValidationAppException("Chưa có nội dung mẫu.");
        request.Design.Validate();
        var entity = id.HasValue ? await Template(id.Value, ct) : new ProductLabelTemplate { StoreId = StoreId };
        if (id.HasValue) CheckVersion(entity, request.RowVersion); else db.Add(entity);
        if (request.Design.PrinterId is not > 0) throw new ValidationAppException("Chọn máy in gắn với mẫu tem trước khi lưu.");
        var printer = await Printer(request.Design.PrinterId.Value, ct);
        request.Design.Validate(printer.PrintableWidthMm);
        entity.Name = request.Design.Name.Trim();
        entity.DefinitionJson = LabelJson.Write(request.Design with { Name = entity.Name, BarcodeFormat = "AUTO" });
        await Save(ct); return TemplateDto(entity);
    }
    public async Task DeleteTemplate(int id, string version, CancellationToken ct)
    {
        var entity = await Template(id, ct); CheckVersion(entity, version); db.Remove(entity); await Save(ct);
    }
    public async Task<object> Printers(CancellationToken ct) => (await db.Set<ProductLabelPrinter>().AsNoTracking()
        .Where(x => x.StoreId == StoreId).OrderBy(x => x.Name).ToListAsync(ct)).Select(PrinterDto).ToArray();
    private static object PrinterDto(ProductLabelPrinter x) => new { x.Id, x.Name, x.WindowsPrinterName, x.Dpi, x.PrintableWidthMm, x.OffsetXmm, x.OffsetYmm, x.Enabled, x.LastSeenAtUtc, rowVersion = Version(x) };
    private async Task<ProductLabelPrinter> Printer(int id, CancellationToken ct) =>
        await db.Set<ProductLabelPrinter>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == id, ct)
        ?? throw new NotFoundAppException("Không tìm thấy máy in.");
    public async Task<object> SavePrinter(int? id, SaveLabelPrinter request, CancellationToken ct)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.WindowsPrinterName) || request.Dpi is not (203 or 300))
            throw new ValidationAppException("Nhập tên máy in và DPI 203 hoặc 300.");
        var entity = id.HasValue ? await Printer(id.Value, ct) : new ProductLabelPrinter { StoreId = StoreId };
        if (id.HasValue) CheckVersion(entity, request.RowVersion); else db.Add(entity);
        var windowsName = request.WindowsPrinterName.Trim();
        if (await db.Set<ProductLabelPrinter>().AnyAsync(x => x.StoreId == StoreId && x.Id != entity.Id && x.WindowsPrinterName == windowsName, ct))
            throw new ConflictAppException("Máy in Windows này đã được cấu hình.");
        entity.Name = request.Name.Trim(); entity.WindowsPrinterName = windowsName; entity.Dpi = request.Dpi;
        entity.PrintableWidthMm = request.PrintableWidthMm; entity.OffsetXmm = request.OffsetXmm; entity.OffsetYmm = request.OffsetYmm; entity.Enabled = request.Enabled;
        await Save(ct); return PrinterDto(entity);
    }

    private async Task<(StockDocument Document, List<LabelProduct> Products, string Hash, int ProvisionalCount)> Source(int receiptId, CancellationToken ct)
    {
        var doc = await db.Set<StockDocument>().AsNoTracking().Include(x => x.Lines.Where(l => !l.IsDeleted))
            .SingleOrDefaultAsync(x => x.Id == receiptId && x.StoreId == StoreId && x.Type == StockDocumentType.Receipt, ct)
            ?? throw new NotFoundAppException("Không tìm thấy phiếu nhập.");
        if (doc.Status == StockDocumentStatus.Cancelled) throw new ConflictAppException("Phiếu nhập đã hủy.");
        var ids = doc.Lines.Select(x => x.ProductVariantId).Distinct().ToArray();
        var variants = await db.ProductVariants.AsNoTracking().Where(x => x.StoreId == StoreId && ids.Contains(x.Id))
            .Include(x => x.Product).ThenInclude(x => x.BaseUnit).ToListAsync(ct);
        var units = await db.ProductUnitConversions.AsNoTracking().Where(x => x.StoreId == StoreId && ids.Contains(x.ProductVariantId) && x.IsBaseUnit && x.Factor == 1 && x.IsActive)
            .Include(x => x.Barcodes.Where(b => !b.IsDeleted && b.IsActive)).ToListAsync(ct);
        var products = new List<LabelProduct>();
        foreach (var group in doc.Lines.GroupBy(x => x.ProductVariantId).OrderBy(x => x.Key))
        {
            var variant = variants.SingleOrDefault(x => x.Id == group.Key);
            var unit = units.FirstOrDefault(x => x.ProductVariantId == group.Key && x.UnitId == variant?.Product.BaseUnitId);
            var barcode = unit?.Barcodes.Where(x => x.StoreId == StoreId).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Id).FirstOrDefault()?.Barcode ?? "";
            decimal received = group.Sum(x => x.BaseQuantity);
            string? problem = variant?.Product is null || variant.Product.StoreId != StoreId ? "Sản phẩm không còn trong danh mục." :
                received != decimal.Truncate(received) ? "Số lượng lẻ: chưa hỗ trợ in theo số lượng nhập." : null;
            var retail = unit?.Price is > 0 ? unit.Price.Value : variant?.Price is > 0 ? variant.Price.Value : variant?.Product.BasePrice ?? 0;
            products.Add(new LabelProduct(group.Key, variant?.ProductVariantName ?? variant?.Product.Name ?? group.First().ProductNameSnapshot,
                barcode, variant?.Product.BaseUnit.Name ?? "", retail, received, problem));
        }
        var provisional = await db.Set<StockDocumentProvisionalItem>().AsNoTracking().Where(x => x.StockDocumentId == doc.Id && x.StoreId == StoreId && x.Status == StockDocumentProvisionalItemStatus.Unresolved).CountAsync(ct);
        // Includes catalog identity/price so the displayed quote cannot silently change at print time.
        return (doc, products, Hash(new { products, provisional, lines = doc.Lines.OrderBy(x => x.Id).Select(x => new { x.Id, x.Quantity, x.Factor, x.BaseQuantity, x.ProductVariantId }) }), provisional);
    }

    public async Task<object> AddReceipt(int receiptId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock($"receipt:{receiptId}", ct);
        var source = await Source(receiptId, ct);
        var task = await db.Set<ProductLabelTask>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.StockDocumentId == receiptId, ct);
        if (task is null)
        {
            if (source.Products.Count == 0) throw new ValidationAppException("Phiếu chưa có sản phẩm đã lưu trong danh mục để in tem.");
            task = new ProductLabelTask { StoreId = StoreId, StockDocumentId = receiptId, DocumentNo = source.Document.DocumentNo,
                SourceHash = source.Hash, LinesJson = LabelJson.Write(source.Products.Select(x => new LabelTaskLine(x, 0)).ToList()) };
            db.Add(task); await Save(ct);
        }
        await tx.CommitAsync(ct); return new { task.Id };
    }
    public async Task<object> Tasks(CancellationToken ct) => (await db.Set<ProductLabelTask>().AsNoTracking().Where(x => x.StoreId == StoreId)
        .OrderByDescending(x => x.Id).Take(500).ToListAsync(ct)).Select(x =>
        {
            var lines = LabelJson.Read<List<LabelTaskLine>>(x.LinesJson); var progress = Progress(lines);
            return new { x.Id, x.StockDocumentId, x.DocumentNo, completed = progress.Total > 0 && progress.Pending == 0, x.CreatedAtUtc, progress,
                required = lines.Sum(l => l.Required), printed = lines.Sum(l => l.Printed) };
        }).ToArray();
    private async Task<ProductLabelTask> TaskEntity(int id, CancellationToken ct) =>
        await db.Set<ProductLabelTask>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == id, ct) ?? throw new NotFoundAppException("Không tìm thấy phiếu in tem.");
    public async Task<object> Detail(int id, CancellationToken ct)
    {
        var task = await TaskEntity(id, ct);
        bool changed; string? sourceError = null; int provisionalCount = 0;
        try { var source = await Source(task.StockDocumentId, ct); changed = source.Hash != task.SourceHash; provisionalCount = source.ProvisionalCount; }
        catch (ConflictAppException ex) { changed = true; sourceError = ex.Message; }
        catch (NotFoundAppException ex) { changed = true; sourceError = ex.Message; }
        var lines = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson);
        var metadata = await ReceiptMetadata(task.StockDocumentId, ct);
        lines = await WithImages(lines, ct);
        return new { task.Id, task.DocumentNo, task.StockDocumentId, task.TemplateId, completed = Progress(lines).Pending == 0 && lines.Any(x => !x.Removed), task.CompletedAtUtc, task.CompletedByUserId,
            metadata.DocumentTitle, metadata.SupplierName, progress = Progress(lines),
            rowVersion = Version(task), sourceChanged = changed, sourceError, provisionalCount, lines, jobs = await Jobs(id, ct) };
    }
    private Task<bool> Active(int taskId, CancellationToken ct) => db.Set<ProductLabelJob>().AnyAsync(x => x.StoreId == StoreId && x.TaskId == taskId &&
        (x.Status == ProductLabelJobStatus.Queued || x.Status == ProductLabelJobStatus.Sending || x.Status == ProductLabelJobStatus.AwaitingConfirmation || x.Status == ProductLabelJobStatus.NeedsAttention), ct);
    public async Task<object> ReceiptPreview(int receiptId, CancellationToken ct)
    {
        var existing = await db.Set<ProductLabelTask>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == StoreId && x.StockDocumentId == receiptId, ct);
        if (existing is not null) return await Detail(existing.Id, ct);
        var source = await Source(receiptId, ct);
        return new { id = (int?)null, stockDocumentId = receiptId, documentNo = source.Document.DocumentNo,
            documentTitle = source.Document.DocumentTitle,
            templateId = (int?)null, completed = false, sourceChanged = false, source.ProvisionalCount,
            lines = await WithImages(source.Products.Select(p => new LabelTaskLine(p, 0)).ToList(), ct), jobs = Array.Empty<object>() };
    }
    private async Task Editable(ProductLabelTask task, string version, CancellationToken ct)
    {
        CheckVersion(task, version);
        if (await Active(task.Id, ct)) throw new ConflictAppException("Phiếu đang có lệnh in chưa xử lý xong. Xác nhận kết quả hoặc hủy lệnh chờ trước.");
    }
    public async Task<object> Refresh(int id, string version, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock($"task:{id}", ct);
        var task = await TaskEntity(id, ct); await Editable(task, version, ct);
        var source = await Source(task.StockDocumentId, ct); var old = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson);
        var lines = source.Products.Select(p => old.FirstOrDefault(x => x.Product.VariantId == p.VariantId) is { } existing
            ? existing with { Product = p, Removed = false } : new LabelTaskLine(p, 0)).ToList();
        lines.AddRange(old.Where(x => source.Products.All(p => p.VariantId != x.Product.VariantId)).Select(x => x with { Removed = true, Required = x.Printed }));
        task.LinesJson = LabelJson.Write(lines); task.SourceHash = source.Hash;
        LabelTaskProgress.CompleteIfHandled(task, db.CurrentUserId);
        // Review the template defaults again after receipt edits; preserve printed history and explicit quantities.
        await Save(ct); await tx.CommitAsync(ct); return await Detail(id, ct);
    }
    public async Task<object> Plan(int id, LabelPlanRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock($"task:{id}", ct);
        var task = await TaskEntity(id, ct); await Editable(task, request.RowVersion, ct);
        if (task.Completed) throw new ConflictAppException("Phiếu đã hoàn thành. Cập nhật từ phiếu nhập để mở lại nếu cần.");
        var source = await Source(task.StockDocumentId, ct);
        if (source.Hash != task.SourceHash) throw new ConflictAppException("Phiếu hoặc giá bán đã đổi. Cập nhật từ phiếu nhập trước.");
        await Template(request.TemplateId, ct);
        var lines = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson);
        ValidateQuantities(request.Lines, true);
        if (request.Lines.Count != lines.Count || request.Lines.Any(x => lines.All(l => l.Product.VariantId != x.VariantId)))
            throw new ValidationAppException("Danh sách sản phẩm không khớp phiếu.");
        lines = lines.Select(l =>
        {
            int qty = request.Lines.Single(x => x.VariantId == l.Product.VariantId).Quantity;
            if (qty < l.Printed || l.Removed && qty != l.Printed) throw new ValidationAppException("Số cần in không được nhỏ hơn số đã nhận; không thêm tem cho dòng đã xóa khỏi phiếu.");
            return l with { Required = qty };
        }).ToList();
        task.TemplateId = request.TemplateId; task.LinesJson = LabelJson.Write(lines);
        await Save(ct); await tx.CommitAsync(ct); return await Detail(id, ct);
    }
    private static void ValidateQuantities(List<LabelQuantity>? lines, bool allowZero)
    {
        if (lines is null || lines.Count is 0 or > 500 || lines.Select(x => x.VariantId).Distinct().Count() != lines.Count ||
            lines.Any(x => x.Quantity < (allowZero ? 0 : 1) || x.Quantity > 10000) || lines.Sum(x => (long)x.Quantity) > 10000)
            throw new ValidationAppException("Số tem phải là số nguyên từ 0 đến 10.000; không lặp sản phẩm, tối đa 10.000 tem mỗi lần.");
    }

    public async Task<object> Enqueue(LabelJobRequest request, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new ConflictAppException("Máy chủ tạo tem cần chạy Windows.");
        if (request.RequestId == Guid.Empty) throw new ValidationAppException("Thiếu mã yêu cầu in.");
        var requestHash = Hash(request);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(request.TaskId.HasValue ? $"task:{request.TaskId}" : $"request:{request.RequestId}", ct);
        var existing = await db.Set<ProductLabelJob>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.RequestId == request.RequestId, ct);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash) throw new ConflictAppException("Mã yêu cầu đã được dùng cho nội dung khác.");
            return new { existing.Id, duplicate = true };
        }
        var template = await Template(request.TemplateId, ct); CheckVersion(template, request.TemplateVersion);
        var design = CurrentDesign(template);
        if (design.PrinterId is null) throw new ConflictAppException("Mẫu chưa gắn máy in. Nhờ admin mở cấu hình mẫu và chọn máy in.");
        if (design.PrinterId != request.PrinterId) throw new ConflictAppException("Máy in không khớp mẫu. Tải lại mẫu tem trước khi in.");
        if (!design.ShowPrintButton) throw new ConflictAppException("Mẫu tem đã được ẩn khỏi danh sách in.");
        if (request.PlanLines is not null && (!request.TaskId.HasValue || request.IsReprint || request.QuickLines is not null))
            throw new ValidationAppException("Chỉ lưu số lượng cùng lệnh in theo phiếu nhập.");
        var printer = await Printer(request.PrinterId, ct);
        if (!printer.Enabled) throw new ConflictAppException("Máy in đang tắt trong cấu hình.");
        var items = new List<LabelPrintItem>();
        if (request.QuickLines is not null)
        {
            if (request.TaskId.HasValue || request.IsReprint || request.Lines is not { Count: 0 })
                throw new ValidationAppException("Lệnh in nhanh không dùng phiếu nhập hoặc danh sách in theo phiếu.");
            items.AddRange(await QuickItems(request.QuickLines, ct));
        }
        else if (request.TaskId.HasValue)
        {
            var task = await TaskEntity(request.TaskId.Value, ct); await Editable(task, request.RowVersion ?? "", ct);
            if (task.Completed && !request.IsReprint && !request.ProductProgress) throw new ConflictAppException("Phiếu đã hoàn thành. Dùng In lại nếu cần.");
            var source = await Source(task.StockDocumentId, ct);
            if (request.ProductProgress && source.ProvisionalCount > 0) throw new ConflictAppException("Ghép các dòng hàng tạm vào danh mục rồi cập nhật phiếu in trước.");
            if (source.Hash != task.SourceHash)
                throw new ConflictAppException("Phiếu, sản phẩm hoặc giá bán đã thay đổi. Cập nhật lại trước khi in.");
            ValidateQuantities(request.Lines, false);
            var lines = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson);
            if (request.PlanLines is not null)
            {
                ValidateQuantities(request.PlanLines, true);
                if (request.PlanLines.Count != lines.Count || request.PlanLines.Any(x => lines.All(l => l.Product.VariantId != x.VariantId)))
                    throw new ValidationAppException("Danh sách sản phẩm không khớp phiếu.");
                lines = lines.Select(l =>
                {
                    var qty = request.PlanLines.Single(x => x.VariantId == l.Product.VariantId).Quantity;
                    if (qty < l.Printed || l.Removed && qty != l.Printed)
                        throw new ValidationAppException("Số cần in không được nhỏ hơn số đã nhận hoặc tăng dòng đã xóa.");
                    return l with { Required = qty };
                }).ToList();
                task.TemplateId = template.Id; task.LinesJson = LabelJson.Write(lines);
            }
            else if (!request.ProductProgress && !request.IsReprint && task.TemplateId != request.TemplateId)
                throw new ConflictAppException("Lưu mẫu và số lượng cần in cho phiếu trước.");
            foreach (var requested in request.Lines)
            {
                var line = lines.SingleOrDefault(x => x.Product.VariantId == requested.VariantId && !x.Removed)
                    ?? throw new ValidationAppException("Sản phẩm không thuộc phiếu in.");
                if (!request.ProductProgress && !request.IsReprint && requested.Quantity > line.Required - line.Printed)
                    throw new ValidationAppException("Số tem vượt phần còn lại. Chỉnh số cần in hoặc dùng In lại.");
                items.Add(new LabelPrintItem(line.Product, requested.Quantity) { CountsForProgress = request.ProductProgress && !request.IsReprint && !line.Done && !line.Skipped });
            }
            if (request.ProductProgress) task.TemplateId = template.Id;
        }
        else
        {
            if (request.IsReprint || request.Lines is not { Count: 0 }) throw new ValidationAppException("Yêu cầu in thử không hợp lệ.");
            items.Add(new LabelPrintItem(ProductLabelRenderer.SampleFor(design), design.Columns));
        }
        if ((request.Reason?.Length ?? 0) > 300 || !request.ProductProgress && request.IsReprint && string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationAppException("Nhập lý do in lại, tối đa 300 ký tự.");
        var payload = new LabelPrintPayload(design, new(printer.Name, printer.WindowsPrinterName, printer.Dpi, printer.PrintableWidthMm, printer.OffsetXmm, printer.OffsetYmm), items) { ProductProgress = request.ProductProgress && request.TaskId.HasValue };
        ProductLabelRenderer.ValidatePayload(payload);
        var job = new ProductLabelJob { StoreId = StoreId, TaskId = request.TaskId, PrinterId = printer.Id, RequestId = request.RequestId, RequestHash = requestHash,
            PayloadJson = LabelJson.Write(payload), Quantity = items.Sum(x => x.Quantity), IsReprint = payload.ProductProgress ? items.All(x => !x.CountsForProgress) : request.IsReprint, Reason = request.Reason?.Trim() ?? "",
            RequestedByName = db.CurrentUserName ?? $"NV {db.CurrentUserId}" };
        db.Add(job); await Save(ct); await tx.CommitAsync(ct); return new { job.Id, duplicate = false };
    }
    public async Task<object> Jobs(int? taskId, CancellationToken ct)
    {
        var query = db.Set<ProductLabelJob>().AsNoTracking()
            .Where(x => x.StoreId == StoreId && (!taskId.HasValue || x.TaskId == taskId)).OrderByDescending(x => x.Id).AsQueryable();
        if (!taskId.HasValue) query = query.Take(200);
        return (await query.ToListAsync(ct))
            .Select(x => new { x.Id, x.TaskId, x.Status, x.Quantity, x.IsReprint, x.Reason, x.RequestedByName, x.CreatedAtUtc, x.SentAtUtc, x.ConfirmedAtUtc,
                x.ConfirmedByName, x.SpoolJobId, x.Error, rowVersion = Version(x), payload = LabelJson.Read<LabelPrintPayload>(x.PayloadJson), result = LabelJson.Read<List<LabelQuantity>>(x.ResultJson) }).ToArray();
    }

    public async Task Confirm(int id, LabelConfirmRequest request, bool cancel, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lookup = await db.Set<ProductLabelJob>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == id, ct) ?? throw new NotFoundAppException("Không tìm thấy lệnh in.");
        await Lock(lookup.TaskId.HasValue ? $"task:{lookup.TaskId}" : $"request:{lookup.RequestId}", ct);
        // Printer lock is also used by the dispatcher, so a queued cancellation cannot race submission.
        await Lock($"printer:{lookup.PrinterId}", ct);
        var job = await db.Set<ProductLabelJob>().SingleAsync(x => x.StoreId == StoreId && x.Id == id, ct); CheckVersion(job, request.RowVersion);
        if (cancel)
        {
            if (job.Status != ProductLabelJobStatus.Queued) throw new ConflictAppException("Chỉ hủy lệnh chưa gửi. Lệnh đã gửi cần xác nhận số tem thực nhận.");
            job.Status = ProductLabelJobStatus.Cancelled;
        }
        else
        {
            if (job.Status == ProductLabelJobStatus.Sending && job.UpdatedAtUtc < DateTime.UtcNow.AddMinutes(-10))
                job.Status = ProductLabelJobStatus.NeedsAttention;
            if (job.Status is not (ProductLabelJobStatus.AwaitingConfirmation or ProductLabelJobStatus.NeedsAttention))
                throw new ConflictAppException("Lệnh chưa sẵn sàng xác nhận. Nếu dịch vụ bị dừng khi gửi, đợi 10 phút và kiểm tra máy in trước.");
            var printed = LabelJson.Read<LabelPrintPayload>(job.PayloadJson).Items;
            if (request.Lines is null || request.Lines.Count != printed.Count ||
                request.Lines.Select(l => (l.VariantId, l.UnitId)).Distinct().Count() != request.Lines.Count ||
                request.Lines.Any(l => l.Quantity < 0 || !printed.Any(p => p.Product.VariantId == l.VariantId && p.Product.UnitId == l.UnitId && l.Quantity <= p.Quantity)))
                throw new ValidationAppException("Số tem nhận phải khớp sản phẩm và không vượt số đã gửi.");
            if (request.Lines.Sum(x => x.Quantity) < job.Quantity && string.IsNullOrWhiteSpace(request.Note))
                throw new ValidationAppException("Ghi lý do khi nhận thiếu tem (hết giấy, tem lỗi…).");
            if ((request.Note?.Length ?? 0) > 500) throw new ValidationAppException("Ghi chú tối đa 500 ký tự.");
            job.ResultJson = LabelJson.Write(request.Lines); job.Status = ProductLabelJobStatus.Confirmed;
            job.Error = request.Note?.Trim();
            var payload = LabelJson.Read<LabelPrintPayload>(job.PayloadJson);
            if (job.TaskId.HasValue && payload.ProductProgress)
            {
                var task = await TaskEntity(job.TaskId.Value, ct);
                LabelTaskProgress.Apply(task, payload, request.Lines, db.CurrentUserId);
            }
            else if (job.TaskId.HasValue && !job.IsReprint)
            {
                var task = await TaskEntity(job.TaskId.Value, ct);
                var lines = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson);
                task.LinesJson = LabelJson.Write(lines.Select(l => l with { Printed = l.Printed + (request.Lines.SingleOrDefault(x => x.VariantId == l.Product.VariantId)?.Quantity ?? 0) }));
            }
        }
        job.ConfirmedAtUtc = DateTime.UtcNow; job.ConfirmedByUserId = db.CurrentUserId; job.ConfirmedByName = db.CurrentUserName;
        await Save(ct); await tx.CommitAsync(ct);
    }
    public async Task Complete(int id, string version, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock($"task:{id}", ct);
        var task = await TaskEntity(id, ct); await Editable(task, version, ct);
        var source = await Source(task.StockDocumentId, ct);
        if (source.ProvisionalCount > 0) throw new ConflictAppException("Phiếu còn hàng tạm chưa ghép danh mục. Hoàn thiện sản phẩm và cập nhật phiếu in trước khi chốt.");
        if (source.Hash != task.SourceHash) throw new ConflictAppException("Phiếu hoặc giá bán đã đổi. Cập nhật từ phiếu nhập trước.");
        var lines = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson);
        if (task.TemplateId is null || lines.Sum(x => x.Required) == 0 || lines.Any(x => x.Printed < x.Required))
            throw new ConflictAppException("Phiếu còn sản phẩm chưa in đủ hoặc chưa chọn số lượng cần in.");
        task.Completed = true; task.CompletedAtUtc = DateTime.UtcNow; task.CompletedByUserId = db.CurrentUserId;
        await Save(ct); await tx.CommitAsync(ct);
    }
}
