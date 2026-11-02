using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Returns;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Orders;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Tests.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06FinancialGuardTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("source-less-insert")] [InlineData("source-less-update")] [InlineData("delete")]
    [InlineData("amount-update")] [InlineData("receipt-budget-overrun")]
    public async Task Genuine_native_completed_sale_rejects_unproved_or_mutated_payment_children_with_precise_source_guard(string operation)
    {
        var (c, _) = await Posted(); var before = await Snapshot(c); var bank = await Web06BankInspection.CaptureAsync(c);
        await using var db = c.Context(); var sale = await db.Orders.SingleAsync(); var original = await db.OrderPayments.SingleAsync();
        Assert.Equal(OrderStatus.Completed, sale.Status); Assert.Equal(200_000m, original.Amount); Assert.NotNull(original.StorefrontCodReceiptId);
        Assert.Null(original.BankVerifiedReceiptId); Assert.Null(original.StorefrontCarrierWithholdingId);
        var now = DateTime.UtcNow;
        async Task Apply()
        {
            switch (operation)
            {
                case "source-less-insert":
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OrderPayments(StoreId,OrderId,Method,Amount,IsDebtCollection,PaidAtUtc,CreatedAtUtc,IsDeleted) VALUES({c.StoreId},{sale.Id},{(int)PaymentMethod.Cash},1,0,{now},{now},0)"); break;
                case "source-less-update":
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OrderPayments SET BankVerifiedReceiptId=NULL,StorefrontCodReceiptId=NULL,StorefrontCarrierWithholdingId=NULL WHERE StoreId={c.StoreId} AND Id={original.Id}"); break;
                case "delete":
                    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE OrderPayments WHERE StoreId={c.StoreId} AND Id={original.Id}"); break;
                case "amount-update":
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OrderPayments SET Amount=Amount+1 WHERE StoreId={c.StoreId} AND Id={original.Id}"); break;
                case "receipt-budget-overrun":
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OrderPayments(StoreId,OrderId,Method,Amount,StorefrontCodReceiptId,IsDebtCollection,PaidAtUtc,CreatedAtUtc,IsDeleted) VALUES({c.StoreId},{sale.Id},{(int)PaymentMethod.Cash},1,{original.StorefrontCodReceiptId},0,{now},{now},0)"); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
        var error = await Assert.ThrowsAsync<SqlException>(Apply); Assert.Equal(51200, error.Number);
        Assert.Equal(before, await Snapshot(c)); await bank.AssertDeltaAsync(db);
        await File.WriteAllTextAsync(Web06Support.Artifact("sql-guards/" + Guid.NewGuid().ToString("N") + ".json"),
            JsonSerializer.Serialize(new { Operation = operation, SqlNumber = error.Number, ActualNativeSale = sale.Id, c.StoreId, NativeSourceSnapshotUnchanged = true, GlobalBankBudgetUnchanged = true }));
    }
    [Fact]
    public async Task Ordinary_real_POS_draft_cash_row_without_native_source_remains_allowed_by_native_only_SQL_guard()
    {
        var c = await Web06Case.CreateAsync(fixture); var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await using var db = c.Context();
        var shift = await db.POSShifts.SingleAsync(x => x.TerminalId == terminal);
        var order = new Order { StoreId = c.StoreId, POSShiftId = shift.Id, Status = OrderStatus.Draft, Subtotal = 100, GrandTotal = 100, BalanceDue = 100 };
        order.Lines.Add(new() { StoreId = c.StoreId, ProductId = c.Seed.ProductId, VariantId = c.Seed.VariantId, SellingUnitId = c.Seed.UnitId,
            ItemName = "WEB06 ordinary POS SQL compatibility control", UnitName = "unit", Quantity = 1, BaseQuantity = 1, Multiplier = 1, UnitPrice = 100, LineTotal = 100 });
        db.Add(order); await db.SaveChangesAsync(); var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OrderPayments(StoreId,OrderId,Method,Amount,IsDebtCollection,PaidAtUtc,CreatedAtUtc,IsDeleted) VALUES({c.StoreId},{order.Id},{(int)PaymentMethod.Cash},10,0,{now},{now},0)");
        var payment = await db.OrderPayments.AsNoTracking().SingleAsync(x => x.OrderId == order.Id); Assert.Equal(10m, payment.Amount);
        Assert.Null(payment.BankVerifiedReceiptId); Assert.Null(payment.StorefrontCodReceiptId); Assert.Null(payment.StorefrontCarrierWithholdingId);
        Assert.Empty(await db.Set<StorefrontSaleBinding>().ToListAsync()); Assert.Equal(OrderStatus.Draft, (await db.Orders.AsNoTracking().SingleAsync()).Status);
        // This is a database compatibility control, not a claim that a POS payment/finalize facade ran.
    }
    [Theory]
    [InlineData("sale-status")] [InlineData("sale-price")] [InlineData("sale-quantity")] [InlineData("sale-origin")]
    [InlineData("physical-cost")] [InlineData("physical-source")] [InlineData("cod-amount")] [InlineData("cod-delete")]
    [InlineData("fee-gross")] [InlineData("fee-owner")] [InlineData("funding-amount")]
    public async Task Direct_SQL_cannot_reclassify_actual_native_sale_sources_or_rewrite_their_physical_and_financial_evidence(string mutation)
    {
        var (c, _) = await Posted(); var before = await Snapshot(c); await using var db = c.Context();
        var statement = mutation switch
        {
            "sale-status" => "UPDATE Orders SET Status=4 WHERE StoreId={0}",
            "sale-price" => "UPDATE OrderLines SET UnitPrice=1 WHERE StoreId={0}",
            "sale-quantity" => "UPDATE OrderLines SET Quantity=Quantity+1 WHERE StoreId={0}",
            "sale-origin" => "UPDATE OrderLines SET StorefrontOrderId=NULL,StorefrontOrderLineId=NULL WHERE StoreId={0}",
            "physical-cost" => "UPDATE DeliveryDispatchCostFragments SET CostAmount=1 WHERE StoreId={0}",
            "physical-source" => "UPDATE DeliveryOrders SET SourceKind=0 WHERE StoreId={0}",
            "cod-amount" => "UPDATE StorefrontCodReceipts SET Amount=1 WHERE StoreId={0}",
            "cod-delete" => "DELETE StorefrontCodReceipts WHERE StoreId={0}",
            "fee-gross" => "UPDATE StorefrontServiceRevenues SET GrossAmount=GrossAmount+1 WHERE StoreId={0}",
            "fee-owner" => "UPDATE StorefrontServiceRevenues SET LegalEntityId=2147483647 WHERE StoreId={0}",
            _ => "UPDATE StorefrontServiceFundings SET Amount=Amount+1 WHERE StoreId={0}"
        };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(statement, c.StoreId)); Assert.True(error.Number == 547 || error.Number >= 51000, "Expected real financial/source integrity guard; SQL=" + error.Number);
        Assert.Equal(before, await Snapshot(c));
        await File.WriteAllTextAsync(Web06Support.Artifact("sql-guards/" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(new { Mutation = mutation, error.Number, UnchangedFinancialAndPhysicalState = true, StoreId = c.StoreId, OrderId = c.OrderId }));
    }
    [Fact]
    public async Task Actual_ordinary_SalesReturnService_rejects_completed_native_source_before_legacy_stock_or_refund_paths()
    {
        var (c, terminal) = await Posted(); var before = await Snapshot(c); await using var db = c.Context(); var sale = await db.Orders.Include(x => x.Lines).SingleAsync();
        var service = new SalesReturnService(new UnitOfWork(db), new OrderRepository(db), new SalesReturnRepository(db), new POSShiftRepository(db), new POSAuditLogRepository(db),
            new WarehouseRepository(db), InventoryPosPostingContractTests.CreateRealMovementService(db), new InventoryMovementFactory(), Web06FinanceSupport.NoExternal<IAuditLogService>(),
            new SelectedStore(c.StoreId), new Web01Actor(c.Seed.UserId, terminalId: terminal), Web06FinanceSupport.NoExternal<IReturnableValuationFragmentService>(), new ReturnCostAllocator(),
            Web06FinanceSupport.NoExternal<ICustomerRewardLedgerRepository>(), Web06FinanceSupport.NoExternal<IOrderRewardCalculator>(), Web06FinanceSupport.NoExternal<IOrderLegalEntityReversalService>(), Web06FinanceSupport.NoExternal<IDraftInvoiceReturnSyncService>());
        var request = new CreateSalesReturnRequest { OrderId = sale.Id, POSShiftId = sale.POSShiftId, Type = SalesReturnType.ReturnOnly, Reason = "Must use actual website return workflow",
            Lines = [new() { OrderLineId = sale.Lines.Single().Id, ReturnQuantity = 1, ReturnBaseQuantity = 1, RefundUnitAmount = 50_000m, Action = SalesReturnLineAction.Restock }] };
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(request)); Assert.Contains("Đơn website", error.Message);
        Assert.Equal(before, await Snapshot(c)); Assert.Empty(await db.SalesReturns.ToListAsync()); Assert.Empty(await db.SalesReturnLines.ToListAsync());
    }
    [Fact]
    public async Task Forged_zero_approval_on_a_positive_native_child_is_rejected_while_real_zero_child_cases_remain_separately_covered()
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); await c.PickAllAsync(); var before = await c.PhysicalSnapshotAsync(); await using var db = c.Context();
        Assert.Equal(200_000m, (await db.DeliveryPickingWorks.SingleAsync()).ApprovedTotal);
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE DeliveryPickingWorks SET ApprovedTotal=0 WHERE StoreId={0}", c.StoreId));
        Assert.True(error.Number == 547 || error.Number >= 51000); Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Released_status_cannot_replace_the_real_consumed_web_or_delivery_transfer_proof(bool web)
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync(); var before = await c.PhysicalSnapshotAsync(); await using var db = c.Context();
        var transfer = await db.Set<StorefrontReservationTransfer>().SingleAsync();
        Assert.Equal(InventoryReservationStatus.Consumed, (await db.InventoryReservations.SingleAsync(x => x.Id == transfer.WebReservationId)).Status);
        Assert.Equal(InventoryReservationStatus.Consumed, (await db.InventoryReservations.SingleAsync(x => x.Id == transfer.DeliveryReservationId)).Status);
        var target = web ? transfer.WebReservationId : transfer.DeliveryReservationId;
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE InventoryReservations SET Status={0} WHERE StoreId={1} AND Id={2}", (int)InventoryReservationStatus.Released, c.StoreId, target));
        Assert.True(error.Number == 547 || error.Number >= 51000); Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }
    private async Task<(Web06Case Case, int Terminal)> Posted()
    {
        var c = await Web06Case.CreateAsync(fixture, shipping: 30_000m); await Web06FinanceSupport.ServiceSettingAsync(c); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 230_000m));
        await using var db = c.Context(); await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>()); return (c, terminal);
    }
    private static async Task<string> Snapshot(Web06Case c)
    {
        await using var db = c.Context();
        async Task<object[]> Rows<T>() where T : BaseEntity
        {
            var properties = db.Model.FindEntityType(typeof(T))!.GetProperties().Where(x => x.PropertyInfo is not null).OrderBy(x => x.Name).ToArray();
            return (await db.Set<T>().AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(row => (object)properties.ToDictionary(p => p.Name, p => p.PropertyInfo!.GetValue(row))).ToArray();
        }
        var value = JsonSerializer.Serialize(new { Physical = await c.PhysicalSnapshotAsync(), Sales = await Rows<Order>(), Lines = await Rows<OrderLine>(), Payments = await Rows<OrderPayment>(),
            Shifts = await Rows<POSShift>(), Cash = await Rows<POSShiftCashTransaction>(), InvoiceHeads = await Rows<InvoiceHead>(), InvoiceLines = await Rows<InvoiceDetail>(),
            Fees = await Rows<StorefrontServiceRevenue>(), Funding = await Rows<StorefrontServiceFunding>(), Receipts = await Rows<StorefrontCodReceipt>(), Bindings = await Rows<StorefrontSaleBinding>(), Returns = await Rows<SalesReturn>() });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
    private sealed class SelectedStore(int id) : ICurrentStore { public int StoreId => id; }
}
