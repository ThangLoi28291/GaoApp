using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GaoApp.Web.Controllers;

// Local Development bridge only. Production integration must use GaoApp's
// authenticated user/store context. Never enable this endpoint publicly.
[ApiController]
[AllowAnonymous]
[Route("api/chatbot/catalog")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ChatbotCatalogController(AppDbContext db, IConfiguration configuration,
    IWebHostEnvironment environment) : ControllerBase
{
    private int StoreId => configuration.GetValue<int>("ChatbotTestApi:StoreId");
    private int WarehouseId => configuration.GetValue<int>("ChatbotTestApi:WarehouseId");
    private int LegalEntityId => configuration.GetValue<int>("ChatbotTestApi:LegalEntityId");

    [HttpGet("context")]
    public async Task<IActionResult> Context(CancellationToken cancellationToken)
    {
        var rejected = await ValidateContextAsync(cancellationToken);
        if (rejected is not null) { return rejected; }
        var store = await db.Stores.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == StoreId, cancellationToken);
        var warehouse = await db.Warehouses.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == WarehouseId && x.StoreId == StoreId, cancellationToken);
        var owner = await db.LegalEntities.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == LegalEntityId && x.StoreId == StoreId, cancellationToken);
        return Ok(new { storeId = StoreId, storeName = store.Name, warehouseId = WarehouseId,
            warehouseName = warehouse.Name, legalEntityId = LegalEntityId, legalEntityName = owner.Name });
    }

    [HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery, Range(1, 200)] int limit = 200,
        [FromQuery, Range(0, int.MaxValue)] int afterVariantId = 0,
        [FromQuery, StringLength(2000)] string? query = null,
        [FromQuery, StringLength(4000)] string? contextIds = null,
        CancellationToken cancellationToken = default)
    {
        var rejected = await ValidateContextAsync(cancellationToken);
        if (rejected is not null) { return rejected; }
        var candidateQuery = db.ProductVariants.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.StoreId == StoreId && !v.IsDeleted && v.IsActive && v.Id > afterVariantId
                && v.Product.StoreId == StoreId && !v.Product.IsDeleted
                && v.Product.IsActive && v.Product.IsSellable);
        if (query is not null)
        {
            var sourceIds = new List<long>();
            foreach (var value in (contextIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!long.TryParse(value, out var id) || id <= 0 || id / 2 > int.MaxValue)
                { return BadRequest(new { message = "Mã ngữ cảnh sản phẩm không hợp lệ." }); }
                sourceIds.Add(id);
            }
            var conversions = sourceIds.Where(id => id % 2 == 0).Select(id => (int)(id / 2)).ToArray();
            var fallbackVariants = sourceIds.Where(id => id % 2 == 1).Select(id => (int)(id / 2)).ToArray();
            var tokens = ChatbotCatalogProjection.SearchTokens(query);
            if (tokens.Count == 0)
            {
                candidateQuery = candidateQuery.Where(v => fallbackVariants.Contains(v.Id)
                    || v.UnitConversions.Any(c => conversions.Contains(c.Id)));
            }
            foreach (var token in tokens)
            {
                var word = token;
                candidateQuery = candidateQuery.Where(v => fallbackVariants.Contains(v.Id)
                    || v.UnitConversions.Any(c => conversions.Contains(c.Id))
                    || v.Sku.ToLower().Contains(word)
                    || (v.ProductVariantNameNormalized != null && v.ProductVariantNameNormalized.Replace(" ", "").Contains(word))
                    || EF.Functions.Collate(v.Product.Name.ToLower().Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains(word)
                    || (v.ProductVariantName != null && EF.Functions.Collate(v.ProductVariantName.ToLower().Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains(word))
                    || v.UnitConversions.Any(c => !c.IsDeleted && c.IsActive && c.StoreId == StoreId
                        && (EF.Functions.Collate(c.Unit.Name.ToLower().Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains(word)
                            || c.Barcodes.Any(b => b.StoreId == StoreId && !b.IsDeleted && b.IsActive && b.Barcode.Contains(word)))));
            }
        }
        var variants = await candidateQuery
            .Include(v => v.Product).ThenInclude(p => p.BaseUnit)
            .Include(v => v.UnitConversions).ThenInclude(c => c.Unit)
            .Include(v => v.UnitConversions).ThenInclude(c => c.Barcodes)
            .AsSplitQuery().OrderBy(v => v.Id).Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = variants.Count > limit;
        var page = variants.Take(limit).ToArray();
        var ids = page.Select(v => v.Id).ToArray();
        var balances = await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.StoreId == StoreId && b.WarehouseId == WarehouseId && !b.IsDeleted
                && ids.Contains(b.ProductVariantId))
            .Select(b => new { b.ProductVariantId, Available = b.OnHandQty - b.ReservedQty })
            .ToListAsync(cancellationToken);
        var available = balances.GroupBy(b => b.ProductVariantId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.Available));
        var items = page.Select(v => ChatbotCatalogProjection.Build(v,
            available.GetValueOrDefault(v.Id), StoreId)).ToArray();
        return Ok(new ChatbotCatalogPage(StoreId, WarehouseId, LegalEntityId,
            DateTimeOffset.UtcNow, items, hasMore, hasMore ? page[^1].Id : null));
    }

    private async Task<IActionResult?> ValidateContextAsync(CancellationToken cancellationToken)
    {
        // Check transport and credentials before querying business data.
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("ChatbotTestApi:Enabled"))
        { return NotFound(); }
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote))
        { return StatusCode(403); }
        var keyPath = configuration["ChatbotTestApi:ApiKeyFilePath"];
        if (string.IsNullOrWhiteSpace(keyPath))
        { return StatusCode(503, new { message = "Chưa cấu hình khóa kết nối chatbot test." }); }
        // Relative configuration follows the web project when source is relocated,
        // regardless of the directory used to launch dotnet, an IDE or a service.
        try { keyPath = Path.GetFullPath(keyPath, environment.ContentRootPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return StatusCode(503, new { message = "Chưa cấu hình khóa kết nối chatbot test." }); }
        if (!System.IO.File.Exists(keyPath))
        { return StatusCode(503, new { message = "Chưa cấu hình khóa kết nối chatbot test." }); }
        var expected = (await System.IO.File.ReadAllTextAsync(keyPath, cancellationToken)).Trim();
        var supplied = Request.Headers["X-Chatbot-Test-Key"].ToString();
        if (expected.Length < 32 || supplied.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
        { return Unauthorized(); }
        if (StoreId <= 0 || WarehouseId <= 0 || LegalEntityId <= 0
            || !await db.Stores.IgnoreQueryFilters().AnyAsync(s => s.Id == StoreId && !s.IsDeleted && s.IsActive, cancellationToken)
            || !await db.LegalEntities.IgnoreQueryFilters().AnyAsync(e => e.Id == LegalEntityId
                && e.StoreId == StoreId && !e.IsDeleted && e.IsActive, cancellationToken)
            || !await db.Warehouses.IgnoreQueryFilters().AnyAsync(w => w.Id == WarehouseId
                && w.StoreId == StoreId && w.LegalEntityId == LegalEntityId && !w.IsDeleted && w.IsActive, cancellationToken))
        { return Conflict(new { message = "Cửa hàng, kho hoặc đơn vị pháp lý test không hợp lệ." }); }
        return null;
    }
}

