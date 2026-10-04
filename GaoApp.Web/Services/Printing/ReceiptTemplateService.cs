using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Printing;

public sealed record ReceiptDesign
{
    [Required, StringLength(100)] public string Name { get; init; } = "Mẫu của cửa hàng";
    [Required, RegularExpression("^(modern|classic|compact|itemwide)$")] public string Layout { get; init; } = "modern";
    [Required, RegularExpression("^(45|80|A6|A5|A4)$")] public string PaperSize { get; init; } = "80";
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$")] public string AccentColor { get; init; } = "#000000";
    [Required, StringLength(100)] public string Title { get; init; } = "HÓA ĐƠN BÁN HÀNG";
    [StringLength(300)] public string HeaderText { get; init; } = "";
    [StringLength(400)] public string FooterText { get; init; } = "Cảm ơn quý khách. Hẹn gặp lại!";
    public bool ShowCustomer { get; init; } = true;
    public bool ShowCashier { get; init; } = true;
    public bool ShowSku { get; init; }
    public bool ShowPayments { get; init; } = true;
}

public sealed record ReceiptTemplateOption(string Key, ReceiptDesign Design, bool BuiltIn, string? RowVersion = null);
public sealed record SaveReceiptTemplateRequest(ReceiptDesign Design, string? RowVersion);
public sealed record ReceiptDefault(ReceiptTemplateOption Template, string RowVersion);
public sealed record SaveReceiptDefaultRequest([Required, StringLength(100)] string Key, [Required] string RowVersion, string? TemplateVersion);
public sealed record ReceiptStoreInfo(string StoreName, string StoreAddress, string StorePhone, string RowVersion);
public sealed record SaveReceiptStoreInfoRequest
{
    [Required, StringLength(200)] public string StoreName { get; init; } = "";
    [StringLength(300)] public string? StoreAddress { get; init; }
    [StringLength(50)] public string? StorePhone { get; init; }
    [Required] public string RowVersion { get; init; } = "";
}

