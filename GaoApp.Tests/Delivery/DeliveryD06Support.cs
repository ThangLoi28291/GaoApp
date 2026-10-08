using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Services.Security;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Delivery;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Inventory;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Delivery;

[CollectionDefinition("DeliveryD06", DisableParallelization = true)]
public sealed class DeliveryD06Collection : ICollectionFixture<DeliveryD02Fixture> { }

// Each order is created through real POS transfer and D04 picking, with its own
// source stock. Trip setup never manufactures custody or a delivery outcome.
internal sealed class DeliveryD06Order(DeliveryD05Case inventory) : IDisposable
{
    internal DeliveryD05Case Inventory => inventory;
    internal DeliveryD04Case Picking => inventory.Picking;
    internal int Id => inventory.Id;
    internal FullApplicationFixture.Account Account => Picking.Account;
    internal FullApplicationFixture.Client Client => Picking.Client;

    internal static Task<DeliveryD06Order> CreateAsync(DeliveryD02Fixture fixture,
        decimal quantity = 2, decimal cost = 9000, bool distinctSource = false,
        int? sharedVariant = null, int storeIndex = 0, int? sourceWarehouseId = null)
        => CreateAsync(fixture.Web, quantity, cost, distinctSource, sharedVariant, storeIndex, sourceWarehouseId);

    internal static async Task<DeliveryD06Order> CreateAsync(FullApplicationFixture web,
        decimal quantity = 2, decimal cost = 9000, bool distinctSource = false,
        int? sharedVariant = null, int storeIndex = 0, int? sourceWarehouseId = null)
    {
        var store = web.Stores[storeIndex];
        var suffix = Guid.NewGuid().ToString("N");
        await using (var db = web.Database.CreateTenantContext(store.StoreId))
        {
            if (sourceWarehouseId is int existingWarehouse)
            {
                Assert.True(await db.Warehouses.AnyAsync(x => x.Id == existingWarehouse && x.IsActive));
                store = store with { WarehouseId = existingWarehouse };
            }
            if (distinctSource)
            {
                var priority = checked((await db.LegalEntities.IgnoreQueryFilters()
                    .Where(x => x.StoreId == store.StoreId).Select(x => (int?)x.SalePriority).MaxAsync() ?? 0) + 1);
                var owner = new LegalEntity { StoreId = store.StoreId, Code = "D06-" + suffix,
                    Name = "D06 owner " + suffix, LegalName = "D06 owner " + suffix, IsActive = true, SalePriority = priority };
                db.LegalEntities.Add(owner); await db.SaveChangesAsync();
                var warehouse = new Warehouse { StoreId = store.StoreId, LegalEntityId = owner.Id,
                    Code = "D06-" + suffix, Name = "D06 warehouse " + suffix, IsActive = true };
                db.Warehouses.Add(warehouse); await db.SaveChangesAsync();
                store = store with { WarehouseId = warehouse.Id };
            }
            var terminal = new POSTerminal { StoreId = store.StoreId, Code = "D06-" + suffix[..26],
                Name = "D06 terminal " + suffix };
            db.POSTerminals.Add(terminal); await db.SaveChangesAsync();
            store = store with { TerminalId = terminal.Id };
        }
        var account = await web.AddAccountAsync(store, "*");
        var client = await web.LoginAsync(account);
        try
        {
            await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open",
                new { openingCash = 0, warehouseId = store.WarehouseId });
            var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
            var cartId = draft.GetProperty("orderId").GetInt32();
            await client.JsonAsync(HttpMethod.Post, "/admin/pos/" + cartId + "/items?variantId=" +
                store.VariantId + "&qty=2");
            var source = new DeliveryD02Case(web, account, client, cartId);
            var picking = await DeliveryD04Case.CreateAsync(source, quantity: quantity, unitPrice: 20000,
                stockLayers: sharedVariant is null ? [(quantity, cost)] : [],
                sharedStockVariantId: sharedVariant);
            source.Detail = await source.PostAsync(source.Change(name: "D06 recipient " + suffix));
            return new(new DeliveryD05Case(picking));
        }
        catch { client.Dispose(); throw; }
    }

    internal async Task<DeliveryPickingDetailDto> ReadyAsync()
    {
        await Picking.ClaimAsync();
        await Picking.ReportAsync((await Picking.GetAsync()).Lines.Single(x => !x.IsReplacement).PlannedQuantityText);
        var detail = await Picking.SubmitAsync();
        if (detail.Delivery.State == nameof(DeliveryState.AwaitingApproval)) detail = await Picking.ApproveAsync();
        Assert.Equal(nameof(DeliveryState.ReadyForHandover), detail.Delivery.State);
        return detail;
    }

    internal Task<string> StockAndFinanceAsync() => inventory.SnapshotAsync();
    internal async Task AddStockAsync(decimal quantity, decimal cost)
    {
        await using var db = Picking.Source.Context();
        await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
            new GaoApp.Application.Services.Inventory.InventoryMovementFactory().CreatePurchaseReceipt(
                Account.Store.WarehouseId, await inventory.VariantAsync(), quantity, cost,
                "D06-EXTRA-STOCK-" + Guid.NewGuid().ToString("N"), 1, "Owned D06 source stock", 1));
    }
    public void Dispose() => inventory.Dispose();
}

