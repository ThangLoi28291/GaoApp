using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Products;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Assistant;

public sealed class AssistantCatalogService(AppDbContext db, TimeProvider clock, AssistantSearchKnowledge? knowledge = null)
{
    private IQueryable<ProductVariant> Query(AssistantIdentity identity)
    {
        var store = AssistantAccess.Id(identity.Scope.StoreId);
        return db.ProductVariants.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.StoreId == store && !v.IsDeleted && v.IsActive
                && v.Product.StoreId == store && !v.Product.IsDeleted && v.Product.IsActive)
            .Include(v => v.Product).ThenInclude(p => p.BaseUnit)
            .Include(v => v.Product).ThenInclude(p => p.Brand)
            .Include(v => v.Product).ThenInclude(p => p.Category)
            .Include(v => v.UnitConversions).ThenInclude(c => c.Unit).AsSplitQuery();
    }
    public AssistantSource Source() { var now = clock.GetUtcNow(); return new("gaoapp", "GaoApp", now.UtcTicks.ToString(), now); }
    // Presentation IDs come from verified ChatbotLocal state, never from text or browser-supplied IDs.
    // SKU/photos/attributes are read in the current GaoApp store. Price time remains the original source time.
    public async Task<(JsonObject? Display, string[] RequiredPermissions)> DisplayAsync(AssistantIdentity identity, JsonElement history, CancellationToken ct)
    {
        if (!history.TryGetProperty("productDisplay", out var raw) || raw.ValueKind == JsonValueKind.Null) return (null, []);
        ProductDisplay? display;
        try { display = raw.Deserialize<ProductDisplay>(new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { throw InvalidDisplay(); }
        if (display is null || display.Kind is not ("choices" or "quote" or "product") || display.Items is null
            || display.Items.Length is < 1 or > 5 || display.MessageId is null || display.ExpiresAt == default
            || display.Items.Any(i => i is null || i.Product is null || string.IsNullOrWhiteSpace(i.Product.Name)
                || i.Product.Name.Length > 300 || i.Product.Name.Any(char.IsControl)
                || !int.TryParse(i.Product.ProductId, out var productId) || productId <= 0
                || !int.TryParse(i.Product.VariantId, out var variantId) || variantId <= 0)
            || display.Items.Select(i => i.Product.VariantId).Distinct().Count() != display.Items.Length) throw InvalidDisplay();
        if (display.Scope != identity.Scope) throw new AssistantException(502, "source_scope_mismatch", "Kết quả sản phẩm không thuộc cửa hàng hiện tại.");
        if (!history.TryGetProperty("items", out var messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() == 0
            || !messages[messages.GetArrayLength()-1].TryGetProperty("messageId", out var lastId) || lastId.GetString() != display.MessageId
            || !messages[messages.GetArrayLength()-1].TryGetProperty("role", out var role) || role.GetString() != "assistant") throw InvalidDisplay();
        if (display.Kind != "choices" && (display.Items.Length != 1 || display.HasMore || display.Items[0].Choice is not null)
            || display.Kind == "choices" && display.Items.Any(i => i.Choice is not (>= 1 and <= 100))
            || display.Kind == "choices" && display.Items.Select(i => i.Choice).Distinct().Count() != display.Items.Length) throw InvalidDisplay();
        if (display.Kind == "quote" && (display.PriceReadAt is null || display.PriceReadAt > clock.GetUtcNow().AddMinutes(1)
            || display.PriceTiers is null || display.PriceTiers.Length is < 1 or > 2 || display.PriceTiers.Any(t => t is not ("retail" or "wholesale"))
            || display.PriceTiers.Distinct().Count() != display.PriceTiers.Length)
            || display.Kind != "quote" && (display.PriceReadAt is not null || display.PriceTiers is not null)) throw InvalidDisplay();
        var required = display.Kind == "quote" ? new[] { AssistantPermissions.Catalog, AssistantPermissions.Retail }
            .Concat(display.PriceTiers!.Contains("wholesale") ? [AssistantPermissions.Wholesale] : Array.Empty<string>()).ToArray() : [AssistantPermissions.Catalog];
        AssistantAccess.Require(identity.Permissions, required);
        var cards = new JsonArray();
        foreach (var item in display.Items)
        {
            ProductVariant variant;
            try { variant = await FindAsync(identity, item.Product.VariantId, ct); }
            catch (AssistantException ex) when (ex.Status == 404) { return (null, required); }
            var current = Build(variant, false);
            if (variant.ProductId.ToString() != item.Product.ProductId) throw InvalidDisplay();
            if (current.Product.Name != item.Product.Name) return (null, required);
            var store = AssistantAccess.Id(identity.Scope.StoreId);
            var images = await db.ProductImages.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.StoreId == store && p.ProductId == variant.ProductId && !p.IsDeleted
                    && p.MediaAsset.StoreId == store && !p.MediaAsset.IsDeleted && !p.MediaAsset.IsTemp)
                .OrderByDescending(p => p.Id == variant.PrimaryProductImageId).ThenByDescending(p => p.IsPrimary)
                .ThenBy(p => p.SortOrder).ThenBy(p => p.Id)
                .Select(p => new { p.MediaAsset.StoragePath, p.MediaAsset.ContentType }).ToArrayAsync(ct);
            var image = images.Where(p => p.ContentType is "image/jpeg" or "image/png" or "image/webp" or "image/gif")
                .Select(p => SafeImagePath(p.StoragePath)).FirstOrDefault(p => p is not null);
            var sku = string.IsNullOrWhiteSpace(variant.Sku) || variant.Sku.Length > 60 || variant.Sku.Any(char.IsControl) ? null : variant.Sku.Trim();
            cards.Add(JsonSerializer.SerializeToNode(new { product = current.Product, item.Choice, sku,
                imageUrl = image, attributes = current.Attributes,
                saleUnits = current.Units.Where(u => u.Unit.CanSell).Select(u => u.Unit.Name).ToArray() }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        var output = JsonNode.Parse(raw.GetRawText())!.AsObject();
        output["items"] = cards;
        return (output, required);
    }
    public static string? SafeImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 260 || !path.StartsWith("uploads/products/", StringComparison.OrdinalIgnoreCase)
            || path.Any(c => char.IsControl(c) || c is '\\' or ':' or '%' or '?' or '#')) return null;
        var parts = path.Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or "..") || !Regex.IsMatch(parts[^1], @"\.(?:jpe?g|png|webp|gif)$", RegexOptions.IgnoreCase)) return null;
        return "/" + string.Join('/', parts.Select(Uri.EscapeDataString));
    }
    private static AssistantException InvalidDisplay() => new(502, "invalid_assistant_response", "Thông tin thẻ sản phẩm chưa hợp lệ.");
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ProductDisplay([property: JsonRequired] AssistantScope Scope, [property: JsonRequired] string Kind,
        [property: JsonRequired] DisplayItem[] Items, [property: JsonRequired] DateTimeOffset ExpiresAt, bool HasMore,
        DateTimeOffset? PriceReadAt, string[]? PriceTiers, [property: JsonRequired] string MessageId);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record DisplayItem([property: JsonRequired] CatalogProduct Product, int? Choice);
    public async Task<string?> ApprovedAliasAsync(AssistantIdentity identity, string query, CancellationToken ct)
    {
        AssistantAccess.Require(identity.Permissions, AssistantPermissions.Catalog);
        if (string.IsNullOrWhiteSpace(query) || query.Length > 2000 || query.Any(char.IsControl)) throw AssistantException.Invalid();
        if (knowledge is null || !AssistantSearchKnowledge.ValidQuery(query)) return null;
        var entries = (await knowledge.ReadAsync(AssistantAccess.Id(identity.Scope.StoreId), ct)).Entries
            .Where(e => e.Status == "approved" && AssistantCatalogSearch.Key(e.Query) == AssistantCatalogSearch.Key(query)).ToArray();
        if (entries.Length == 0) return null;
        var ids = entries.Select(e => e.VariantId).Distinct().ToArray();
        var products = await Query(identity).Where(v => ids.Contains(v.Id)).Select(v => new { v.Id, v.ProductId }).ToArrayAsync(ct);
        return entries.Any(e => products.Any(p => p.Id == e.VariantId && p.ProductId == e.ProductId)) ? query.Trim() : null;
    }
    public async Task<SearchDocument[]> SearchDocumentsAsync(AssistantIdentity identity, CancellationToken ct)
    {
        AssistantAccess.Require(identity.Permissions, AssistantPermissions.Catalog);
        var store = AssistantAccess.Id(identity.Scope.StoreId);
        // Project only names/identities. Unit conversions and prices are loaded after ranking.
        var rows = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.StoreId == store && v.IsActive && !v.IsDeleted && v.Product.StoreId == store && v.Product.IsActive && !v.Product.IsDeleted)
            .OrderBy(v => v.Id).Select(v => new {
                v.Id, v.ProductId, ProductName = v.Product.Name, VariantName = v.ProductVariantName, v.Sku,
                Brand = v.Product.Brand != null && v.Product.Brand.StoreId == store && v.Product.Brand.IsActive && !v.Product.Brand.IsDeleted ? v.Product.Brand.Name : null,
                Family = v.Product.Category != null && v.Product.Category.StoreId == store && v.Product.Category.IsActive && !v.Product.Category.IsDeleted ? v.Product.Category.Name : null
            }).Take(AssistantCatalogSearch.MaximumDocuments + 1).ToArrayAsync(ct);
        if (rows.Length > AssistantCatalogSearch.MaximumDocuments)
            throw new AssistantException(503, "search_catalog_limit", "Danh mục vượt giới hạn chỉ mục hiện tại. Cần kiểm tra trước khi mở rộng.");
        var aliases = knowledge is null ? [] : (await knowledge.ReadAsync(store, ct)).Entries.Where(e => e.Status == "approved").ToArray();
        var byVariant = aliases.GroupBy(a => a.VariantId).ToDictionary(g => g.Key, g => g.ToArray());
        return rows.Select(v => {
            var name = string.IsNullOrWhiteSpace(v.VariantName) ? v.ProductName : v.VariantName!;
            if (!name.Contains(v.ProductName, StringComparison.OrdinalIgnoreCase)) name = v.ProductName + " - " + name;
            name = name[..Math.Min(300, name.Length)];
            return new SearchDocument(v.Id, v.ProductId, name, v.Sku ?? "", AssistantCatalogSearch.Infer(name, v.Brand, v.Family),
                (byVariant.GetValueOrDefault(v.Id) ?? []).Where(a => a.ProductId == v.ProductId).Select(a => a.Query).Distinct().ToArray());
        }).ToArray();
    }
    public async Task<(CatalogVariant[] Variants, string? Cursor, CatalogSearchInfo? Search)> SearchAsync(AssistantIdentity identity, CatalogSearch input, CancellationToken ct)
    {
        AssistantAccess.Require(identity.Permissions, AssistantPermissions.Catalog);
        if (string.IsNullOrWhiteSpace(input.Query) || input.Query.Length > 2000 || input.Query.Any(char.IsControl) || input.Limit is < 1 or > 100
            || input.ContextVariantIds?.Length > 100 || input.Cursor?.Length > 512) throw AssistantException.Invalid();
        if (input.Query.StartsWith("barcode:", StringComparison.Ordinal)) {
            var barcode = await SearchBarcodeAsync(identity, input, ct); return (barcode.Variants, barcode.Cursor, null);
        }
        if (input.Attributes is { } filter && (new[] { filter.Brand, filter.Family, filter.Capacity, filter.Flavor }.Any(v => v is not null && (string.IsNullOrWhiteSpace(v) || v.Length > 120 || v.Any(char.IsControl)))
            || filter.Sugar is not (null or "sweetened" or "less_sugar" or "unsweetened" or "not_applicable"))) throw AssistantException.Invalid();
        var documents = await SearchDocumentsAsync(identity, ct);
        var ranked = AssistantCatalogSearch.Rank(documents, input, identity.Scope.StoreId);
        var ids = ranked.Hits.Select(h => h.Document.Id).ToArray();
        var fresh = ids.Length == 0 ? [] : await Query(identity).Where(v => ids.Contains(v.Id)).ToArrayAsync(ct);
        var matches = new List<CatalogVariant>();
        foreach (var hit in ranked.Hits) {
            var variant = fresh.SingleOrDefault(v => v.Id == hit.Document.Id); if (variant is null) continue;
            var item = Build(variant, identity.Permissions.Contains(AssistantPermissions.Retail));
            // A name change between retrieval and hydration invalidates the page instead of returning stale identities.
            if (item.Product.Name != hit.Document.Name) throw new AssistantException(409, "catalog_changed", "Danh mục vừa thay đổi. Mình tìm lại sản phẩm nhé.");
            matches.Add(item);
        }
        return (matches.ToArray(), ranked.Cursor, ranked.Search);
    }
    private async Task<(CatalogVariant[] Variants, string? Cursor)> SearchBarcodeAsync(AssistantIdentity identity, CatalogSearch input, CancellationToken ct)
    {
        var barcode = input.Query[8..];
        if (barcode.Length is < 1 or > 64 || barcode.Any(c => c < 33 || c > 126)) throw AssistantException.Invalid();
        var last = 0;
        if (input.Cursor is { } cursor && (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out last) || last < 0))
            throw AssistantException.Invalid();
        var store = AssistantAccess.Id(identity.Scope.StoreId);
        var variants = await Query(identity).Where(v => v.Id > last &&
            db.ProductVariantUnitBarcodes.IgnoreQueryFilters().Any(b => b.StoreId == store && !b.IsDeleted && b.IsActive && b.Barcode == barcode
                && b.ProductUnitConversion.StoreId == store && !b.ProductUnitConversion.IsDeleted && b.ProductUnitConversion.IsActive
                && b.ProductUnitConversion.Factor > 0 && b.ProductUnitConversion.ProductVariantId == v.Id
                && b.ProductUnitConversion.Unit.StoreId == store && !b.ProductUnitConversion.Unit.IsDeleted && b.ProductUnitConversion.Unit.IsActive))
            .OrderBy(v => v.Id).Take(input.Limit + 1).ToArrayAsync(ct);
        return (variants.Take(input.Limit).Select(v => Build(v, identity.Permissions.Contains(AssistantPermissions.Retail))).ToArray(),
            variants.Length > input.Limit ? variants[input.Limit - 1].Id.ToString(CultureInfo.InvariantCulture) : null);
    }

    public async Task<ProductVariant> FindAsync(AssistantIdentity identity, string id, CancellationToken ct)
    {
        AssistantAccess.Require(identity.Permissions, AssistantPermissions.Catalog);
        var key = AssistantAccess.Id(id);
        return await Query(identity).SingleOrDefaultAsync(v => v.Id == key, ct)
            ?? throw new AssistantException(404, "variant_not_found", "Không tìm thấy sản phẩm được phép truy cập.");
    }
    public async Task<QuotedItem[]> QuoteAsync(AssistantIdentity identity, QuoteLookup request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Length is < 1 or > 20 || request.Tiers is null || request.Tiers.Length is < 1 or > 2
            || request.Tiers.Any(t => t is not ("retail" or "wholesale")) || request.Tiers.Distinct().Count() != request.Tiers.Length
            || request.Items.Any(i => i is null) || request.Items.Distinct().Count() != request.Items.Length) throw AssistantException.Invalid();
        AssistantAccess.Require(identity.Permissions, AssistantPermissions.Catalog, AssistantPermissions.Retail);
        if (request.Tiers.Contains("wholesale")) AssistantAccess.Require(identity.Permissions, AssistantPermissions.Wholesale);
        var quotes = new List<QuotedItem>();
        foreach (var input in request.Items)
        {
            var v = await FindAsync(identity, input.VariantId, ct);
            if (v.ProductId != AssistantAccess.Id(input.ProductId)) throw InvalidSelection();
            var variant = Build(v, true);
            var unit = variant.Units.SingleOrDefault(u => u.Unit.UnitId == input.UnitId && u.Unit.ConversionId == input.ConversionId)?.Unit;
            if (unit is null || !unit.CanSell) throw InvalidSelection();
            var conversion = input.ConversionId is { } cid ? v.UnitConversions.Single(c => c.Id == AssistantAccess.Id(cid)) : null;
            var prices = request.Tiers.Select(t => Price(v, conversion, t)).ToArray();
            quotes.Add(new(variant.Product, unit, "VND", prices));
        }
        return quotes.ToArray();
    }
    public static QuotedPrice Price(ProductVariant v, ProductUnitConversion? unit, string tier)
    {
        var rawWholesale = unit is null ? v.WholesalePrice : unit.WholesalePrice;
        var rawRetail = unit?.Price ?? v.Price ?? v.Product.BasePrice;
        if (tier == "wholesale" && rawWholesale is < 0 || rawRetail < 0)
            return new(tier, null, "error", null, null, "invalid_stored_price");
        var wholesale = tier == "wholesale" ? ProductSalePricePolicy.Wholesale(v, unit) : null;
        var value = wholesale ?? ProductSalePricePolicy.Retail(v, unit);
        if (value is null) return new(tier, null, "missing", null);
        var fallback = tier == "wholesale" && wholesale is null;
        return new(tier, fallback ? "retail" : tier, "available", Decimal(value.Value), fallback ? "wholesale_missing" : null);
    }
    public static CatalogVariant Build(ProductVariant v, bool retailAllowed)
    {
        var product = v.Product;
        if (product.BaseUnit is null || product.BaseUnit.IsDeleted || !product.BaseUnit.IsActive || product.BaseUnit.StoreId != v.StoreId)
            throw new AssistantException(503, "catalog_source_unavailable", "Sản phẩm chưa có đơn vị gốc hợp lệ.");
        var conversions = v.UnitConversions.Where(c => c.StoreId == v.StoreId && c.IsActive && !c.IsDeleted && c.Factor > 0
            && c.Unit is not null && c.Unit.IsActive && !c.Unit.IsDeleted && c.Unit.StoreId == v.StoreId)
            .OrderByDescending(c => c.IsBaseUnit).ThenBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
        var baseConversion = conversions.FirstOrDefault(c => c.UnitId == product.BaseUnitId && c.Factor == 1);
        CatalogUnit Unit(ProductUnitConversion? c) => new((c?.UnitId ?? product.BaseUnitId).ToString(), c?.Id.ToString(),
            c?.Unit.Name ?? product.BaseUnit.Name, Decimal(c?.Factor ?? 1), c == baseConversion && c is not null || c is null,
            c is not null, product.IsSellable);
        var units = conversions.Select(c => new CatalogSaleUnit(Unit(c), Retail(v, c, retailAllowed))).ToList();
        if (baseConversion is null) units.Insert(0, new(Unit(null), Retail(v, null, retailAllowed)));
        if (units.Count > 100) throw new AssistantException(503, "catalog_source_unavailable", "Sản phẩm vượt giới hạn đơn vị của hợp đồng hiện tại.");
        var name = string.IsNullOrWhiteSpace(v.ProductVariantName) ? product.Name : v.ProductVariantName!;
        if (!name.Contains(product.Name, StringComparison.OrdinalIgnoreCase)) name = product.Name + " - " + name;
        name = name[..Math.Min(name.Length, 300)];
        var normalized = Normalize(name);
        var inferred = AssistantCatalogSearch.Infer(name);
        var brand = product.Brand is { IsActive: true, IsDeleted: false } b && b.StoreId == v.StoreId ? b.Name : null;
        if (string.IsNullOrWhiteSpace(brand))
        {
            if (Regex.IsMatch(normalized, @"\bth\b")) brand = "TH";
            else if (normalized.Contains("vinamilk")) brand = "Vinamilk";
        }
        return new(new(product.Id.ToString(), v.Id.ToString(), name), new() {
            Brand = AssistantCatalogSearch.Infer(name, brand).Brand, Family = normalized.Contains("sua tuoi") ? "sữa tươi"
                : product.Category is { IsActive: true, IsDeleted: false } category && category.StoreId == v.StoreId ? Short(category.Name) : null,
            Sugar = inferred.Sugar, Capacity = inferred.Capacity },
            Unit(baseConversion), units.ToArray(), true);
    }
    private static string? Short(string? text) => string.IsNullOrWhiteSpace(text) ? null : text[..Math.Min(120, text.Length)];
    private static CatalogPrice Retail(ProductVariant v, ProductUnitConversion? c, bool allowed)
    { if (allowed && (c?.Price ?? v.Price ?? v.Product.BasePrice) < 0) return new("error", null, "invalid_stored_price");
        var price = allowed ? ProductSalePricePolicy.Retail(v, c) : null; return new(price is null ? "missing" : "available", price is { } p ? Decimal(p) : null); }
    public static string Decimal(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    public static string Normalize(string text) => new string(text.ToLowerInvariant().Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ', 'd');
    private static bool Matches(CatalogAttributes value, CatalogAttributes? filter)
    {
        if (filter is null) return true;
        bool Same(string? actual, string? expected) => expected is null || actual is not null && Normalize(actual).Contains(Normalize(expected));
        bool Brand(string? actual, string? expected) => expected is null || actual is not null
            && Regex.IsMatch(Normalize(actual), @"\b" + Regex.Escape(Normalize(expected)) + @"\b");
        return Brand(value.Brand, filter.Brand) && Same(value.Family, filter.Family)
            && (filter.Sugar is null || value.Sugar == filter.Sugar)
            && (filter.Capacity is null || value.Capacity is not null && Normalize(value.Capacity) == Normalize(filter.Capacity))
            && Same(value.Flavor, filter.Flavor);
    }
    private static AssistantException InvalidSelection() => new(422, "invalid_selection", "Sản phẩm hoặc đơn vị không còn hợp lệ.");
    private static readonly HashSet<string> Stop = new("gia bao nhieu tien con co khong hang ton kho hien tai la may so luong kha dung san pham ma cua giup em toi cho anh chi nay do loai".Split(' '));
}