public sealed class ReceiptTemplateService(AppDbContext db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ConflictAppException("Chưa xác định cửa hàng.");
    public static IReadOnlyList<ReceiptTemplateOption> BuiltIns() =>
        (from layout in new[] { ("modern", "Hiện đại", "#000000"), ("classic", "Thanh lịch", "#000000"), ("compact", "Gọn gàng", "#000000"), ("itemwide", "Tên hàng rộng", "#000000") }
         from size in layout.Item1 == "itemwide" ? new[] { "80" } : new[] { "45", "80", "A6", "A5", "A4" }
         select new ReceiptTemplateOption($"{layout.Item1}-{size}", new ReceiptDesign
         { Name = $"{layout.Item2} · {size}{(size is "45" or "80" ? " mm" : "")}", Layout = layout.Item1, PaperSize = size, AccentColor = layout.Item3 }, true)).ToArray();

    public async Task<IReadOnlyList<ReceiptTemplateOption>> ListAsync(CancellationToken ct)
    {
        var storeId = StoreId;
        var saved = await db.Set<PosReceiptTemplate>().AsNoTracking().Where(x => x.StoreId == storeId).OrderBy(x => x.Name).ToListAsync(ct);
        return BuiltIns().Concat(saved.Select(ToOption)).ToArray();
    }

    public async Task<ReceiptDefault> GetDefaultAsync(CancellationToken ct)
    {
        var storeId = StoreId;
        var store = await db.Stores.AsNoTracking().SingleAsync(x => x.Id == storeId && !x.IsDeleted, ct);
        var builtins = BuiltIns();
        var selected = builtins.FirstOrDefault(x => x.Key == store.ReceiptTemplateKey);
        if (selected == null && TryCustomId(store.ReceiptTemplateKey, out var id))
        {
            var custom = await db.Set<PosReceiptTemplate>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, ct);
            if (custom != null) selected = ToOption(custom);
        }
        return new(selected ?? builtins.Single(x => x.Key == "modern-80"), Convert.ToBase64String(store.RowVersion));
    }

    public async Task<ReceiptDefault> SaveDefaultAsync(SaveReceiptDefaultRequest request, CancellationToken ct)
    {
        var storeId = StoreId;
        var store = await db.Stores.SingleAsync(x => x.Id == storeId && !x.IsDeleted, ct);
        if (request.RowVersion != Convert.ToBase64String(store.RowVersion))
            throw new ConflictAppException("Cấu hình cửa hàng đã thay đổi. Tải lại trang trước khi đặt mẫu mặc định.");
        if (TryCustomId(request.Key, out var id)) CheckVersion(await FindAsync(id, storeId, ct), request.TemplateVersion);
        else if (!BuiltIns().Any(x => x.Key == request.Key)) throw new ValidationAppException("Không tìm thấy mẫu hóa đơn để áp dụng.");
        store.ReceiptTemplateKey = request.Key;
        // Always touch the store row, even when applying the same key, to serialize against deletion.
        db.Entry(store).Property(x => x.ReceiptTemplateKey).IsModified = true;
        await SaveChanges(ct);
        return await GetDefaultAsync(ct);
    }

    private static bool TryCustomId(string? key, out int id)
    {
        id = 0;
        return key?.StartsWith("custom-", StringComparison.Ordinal) == true && int.TryParse(key[7..], out id) && id > 0;
    }

    public async Task<ReceiptStoreInfo> GetStoreInfoAsync(CancellationToken ct)
    {
        var storeId = StoreId;
        var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId && !x.IsDeleted, ct)
            ?? throw new NotFoundAppException("Không tìm thấy cửa hàng.");
        return ToStoreInfo(store);
    }

    public async Task<ReceiptStoreInfo> SaveStoreInfoAsync(SaveReceiptStoreInfoRequest request, CancellationToken ct)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true)
            || string.IsNullOrWhiteSpace(request.StoreName))
            throw new ValidationAppException("Nhập tên tiệm (tối đa 200 ký tự), địa chỉ (300) và điện thoại (50). "
                + string.Join(" ", errors.Select(x => x.ErrorMessage)));
        var storeId = StoreId;
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId && !x.IsDeleted, ct)
            ?? throw new NotFoundAppException("Không tìm thấy cửa hàng.");
        if (request.RowVersion != Convert.ToBase64String(store.RowVersion))
            throw new ConflictAppException("Thông tin tiệm đã thay đổi ở máy khác. Tải lại trang trước khi lưu.");
        store.ReceiptName = request.StoreName.Trim();
        store.ReceiptAddress = string.IsNullOrWhiteSpace(request.StoreAddress) ? null : request.StoreAddress.Trim();
        store.ReceiptPhone = string.IsNullOrWhiteSpace(request.StorePhone) ? null : request.StorePhone.Trim();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Thông tin tiệm đã thay đổi ở máy khác. Tải lại trang trước khi lưu."); }
        return ToStoreInfo(store);
    }

    private static ReceiptStoreInfo ToStoreInfo(Store store) => new(store.ReceiptName ?? store.Name,
        store.ReceiptAddress ?? "", store.ReceiptPhone ?? "", Convert.ToBase64String(store.RowVersion));

    public async Task<ReceiptTemplateOption> SaveAsync(int? id, SaveReceiptTemplateRequest request, CancellationToken ct)
    {
        var storeId = StoreId;
        if (request.Design is null) throw new ConflictAppException("Chưa có nội dung mẫu in.");
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request.Design, new ValidationContext(request.Design), errors, true))
            throw new ValidationAppException(string.Join(" ", errors.Select(x => x.ErrorMessage)));
        var design = request.Design with { Name = request.Design.Name.Trim(), Title = request.Design.Title.Trim(), AccentColor = "#000000",
            PaperSize = request.Design.Layout == "itemwide" ? "80" : request.Design.PaperSize };
        if (string.IsNullOrWhiteSpace(design.Name) || string.IsNullOrWhiteSpace(design.Title))
            throw new ConflictAppException("Nhập tên mẫu và tiêu đề hóa đơn.");
        PosReceiptTemplate template;
        if (id.HasValue)
        {
            template = await FindAsync(id.Value, storeId, ct);
            CheckVersion(template, request.RowVersion);
        }
        else
        {
            template = new PosReceiptTemplate { StoreId = storeId };
            db.Add(template);
        }
        template.Name = design.Name;
        template.DefinitionJson = JsonSerializer.Serialize(design, Json);
        await SaveChanges(ct);
        return ToOption(template);
    }

    public async Task DeleteAsync(int id, string? rowVersion, CancellationToken ct)
    {
        var storeId = StoreId;
        var store = await db.Stores.SingleAsync(x => x.Id == storeId && !x.IsDeleted, ct);
        if (store.ReceiptTemplateKey == $"custom-{id}")
            throw new ConflictAppException("Mẫu này đang được dùng mặc định. Chọn mẫu mặc định khác trước khi xóa.");
        var template = await FindAsync(id, StoreId, ct);
        CheckVersion(template, rowVersion);
        db.Entry(store).Property(x => x.ReceiptTemplateKey).IsModified = true;
        db.Remove(template);
        await SaveChanges(ct);
    }

    private async Task<PosReceiptTemplate> FindAsync(int id, int storeId, CancellationToken ct) =>
        await db.Set<PosReceiptTemplate>().SingleOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, ct)
        ?? throw new NotFoundAppException("Không tìm thấy mẫu in.");

    private static void CheckVersion(PosReceiptTemplate template, string? version)
    {
        if (version != Convert.ToBase64String(template.RowVersion))
            throw new ConflictAppException("Mẫu đã được cập nhật ở máy khác. Tải lại danh sách trước khi sửa.");
    }

    private async Task SaveChanges(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Mẫu đã thay đổi. Tải lại danh sách trước khi tiếp tục."); }
    }

    private static ReceiptTemplateOption ToOption(PosReceiptTemplate x) => new($"custom-{x.Id}",
        JsonSerializer.Deserialize<ReceiptDesign>(x.DefinitionJson, Json)! with { AccentColor = "#000000" }, false, Convert.ToBase64String(x.RowVersion));
}