internal sealed class DeliveryD06Case(FullApplicationFixture web, IReadOnlyList<DeliveryD06Order> orders,
    FullApplicationFixture.Account courier, FullApplicationFixture.Account otherCourier) : IDisposable
{
    internal FullApplicationFixture Web => web;
    internal IReadOnlyList<DeliveryD06Order> Orders => orders;
    internal FullApplicationFixture.Account Actor => orders[0].Account;
    internal FullApplicationFixture.Account Courier => courier;
    internal FullApplicationFixture.Account OtherCourier => otherCourier;
    internal int TripId { get; private set; }
    internal static Task<DeliveryD06Case> CreateAsync(DeliveryD02Fixture fixture, int count = 2, bool ready = true)
        => CreateAsync(fixture.Web, count, ready);

    internal static async Task<DeliveryD06Case> CreateAsync(FullApplicationFixture web, int count = 2, bool ready = true)
    {
        var created = new List<DeliveryD06Order>();
        try
        {
            for (var index = 0; index < count; index++)
            {
                var item = await DeliveryD06Order.CreateAsync(web, index == 0 ? 2 : 3,
                    index == 0 ? 9000 : 11000, distinctSource: true);
                created.Add(item); if (ready) await item.ReadyAsync();
            }
            var courier = await web.AddAccountAsync(created[0].Account.Store);
            var other = await web.AddAccountAsync(created[0].Account.Store);
            return new(web, created, courier, other);
        }
        catch { foreach (var item in created) item.Dispose(); throw; }
    }

    internal AppDbContext Context(FullApplicationFixture.Account? actor = null, IInterceptor? interceptor = null)
    {
        actor ??= Actor;
        var tenant = new TenantContext(); tenant.SetStore(actor.Store.StoreId, actor.Store.Host);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(web.Database.ConnectionString);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options, tenant, new D06CurrentUser(actor.UserId, actor.Store.TerminalId));
    }
    internal DeliveryTripService Service(AppDbContext db, FullApplicationFixture.Account? actor = null)
    {
        actor ??= Actor;
        return new(db, new D06CurrentUser(actor.UserId, actor.Store.TerminalId),
            new CurrentStorePermissionService(new UserInStoreRepository(db)), orders[0].Inventory.Service(db, actor),
            new InventoryBalanceRepository(db));
    }
    internal async Task<IReadOnlyList<DeliveryTripOrderSelection>> SelectionsAsync(bool reversed = false)
    {
        await using var db = Context();
        var ids = orders.Select(x => x.Id).ToArray();
        var current = await db.DeliveryOrders.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync();
        var result = orders.Select(x => new DeliveryTripOrderSelection(x.Id,
            Convert.ToBase64String(current.Single(o => o.Id == x.Id).RowVersion))).ToArray();
        return reversed ? result.Reverse().ToArray() : result;
    }
    internal async Task<DeliveryTripCommandAck> CreateDraftAsync(Guid? key = null, bool reversed = false,
        FullApplicationFixture.Account? actor = null)
    {
        var request = new DeliveryTripCreateDraftRequest(key ?? Guid.NewGuid(), courier.UserId,
            await SelectionsAsync(reversed), "D06 actual multi-order route");
        await using var db = Context(actor);
        var ack = await Service(db, actor).CreateDraftAsync(request); TripId = ack.TripId; return ack;
    }
    internal async Task<DeliveryTripDetailDto> DetailAsync(FullApplicationFixture.Account? actor = null)
    {
        await using var db = Context(actor); return await Service(db, actor).GetAsync(TripId);
    }
    internal async Task<DeliveryTripHandoverRequest> HandoverRequestAsync(Guid? key = null)
    {
        var trip = await DetailAsync();
        return new(key ?? Guid.NewGuid(), trip.Version, await SelectionsAsync(), trip.CourierUserId, true);
    }
    internal async Task<DeliveryTripCommandAck> HandoverAsync(DeliveryTripHandoverRequest? request = null,
        FullApplicationFixture.Account? actor = null, IInterceptor? interceptor = null)
    {
        request ??= await HandoverRequestAsync();
        await using var db = Context(actor, interceptor); return await Service(db, actor).HandoverAsync(TripId, request);
    }
    internal async Task<DeliveryTripCommandAck> DepartAsync(DeliveryTripDepartureRequest? request = null,
        FullApplicationFixture.Account? actor = null)
    {
        request ??= new(Guid.NewGuid(), (await DetailAsync()).Version, await SelectionsAsync());
        await using var db = Context(actor); return await Service(db, actor).DepartAsync(TripId, request);
    }
    internal string Path(string operation = "") => "/admin/api/delivery-trips/" + TripId +
        (operation.Length == 0 ? "" : "/" + operation);

    internal async Task<string> StockAsync()
    {
        // Snapshot after all fixture creation, including every selected source.
        // The full D05 snapshot also records finance and receipt/history effects.
        return JsonSerializer.Serialize(await Task.WhenAll(orders.Select(x => x.StockAndFinanceAsync())));
    }
    internal async Task<string> InventoryOnlyAsync()
    {
        await using var db = Context();
        return JsonSerializer.Serialize(new {
            Finance = await orders[0].Inventory.FinanceAsync(),
            Balance = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.OnHandQty, x.ReservedQty, x.InventoryValue }).ToArrayAsync(),
            Holds = await db.InventoryReservations.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Status, x.ReservedQty }).ToArrayAsync(),
            Movements = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.QuantityChange, x.TotalCost }).ToArrayAsync(),
            Debits = await db.InventoryCostBudgetDebits.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Journal = await db.DeliveryJournalEntries.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Returns = await db.DeliveryReturnedCostFragments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync() });
    }
    internal async Task<string> SnapshotAsync()
    {
        await using var db = Context();
        return JsonSerializer.Serialize(new {
            Stock = await StockAsync(),
            Trips = await db.DeliveryTrips.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Bindings = await db.DeliveryTripOrders.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Custody = await db.DeliveryCustodyEvents.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Lines = await db.DeliveryCustodyLines.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            History = await db.DeliveryTripRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Receipts = await db.DeliveryTripCommandReceipts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Events = await db.DeliveryTripOutboxMessages.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync() });
    }
    public void Dispose() { foreach (var order in orders) order.Dispose(); }
}

