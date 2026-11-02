using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Promotions;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Printing;

public sealed record PromotionLabelCampaign(int Id, string Name, DateTime StartAtUtc, DateTime EndAtUtc);
public sealed record PromotionLabelOption(int ConversionId, string Name, string Sku, string Barcode,
    string Unit, decimal RetailPrice, decimal SalePrice, decimal MinimumQuantity,
    int PromotionId, string PromotionName, DateTime StartAtUtc, DateTime EndAtUtc,
    string Fingerprint, string? Problem);
public sealed record PromotionLabelPage(IReadOnlyList<PromotionLabelOption> Items, int Total, int Page, int PageSize);
public sealed record PromotionLabelSelection(int ConversionId, int Quantity, string Fingerprint);
public sealed record PreparePromotionLabels(int PromotionId, List<PromotionLabelSelection>? Lines);
public sealed record RefreshPromotionLabels(int PromotionId, List<int>? ConversionIds);
public sealed record PromotionLabelPrintItem(PromotionLabelOption Product, int Quantity);

// Only prepares current, tenant-scoped label data. Never creates a server printer job.
public sealed class PromotionLabelService(AppDbContext db)
{
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value
        : throw new ConflictAppException("Chưa xác định cửa hàng.");
    private IQueryable<Promotion> EligibleCampaigns() => db.Promotions.AsNoTracking()
        .Where(x => x.StoreId == StoreId && !x.IsDeleted && x.IsActive &&
            x.Type == PromotionType.ProductDiscount && x.EndAtUtc >= DateTime.UtcNow &&
            (x.CustomerPriceTier == null || x.CustomerPriceTier.Trim() == "" ||
                x.CustomerPriceTier.Trim().ToUpper() == CustomerPriceTiers.Retail));

    public Task<List<PromotionLabelCampaign>> Campaigns(CancellationToken ct) => EligibleCampaigns()
        .OrderByDescending(x => x.Priority).ThenByDescending(x => x.Id)
        .Select(x => new PromotionLabelCampaign(x.Id, x.Name, x.StartAtUtc, x.EndAtUtc)).ToListAsync(ct);

    private async Task<Promotion> Campaign(int id, CancellationToken ct) =>
        await EligibleCampaigns().Include(x => x.Items.Where(i => i.StoreId == StoreId && !i.IsDeleted))
            .SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new NotFoundAppException("Chương trình không còn hợp lệ để in tem giảm giá bán lẻ.");

    private IQueryable<ProductUnitConversion> Catalog(int promotionId) => db.ProductUnitConversions.AsNoTracking()
        .Where(x => x.StoreId == StoreId && !x.IsDeleted && x.IsActive && x.Factor > 0 &&
            x.ProductVariant.StoreId == StoreId && !x.ProductVariant.IsDeleted && x.ProductVariant.IsActive &&
            x.ProductVariant.Product.StoreId == StoreId && !x.ProductVariant.Product.IsDeleted && x.ProductVariant.Product.IsActive &&
            x.Unit.StoreId == StoreId && !x.Unit.IsDeleted && x.Unit.IsActive &&
            db.PromotionItems.Any(i => i.StoreId == StoreId && !i.IsDeleted && i.PromotionId == promotionId &&
                i.ProductId == x.ProductVariant.ProductId &&
                (i.VariantId == null || i.VariantId <= 0 || i.VariantId == x.ProductVariantId) &&
                (i.ProductUnitConversionId == null || i.ProductUnitConversionId <= 0 || i.ProductUnitConversionId == x.Id)));

