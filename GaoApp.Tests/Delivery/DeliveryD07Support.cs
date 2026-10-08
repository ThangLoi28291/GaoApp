using System.Text.Json;
using System.Globalization;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Infrastructure.Data;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Delivery;

[CollectionDefinition("DeliveryD07", DisableParallelization = true)]
public sealed class DeliveryD07Collection : ICollectionFixture<DeliveryD02Fixture> { }

// Reports start from a real POS/picking/export/physical custody/departure chain.
// No outcome, arrival, sale or receipt is manufactured by this setup.
internal sealed class DeliveryD07Case(DeliveryD06Case trip) : IDisposable
{
    internal DeliveryD06Case Trip => trip;
    internal DeliveryD06Order Order(int index = 0) => trip.Orders[index];
    internal int Id(int index = 0) => Order(index).Id;
    internal int TripId => trip.TripId;
    internal FullApplicationFixture.Account Actor => trip.Actor;
    internal FullApplicationFixture.Account Courier => trip.Courier;
    internal string Path(int index = 0) => trip.Path("orders/" + Id(index) + "/outcome");

    internal static async Task<DeliveryD07Case> CreateAsync(DeliveryD02Fixture fixture, int count = 2)
    {
        var orders = new List<DeliveryD06Order>();
        try
        {
            for (var index = 0; index < count; index++)
            {
                var order = await DeliveryD06Order.CreateAsync(fixture, quantity: index == 0 ? 4 : 3,
                    cost: index == 0 ? 9000 : 11000, distinctSource: true);
                orders.Add(order); await order.ReadyAsync();
            }
            var courier = await fixture.Web.AddAccountAsync(orders[0].Account.Store,
                PermissionCodes.Delivery.View, PermissionCodes.Delivery.ReportOwnOutcome);
            var other = await fixture.Web.AddAccountAsync(orders[0].Account.Store,
                PermissionCodes.Delivery.View, PermissionCodes.Delivery.ReportOwnOutcome);
            var trip = new DeliveryD06Case(fixture.Web, orders, courier, other);
            await trip.CreateDraftAsync(); await trip.HandoverAsync(); await trip.DepartAsync();
            return new(trip);
        }
        catch { foreach (var order in orders) order.Dispose(); throw; }
    }

    internal AppDbContext Context(FullApplicationFixture.Account? actor = null, IInterceptor? interceptor = null)
        => trip.Context(actor, interceptor);
    internal Task<string> StockAndFinanceAsync() => trip.InventoryOnlyAsync();

    internal async Task<DeliveryOutcomeRequest> RequestAsync(int index = 0, string outcome = "Full",
        string delivered = "4", string? returned = null, Guid? key = null)
    {
        var detail = await trip.DetailAsync(); var row = detail.Orders.Single(x => x.DeliveryOrderId == Id(index));
        var line = Assert.Single(row.Lines);
        returned ??= (decimal.Parse(line.DispatchedBaseQuantityText, CultureInfo.InvariantCulture) -
            decimal.Parse(delivered, CultureInfo.InvariantCulture)).ToString("0.####", CultureInfo.InvariantCulture);
        return new(key ?? Guid.NewGuid(), detail.Version, row.Version, Courier.UserId, "Phone", outcome,
            outcome == "Full" ? null : "Khách báo chưa nhận đủ hàng", "0", "0", null,
            [new(line.LineId, delivered, returned)]);
    }

    internal async Task<DeliveryTripCommandAck> ReportAsync(DeliveryOutcomeRequest request,
        int index = 0, FullApplicationFixture.Account? actor = null, IInterceptor? interceptor = null)
    {
        await using var db = Context(actor, interceptor);
        return await trip.Service(db, actor).RecordOutcomeAsync(TripId, Id(index), request);
    }

    internal async Task<DeliveryTripReturnRequest> ReturnRequestAsync(Guid? key = null)
        => new(key ?? Guid.NewGuid(), (await trip.DetailAsync()).Version, Courier.UserId, true, "Người giao đã về tiệm");

    internal async Task<DeliveryTripCommandAck> ArrivalAsync(DeliveryTripReturnRequest? request = null,
        FullApplicationFixture.Account? actor = null, IInterceptor? interceptor = null)
    {
        request ??= await ReturnRequestAsync(); await using var db = Context(actor, interceptor);
        return await trip.Service(db, actor).RecordReturnToStoreAsync(TripId, request);
    }

    internal async Task<string> SnapshotAsync()
    {
        await using var db = Context();
        return JsonSerializer.Serialize(new {
            Trip = await trip.SnapshotAsync(),
            Orders = await db.DeliveryOrders.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Revisions = await db.DeliveryRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Outbox = await db.DeliveryOutboxMessages.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync() });
    }

    internal async Task<string> OtherOrderAsync(int index = 1)
    {
        await using var db = Context(); var id = Id(index);
        return JsonSerializer.Serialize(new {
            Order = await db.DeliveryOrders.AsNoTracking().SingleAsync(x => x.Id == id),
            Revisions = await db.DeliveryRevisions.AsNoTracking().Where(x => x.DeliveryOrderId == id).OrderBy(x => x.Id).ToArrayAsync(),
            Binding = await db.DeliveryTripOrders.AsNoTracking().SingleAsync(x => x.DeliveryOrderId == id && x.IsActive),
            Outbox = await db.DeliveryOutboxMessages.AsNoTracking().Where(x => x.DeliveryOrderId == id).OrderBy(x => x.Id).ToArrayAsync() });
    }

    internal async Task<string> SourceAsync()
    {
        await using var db = Context(); var ids = trip.Orders.Select(x => x.Id).ToArray();
        return JsonSerializer.Serialize(new {
            Customer = await db.DeliveryOrders.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.SourceCartId, x.CustomerId, x.RecipientName, x.RecipientPhone,
                    x.RecipientAddress, x.SourceWarehouseId, x.SourceLegalEntityId, x.QuotedTotal }).ToArrayAsync(),
            Quote = await db.DeliveryOrderLines.AsNoTracking().Where(x => ids.Contains(x.DeliveryOrderId)).OrderBy(x => x.Id).ToArrayAsync() });
    }
    public void Dispose() => trip.Dispose();
}

internal sealed class D07InjectedFailure : Exception;
internal sealed class D07ReceiptFailure : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<DeliveryTripCommandReceipt>()
            .Any(x => x.State == EntityState.Added && x.Entity.Operation is "outcome" or "return-to-store"))
            throw new D07InjectedFailure();
        return ValueTask.FromResult(result);
    }
}