public sealed record ChatbotCatalogPage(int StoreId, int WarehouseId, int LegalEntityId,
    DateTimeOffset GeneratedAtUtc, IReadOnlyList<ChatbotVariant> Variants,
    bool HasMore, int? NextAfterVariantId);
public sealed record ChatbotVariant(int VariantId, int ProductId, string Sku, string Name,
    IReadOnlyList<ChatbotUnit> Units);
public sealed record ChatbotUnit(int? ConversionId, int UnitId, string UnitName, decimal Factor,
    decimal PriceVnd, decimal AvailableQuantity, bool IsDefaultForSale, IReadOnlyList<string> Barcodes);

public static class ChatbotCatalogProjection
{
    private static readonly HashSet<string> FactWords = new("gia bao nhieu tien con co khong hang ton kho hien tai la may so luong kha dung san pham ma cua giup em toi cho anh chi nay do loai".Split(' '));
    public static IReadOnlyList<string> SearchTokens(string query)
    {
        var normalized = new string(query.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ', 'd');
        return Regex.Matches(normalized, "[a-z0-9]+")
            .Select(match => match.Value).Where(word => !FactWords.Contains(word)).Distinct().Take(12).ToArray();
    }

    public static ChatbotVariant Build(ProductVariant variant, decimal availableQuantity, int storeId)
    {
        var product = variant.Product;
        var parentName = product.Name.Trim();
        var variantName = variant.ProductVariantName?.Trim();
        var name = string.IsNullOrWhiteSpace(variantName) ? parentName
            : variantName.Contains(parentName, StringComparison.OrdinalIgnoreCase) ? variantName
            : $"{parentName} - {variantName}";
        var conversions = variant.UnitConversions.Where(c => c.StoreId == storeId && !c.IsDeleted
                && c.IsActive && c.Factor > 0 && c.Unit is not null && c.Unit.StoreId == storeId
                && !c.Unit.IsDeleted && c.Unit.IsActive && ResolveRetailPrice(variant, c) >= 0)
            .OrderByDescending(c => c.IsDefaultForSale).ThenByDescending(c => c.IsBaseUnit)
            .ThenBy(c => c.SortOrder).ThenBy(c => c.Id).ToArray();
        var preferred = conversions.FirstOrDefault();
        var units = conversions.Select(c => new ChatbotUnit(c.Id, c.UnitId, c.Unit.Name, c.Factor,
            ResolveRetailPrice(variant, c), ConvertAvailableQuantity(availableQuantity, c.Factor),
            c == preferred, c.Barcodes.Where(b => b.StoreId == storeId && !b.IsDeleted && b.IsActive)
                .OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id)
                .Select(b => b.Barcode).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().ToArray())).ToArray();
        if (units.Length == 0 && product.BaseUnit is { IsDeleted: false, IsActive: true } baseUnit
            && baseUnit.StoreId == storeId && ResolveRetailPrice(variant, null) >= 0)
        {
            units = [new ChatbotUnit(null, product.BaseUnitId, baseUnit.Name, 1,
                ResolveRetailPrice(variant, null), Math.Max(0, availableQuantity), true, [])];
        }
        return new ChatbotVariant(variant.Id, product.Id, variant.Sku, name, units);
    }

    // Mirrors the retail fallback in POSService.ResolveSalePriceByTier.
    public static decimal ResolveRetailPrice(ProductVariant variant, ProductUnitConversion? conversion) =>
        conversion?.Price is > 0 ? conversion.Price.Value
        : variant.Price is > 0 ? variant.Price.Value : variant.Product.BasePrice;

    // Base/partial units preserve decimal quantities; a pack contains whole units.
    public static decimal ConvertAvailableQuantity(decimal quantity, decimal factor) =>
        factor > 1 ? Math.Floor(Math.Max(0, quantity) / factor) : Math.Max(0, quantity) / factor;
}