    public async Task<PromotionLabelPage> Products(int promotionId, string? q, int page, int pageSize, CancellationToken ct)
    {
        var campaign = await Campaign(promotionId, ct);
        q = q?.Trim() ?? "";
        if (q.Length > 100) throw new ValidationAppException("Tìm kiếm tối đa 100 ký tự.");
        page = Math.Clamp(page, 1, 1000000); pageSize = Math.Clamp(pageSize, 1, 100);
        var catalog = Catalog(promotionId);
        if (q.Length > 0) catalog = catalog.Where(x => x.ProductVariant.Product.Name.Contains(q) ||
            (x.ProductVariant.ProductVariantName != null && x.ProductVariant.ProductVariantName.Contains(q)) ||
            x.ProductVariant.Sku.Contains(q) ||
            x.Barcodes.Any(b => b.StoreId == StoreId && !b.IsDeleted && b.IsActive && b.Barcode.Contains(q)));
        var count = await catalog.CountAsync(ct);
        page = Math.Min(page, Math.Max(1, (count + pageSize - 1) / pageSize));
        var units = await Load(catalog.OrderBy(x => x.ProductVariant.Product.Name).ThenBy(x => x.ProductVariantId)
            .ThenByDescending(x => x.IsBaseUnit).ThenBy(x => x.Factor).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize), ct);
        return new(await Options(campaign, units, ct), count, page, pageSize);
    }

    private static Task<List<ProductUnitConversion>> Load(IQueryable<ProductUnitConversion> query, CancellationToken ct) => query
        .Include(x => x.Unit).Include(x => x.ProductVariant).ThenInclude(x => x.Product)
        .Include(x => x.Barcodes.Where(b => !b.IsDeleted && b.IsActive)).ToListAsync(ct);

    private static bool Matches(PromotionItem item, ProductUnitConversion unit) =>
        item.ProductId == unit.ProductVariant.ProductId &&
        (!item.VariantId.HasValue || item.VariantId <= 0 || item.VariantId == unit.ProductVariantId) &&
        (!item.ProductUnitConversionId.HasValue || item.ProductUnitConversionId <= 0 || item.ProductUnitConversionId == unit.Id);

    private async Task<List<PromotionLabelOption>> Options(Promotion campaign, List<ProductUnitConversion> units, CancellationToken ct)
    {
        if (units.Count == 0) return [];
        var now = DateTime.UtcNow;
        var at = campaign.StartAtUtc > now ? campaign.StartAtUtc : now;
        var productIds = units.Select(x => x.ProductVariant.ProductId).Distinct().ToArray();
        var competitors = await EligibleCampaigns().Where(x => x.StartAtUtc <= at && x.EndAtUtc >= at &&
                x.Items.Any(i => i.StoreId == StoreId && !i.IsDeleted && productIds.Contains(i.ProductId)))
            .Include(x => x.Items.Where(i => i.StoreId == StoreId && !i.IsDeleted))
            .OrderByDescending(x => x.Priority).ThenByDescending(x => x.Id).ToListAsync(ct);
        return units.Select(unit =>
        {
            var variant = unit.ProductVariant;
            var retail = unit.Price is > 0 ? unit.Price.Value : variant.Price is > 0 ? variant.Price.Value : variant.Product.BasePrice;
            var sale = ProductPromotionPrice.SalePrice(retail, campaign.DiscountType, campaign.DiscountValue);
            var minimum = campaign.Items.Where(i => Matches(i, unit)).Min(i => Math.Max(1, i.MinQuantity));
            var winner = competitors.FirstOrDefault(p => p.Items.Any(i => Matches(i, unit) && i.MinQuantity <= minimum));
            var barcode = unit.Barcodes.Where(b => b.StoreId == StoreId).OrderByDescending(b => b.IsPrimary)
                .ThenBy(b => b.Id).FirstOrDefault()?.Barcode ?? "";
            var problem = !Regex.IsMatch(barcode, "^[!-~]{1,80}$") ? "Thiếu mã vạch hợp lệ cho đơn vị này." :
                retail <= 0 ? "Chưa có giá bán lẻ." : sale >= retail ? "Chương trình không làm giảm giá bán." :
                winner?.Id != campaign.Id ? $"POS ưu tiên chương trình: {winner?.Name ?? "khác"}." : null;
            var name = string.IsNullOrWhiteSpace(variant.ProductVariantName) ? variant.Product.Name : variant.ProductVariantName;
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                unit.Id, Name = name, variant.Sku, Barcode = barcode, Unit = unit.Unit.Name, Retail = retail, Sale = sale, Minimum = minimum,
                PromotionId = campaign.Id, PromotionName = campaign.Name, campaign.DiscountType, campaign.DiscountValue,
                campaign.StartAtUtc, campaign.EndAtUtc, campaign.Priority, campaign.RowVersion, Problem = problem
            }))));
            return new PromotionLabelOption(unit.Id, name!, variant.Sku, barcode, unit.Unit.Name, retail, sale, minimum,
                campaign.Id, campaign.Name, campaign.StartAtUtc, campaign.EndAtUtc, fingerprint, problem);
        }).ToList();
    }

    public async Task<List<PromotionLabelPrintItem>> Prepare(PreparePromotionLabels request, CancellationToken ct)
    {
        var lines = request.Lines;
        if (lines is null || lines.Count is 0 or > 500 || lines.Any(x => x is null || x.ConversionId <= 0 || x.Quantity is < 1 or > 1000) ||
            lines.Select(x => x.ConversionId).Distinct().Count() != lines.Count || lines.Sum(x => (long)x.Quantity) > 1000)
            throw new ValidationAppException("Chọn tối đa 500 đơn vị, không trùng đơn vị; tổng 1–1.000 tem mỗi lần.");
        var campaign = await Campaign(request.PromotionId, ct);
        var ids = lines.Select(x => x.ConversionId).ToArray();
        var options = await Options(campaign, await Load(Catalog(campaign.Id).Where(x => ids.Contains(x.Id)), ct), ct);
        return lines.Select(line =>
        {
            var option = options.SingleOrDefault(x => x.ConversionId == line.ConversionId)
                ?? throw new NotFoundAppException("Sản phẩm hoặc đơn vị không còn thuộc chương trình này.");
            if (option.Problem is not null) throw new ConflictAppException(option.Problem);
            if (option.Fingerprint != line.Fingerprint)
                throw new ConflictAppException("Giá, mã vạch hoặc khuyến mãi đã thay đổi. Cập nhật danh sách và kiểm tra lại trước khi in.");
            return new PromotionLabelPrintItem(option, line.Quantity);
        }).ToList();
    }

    public async Task<List<PromotionLabelOption>> Refresh(RefreshPromotionLabels request, CancellationToken ct)
    {
        if (request.ConversionIds is null || request.ConversionIds.Count > 500 || request.ConversionIds.Any(id => id <= 0))
            throw new ValidationAppException("Chọn tối đa 500 đơn vị sản phẩm.");
        var campaign = await Campaign(request.PromotionId, ct);
        var ids = request.ConversionIds.Distinct().ToArray();
        return await Options(campaign, await Load(Catalog(campaign.Id).Where(x => ids.Contains(x.Id)), ct), ct);
    }
}
