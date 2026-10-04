using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Products;

public sealed partial class ReceiptSellingPriceService(AppDbContext db, ITenantContext tenant, ICurrentUser user)
    : IReceiptSellingPriceService
{
    private const string HistoryEntity = "ReceiptSellingPrice";
    private const string ReviewEntity = "ReceiptPriceReview";
    private int StoreId => tenant.StoreId is > 0 ? tenant.StoreId.Value
        : throw new BusinessRuleException("Vui lòng chọn cửa hàng.");

    public async Task<ReceiptSellingPriceDto> GetAsync(int documentId, int lineId, CancellationToken ct)
    {
        var (document, line, variant, units) = await LoadAsync(documentId, lineId, ct);
        var key = variant.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var history = await db.AuditLogs.AsNoTracking()
            .Where(x => x.StoreId == StoreId && x.EntityName == HistoryEntity && x.EntityId == key)
            .OrderByDescending(x => x.Id).Take(30)
            .Select(x => new ReceiptSellingPriceHistoryDto(x.CreatedAtUtc, x.ActorUserName, x.Summary)).ToListAsync(ct);
        return new(document.ReceiptSource, string.IsNullOrWhiteSpace(variant.ProductVariantName)
                ? variant.Product.Name : variant.ProductVariantName,
            variant.Product.BaseUnit?.Name ?? "Đơn vị gốc", Version(variant.Product.RowVersion),
            Version(variant.RowVersion), Version(line.RowVersion), Prices(variant, units), history)
        {
            ProductPrice = variant.Product.BasePrice,
            BaseRetailPrice = EffectivePrice(null, variant),
            ProductPriceUnitId = DefaultPriceUnit(variant, units), CatalogVersion = CatalogVersion(variant, units)
        };
    }

    public async Task<ReceiptSellingPriceDto> UpdateAsync(int documentId, int lineId,
        UpdateReceiptSellingPricesRequest request, CancellationToken ct)
    {
        if (request.Units == null || request.Units.Count > 100 || request.Units.Any(x => x == null) ||
            request.Units.Select(x => x.Id).Distinct().Count() != request.Units.Count ||
            request.Units.Any(x => !ValidMoney(x.Price) || x.TargetPercent is < 0 or > 95 ||
                x.WholesaleTargetPercent is < 0 or > 95 ||
                (x.UpdateWholesalePrice && x.WholesalePrice.HasValue && !ValidMoney(x.WholesalePrice.Value))) ||
            request.EstimatedBaseCost <= 0 || request.EstimatedBaseCost > 999999999999m ||
            request.Basis is not ("margin" or "markup"))
            throw new BusinessRuleException("Giá bán hoặc tỷ lệ lợi nhuận không hợp lệ.");

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (document, line, variant, units) = await LoadAsync(documentId, lineId, ct);
            EnsureVersion(request.ProductVersion, variant.Product.RowVersion);
            EnsureVersion(request.VariantVersion, variant.RowVersion);
            EnsureVersion(request.LineVersion, line.RowVersion);
            if (request.CatalogVersion != CatalogVersion(variant, units))
                throw new BusinessRuleException("Danh mục giá đã thay đổi. Đóng và mở lại bảng giá trước khi lưu.");
            var prices = Prices(variant, units);
            foreach (var change in request.Units)
            {
                var price = prices.SingleOrDefault(x => x.Id == change.Id)
                    ?? throw new BusinessRuleException("Đơn vị bán không thuộc sản phẩm trên phiếu hoặc đã ngừng hoạt động.");
                if (change.RowVersion != price.RowVersion)
                    throw new BusinessRuleException("Giá bán đã thay đổi. Vui lòng mở lại bảng giá.");
            }
            var sourceId = request.ProductPriceUnitId ?? DefaultPriceUnit(variant, units);
            var source = prices.SingleOrDefault(x => x.Id == sourceId)
                ?? throw new BusinessRuleException("Chọn đơn vị giá lẻ để cập nhật Product.");
            var sourcePrice = request.Units.SingleOrDefault(x => x.Id == sourceId)?.Price ?? source.Price;
            var productPrice = decimal.Round(sourcePrice / source.Factor, 2, MidpointRounding.AwayFromZero);
            if (!request.ReviewOnly && !ValidMoney(productPrice))
                throw new BusinessRuleException("Giá Product quy về đơn vị gốc không hợp lệ.");

            var entries = new List<AuditLog>();
            var changed = false;
            var syncProduct = !request.ReviewOnly && productPrice != variant.Product.BasePrice;
            var writes = request.Units.ToList();
            // Keep the source unit's displayed retail price stable when normalizing an inherited Product price.
            var sourceConversion = units.SingleOrDefault(x => x.Id == sourceId);
            if (syncProduct && sourceConversion != null && Positive(sourceConversion.Price) == null &&
                Positive(variant.Price) == null && writes.All(x => x.Id != sourceId))
                writes.Add(new() { Id = sourceId, Price = source.Price, RowVersion = source.RowVersion });
            try
            {
                foreach (var change in writes.Where(_ => !request.ReviewOnly))
                {
                    var unit = units.SingleOrDefault(x => x.Id == change.Id);
                    var before = prices.Single(x => x.Id == change.Id);
                    var wholesale = change.UpdateWholesalePrice ? change.WholesalePrice : before.WholesalePrice;
                    var retailChanged = before.Price != change.Price;
                    var preserveRetail = syncProduct && unit != null && Positive(unit.Price) == null && Positive(variant.Price) == null;
                    if (!retailChanged && wholesale == before.WholesalePrice && !preserveRetail) continue;
                    var now = DateTime.UtcNow;
                    // Preserve an inherited retail price when only the wholesale price changes.
                    var rawRetail = retailChanged || preserveRetail ? change.Price : unit == null ? variant.Price : unit.Price;
                    var affected = unit == null
                        ? await db.ProductVariants.Where(x => x.Id == variant.Id && x.StoreId == StoreId && x.RowVersion == variant.RowVersion)
                            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Price, rawRetail)
                                .SetProperty(x => x.WholesalePrice, wholesale).SetProperty(x => x.UpdatedAtUtc, now)
                                .SetProperty(x => x.UpdatedBy, user.UserId), ct)
                        : await db.ProductUnitConversions.Where(x => x.Id == unit.Id && x.StoreId == StoreId && x.RowVersion == unit.RowVersion)
                            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Price, rawRetail)
                                .SetProperty(x => x.WholesalePrice, wholesale).SetProperty(x => x.UpdatedAtUtc, now)
                                .SetProperty(x => x.UpdatedBy, user.UserId), ct);
                    if (affected != 1) throw new BusinessRuleException("Giá bán đã thay đổi. Vui lòng mở lại bảng giá.");
                    changed = true;
                    entries.Add(Audit(HistoryEntity, variant.Id.ToString(),
                        $"{before.UnitName}: lẻ {before.Price:N2} → {change.Price:N2} đ; sỉ {WholesaleText(before.WholesalePrice)} → {WholesaleText(wholesale)} · Phiếu {document.DocumentNo}",
                        new { ProductUnitConversionId = unit?.Id, Price = before.Price, before.WholesalePrice },
                        new { ProductUnitConversionId = unit?.Id, change.Price, WholesalePrice = wholesale,
                            StockDocumentId = documentId, StockDocumentLineId = lineId,
                            request.EstimatedBaseCost, request.Basis, change.TargetPercent, change.WholesaleTargetPercent }));
                }
                if (syncProduct)
                {
                    var affected = await db.Products.Where(x => x.Id == variant.ProductId && x.StoreId == StoreId && x.RowVersion == variant.Product.RowVersion)
                        .ExecuteUpdateAsync(set => set.SetProperty(x => x.BasePrice, productPrice)
                            .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow).SetProperty(x => x.UpdatedBy, user.UserId), ct);
                    if (affected != 1) throw new BusinessRuleException("Giá Product đã thay đổi. Vui lòng mở lại bảng giá.");
                    changed = true;
                    entries.Add(Audit(HistoryEntity, variant.Id.ToString(),
                        $"Giá Product / {variant.Product.BaseUnit?.Name}: {variant.Product.BasePrice:N2} → {productPrice:N2} đ · Phiếu {document.DocumentNo}",
                        new { ProductId = variant.ProductId, BasePrice = variant.Product.BasePrice },
                        new { ProductId = variant.ProductId, BasePrice = productPrice, StockDocumentId = documentId,
                            StockDocumentLineId = lineId, SourceUnitId = sourceId, SourceRetailPrice = sourcePrice, source.Factor }));
                }
                var reviewKey = ReviewKey(documentId, lineId);
                var prior = await db.AuditLogs.AsNoTracking().Where(x => x.StoreId == StoreId &&
                    x.EntityName == ReviewEntity && x.EntityId == reviewKey).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
                var (_, _, currentVariant, currentUnits) = await LoadAsync(documentId, lineId, ct);
                var review = new ReviewSnapshot(CatalogVersion(currentVariant, currentUnits), request.EstimatedBaseCost,
                    changed || ReadReview(prior?.NewValuesJson)?.HasChanges == true, request.Basis,
                    request.Units.ToDictionary(x => x.Id, x => x.TargetPercent),
                    request.Units.ToDictionary(x => x.Id, x => x.WholesaleTargetPercent));
                entries.Add(Audit(ReviewEntity, reviewKey, changed ? "Đã cập nhật và đánh giá giá bán" : "Đã kiểm tra, giữ giá bán", null, review));
                db.AuditLogs.AddRange(entries);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            finally { foreach (var entry in entries) db.Entry(entry).State = EntityState.Detached; }
        });
        return await GetAsync(documentId, lineId, ct);
    }

    private AuditLog Audit(string entity, string key, string summary, object? before, object after) => new()
    {
        StoreId = StoreId, ActorUserId = user.UserId, ActorUserName = user.UserName,
        Module = AuditModuleType.Pricing, ActionType = entity == ReviewEntity ? AuditActionType.Update : AuditActionType.ChangePrice,
        EntityName = entity, EntityId = key, Summary = summary,
        OldValuesJson = before == null ? null : JsonSerializer.Serialize(before), NewValuesJson = JsonSerializer.Serialize(after),
        ChangedColumnsJson = entity == ReviewEntity ? "[]" : "[\"Price\",\"WholesalePrice\",\"BasePrice\"]",
        CreatedAtUtc = DateTime.UtcNow, IsSuccess = true
    };

    private async Task<(StockDocument Document, StockDocumentLine Line, ProductVariant Variant, List<ProductUnitConversion> Units)>
        LoadAsync(int documentId, int lineId, CancellationToken ct)
    {
        var document = await DocumentAsync(documentId, ct);
        if (document.Status is not (StockDocumentStatus.PendingApproval or StockDocumentStatus.Confirmed))
            throw new BusinessRuleException("Chỉnh giá bán tại phiếu chờ duyệt hoặc đã duyệt.");
        var line = await db.StockDocumentLines.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == lineId && x.StockDocumentId == documentId && !x.IsDeleted, ct)
            ?? throw new BusinessRuleException("Không tìm thấy dòng hàng trên phiếu nhập.");
        var variant = await db.ProductVariants.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .SingleOrDefaultAsync(x => x.Id == line.ProductVariantId && x.StoreId == StoreId && !x.IsDeleted &&
                x.Product.StoreId == StoreId && !x.Product.IsDeleted, ct)
            ?? throw new BusinessRuleException("Sản phẩm không còn trong danh mục.");
        var units = await db.ProductUnitConversions.AsNoTracking().Include(x => x.Unit)
            .Where(x => x.ProductVariantId == variant.Id && x.StoreId == StoreId && !x.IsDeleted && x.IsActive &&
                x.Factor > 0 && x.Unit.StoreId == StoreId && !x.Unit.IsDeleted)
            .OrderBy(x => x.Factor).ThenBy(x => x.Id).ToListAsync(ct);
        return (document, line, variant, units);
    }

    private async Task<StockDocument> DocumentAsync(int documentId, CancellationToken ct) =>
        await db.StockDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.StoreId == StoreId &&
            !x.IsDeleted && x.Type == StockDocumentType.Receipt, ct) ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập.");
    private static List<ReceiptSellingPriceUnitDto> Prices(ProductVariant variant, List<ProductUnitConversion> units) =>
        units.Count == 0
            ? new() { new(0, variant.Product.BaseUnit?.Name ?? "Đơn vị gốc", 1, EffectivePrice(null, variant), Version(variant.RowVersion))
                { WholesalePrice = Positive(variant.WholesalePrice), IsBaseUnit = true } }
            : units.Select(x => new ReceiptSellingPriceUnitDto(x.Id, x.Unit.Name, x.Factor, EffectivePrice(x, variant), Version(x.RowVersion))
                { WholesalePrice = Positive(x.WholesalePrice), IsBaseUnit = x.Factor == 1 && x.UnitId == variant.Product.BaseUnitId }).ToList();
    private static int DefaultPriceUnit(ProductVariant variant, List<ProductUnitConversion> units) =>
        units.FirstOrDefault(x => x.UnitId == variant.Product.BaseUnitId && x.Factor == 1)?.Id ?? units.FirstOrDefault()?.Id ?? 0;
    private static decimal EffectivePrice(ProductUnitConversion? unit, ProductVariant variant) =>
        Positive(unit?.Price) ?? Positive(variant.Price) ?? variant.Product.BasePrice;
    private static decimal? Positive(decimal? price) => price is > 0 ? price : null;
    private static bool ValidMoney(decimal value) => value > 0 && value <= 999999999999m && value == decimal.Round(value, 2);
    private static string WholesaleText(decimal? value) => value.HasValue ? $"{value:N2} đ" : "theo giá lẻ";
    private static string Version(byte[] value) => Convert.ToBase64String(value);
    private static string CatalogVersion(ProductVariant variant, List<ProductUnitConversion> units) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Version(variant.RowVersion) + ":" +
            Version(variant.Product.RowVersion) + ":" + string.Join(";", units.OrderBy(x => x.Id).Select(x => x.Id + ":" + Version(x.RowVersion))))));
    private static void EnsureVersion(string actual, byte[] expected)
    {
        if (string.IsNullOrWhiteSpace(actual) || actual != Version(expected))
            throw new BusinessRuleException("Dữ liệu giá hoặc dòng hàng đã thay đổi. Đóng và mở lại bảng giá trước khi lưu.");
    }
}