internal sealed class D06CurrentUser(int id, int terminalId) : ICurrentUser
{
    public int? UserId => id;
    public string? UserName => "D06 real SQL actor";
    public int? TerminalId => terminalId;
    public string? TerminalCode => "D06";
    public bool IsAuthenticated => true;
}

public static class DeliveryD06BrowserFixture
{
    public static async Task<object> PrepareAsync(object fullApplicationFixture)
    {
        var web = (FullApplicationFixture)fullApplicationFixture;
        using var primary = await DeliveryD06Case.CreateAsync(web);
        using var spare = await DeliveryD06Order.CreateAsync(web, quantity: 1, cost: 7000, distinctSource: true);
        await spare.ReadyAsync();
        using var handed = await DeliveryD06Case.CreateAsync(web);
        await handed.CreateDraftAsync(); await handed.HandoverAsync();
        using var departed = await DeliveryD06Case.CreateAsync(web);
        await departed.CreateDraftAsync(); await departed.HandoverAsync(); await departed.DepartAsync();
        var store = primary.Actor.Store;
        var viewer = await web.AddAccountAsync(store, PermissionCodes.Delivery.View);
        var depart = await web.AddAccountAsync(store, PermissionCodes.Delivery.View, PermissionCodes.Delivery.RecordDeparture);
        var reassign = await web.AddAccountAsync(store, PermissionCodes.Delivery.View,
            PermissionCodes.Delivery.Handover, PermissionCodes.Delivery.ReassignCourier);
        var cancel = await web.AddAccountAsync(store, PermissionCodes.Delivery.View, PermissionCodes.Delivery.Cancel);
        async Task<object> Order(DeliveryD06Order order)
        {
            var picking = await order.Picking.GetAsync();
            var inventory = await order.Inventory.GetAsync();
            return new { order.Id, order.Picking.Source.Detail.LookupToken,
                picking.Delivery.Version, picking.Delivery.Code, picking.Delivery.State,
                picking.Delivery.RecipientName, picking.Delivery.SourceWarehouseId, picking.Delivery.SourceLegalEntityId,
                inventory.SourceWarehouseName, inventory.SourceLegalEntityName,
                Lines = picking.Lines.Select(x => new { x.LineId, x.VariantId, x.ApprovedBaseQuantityText }).ToArray() };
        }
        foreach (var order in primary.Orders)
        {
            var inventory = await order.Inventory.GetAsync();
            Assert.Null(inventory.Custody); Assert.Null(inventory.InCustodyBaseQuantityText);
            Assert.Equal("0", inventory.DispatchedBaseQuantityText);
        }
        var handedDetail = await handed.DetailAsync(); var departedDetail = await departed.DetailAsync();
        Assert.Equal(nameof(DeliveryTripState.HandedOver), handedDetail.State);
        Assert.Equal(nameof(DeliveryTripState.Departed), departedDetail.State);
        return new {
            Orders = await Task.WhenAll(primary.Orders.Select(Order)), Spare = await Order(spare),
            Creator = primary.Actor, ViewOnly = viewer, DepartOnly = depart, Reassign = reassign, Cancel = cancel,
            Courier = primary.Courier, OtherCourier = primary.OtherCourier,
            HandedOver = new { Id = handed.TripId, handedDetail.Version,
                OrderIds = handed.Orders.Select(x => x.Id).ToArray(), Courier = handed.Courier },
            Departed = new { Id = departed.TripId, departedDetail.Version,
                OrderIds = departed.Orders.Select(x => x.Id).ToArray(), Courier = departed.Courier },
            store.StoreId, store.TerminalId,
            Expected = new { Quantities = new[] { "2", "3" }, Costs = new[] { "18000", "33000" },
                InitialCustody = (string?)null, PrimaryInitiallyExported = false } };
    }
}
