using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using GaoApp.Application.Services.Orders;
using GaoApp.Application.Services.Promotions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Promotions;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace GaoApp.Tests.Promotions;

[Collection("SqlServerConcurrency")]
public sealed class PromotionPosCapacitySqlServerTests(ITestOutputHelper output)
{
    // Timings are diagnostic, not pass/fail thresholds: shared machines and SQL cold starts vary.
    [Fact]
    [Trait("Category", "Performance")]
    public async Task Profile_one_hundred_active_promotions_with_real_pos_and_disposable_sql()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var variants = new List<int>();
        int productId, baseUnitId;
        var barcode = "2099611000001";
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var first = await db.ProductVariants.Include(x => x.Product).Include(x => x.UnitConversions)
                .SingleAsync(x => x.Id == store.VariantId);
            productId = first.ProductId;
            baseUnitId = first.Product.BaseUnitId;
            first.Price = 10000;
            first.Product.BasePrice = 10000;
            variants.Add(first.Id);
            var extra = Enumerable.Range(1, 49).Select(i => new ProductVariant
            {
                StoreId = store.StoreId, ProductId = productId, Sku = $"PROMO-CAP-{i:D3}",
                ProductVariantName = $"Promotion capacity variant {i}", Price = 10000, CostPrice = 5000,
                IsActive = true
            }).ToList();
            db.AddRange(extra);
            await db.SaveChangesAsync();
            variants.AddRange(extra.Select(x => x.Id));
            var firstUnit = first.UnitConversions.FirstOrDefault(x => x.Factor == 1 && x.UnitId == baseUnitId);
            if (firstUnit == null)
            {
                firstUnit = new ProductUnitConversion
                {
                    StoreId = store.StoreId, ProductVariantId = first.Id, UnitId = baseUnitId,
                    Factor = 1, Price = 10000, IsBaseUnit = true, IsDefaultForSale = true
                };
                db.Add(firstUnit);
            }
            else firstUnit.Price = 10000;
            foreach (var variant in extra)
            {
                db.Add(new ProductUnitConversion
                {
                    StoreId = store.StoreId, ProductVariantId = variant.Id, UnitId = baseUnitId,
                    Factor = 1, Price = 10000, IsBaseUnit = true, IsDefaultForSale = true
                });
                db.Add(new InventoryBalance
                {
                    StoreId = store.StoreId, WarehouseId = store.WarehouseId,
                    ProductVariantId = variant.Id, OnHandQty = 100
                });
            }
            await db.SaveChangesAsync();
            db.Add(new ProductVariantUnitBarcode
            {
                StoreId = store.StoreId, ProductUnitConversionId = firstUnit.Id,
                Barcode = barcode, IsActive = true, IsPrimary = true
            });
            db.AddRange(BuildPromotions(store.StoreId, productId, baseUnitId, variants));
            await db.SaveChangesAsync();
            await db.Promotions.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
        }

        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var cart = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        var orderId = cart.GetProperty("orderId").GetInt32();
        foreach (var variantId in variants.Take(20))
            cart = await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={variantId}&qty=2", new { });
        var lineId = cart.GetProperty("lines").EnumerateArray()
            .Single(x => x.GetProperty("variantId").GetInt32() == store.VariantId).GetProperty("lineId").GetInt32();

        var profiles = new List<object>();
        var engine = new PromotionEngine(new PromotionTestRepository());
        var recalc = typeof(POSService).GetMethod("Recalc", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Action<Order>>();
        foreach (var activeCount in new[] { 0, 100 })
        {
            await using (var db = app.Database.CreateTenantContext(store.StoreId))
                await db.Promotions.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, activeCount > 0));
            var counter = new QueryCounter();
            await using var readDb = app.Database.CreateTenantContext(store.StoreId, counter);
            var repository = new PromotionRepository(readDb);
            List<Promotion> products = [], combos = [];
            var sqlTimes = new List<double>();
            for (var i = 0; i < 35; i++)
            {
                counter.Count = 0;
                var watch = Stopwatch.StartNew();
                products = await repository.GetActiveProductDiscountPromotionsAsync(store.StoreId);
                combos = await repository.GetActiveComboPromotionsAsync(store.StoreId);
                watch.Stop();
                Assert.Equal(2, counter.Count);
                Assert.Equal(activeCount, products.Count + combos.Count);
                if (i >= 5) sqlTimes.Add(watch.Elapsed.TotalMilliseconds);
            }
            var calculationProfiles = new List<object>();
            foreach (var lineCount in new[] { 20, 50 })
            {
                var order = new Order
                {
                    StoreId = store.StoreId,
                    Lines = variants.Take(lineCount).Select((id, index) => new OrderLine
                    {
                        Id = index + 1, StoreId = store.StoreId, ProductId = productId,
                        VariantId = id, BaseUnitId = baseUnitId, Quantity = 2, Multiplier = 1,
                        UnitPrice = 10000, BaseQuantity = 2
                    }).ToList()
                };
                var times = new List<double>();
                for (var i = 0; i < 530; i++)
                {
                    var watch = Stopwatch.StartNew();
                    await engine.ApplyOrderPromotionsAsync(order, products, combos);
                    recalc(order);
                    watch.Stop();
                    Assert.True(order.GrandTotal > 0);
                    Assert.All(order.Lines.Where(x => !x.IsPromotionGift), x => Assert.Equal(10000, x.UnitPrice));
                    if (i >= 30) times.Add(watch.Elapsed.TotalMilliseconds);
                }
                calculationProfiles.Add(new { lineCount, engineAndTotalMs = Summarize(times) });
            }

            var scanTimes = new List<double>();
            for (var i = 0; i < 35; i++)
            {
                // Restore the same 20-line cart outside the measurement, so each scan adds one unit.
                await client.JsonAsync(HttpMethod.Patch, $"/admin/pos/lines/{lineId}?qty=2", new { });
                var watch = Stopwatch.StartNew();
                cart = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/scan", new { barcode, quantity = 1 });
                watch.Stop();
                Assert.Equal(3, cart.GetProperty("lines").EnumerateArray()
                    .Single(x => x.GetProperty("variantId").GetInt32() == store.VariantId &&
                        !x.GetProperty("isPromotionGift").GetBoolean()).GetProperty("quantity").GetDecimal());
                Assert.True(cart.GetProperty("grandTotal").GetDecimal() > 0);
                if (i >= 5) scanTimes.Add(watch.Elapsed.TotalMilliseconds);
            }
            profiles.Add(new
            {
                activeCount, conditionsLoaded = products.Sum(x => x.Items.Count) + combos.Sum(x => x.ComboRules.Count),
                promotionSqlQueriesPerRecalculation = 2, loadFromSqlMs = Summarize(sqlTimes),
                calculationProfiles, scanHttpWith20LinesMs = Summarize(scanTimes)
            });
        }

        // Historical/expired campaigns must not enter the POS engine.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var expired = BuildPromotions(store.StoreId, productId, baseUnitId, variants);
            foreach (var promotion in expired)
            {
                promotion.Name += " expired";
                promotion.StartAtUtc = DateTime.UtcNow.AddDays(-10);
                promotion.EndAtUtc = DateTime.UtcNow.AddDays(-1);
            }
            db.AddRange(expired);
            await db.SaveChangesAsync();
            var repo = new PromotionRepository(db);
            Assert.Equal(200, await db.Promotions.CountAsync());
            Assert.Equal(100, (await repo.GetActiveProductDiscountPromotionsAsync(store.StoreId)).Count +
                (await repo.GetActiveComboPromotionsAsync(store.StoreId)).Count);
        }
        var report = JsonSerializer.Serialize(new
        {
            note = "Local diagnostic; warmed SQL/HTTP, sequential requests, diagnostic build. No live store data. Not a concurrent-terminal capacity guarantee.",
            programMix = "40 discounts + 20 buy-X-get-Y + 20 required-item combos + 20 mixed-quantity combos; 10 conditions each",
            sqlAndHttpSamples = 30, engineSamples = 500, profiles
        }, new JsonSerializerOptions { WriteIndented = true });
        var reportDirectory = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "pos-promotion-capacity");
        Directory.CreateDirectory(reportDirectory);
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "report.json"), report);
        output.WriteLine(report);
    }

    private static List<Promotion> BuildPromotions(int storeId, int productId, int unitId, List<int> variants)
        => Enumerable.Range(0, 100).Select(index =>
        {
            var type = index < 40 ? PromotionType.ProductDiscount :
                index < 60 ? PromotionType.BuyXGetY : PromotionType.ComboFixedPrice;
            var members = Enumerable.Range(0, 10).Select(i => variants[(index + i) % variants.Count]).ToArray();
            return new Promotion
            {
                StoreId = storeId, Name = $"Capacity campaign {index:D3}", Type = type,
                DiscountType = PromotionDiscountType.Percentage, DiscountValue = 5,
                StartAtUtc = DateTime.UtcNow.AddDays(-1), EndAtUtc = DateTime.UtcNow.AddDays(1), IsActive = true,
                Priority = index % 5, BuyQuantity = 10, GetQuantity = 1,
                ComboPricingMode = index < 80 ? ComboPricingMode.RequiredItems : ComboPricingMode.MixedQuantity,
                ComboQuantity = 12, ComboBaseUnitId = unitId, ComboFixedPrice = index < 80 ? 80000 : 96000,
                Items = type == PromotionType.ComboFixedPrice ? [] : members.Select(id => new PromotionItem
                {
                    StoreId = storeId, ProductId = productId, VariantId = id, MinQuantity = 1
                }).ToList(),
                ComboRules = type != PromotionType.ComboFixedPrice ? [] : members.Select(id => new PromotionComboRule
                {
                    StoreId = storeId, ProductId = productId, VariantId = id, RequiredQuantity = 1
                }).ToList()
            };
        }).ToList();

    private static object Summarize(List<double> samples)
    {
        var ordered = samples.Order().ToArray();
        return new
        {
            median = Math.Round(ordered[ordered.Length / 2], 3),
            p95 = Math.Round(ordered[(int)Math.Ceiling(ordered.Length * .95) - 1], 3),
            max = Math.Round(ordered[^1], 3)
        };
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
