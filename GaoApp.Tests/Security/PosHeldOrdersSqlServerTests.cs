using System.Data.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PosHeldOrdersSqlServerTests
{
    [Fact]
    public async Task Held_summary_preserves_store_order_and_line_totals_without_loading_payment_or_product_graphs()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var store = await database.SeedInventoryCatalogAsync();
        var foreign = await database.SeedInventoryCatalogAsync();
        var heldAt = new DateTime(2026, 10, 16, 7, 0, 0, DateTimeKind.Utc);
        int firstId, secondId, oldId;
        await using (var db = database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.ProductVariantId);
            var shift = new POSShift { StoreId = store.StoreId, WarehouseId = store.WarehouseId,
                OpenedByUserId = 1, ShiftCode = "HELD-SHIFT", Terminal = new POSTerminal {
                    StoreId = store.StoreId, Code = "HELD", Name = "Held terminal" } };
            var customer = new Customer { StoreId = store.StoreId, Code = "HELD", Name = "Held customer", Phone = "0900000001" };
            Order Held(string number, DateTime at) => new() { StoreId = store.StoreId, POSShift = shift,
                Customer = customer, OrderNumber = number, Status = OrderStatus.OnHold, HeldAtUtc = at };
            var old = Held("HELD-OLD", heldAt.AddMinutes(-1));
            var first = Held("HELD-FIRST", heldAt);
            var second = Held("HELD-SECOND", heldAt);
            OrderLine Line(decimal quantity, decimal total) => new() { StoreId = store.StoreId,
                ProductId = variant.ProductId, VariantId = variant.Id, ItemName = "Held item",
                Quantity = quantity, LineTotal = total };
            first.Lines.Add(Line(2, 200));
            first.Lines.Add(Line(3, 450));
            var removedLine = Line(100, 10000);
            first.Lines.Add(removedLine);
            first.Payments.Add(new OrderPayment { StoreId = store.StoreId, Method = PaymentMethod.Cash, Amount = 100 });
            var draft = Held("HELD-DRAFT", heldAt.AddMinutes(1)); draft.Status = OrderStatus.Draft;
            var deleted = Held("HELD-DELETED", heldAt.AddMinutes(2));
            db.AddRange(old, first, second, draft, deleted);
            await db.SaveChangesAsync();
            oldId = old.Id; firstId = first.Id; secondId = second.Id;
            await db.OrderLines.Where(x => x.Id == removedLine.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
            await db.Orders.Where(x => x.Id == deleted.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
        }
        await using (var db = database.CreateTenantContext(foreign.StoreId))
        {
            var shift = new POSShift { StoreId = foreign.StoreId, WarehouseId = foreign.WarehouseId,
                OpenedByUserId = 1, ShiftCode = "HELD-FOREIGN-SHIFT", Terminal = new POSTerminal {
                    StoreId = foreign.StoreId, Code = "HELD-FOREIGN", Name = "Foreign held terminal" } };
            db.Add(new Order { StoreId = foreign.StoreId, POSShift = shift, OrderNumber = "HELD-FOREIGN",
                Status = OrderStatus.OnHold, HeldAtUtc = heldAt.AddHours(1) });
            await db.SaveChangesAsync();
        }
        var capture = new Capture();
        await using var read = database.CreateTenantContext(store.StoreId, capture);
        var orders = await new OrderRepository(read).GetHeldOrdersByStoreAsync(store.StoreId);
        Assert.Equal(new[] { Math.Max(firstId, secondId), Math.Min(firstId, secondId), oldId }, orders.Select(x => x.Id));
        var held = Assert.Single(orders, x => x.Id == firstId);
        Assert.Equal(2, held.Lines.Count);
        Assert.Equal(5m, held.Lines.Sum(x => x.Quantity));
        Assert.Equal(650m, held.Lines.Sum(x => x.LineTotal));
        Assert.Equal("Held customer", held.Customer!.Name);
        Assert.Equal("0900000001", held.Customer.Phone);
        Assert.Equal("HELD-SHIFT", held.POSShift!.ShiftCode);
        Assert.Equal("Held terminal", held.POSShift.Terminal.Name);
        Assert.Empty(held.Payments);
        Assert.All(held.Lines, line => Assert.Null(line.Variant));
        Assert.Empty(read.ChangeTracker.Entries());
        Assert.Equal(2, capture.Sql.Count);
        foreach (var sql in capture.Sql)
        {
            Assert.DoesNotContain("[OrderPayments]", sql);
            Assert.DoesNotContain("[ProductVariants]", sql);
            Assert.DoesNotContain("[OrderInventoryIssues]", sql);
        }

        capture.Sql.Clear();
        var summaries = await new OrderRepository(read).GetHeldOrderSummariesByStoreAsync(store.StoreId);
        Assert.Equal(orders.Select(x => x.Id), summaries.Select(x => x.OrderId));
        var summary = Assert.Single(summaries, x => x.OrderId == firstId);
        Assert.Equal(2, summary.LineCount);
        Assert.Equal(5m, summary.TotalQuantity);
        Assert.Equal(650m, summary.Subtotal);
        Assert.Equal("Held customer", summary.CustomerName);
        Assert.Equal("0900000001", summary.CustomerPhone);
        Assert.Equal("HELD-SHIFT", summary.ShiftCode);
        Assert.Equal("Held terminal", summary.TerminalName);
        Assert.Equal(1, summary.HeldByUserId);
        var empty = Assert.Single(summaries, x => x.OrderId == secondId);
        Assert.Equal(0, empty.LineCount);
        Assert.Equal(0m, empty.TotalQuantity);
        Assert.Equal(0m, empty.Subtotal);
        Assert.Empty(read.ChangeTracker.Entries());
        var projectionSql = Assert.Single(capture.Sql);
        Assert.Contains("SUM(", projectionSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT(", projectionSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[OrderPayments]", projectionSql);
        Assert.DoesNotContain("[ProductVariants]", projectionSql);
        Assert.DoesNotContain("[OrderInventoryIssues]", projectionSql);
    }

    private sealed class Capture : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
