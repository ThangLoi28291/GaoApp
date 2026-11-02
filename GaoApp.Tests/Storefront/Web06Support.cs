using System.Globalization;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Common;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

[CollectionDefinition("StorefrontWeb06", DisableParallelization = true)]
public sealed class Web06Collection : ICollectionFixture<Web01Fixture> { }

internal static class Web06Support
{
    internal static string Artifact(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(Web03Support.Root(), ".artifacts/storefront-rollout/WEB06/tests", relative));
        StorefrontTestResourceMetadata.BeforeDirectory(Path.GetDirectoryName(path)!, "WEB06 artifact parent");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    internal static string Runtime(string purpose)
    {
        var path = Artifact("runtime/" + Guid.NewGuid().ToString("N")); StorefrontTestResourceMetadata.BeforeDirectory(path, purpose); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "fixture-purpose.txt"), purpose); return path;
    }
    internal static string Quantity(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    internal static async Task AssertPurchasedTwoGiftParityAsync(Web06Case c, decimal expectedGoods)
    {
        await using var db = c.Context(); var request = c.Checkout.Request(2m);
        var pos = await Web04PosOracle.PriceAsync(db, request.Items);
        Assert.Equal(expectedGoods, pos.Order.GrandTotal); Assert.Equal(expectedGoods, pos.Approved.Sum(x => x.Net));
        Assert.Equal(2m, Assert.Single(pos.Order.Lines, x => !x.IsPromotionGift).Quantity);
        Assert.Equal(1m, Assert.Single(pos.Order.Lines, x => x.IsPromotionGift).Quantity);
        var lines = await db.Set<StorefrontOrderLine>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync();
        Assert.Equal(expectedGoods, lines.Sum(x => x.NetAmount)); Assert.Equal(3m, lines.Sum(x => x.BaseQuantity));
        Assert.Equal(2m, Assert.Single(lines, x => !x.IsGift).Quantity); Assert.Equal(1m, Assert.Single(lines, x => x.IsGift).Quantity);
        var sources = await (from link in db.Set<StorefrontReservationLink>()
                             join hold in db.InventoryReservations on link.InventoryReservationId equals hold.Id
                             join source in db.Set<StorefrontFulfillmentSource>() on hold.StorefrontFulfillmentSourceId equals (int?)source.Id
                             where link.StorefrontOrderId == c.OrderId && source.StorefrontOrderId == c.OrderId
                             select new { source.SourceWarehouseId, source.SourceLegalEntityId, link.OrderLineId,
                                 HoldLineId = hold.StorefrontOrderLineId, hold.WarehouseId, HoldOwner = hold.SourceLegalEntityId,
                                 hold.ReferenceType, hold.Status, BaseQuantity = hold.ReservedQty }).ToListAsync();
        Assert.All(sources, row =>
        {
            Assert.Equal(InventoryReferenceType.Storefront, row.ReferenceType); Assert.Equal(InventoryReservationStatus.Active, row.Status);
            Assert.Equal((int?)row.OrderLineId, row.HoldLineId); Assert.Equal(row.SourceWarehouseId, row.WarehouseId);
            Assert.Equal((int?)row.SourceLegalEntityId, row.HoldOwner);
        });
        Assert.Equal(2m, sources.Where(x => x.SourceWarehouseId == c.Seed.WarehouseId).Sum(x => x.BaseQuantity));
        Assert.Equal(1m, sources.Where(x => x.SourceWarehouseId != c.Seed.WarehouseId).Sum(x => x.BaseQuantity));
        Assert.Equal(3m, await db.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.ReservedQty));
        Assert.Empty(await db.Set<StorefrontLineSource>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
    }
    internal static readonly string[] PhysicalPermissions = [StorefrontPermissions.ViewOrders, StorefrontPermissions.Prepare,
        StorefrontPermissions.Dispatch, StorefrontPermissions.RecordDelivery, StorefrontPermissions.ReceiveReturns, StorefrontPermissions.Reconcile];
}

