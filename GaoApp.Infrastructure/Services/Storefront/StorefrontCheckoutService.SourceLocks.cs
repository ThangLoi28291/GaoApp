using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontCheckoutService
{
    private async Task LockPricingSources(StorefrontQuoteRequest request, CancellationToken ct)
    {
        var input = Normalize(request);
        var products = input.Items.Select(x => x.ProductId).Distinct().Order().ToArray();
        var variants = input.Items.Select(x => x.VariantId).Distinct().Order().ToArray();
        var units = input.Items.Select(x => x.UnitId).Distinct().Order().ToArray();
        await Hold(typeof(GaoApp.Domain.Entities.Store), "[Id]=@store", [], ct, storeEntity: true);
        await Hold(typeof(Product), "[Id] IN ({ids})", products, ct);
        await Hold(typeof(ProductVariant), "[Id] IN ({ids})", variants, ct);
        await Hold(typeof(ProductUnitConversion), "[ProductVariantId] IN ({ids})", variants, ct);
        var productTable = Table(typeof(Product)); var conversionTable = Table(typeof(ProductUnitConversion));
        static string Parameters(int start, int count) => string.Join(',', Enumerable.Range(start, count).Select(index => "@id" + index));
        var unitIds = Parameters(0, units.Length); var productIds = Parameters(units.Length, products.Length); var variantIds = Parameters(units.Length + products.Length, variants.Length);
        await Hold(typeof(Unit), $"[Id] IN ({unitIds}) OR [Id] IN (SELECT [BaseUnitId] FROM {productTable} WHERE [StoreId]=@store AND [Id] IN ({productIds})) OR " +
            $"[Id] IN (SELECT [UnitId] FROM {conversionTable} WHERE [StoreId]=@store AND [ProductVariantId] IN ({variantIds}))", units.Concat(products).Concat(variants).ToArray(), ct);
        await Hold(typeof(LegalEntity), "1=1", [], ct); await Hold(typeof(Warehouse), "1=1", [], ct);
        await Hold(typeof(Promotion), "1=1", [], ct); await Hold(typeof(PromotionItem), "1=1", [], ct); await Hold(typeof(PromotionComboRule), "1=1", [], ct);
        if(input.PaymentMethod==GaoApp.Domain.Storefront.StorefrontPaymentMethod.Qr)
        {
            await Hold(typeof(StoreAcbSettings),"1=1",[],ct);await Hold(typeof(StoreBankAccount),"1=1",[],ct);
            await Hold(typeof(GaoApp.Domain.Storefront.StorefrontBankCollectionAuthority),"1=1",[],ct);
        }
    }
    private string Table(Type type)
    {
        var entity = db.Model.FindEntityType(type) ?? throw new InvalidOperationException("Missing source model.");
        static string Quote(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
        return Quote(entity.GetSchema() ?? "dbo") + "." + Quote(entity.GetTableName()!);
    }
    private async Task Hold(Type type, string predicate, int[] ids, CancellationToken ct, bool storeEntity = false)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction(); command.CommandTimeout = 15;
        var store = command.CreateParameter(); store.ParameterName = "@store"; store.Value = Store; command.Parameters.Add(store);
        var names = new List<string>();
        foreach (var id in ids)
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = "@id" + names.Count; parameter.Value = id; command.Parameters.Add(parameter); names.Add(parameter.ParameterName);
        }
        predicate = predicate.Replace("{ids}", string.Join(',', names), StringComparison.Ordinal);
        // RowVersion is updated by every source edit. HOLDLOCK also protects activation/insert ranges through commit.
        command.CommandText = $"SELECT [Id],[RowVersion] FROM {Table(type)} WITH(HOLDLOCK,ROWLOCK) WHERE " +
            (storeEntity ? "" : "[StoreId]=@store AND ") + "(" + predicate + ") ORDER BY [Id];";
        await using var reader = await command.ExecuteReaderAsync(ct); var count = 0;
        while (await reader.ReadAsync(ct)) Require(++count <= 10000, "Nguồn cấu hình vượt giới hạn kiểm tra. Cửa hàng cần rà lại trước khi nhận đơn.");
    }
}
