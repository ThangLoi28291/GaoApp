using System.Globalization;
using System.Reflection;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Services.Security;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Delivery;
using GaoApp.Tests.Inventory;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Delivery;

[CollectionDefinition("DeliveryD05", DisableParallelization = true)]
public sealed class DeliveryD05Collection : ICollectionFixture<DeliveryD02Fixture> { }

internal sealed class DeliveryD05Case(DeliveryD04Case picking) : IDisposable
{
    internal DeliveryD04Case Picking => picking;
    internal int Id => picking.Id;
    internal string Path(string operation = "") => "/admin/api/deliveries/" + Id + "/inventory" +
        (operation.Length == 0 ? "" : "/" + operation);
    internal static async Task<DeliveryD05Case> CreateAsync(DeliveryD02Fixture fixture, decimal ordered = 8,
        IReadOnlyList<(decimal Quantity, decimal Cost)>? stock = null, int? sharedVariant = null)
        => new(await DeliveryD04Case.CreateAsync(fixture, quantity: ordered, unitPrice: ordered < .01m ? 10000m : 20m,
            stockLayers: stock ?? [(4m, 9000m), (4m, 11000m)], sharedStockVariantId: sharedVariant));
    internal async Task<int> VariantAsync()
    {
        await using var db = picking.Source.Context();
        return await db.DeliveryOrderLines.Where(x => x.DeliveryOrderId == Id).Select(x => x.VariantId).SingleAsync();
    }
    internal async Task SeedHistoricalFinalSourceAsync(decimal quantity, decimal unitCost, decimal amount, bool append = false)
    {
        // Controlled historical SQL graph, not an invocation of the legacy
        // writer: precise source evidence and Final-zero are tested unchanged.
        await using var db = picking.Source.Context(); await using var transaction = await db.Database.BeginTransactionAsync();
        var variant = await VariantAsync(); var warehouse = picking.Account.Store.WarehouseId;
        if (!append) Assert.False(await db.InventoryTransactions.AnyAsync(x => x.ProductVariantId == variant));
        var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.ProductVariantId == variant && x.WarehouseId == warehouse);
        var beforeQuantity = balance?.OnHandQty ?? 0; var beforeAmount = balance?.InventoryValue ?? 0;
        var afterAverage = decimal.Round((beforeAmount + amount) / (beforeQuantity + quantity), 6, MidpointRounding.AwayFromZero);
        var now = DateTime.UtcNow; var reference = "D05-HISTORICAL-FINAL-" + Id;
        var movement = new InventoryTransaction { StoreId = picking.Account.Store.StoreId, WarehouseId = warehouse,
            ProductVariantId = variant, TransactionType = InventoryTransactionType.PurchaseReceipt,
            ReferenceType = InventoryReferenceType.PurchaseReceipt, ReferenceId = reference, ReferenceLineId = 1,
            PostingPrecisionPolicy = 0, QuantityChange = quantity, BeforeQty = beforeQuantity, AfterQty = beforeQuantity + quantity,
            UnitCostSnapshot = unitCost, TotalCost = amount, BeforeInventoryValue = beforeAmount, AfterInventoryValue = beforeAmount + amount,
            RunningAverageUnitCostAfter = afterAverage, CostSourceType = InventoryCostSourceType.Manual,
            CostFinalizedAtUtc = now, OccurredAtUtc = now };
        db.InventoryTransactions.Add(movement); await db.SaveChangesAsync();
        var valuation = new InventoryValuationEntry { StoreId = movement.StoreId, WarehouseId = warehouse,
            ProductVariantId = variant, InventoryTransactionId = movement.Id, EntryType = InventoryValuationEntryType.Inbound,
            ReferenceType = movement.ReferenceType, ReferenceId = reference, ReferenceLineId = 1, Quantity = quantity,
            UnitCost = unitCost, Amount = amount, RunningQtyAfter = beforeQuantity + quantity, RunningValueAfter = beforeAmount + amount,
            RunningAverageUnitCostAfter = afterAverage, CostSourceType = InventoryCostSourceType.Manual,
            CostFinalizedAtUtc = now, OccurredAtUtc = now };
        db.InventoryValuationEntries.Add(valuation); await db.SaveChangesAsync();
        var layer = new InventoryCostLayer { StoreId = movement.StoreId, WarehouseId = warehouse, ProductVariantId = variant,
            InventoryTransactionId = movement.Id, InventoryValuationEntryId = valuation.Id,
            ReferenceType = movement.ReferenceType, ReferenceId = reference, ReferenceLineId = 1,
            OriginalQuantity = quantity, RemainingQuantity = quantity, RemainingOpenProvisionalQty = quantity,
            UnitCost = unitCost, OccurredAtUtc = now };
        db.InventoryCostLayers.Add(layer); await db.SaveChangesAsync(); valuation.InventoryCostLayerId = layer.Id;
        if (balance is null) { balance = new() { StoreId = movement.StoreId, WarehouseId = warehouse, ProductVariantId = variant }; db.InventoryBalances.Add(balance); }
        balance.OnHandQty = beforeQuantity + quantity; balance.InventoryValue = beforeAmount + amount;
        balance.AverageUnitCost = decimal.Round(balance.InventoryValue / balance.OnHandQty, 6, MidpointRounding.AwayFromZero);
        balance.LastInboundUnitCost = unitCost; balance.LastInboundAtUtc = now; balance.LastValuationAtUtc = now;
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
    internal async Task<DeliveryPickingDetailDto> ReadyAsync(string? picked = null)
    {
        await picking.ClaimAsync();
        await picking.ReportAsync(picked ?? "8", picked is null ? null : "Có thiếu hàng thực tế");
        var detail = await picking.SubmitAsync();
        return detail.Delivery.State == nameof(DeliveryState.AwaitingApproval) ? await picking.ApproveAsync() : detail;
    }
    internal DeliveryInventoryService Service(AppDbContext db, FullApplicationFixture.Account? actor = null)
    {
        actor ??= picking.Account;
        return new(db, new D05Actor(actor.UserId, actor.Store.TerminalId),
            new CurrentStorePermissionService(new UserInStoreRepository(db)), new InventoryBalanceRepository(db),
            InventoryPosPostingContractTests.CreateRealMovementService(db));
    }
    internal async Task<DeliveryInventorySummaryDto> GetAsync(FullApplicationFixture.Account? actor = null)
    {
        await using var db = picking.Source.Context();
        return await Service(db, actor).GetAsync(Id);
    }
    internal async Task<DeliveryInventoryPostingAck> DispatchAsync(DeliveryInventoryDispatchRequest? request = null,
        IInterceptor? interceptor = null)
    {
        var detail = await picking.GetAsync();
        request ??= new(Guid.NewGuid(), detail.Delivery.Version, detail.ApprovedRevision!.Value);
        await using var db = picking.Source.Context(interceptor);
        await using var tx = await db.Database.BeginTransactionAsync();
        var ack = await Service(db).DispatchInTransactionAsync(Id, request);
        await tx.CommitAsync(); return ack;
    }
    internal async Task<DeliveryInventoryPostingAck> ReceiveAsync(string quantity,
        DeliveryInventoryReceiveRequest? request = null, IInterceptor? interceptor = null)
    {
        var detail = await picking.GetAsync();
        var line = Assert.Single(detail.Lines, x => x.IsActive && !x.IsReplacement);
        request ??= new(Guid.NewGuid(), detail.Delivery.Version, [new(line.LineId, quantity)], "Nhận lại hàng thực tế tại kho");
        await using var db = picking.Source.Context(interceptor);
        await using var tx = await db.Database.BeginTransactionAsync();
        var ack = await Service(db).ReceiveReturnsInTransactionAsync(Id, request);
        await tx.CommitAsync(); return ack;
    }
    internal async Task<IReadOnlyList<DeliveryInventoryResidualCostDto>> ResidualAsync(string delivered,
        FullApplicationFixture.Account? actor = null)
    {
        var detail = await picking.GetAsync();
        var line = Assert.Single(detail.Lines, x => x.IsActive && !x.IsReplacement);
        await using var db = picking.Source.Context();
        await using var tx = await db.Database.BeginTransactionAsync();
        var costs = await Service(db, actor).GetResidualCostsInTransactionAsync(Id, [new(line.LineId, delivered)]);
        await tx.CommitAsync(); return costs;
    }
    internal async Task<D05PosCashSale> FinalizePosCashSaleAsync(decimal quantity)
    {
        var variant = await VariantAsync();
        var draft = await picking.Client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var orderId = draft.GetProperty("orderId").GetInt32();
        var priced = await picking.Client.JsonAsync(HttpMethod.Post, "/admin/pos/" + orderId + "/items?variantId=" + variant +
            "&qty=" + quantity.ToString(CultureInfo.InvariantCulture));
        var amount = priced.GetProperty("grandTotal").GetDecimal(); Assert.True(amount > 0);
        var result = new D05PosCashSale(orderId, Guid.NewGuid(), amount);
        await ReplayPosCashSaleAsync(result);
        return result;
    }
    internal async Task ReplayPosCashSaleAsync(D05PosCashSale sale)
    {
        var result = await picking.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-and-finalize",
            new { orderId = sale.OrderId, clientRequestId = sale.RequestId, method = 0, amount = sale.CashAmount });
        Assert.True(result.GetProperty("finalized").GetBoolean());
        Assert.Equal(sale.OrderId, result.GetProperty("orderId").GetInt32());
    }
    internal async Task<string> FinanceAsync()
    {
        await using var db = picking.Source.Context();
        return JsonSerializer.Serialize(new
        {
            Orders = await db.Orders.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Status, x.GrandTotal, x.PaidTotal, x.BalanceDue }).ToArrayAsync(),
            Payments = await db.OrderPayments.CountAsync(), Cash = await db.POSShiftCashTransactions.CountAsync(),
            Debt = await db.Set<CustomerReceivableEntry>().CountAsync(), Deposits = await db.Set<CustomerDepositEntry>().CountAsync(),
            Rewards = await db.CustomerRewardLedgers.CountAsync(), Invoices = await db.InvoiceHeads.CountAsync()
        });
    }
    internal async Task<string> SnapshotAsync()
    {
        await using var db = picking.Source.Context();
        return JsonSerializer.Serialize(new
        {
            Finance = await FinanceAsync(),
            Balances = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.OnHandQty, x.ReservedQty, x.InventoryValue, x.QuantityPrecisionVersion }).ToArrayAsync(),
            Holds = await db.InventoryReservations.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.ReservedQty, x.Status, x.DeliveryOrderId, x.DeliveryOrderLineId }).ToArrayAsync(),
            Movements = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.QuantityChange, x.UnitCostSnapshot, x.TotalCost, x.PostingPrecisionPolicy }).ToArrayAsync(),
            Layers = await db.InventoryCostLayers.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.RemainingQuantity, x.CostBudgetPolicy, x.BudgetAnchorQuantity, x.BudgetAnchorAmount, x.RemainingCostAmount }).ToArrayAsync(),
            Allocations = await db.InventoryCostLayerAllocations.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Quantity, x.Amount, x.ResolvedQuantity, x.ResolvedAmount, x.IsResolved, x.ResolvedByInventoryCostLayerId }).ToArrayAsync(),
            Valuations = await db.InventoryValuationEntries.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Quantity, x.UnitCost, x.Amount, x.IsProvisional, x.CostFinalizedAtUtc, x.SourceValuationEntryId, x.RevaluationOfEntryId }).ToArrayAsync(),
            Debits = await db.InventoryCostBudgetDebits.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.AnchorQuantity, x.AnchorAmount, x.StartQuantity, x.EndQuantity, x.Amount }).ToArrayAsync(),
            Journal = await db.DeliveryJournalEntries.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.BaseQuantity, x.CostAmount, x.Kind, x.MoneyAmount, x.SaleOrderId }).ToArrayAsync(),
            Fragments = await db.DeliveryDispatchCostFragments.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.BaseQuantity, x.CostAmount, x.InventoryCostBudgetDebitId }).ToArrayAsync(),
            Returns = await db.DeliveryReturnedCostFragments.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.StartQuantity, x.EndQuantity, x.BaseQuantity, x.CostAmount }).ToArrayAsync(),
            Delivery = await db.DeliveryOrders.AsNoTracking().Where(x => x.Id == Id)
                .Select(x => new { x.State, x.Revision, x.RowVersion }).SingleAsync(),
            Revisions = await db.DeliveryRevisions.CountAsync(), Receipts = await db.DeliveryCommandReceipts.CountAsync(),
            Events = await db.DeliveryOutboxMessages.CountAsync()
        });
    }
    internal static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    public void Dispose() => picking.Dispose();

    private sealed class D05Actor(int id, int terminal) : ICurrentUser
    {
        public int? UserId => id;
        public string? UserName => "D05 real fixture actor";
        public int? TerminalId => terminal;
        public string? TerminalCode => "D05";
        public bool IsAuthenticated => true;
    }
}

