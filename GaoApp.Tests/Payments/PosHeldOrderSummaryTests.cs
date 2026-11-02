using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Orders;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Held_order_summary_returns_all_rows_and_live_line_totals_without_tracking_the_order_graph()
    {
        await using var f = await Fixture.Create();
        var heldAt = new DateTime(2026, 10, 27, 7, 0, 0, DateTimeKind.Utc);
        f.Order.Status = OrderStatus.OnHold;
        f.Order.HeldAtUtc = heldAt;
        f.Order.HoldCode = "HELD-100";
        f.Order.HoldNote = "Synthetic held order";
        f.Order.Customer = new Customer { StoreId = 1, Code = "HELD", Name = "Held customer", Phone = "0900000001" };
        f.Order.POSShift.ShiftCode = "HELD-SHIFT";
        f.Order.POSShift.OpenedByUserId = 7;
        var terminal = new POSTerminal { Id = 3, StoreId = 1, Code = "HELD", Name = "Held terminal" };
        f.Db.POSTerminals.Add(terminal);
        f.Order.POSShift.Terminal = terminal;
        f.Order.Lines.Single().IsDeleted = true;
        f.Order.Lines.Add(new OrderLine { StoreId = 1, Quantity = 2.5m, LineTotal = 25000 });
        f.Order.Lines.Add(new OrderLine { StoreId = 1, Quantity = 3m, LineTotal = 45000 });
        for (var i = 0; i < 600; i++)
            f.Db.Orders.Add(new Order { StoreId = 1, POSShift = f.Order.POSShift, Status = OrderStatus.OnHold,
                HeldAtUtc = heldAt.AddMinutes(-1), HoldCode = "HELD-EMPTY-" + i });
        var draft = new Order { StoreId = 1, POSShift = f.Order.POSShift, Status = OrderStatus.Draft, HeldAtUtc = heldAt.AddHours(1) };
        var deleted = new Order { StoreId = 1, POSShift = f.Order.POSShift, Status = OrderStatus.OnHold, IsDeleted = true, HeldAtUtc = heldAt.AddHours(1) };
        f.Db.AddRange(draft, deleted);
        await f.Db.SaveChangesAsync();
        f.Db.Orders.Remove(deleted);
        await f.Db.SaveChangesAsync();
        Assert.True(await f.Db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == deleted.Id).Select(x => x.IsDeleted).SingleAsync());
        f.Db.ChangeTracker.Clear();

        var summaries = await new OrderRepository(f.Db).GetHeldOrderSummariesByStoreAsync(1);

        Assert.Equal(601, summaries.Count);
        var first = summaries[0];
        Assert.Equal(100, first.OrderId);
        Assert.Equal("HELD-100", first.HoldCode);
        Assert.Equal("Synthetic held order", first.HoldNote);
        Assert.Equal(heldAt, first.HeldAtUtc);
        Assert.Equal(2, first.LineCount);
        Assert.Equal(5.5m, first.TotalQuantity);
        Assert.Equal(70000m, first.Subtotal);
        Assert.Equal("Held customer", first.CustomerName);
        Assert.Equal("0900000001", first.CustomerPhone);
        Assert.Equal(10, first.PosShiftId);
        Assert.Equal("HELD-SHIFT", first.ShiftCode);
        Assert.Equal("3", first.TerminalId);
        Assert.Equal("Held terminal", first.TerminalName);
        Assert.Equal(7, first.HeldByUserId);
        Assert.Equal(summaries.Skip(1).Select(x => x.OrderId).OrderDescending(), summaries.Skip(1).Select(x => x.OrderId));
        Assert.All(summaries.Skip(1), item =>
        {
            Assert.Equal(0, item.LineCount);
            Assert.Equal(0m, item.TotalQuantity);
            Assert.Equal(0m, item.Subtotal);
            Assert.Null(item.CustomerName);
        });
        Assert.DoesNotContain(summaries, x => x.OrderId == draft.Id || x.OrderId == deleted.Id);
        Assert.Empty(f.Db.ChangeTracker.Entries());
        Assert.Empty(await new OrderRepository(f.Db).GetHeldOrderSummariesByStoreAsync(2));
    }
}