// Global evidence deliberately has no tenant filter. Inspect the actual case
// physical account across every origin and retain global before/after budgets;
// unrelated serial cases must not be mistaken for evidence created by this one.
internal sealed class Web06BankInspection
{
    private sealed record Totals(int Receipts, decimal Amount, int Consumptions, int Conflicts);
    private readonly int storeId;
    private readonly string account;
    private readonly Totals before;
    private Web06BankInspection(int storeId, string account, Totals before) { this.storeId = storeId; this.account = account; this.before = before; }
    internal static async Task<Web06BankInspection> CaptureAsync(Web06Case c)
    {
        await using var db = c.Context(); var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == c.Seed.BankAccountId);
        return new(c.StoreId, GaoApp.Infrastructure.Services.Banking.GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber), await ReadAsync(db));
    }
    internal IQueryable<BankVerifiedReceipt> Receipts(AppDbContext db) => db.Set<BankVerifiedReceipt>().Where(x => x.AccountIdentity == account);
    internal IQueryable<BankReceiptConsumption> Consumptions(AppDbContext db) => db.Set<BankReceiptConsumption>()
        .Where(x => db.Set<BankVerifiedReceipt>().Any(r => r.Id == x.BankVerifiedReceiptId && r.AccountIdentity == account));
    internal IQueryable<BankReceiptConflict> Conflicts(AppDbContext db) => db.Set<BankReceiptConflict>()
        .Where(x => db.Set<BankVerifiedReceipt>().Any(r => r.Id == x.BankVerifiedReceiptId && r.AccountIdentity == account));
    internal async Task AssertDeltaAsync(AppDbContext db, int receipts = 0, decimal amount = 0, int consumptions = 0, int conflicts = 0)
    {
        var after = await ReadAsync(db);
        Assert.Equal(before.Receipts + receipts, after.Receipts); Assert.Equal(before.Amount + amount, after.Amount);
        Assert.Equal(before.Consumptions + consumptions, after.Consumptions); Assert.Equal(before.Conflicts + conflicts, after.Conflicts);
        Assert.All(await Receipts(db).AsNoTracking().ToListAsync(), receipt => Assert.Equal(storeId, receipt.OriginStoreId));
    }
    private static async Task<Totals> ReadAsync(AppDbContext db) => new(await db.Set<BankVerifiedReceipt>().CountAsync(),
        await db.Set<BankVerifiedReceipt>().SumAsync(x => x.Amount), await db.Set<BankReceiptConsumption>().CountAsync(), await db.Set<BankReceiptConflict>().CountAsync());
}

// Real checkout, real typed stock/FIFO, actual store/user/courier principals.
// No positive delivery/dispatch/payment is seeded and no POS source is fabricated.
internal sealed class Web06Case
{
    internal required Web04Case Checkout;
    internal required StorefrontPlacedOrder Placed;
    internal required int OrderId;
    internal required int CourierId;
    internal required decimal OrderedBase;
    internal required IReadOnlyDictionary<int, decimal> InitialStock;
    internal Web05Case? QrCase;
    internal Web05BankTransport? QrTransport;
    internal int? IntentId;
    internal string? ProviderReference;
    internal Web01Case Seed => Checkout.Seed;
    internal int StoreId => Seed.StoreId;
    internal AppDbContext Context(IInterceptor? interceptor = null) => Seed.Database.CreateTenantContext(StoreId, interceptor);
    internal IStorefrontFulfillmentService Service(AppDbContext db, IEnumerable<string>? permissions = null, int? actor = null, bool authenticated = true)
    {
        var actorId = actor ?? Seed.UserId;
        return new StorefrontFulfillmentService(db, new Web01Actor(actorId, authenticated),
            new Web01Permissions(StoreId, actorId, permissions ?? Web06Support.PhysicalPermissions),
            new InventoryBalanceRepository(db), InventoryPosPostingContractTests.CreateRealMovementService(db), TimeProvider.System);
    }

