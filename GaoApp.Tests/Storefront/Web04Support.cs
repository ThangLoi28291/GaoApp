using System.Text.Json;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Promotions;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Services.Orders;
using GaoApp.Application.Services.Promotions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Promotions;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GaoApp.Tests.Storefront;

[CollectionDefinition("StorefrontWeb04", DisableParallelization = true)]
public sealed class Web04Collection : ICollectionFixture<Web01Fixture> { }

internal sealed class Web04Clock : TimeProvider
{
    internal DateTime Utc { get; set; } = DateTime.UtcNow;
    public override DateTimeOffset GetUtcNow() => new(Utc);
}

internal sealed record Web04Case(Web01Case Seed, int PackConversion, int PackUnit, string Directory, string AddressFile, string KeysDirectory, Web04Clock Clock)
{
    internal int StoreId => Seed.StoreId;
    internal AppDbContext Context() => Seed.Context();
    internal IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["StorefrontAddressCatalog:Path"] = AddressFile }).Build();
    internal IDataProtectionProvider Protection()
    {
        StorefrontTestResourceMetadata.Provider(KeysDirectory, "GaoApp.Storefront");
        return DataProtectionProvider.Create(new DirectoryInfo(KeysDirectory), b => b.SetApplicationName("GaoApp.Storefront"));
    }
    internal StorefrontCheckoutService Service(AppDbContext db, IDataProtectionProvider? protection = null, TimeProvider? clock = null,
        IInventoryBalanceRepository? balances = null, IPromotionEngine? engine = null, IOrderLegalEntityAllocationService? allocation = null)
    {
        var repository = new PromotionRepository(db);
        return new(db, new StorefrontCatalogService(db), new StorefrontAddressCatalog(Configuration), repository,
            engine ?? new PromotionEngine(repository), balances ?? new InventoryBalanceRepository(db), allocation ?? new OrderLegalEntityAllocationService(), protection ?? Protection(), clock ?? Clock);
    }
    internal StorefrontQuoteRequest Request(decimal quantity = 1, bool pack = false) => new()
    {
        Items = [new() { ProductId = Seed.ProductId, VariantId = Seed.VariantId, UnitId = pack ? PackUnit : Seed.UnitId, Quantity = quantity }],
        RecipientName = "Khách thử nghiệm WEB04", RecipientPhone = "0901234567", ProvinceCode = "01", WardCode = "01001",
        StreetAddress = "123 Đường thử nghiệm", Note = "Dữ liệu fixture, không giao hàng thật"
    };
    internal static StorefrontPlaceOrderRequest PlaceRequest(StorefrontQuoteDto quote, Guid? id = null)
        => new() { ClientRequestId = id ?? Guid.NewGuid(), QuoteToken = quote.Token, Fingerprint = quote.Fingerprint };
    internal async Task<StorefrontQuoteDto> QuoteAsync(StorefrontQuoteRequest? request = null)
    { await using var db = Context(); return await Service(db).QuoteAsync(request ?? Request()); }
    internal async Task<StorefrontPlacementResult> PlaceAsync(StorefrontPlaceOrderRequest request)
    { await using var db = Context(); return await Service(db).PlaceAsync(request); }
    internal async Task SettingsAsync(bool? kg = null, bool? negative = null, bool? cod = null)
    {
        await using var db = Context(); var settings = (await Seed.FullService(db).GetAsync()).Settings;
        settings.IsEnabled = false;
        if (kg.HasValue) settings.ShippingByWeight = kg.Value;
        if (negative.HasValue) settings.AllowNegativeInventory = negative.Value;
        if (cod.HasValue) settings.CodEnabled = cod.Value;
        await Seed.FullService(db).SaveAsync(settings);
        // The actual public release gate remains closed. Only the GUID fixture is enabled.
        await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, true));
    }
    internal async Task StockAsync(decimal onHand, decimal held = 0)
    {
        await using var db = Context();
        await db.InventoryBalances.Where(x => x.WarehouseId == Seed.WarehouseId && x.ProductVariantId == Seed.VariantId)
            .ExecuteUpdateAsync(x => x.SetProperty(b => b.OnHandQty, onHand).SetProperty(b => b.ReservedQty, held));
    }
    internal async Task RateAsync(decimal fee = 20000, decimal? free = null, bool national = false, string? ward = null)
    {
        await using var db = Context(); var page = await Seed.FullService(db).GetAsync();
        var zone = page.Zones.Single(x => x.Code == (national ? "NATIONAL" : "LOCAL"));
        await Seed.FullService(db).SaveZoneAsync(new() { Id = zone.Id, Version = zone.Version, SettingsVersion = page.Settings.Version,
            Code = zone.Code, Name = zone.Name, ProvinceCode = zone.ProvinceCode, WardCode = ward ?? zone.WardCode, DeliveryMethod = zone.DeliveryMethod,
            IsActive = true, MinDeliveryDays = zone.MinDeliveryDays, MaxDeliveryDays = zone.MaxDeliveryDays, BaseFee = fee,
            AdditionalFee = 5000, FirstWeightKg = 1, AdditionalStepKg = 0.5m, FreeFromGoodsAmount = free });
    }
    internal async Task WriteAddressAsync(string version = "SYNTHETIC-W04-V1")
        => await File.WriteAllTextAsync(AddressFile, JsonSerializer.Serialize(new { version, source = "WEB04 synthetic isolated fixture, not production geography", provinces = new[] {
            new { code = "01", name = "Tỉnh thử nghiệm A", wards = new[] { new { code = "01001", name = "Phường thử nghiệm A1" }, new { code = "01002", name = "Phường thử nghiệm A2" } } },
            new { code = "02", name = "Tỉnh thử nghiệm B", wards = new[] { new { code = "02001", name = "Phường thử nghiệm B1" }, new { code = "02002", name = "Phường thử nghiệm B2" } } } } }));
    internal async Task<string> CountsAsync()
    {
        await using var db = Context();
        return JsonSerializer.Serialize(new { Orders = await db.StorefrontOrders.CountAsync(), Lines = await db.Set<StorefrontOrderLine>().CountAsync(),
            Holds = await db.InventoryReservations.CountAsync(), Links = await db.Set<StorefrontReservationLink>().CountAsync(),
            Receipts = await db.Set<StorefrontCommandReceipt>().CountAsync(), Events = await db.Set<StorefrontEvent>().CountAsync(),
            Outbox = await db.Set<StorefrontOutboxMessage>().CountAsync(), Fulfillments = await db.Set<StorefrontFulfillmentSource>().CountAsync(),
            Pos = await db.Orders.CountAsync(), Shifts = await db.POSShifts.CountAsync(), Terminals = await db.POSTerminals.CountAsync(),
            Physical = await db.InventoryTransactions.CountAsync(), Payments = await db.OrderPayments.CountAsync(), Menus = await db.AdminMenuItems.CountAsync(),
            Balances = await db.InventoryBalances.OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty, x.ReservedQty, x.InventoryValue, x.AverageUnitCost }).ToListAsync() });
    }
}

