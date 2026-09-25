using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class ProductVariantCostSqlServerTests
{
    [Fact]
    public async Task Save_rejects_missing_cost_before_writing_any_row_and_accepts_corrected_cost()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var original = await db.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == store.VariantId);
        var productId = original.ProductId;
        var response = await client.JsonAsync(HttpMethod.Get, $"/Admin/Product/VariantsData?productId={productId}");
        var row = response.GetProperty("data").EnumerateArray().Single(v => v.GetProperty("id").GetInt32() == original.Id);
        var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.GetRawText())!;
        values["costPrice"] = 4321;
        values["productVariantName"] = "Tên chỉ được lưu khi cả danh sách hợp lệ";
        foreach (var invalid in new object[] {
            new { sku = "missing-cost", productVariantName = "Chưa nhập giá vốn", isActive = true },
            new { sku = "zero-cost", productVariantName = "Giá vốn bằng không", costPrice = 0, isActive = false },
            new { sku = "negative-cost", productVariantName = "Giá vốn âm", costPrice = -1, isActive = true }
        })
        {
            var rejected = await client.JsonAsync(HttpMethod.Post, "/Admin/Product/SaveVariants",
                new { productId, variants = new object[] { values, invalid } });
            Assert.False(rejected.GetProperty("ok").GetBoolean());
            Assert.Contains("giá vốn lớn hơn 0", rejected.GetProperty("message").GetString());
            var saved = await db.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == original.Id);
            Assert.Equal(original.CostPrice, saved.CostPrice);
            Assert.Equal(original.ProductVariantName, saved.ProductVariantName);
            Assert.Equal(1, await db.ProductVariants.CountAsync(v => v.ProductId == productId));
        }
        var accepted = await client.JsonAsync(HttpMethod.Post, "/Admin/Product/SaveVariants",
            new { productId, variants = new[] { values } });
        Assert.True(accepted.GetProperty("ok").GetBoolean(), accepted.ToString());
        var corrected = await db.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == original.Id);
        Assert.Equal(4321m, corrected.CostPrice);
        Assert.Equal(values["productVariantName"], corrected.ProductVariantName);
    }
}