    internal static async Task<Web06Case> CreateAsync(Web01Fixture fixture, decimal quantity = 4m, decimal stock = 10m,
        bool negative = false, bool national = false, bool multipleSources = false, bool warehouseNegative = false, decimal shipping = 0,
        bool usePack = false, decimal packFactor = 12m, decimal unitPrice = 50_000m, decimal? primaryMultiStock = null, bool sameSkuGift = false,
        bool hasInputInvoice = true, Func<Web04Case, Task>? beforeQuote = null, StorefrontPaymentMethod payment = StorefrontPaymentMethod.Cod,
        Action<StorefrontQuoteRequest>? configureRequest = null, decimal primaryUnitCost = 10_000m)
    {
        var seed = await fixture.CaseAsync(); var pack = await seed.PackAsync(factor: packFactor, price: unitPrice * packFactor);
        await seed.EnableSyntheticCatalogAsync();
        var directory = Web06Support.Runtime("WEB06 real physical checkout fixture");
        var keys = Path.Combine(directory, "guest-keys"); StorefrontTestResourceMetadata.BeforeDirectory(keys, "WEB06 guest key root"); Directory.CreateDirectory(keys);
        var checkout = new Web04Case(seed, pack.Conversion, pack.Unit, directory, Path.Combine(directory, "synthetic-addresses.json"), keys, new());
        await checkout.WriteAddressAsync(); await checkout.SettingsAsync(negative: negative);
        var initial = new Dictionary<int, decimal>(); int courierId;
        await using (var db = seed.Context())
        {
            var product = await db.Products.SingleAsync(x => x.Id == seed.ProductId); product.BasePrice = unitPrice;
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.VariantId);
            variant.Price = unitPrice; variant.CostPrice = 10_000m; variant.HasInputInvoice = hasInputInvoice;
            (await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.ConversionId)).Price = unitPrice;
            var warehouse = await db.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId); warehouse.AllowNegativeInventory = warehouseNegative;
            var owner = await db.LegalEntities.SingleAsync(x => x.Id == seed.LegalEntityId);
            owner.SalePriority = 1; owner.DefaultWarehouseId = warehouse.Id;
            var membership = await db.UserInStores.Include(x => x.Role).SingleAsync(x => x.UserId == seed.UserId);
            var courier = new User { UserName = "w06-courier-" + Guid.NewGuid().ToString("N"), PasswordHash = "synthetic-only", FullName = "WEB06 synthetic courier" };
            db.UserInStores.Add(new() { StoreId = seed.StoreId, User = courier, RoleId = membership.RoleId });
            await db.SaveChangesAsync(); courierId = courier.Id;
            initial.Add(warehouse.Id, multipleSources ? primaryMultiStock ?? 2.5m : stock);
            if (multipleSources)
            {
                var second = new LegalEntity { StoreId = seed.StoreId, Code = "W06-B", Name = "WEB06 owner B", LegalName = "WEB06 owner B", IsActive = true, SalePriority = 2 };
                db.LegalEntities.Add(second); await db.SaveChangesAsync();
                var other = new Warehouse { StoreId = seed.StoreId, LegalEntityId = second.Id, Code = "W06-B", Name = "WEB06 warehouse B", IsActive = true, AllowNegativeInventory = warehouseNegative };
                db.Warehouses.Add(other); await db.SaveChangesAsync(); second.DefaultWarehouseId = other.Id;
                var storeRow = await db.Stores.SingleAsync(x => x.Id == seed.StoreId);
                storeRow.IsMultiLegalEntityEnabled = true; storeRow.MultiLegalEntityActivatedAtUtc = DateTime.UtcNow.AddDays(-1);
                initial.Add(other.Id, 10m); await db.SaveChangesAsync();
            }
            await seed.Products(db).SaveAsync(seed.ProductId, seed.Publication(pack: pack.Conversion));
            foreach (var (warehouseId, onHand) in initial)
            {
                if (onHand == 0) continue;
                var cost = warehouseId == seed.WarehouseId ? primaryUnitCost : 20_000m;
                var receipt = new InventoryMovementFactory().CreateAdjustmentIncrease(warehouseId, seed.VariantId, onHand, cost,
                    "WEB06 owned physical opening stock; no customer sale", DateTime.UtcNow.AddMinutes(-1));
                await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(receipt);
            }
            var local = await seed.ZoneAsync("LOCAL", "01"); local.BaseFee = shipping; await seed.FullService(db).SaveZoneAsync(local);
            var country = await seed.ZoneAsync("NATIONAL", "", "", StorefrontDeliveryMethod.Nationwide); country.BaseFee = shipping;
            await seed.FullService(db).SaveZoneAsync(country);
        }
        if (sameSkuGift)
            await Web04PromotionTests.Promotion(checkout, new() { Name = "WEB06 sealed same-SKU buy2/get1", Type = PromotionType.BuyXGetY,
                BuyQuantity = 2, GetQuantity = 1, RequireGiftQuantityInCart = true, Items = [new() { ProductId = seed.ProductId, VariantId = seed.VariantId }] });
        if (beforeQuote is not null) await beforeQuote(checkout);
        var request = checkout.Request(quantity, pack: usePack); request.PaymentMethod = payment;
        if (national) { request.ProvinceCode = "02"; request.WardCode = "02001"; }
        configureRequest?.Invoke(request);
        checkout.Clock.Utc = DateTime.UtcNow;
        var qr = payment == StorefrontPaymentMethod.Qr ? await Web06FinanceSupport.ConfigureQrAsync(checkout) : null;
        var quote = qr is null ? await checkout.QuoteAsync(request) : await qr.QuoteAsync(request);
        var placeRequest = Web04Case.PlaceRequest(quote);
        var placed = qr is null ? await checkout.PlaceAsync(placeRequest) : await qr.PlaceAsync(placeRequest); Assert.Equal(StorefrontPlacementStatus.Placed, placed.Status);
        Assert.NotNull(placed.Order);
        await using var lookup = seed.Context();
        var orderId = await lookup.StorefrontOrders.Where(x => x.Code == placed.Order.Code).Select(x => x.Id).SingleAsync();
        var result = new Web06Case { Checkout = checkout, Placed = placed.Order, OrderId = orderId, CourierId = courierId, OrderedBase = quote.Lines.Sum(x => x.BaseQuantity), InitialStock = initial, QrCase = qr };
        if (qr is not null)
        {
            var intent = await lookup.Set<StorefrontPaymentIntent>().AsNoTracking().SingleAsync(); result.IntentId = intent.Id; result.ProviderReference = intent.ProviderReference;
            var account = (await lookup.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == seed.BankAccountId)).AccountNumber;
            result.QrTransport = new(account, checkout.Clock); await qr.ProcessAsync(intent.Id, result.QrTransport);
            result.QrTransport.Pay(intent.ProviderReference, quote.TotalDue); await qr.ProcessAsync(intent.Id, result.QrTransport);
            await using var verify = seed.Context(); Assert.Equal(StorefrontPaymentState.Confirmed, (await verify.StorefrontOrders.SingleAsync()).PaymentState);
        }
        return result;
    }

    internal async Task<StorefrontFulfillmentDetail> DetailAsync()
    { await using var db = Context(); return await Service(db).GetAsync(OrderId); }
    internal async Task<T> RequestAsync<T>(Guid? key = null, string note = "WEB06 controlled staff evidence") where T : StorefrontOperationRequest, new()
    {
        var detail = await DetailAsync(); return new() { ClientRequestId = key ?? Guid.NewGuid(), ExpectedRevision = detail.Revision, ExpectedVersion = detail.Version, Note = note };
    }
    internal async Task PrepareAsync(Guid? key = null)
    { var request = await RequestAsync<StorefrontOperationRequest>(key); await using var db = Context(); await Service(db).StartPreparationAsync(OrderId, request); }
    internal async Task<StorefrontPickingRequest> PickingAsync(decimal? totalPerLine = null, Guid? key = null)
    {
        var request = await RequestAsync<StorefrontPickingRequest>(key); var detail = await DetailAsync();
        request.Lines = detail.Children.SelectMany(child => child.Lines.Select(line => new StorefrontPickedLine(child.SourceId, line.OrderLineId,
            totalPerLine.HasValue ? Web06Support.Quantity(totalPerLine.Value) : line.BaseQuantity))).ToList(); return request;
    }
    internal async Task PickAllAsync()
    { var request = await PickingAsync(); await using var db = Context(); await Service(db).RecordPickingAsync(OrderId, request); }
    internal async Task<StorefrontDispatchRequest> DispatchRequestAsync(Guid? key = null)
    {
        var request = await RequestAsync<StorefrontDispatchRequest>(key); var detail = await DetailAsync();
        request.Carriers = detail.Children.Select(child => detail.DeliveryMethod == StorefrontDeliveryMethod.Local
            ? new StorefrontCarrierAssignment(child.SourceId, CourierId, null, null)
            : new StorefrontCarrierAssignment(child.SourceId, null, "Synthetic carrier", "W06-" + child.SourceId)).ToList(); return request;
    }
    internal async Task DispatchAsync()
    { var request = await DispatchRequestAsync(); await using var db = Context(); await Service(db).DispatchAsync(OrderId, request); }
    internal async Task OutcomeAllAsync(bool delivered)
    {
        var children = (await DetailAsync()).Children.Select(x => x.SourceId).ToArray();
        foreach (var sourceId in children)
        {
            var request = await RequestAsync<StorefrontDeliveryOutcomeRequest>(); request.FulfillmentSourceId = sourceId;
            request.Delivered = delivered; request.ResultOccurredAt = DateTimeOffset.UtcNow;
            request.EvidenceReference = "WEB06 synthetic actual " + (delivered ? "delivery" : "failed attempt");
            await using var db = Context(); await Service(db).RecordOutcomeAsync(OrderId, request);
        }
    }
    internal async Task<StorefrontReceiveReturnRequest> ReturnRequestAsync(decimal? delta = null)
    {
        var request = await RequestAsync<StorefrontReceiveReturnRequest>(); var detail = await DetailAsync();
        request.Lines = detail.Children.SelectMany(child => child.Lines.Select(line => new StorefrontPickedLine(child.SourceId, line.OrderLineId,
            Web06Support.Quantity(delta ?? decimal.Parse(line.DispatchedBaseQuantity, CultureInfo.InvariantCulture) - decimal.Parse(line.ReturnedBaseQuantity, CultureInfo.InvariantCulture)))))
            .Where(x => decimal.Parse(x.BaseQuantity, CultureInfo.InvariantCulture) > 0).ToList(); return request;
    }
    internal async Task<StorefrontIncidentRequest> IncidentRequestAsync()
    {
        var request = await RequestAsync<StorefrontIncidentRequest>(); request.CustodySourceIds = (await DetailAsync()).Children.Select(x => x.SourceId).ToList();
        request.EvidenceReference = "WEB06 actual custody discrepancy, synthetic fixture"; return request;
    }
    internal async Task<StorefrontIncidentReconcileRequest> ReconcileRequestAsync(int incidentId, StorefrontIncidentResolution resolution)
    {
        var request = await RequestAsync<StorefrontIncidentReconcileRequest>(); request.IncidentId = incidentId; request.Resolution = resolution;
        var detail = await DetailAsync(); request.CountedLines = detail.Children.SelectMany(child => child.Lines.Select(line =>
            new StorefrontPickedLine(child.SourceId, line.OrderLineId, line.BaseQuantity))).ToList();
        request.Carriers = (await DispatchRequestAsync()).Carriers; request.EvidenceReference = "WEB06 authorized full physical count, synthetic fixture";
        return request;
    }

    internal async Task<string> PhysicalSnapshotAsync()
    {
        await using var db = Context();
        return JsonSerializer.Serialize(new { Parent = await Scalars<StorefrontOrder>(db), Work = await Scalars<StorefrontFulfillmentWork>(db),
            Children = await Scalars<DeliveryOrder>(db), Lines = await Scalars<DeliveryOrderLine>(db), Picking = await Scalars<DeliveryPickingLine>(db),
            PickingWork = await Scalars<DeliveryPickingWork>(db), Holds = await Scalars<InventoryReservation>(db), Balances = await Scalars<InventoryBalance>(db),
            Transactions = await Scalars<InventoryTransaction>(db), Valuation = await Scalars<InventoryValuationEntry>(db), Layers = await Scalars<InventoryCostLayer>(db),
            Journal = await Scalars<DeliveryJournalEntry>(db), Costs = await Scalars<DeliveryDispatchCostFragment>(db), Returns = await Scalars<DeliveryReturnedCostFragment>(db),
            Sources = await Scalars<StorefrontLineSource>(db), Transfers = await Scalars<StorefrontReservationTransfer>(db), Incidents = await Scalars<StorefrontCustodyIncident>(db),
            Evidence = await Scalars<StorefrontFulfillmentEvidence>(db), Commands = await Scalars<StorefrontCommandReceipt>(db), Events = await Scalars<StorefrontEvent>(db), Outbox = await Scalars<StorefrontOutboxMessage>(db) });
    }
    private static async Task<object[]> Scalars<T>(AppDbContext db) where T : BaseEntity
    {
        var properties = db.Model.FindEntityType(typeof(T))!.GetProperties().Where(x => x.PropertyInfo is not null).OrderBy(x => x.Name).ToArray();
        return (await db.Set<T>().AsNoTracking().OrderBy(x => x.Id).ToListAsync())
            .Select(row => (object)properties.ToDictionary(x => x.Name, x => x.PropertyInfo!.GetValue(row))).ToArray();
    }
}