internal static class Web04Support
{
    internal static string Artifact(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(Web03Support.Root(), ".artifacts/storefront-rollout/WEB04/tests", relative));
        StorefrontTestResourceMetadata.BeforeDirectory(Path.GetDirectoryName(path)!, "WEB04 artifact parent");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    internal static async Task<Web04Case> Web04Async(this Web01Fixture fixture)
    {
        var seed = await fixture.CaseAsync(); var pack = await seed.PackAsync(); await seed.EnableSyntheticCatalogAsync();
        var directory = Artifact("runtime/" + Guid.NewGuid().ToString("N")); StorefrontTestResourceMetadata.BeforeDirectory(directory, "WEB04 checkout runtime"); System.IO.Directory.CreateDirectory(directory);
        var keys = Path.Combine(directory, "keys"); StorefrontTestResourceMetadata.BeforeDirectory(keys, "WEB04 checkout key root"); System.IO.Directory.CreateDirectory(keys);
        var c = new Web04Case(seed, pack.Conversion, pack.Unit, directory, Path.Combine(directory, "synthetic-addresses.json"), keys, new()); await c.WriteAddressAsync();
        await using var db = seed.Context();
        var product = await db.Products.SingleAsync(x => x.Id == seed.ProductId); product.BasePrice = 10000;
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.VariantId); variant.Price = 20000; variant.WholesalePrice = 1;
        var conversion = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.ConversionId); conversion.Price = 25000; conversion.IsBaseUnit = true;
        db.InventoryBalances.Add(new() { StoreId = seed.StoreId, WarehouseId = seed.WarehouseId, ProductVariantId = seed.VariantId, OnHandQty = 100 });
        await db.SaveChangesAsync(); await seed.Products(db).SaveAsync(seed.ProductId, seed.Publication(pack: pack.Conversion));
        var local = await seed.ZoneAsync("LOCAL", "01"); local.Name = "Giao gần fixture"; await seed.FullService(db).SaveZoneAsync(local);
        var national = await seed.ZoneAsync("NATIONAL", "", "", StorefrontDeliveryMethod.Nationwide); national.Name = "Giao toàn quốc fixture"; national.BaseFee = 30000; await seed.FullService(db).SaveZoneAsync(national);
        return c;
    }
    internal static string GuestToken(StorefrontPlacedOrder order)
    { Assert.StartsWith("/shop/orders#", order.GuestLink); return order.GuestLink.Split('#')[1]; }
}
