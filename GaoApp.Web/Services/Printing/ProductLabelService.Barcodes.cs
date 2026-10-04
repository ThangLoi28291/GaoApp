using System.Data;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Printing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Printing;

public sealed record LabelBarcodeRequest(int TemplateId, string TemplateVersion, int? TaskId,
    string? RowVersion, List<int> VariantIds, List<QuickLabelSelection>? QuickLines, string? PlanToken = null);
public sealed record LabelBarcodeIssue(int ConversionId, LabelProduct Product, string NewBarcode, bool Reuse);
public sealed record LabelBarcodePlan(string Token, List<LabelBarcodeIssue> Issues);

public sealed partial class ProductLabelService
{
    // This narrow printing operation only supplements active aliases; it must never use
    // ChangeBarcodeAsync, whose replacement policy deactivates the old barcode.
    public async Task<LabelBarcodePlan> CheckLabelBarcodes(LabelBarcodeRequest request, CancellationToken ct)
    {
        var template = await Template(request.TemplateId, ct);
        CheckVersion(template, request.TemplateVersion);
        var design = CurrentDesign(template);
        var printer = await Printer(design.PrinterId ?? 0, ct);
        if (!printer.Enabled) throw new ConflictAppException("Máy in đã ngừng hoạt động.");
        design.Validate(printer.PrintableWidthMm);
        List<QuickLabelOption> options;
        string? sourceHash = null;
        if (request.TaskId is int taskId)
        {
            var task = await TaskEntity(taskId, ct);
            await Editable(task, request.RowVersion ?? "", ct);
            var source = await Source(task.StockDocumentId, ct);
            if (source.Hash != task.SourceHash || source.ProvisionalCount > 0)
                throw new ConflictAppException("Phiếu hoặc danh mục đã thay đổi. Cập nhật từ phiếu nhập trước khi tiếp tục.");
            sourceHash = source.Hash;
            var ids = request.VariantIds;
            if (ids is null || ids.Count is 0 or > 500 || ids.Distinct().Count() != ids.Count || ids.Any(id => source.Products.All(p => p.VariantId != id)))
                throw new ValidationAppException("Chọn sản phẩm thuộc phiếu nhập này.");
            var conversions = await QuickCatalog().Where(x => ids.Contains(x.ProductVariantId) && x.IsBaseUnit && x.Factor == 1 && x.UnitId == x.ProductVariant.Product.BaseUnitId)
                .Select(x => x.Id).ToListAsync(ct);
            options = await QuickOptions(conversions, ct);
            if (options.Count != ids.Count) throw new ConflictAppException("Có sản phẩm chưa có đơn vị gốc đang hoạt động. Kiểm tra danh mục trước.");
            options = options.Select(o => o with { Product = source.Products.Single(p => p.VariantId == o.Product.VariantId) }).ToList();
        }
        else
        {
            var lines = request.QuickLines;
            if (lines is null || lines.Count is 0 or > 500 || lines.Select(x => x.ConversionId).Distinct().Count() != lines.Count)
                throw new ValidationAppException("Chọn 1–500 đơn vị sản phẩm.");
            options = await QuickOptions(lines.Select(x => x.ConversionId).ToList(), ct);
            if (options.Count != lines.Count) throw new NotFoundAppException("Sản phẩm hoặc đơn vị in không còn hoạt động.");
            if (options.Any(o => lines.Single(l => l.ConversionId == o.ConversionId).Fingerprint != o.Fingerprint))
                throw new ConflictAppException("Sản phẩm hoặc giá bán đã đổi. Cập nhật danh sách trước khi in.");
        }
        var idsToCheck = options.Select(o => o.ConversionId).ToList();
        var aliases = await db.Set<ProductVariantUnitBarcode>().AsNoTracking()
            .Where(b => b.StoreId == StoreId && idsToCheck.Contains(b.ProductUnitConversionId) && b.IsActive)
            .OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id).ToListAsync(ct);
        var issues = new List<LabelBarcodeIssue>();
        var reserved = new HashSet<string>();
        foreach (var option in options)
        {
            if (FitsLabel(design, printer.Dpi, option.Product)) continue;
            var reusable = aliases.FirstOrDefault(b => b.ProductUnitConversionId == option.ConversionId && FitsLabel(design, printer.Dpi, option.Product with { Barcode = b.Barcode }));
            var barcode = reusable?.Barcode;
            if (barcode is null)
            {
                if (StoreId > 999 || option.ConversionId > 9_999_000)
                    throw new ValidationAppException("Không thể sinh mã theo cấu hình mã nội bộ hiện tại. Chọn tem lớn hoặc liên hệ quản trị.");
                for (var attempt = 0; attempt < 1000; attempt++)
                {
                    var candidate = Ean13Helper.GenerateInternal(StoreId, option.ConversionId + attempt);
                    if (reserved.Contains(candidate) || await db.Set<ProductVariantUnitBarcode>().IgnoreQueryFilters()
                        .AnyAsync(b => b.StoreId == StoreId && b.Barcode == candidate, ct)) continue;
                    barcode = candidate; reserved.Add(candidate); break;
                }
                if (barcode is null) throw new ConflictAppException("Chưa cấp được mã nội bộ duy nhất. Thử lại hoặc chọn tem lớn.");
            }
            if (!FitsLabel(design, printer.Dpi, option.Product with { Barcode = barcode }))
                throw new ValidationAppException("Khổ tem này không đủ chỗ cả với mã nội bộ. Chọn khổ tem rộng hơn.");
            issues.Add(new(option.ConversionId, option.Product, barcode, reusable is not null));
        }
        return new(Hash(new { request.TemplateId, request.TemplateVersion, request.TaskId, request.RowVersion,
            sourceHash, printer = Version(printer), options, issues,
            aliases = aliases.Select(b => new { b.Id, b.Barcode, b.IsPrimary, version = Version(b) }) }), issues);
    }

    private static bool FitsLabel(ProductLabelDesign design, int dpi, LabelProduct product)
    {
        if (!OperatingSystem.IsWindows()) throw new ConflictAppException("Kiểm tra tem cần máy chủ Windows.");
        try { using var image = ProductLabelRenderer.RenderRow(design, [product], dpi, 0, 0); return true; }
        catch (ValidationAppException) { return false; }
    }

    public async Task<object> PrepareLabelBarcodes(LabelBarcodeRequest request, CancellationToken ct)
    {
        // Serialize allocation in this store and keep catalog + task snapshot + audit atomic.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (request.TaskId is int taskId) await Lock($"task:{taskId}", ct);
        await Lock("barcode-allocation", ct);
        var plan = await CheckLabelBarcodes(request, ct);
        if (string.IsNullOrEmpty(request.PlanToken) || plan.Token != request.PlanToken)
            throw new ConflictAppException("Danh mục hoặc mã vạch vừa thay đổi. Kiểm tra lại trước khi xác nhận.");
        foreach (var issue in plan.Issues)
        {
            var aliases = await db.Set<ProductVariantUnitBarcode>()
                .Where(b => b.StoreId == StoreId && b.ProductUnitConversionId == issue.ConversionId && b.IsActive).ToListAsync(ct);
            var old = aliases.OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id).FirstOrDefault();
            foreach (var alias in aliases) alias.IsPrimary = false;
            await Save(ct); // Release the filtered unique primary key before assigning its replacement.
            var target = issue.Reuse ? aliases.Single(b => b.Barcode == issue.NewBarcode) : new ProductVariantUnitBarcode
            {
                StoreId = StoreId, ProductUnitConversionId = issue.ConversionId, Barcode = issue.NewBarcode,
                BarcodeType = BarcodeType.Internal, IsActive = true, Note = "Bổ sung mã để in tem; giữ mã cũ hoạt động."
            };
            if (!issue.Reuse) db.Add(target);
            target.IsPrimary = true;
            await Save(ct);
            db.Add(new ProductVariantBarcodeHistory
            {
                StoreId = StoreId, ProductVariantId = issue.Product.VariantId, ProductUnitConversionId = issue.ConversionId,
                OldBarcodeId = old?.Id, OldBarcode = old?.Barcode, NewBarcodeId = target.Id, NewBarcode = target.Barcode,
                ActionType = BarcodeHistoryActionType.Assigned,
                Reason = issue.Reuse ? "Dùng mã phù hợp đã có làm mặc định để in tem; giữ mã cũ hoạt động." : "Bổ sung mã để in tem, đặt mặc định; giữ mã cũ hoạt động.",
                ChangedByUserId = db.CurrentUserId, ChangedByUserName = db.CurrentUserName, ChangedAtUtc = DateTime.UtcNow
            });
        }
        await Save(ct);
        if (request.TaskId is int id)
        {
            var task = await TaskEntity(id, ct);
            var source = await Source(task.StockDocumentId, ct);
            var lines = LabelJson.Read<List<LabelTaskLine>>(task.LinesJson);
            // Only the selected barcode changes: never reset progress, quantities or historical print payloads.
            task.LinesJson = LabelJson.Write(lines.Select(l => plan.Issues.FirstOrDefault(i => i.Product.VariantId == l.Product.VariantId) is { } issue
                ? l with { Product = l.Product with { Barcode = issue.NewBarcode } } : l).ToList());
            task.SourceHash = source.Hash;
            await Save(ct);
        }
        await tx.CommitAsync(ct);
        return new { changed = plan.Issues.Count,
            task = request.TaskId is int resultId ? await Detail(resultId, ct) : null,
            options = request.TaskId is null ? await QuickOptions(request.QuickLines!.Select(x => x.ConversionId).ToList(), ct) : null };
    }
}