internal sealed record D05PosCashSale(int OrderId, Guid RequestId, decimal CashAmount);

// Used only by the old pre-lock unit test. An unexpected read fails rather than
// pretending that protected reservations/source evidence do not exist.
public class D05UnusedRepositoryProxy : DispatchProxy
{
    public static T Create<T>() where T : class => DispatchProxy.Create<T, D05UnusedRepositoryProxy>();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        => throw new NotSupportedException("Pre-lock test unexpectedly queried " + targetMethod?.Name);
}

public static class DeliveryD05BrowserFixture
{
    public static async Task<object> PrepareAsync(object fullApplicationFixture)
    {
        var web = (FullApplicationFixture)fullApplicationFixture;
        async Task<DeliveryD05Case> Create(decimal qty, IReadOnlyList<(decimal Quantity, decimal Cost)> layers)
        {
            var store = web.Stores[0];
            await using (var db = web.Database.CreateTenantContext(store.StoreId))
            {
                var terminal = new POSTerminal { StoreId = store.StoreId, Code = "D05-" + Guid.NewGuid().ToString("N")[..12], Name = "D05 browser terminal" };
                db.POSTerminals.Add(terminal); await db.SaveChangesAsync(); store = store with { TerminalId = terminal.Id };
            }
            var account = await web.AddAccountAsync(store, "*");
            var client = await web.LoginAsync(account);
            try
            {
                await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
                var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
                var cartId = draft.GetProperty("orderId").GetInt32();
                await client.JsonAsync(HttpMethod.Post, "/admin/pos/" + cartId + "/items?variantId=" + store.VariantId + "&qty=2");
                return new(await DeliveryD04Case.CreateAsync(new DeliveryD02Case(web, account, client, cartId), quantity: qty, stockLayers: layers));
            }
            catch { client.Dispose(); throw; }
        }
        using var created = await Create(8, [(8m, 10000m)]);
        using var held = await Create(8, [(8m, 10000m)]);
        await held.Picking.ClaimAsync(); await held.Picking.ReportAsync("8");
        using var posted = await Create(8, [(4m, 9000m), (4m, 11000m)]);
        await posted.ReadyAsync(); await posted.DispatchAsync(); await posted.ReceiveAsync("2");
        var viewer = await web.AddAccountAsync(held.Picking.Account.Store, PermissionCodes.Delivery.View);
        var costViewer = await web.AddAccountAsync(held.Picking.Account.Store, PermissionCodes.Delivery.View, PermissionCodes.Inventory.Valuation.View);
        var cancel = await web.AddAccountAsync(held.Picking.Account.Store, PermissionCodes.Delivery.View, PermissionCodes.Delivery.Cancel);
        var summary = await posted.GetAsync();
        Assert.Equal("8", summary.DispatchedBaseQuantityText); Assert.Equal("2", summary.AcceptedReturnedBaseQuantityText);
        Assert.Equal("6", summary.StockOutsideBaseQuantityText); Assert.Null(summary.InCustodyBaseQuantityText);
        return new
        {
            Created = new { created.Id, created.Picking.Source.Detail.LookupToken },
            Held = new { held.Id, held.Picking.Source.Detail.LookupToken },
            Posted = new { posted.Id, posted.Picking.Source.Detail.LookupToken },
            Creator = held.Picking.Account, Viewer = viewer, CostViewer = costViewer, Cancel = cancel,
            held.Picking.Account.Store.StoreId, held.Picking.Account.Store.TerminalId,
            summary.SourceWarehouseId, summary.SourceWarehouseName, summary.SourceLegalEntityId, summary.SourceLegalEntityName,
            Expected = new { DispatchedBaseQuantity = "8", AcceptedReturnedBaseQuantity = "2", StockOutsideBaseQuantity = "6",
                InCustodyBaseQuantity = (string?)null, DispatchedCost = "80000", AcceptedReturnedCost = "22000" }
        };
    }
}
