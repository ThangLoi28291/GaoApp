using System.Reflection;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.Services.Orders;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Delivery;

internal static class D01Data
{
    public static readonly DateTimeOffset Created = new(2026, 10, 6, 6, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Departed = Created.AddMinutes(80); // 14:20 UTC+7
    public static readonly DateTimeOffset Reconciled = Created.AddHours(3);
    public static readonly DateTimeOffset Closed = Created.AddDays(1);
    public static DeliveryOrigin Origin => new(1, 11, 101, 10, 1, 1001, 501, Created, 99);
    public static DeliveryClosingCounter CounterB => new(1, 2, 2002, 22, 20, true);
    public static DeliveryActor Cashier => Actor(DeliveryCapability.View | DeliveryCapability.Finalize |
        DeliveryCapability.ReconcileCash | DeliveryCapability.ConfirmBankTransfer | DeliveryCapability.Credit);
    public static DeliveryActor Actor(DeliveryCapability capabilities, int userId = 20)
        => new(1, userId, capabilities, new HashSet<int> { 11 }, new HashSet<int> { 101 });
    public static DeliveryQuantityLine A => new(1, 8, 8, 8, 6, 2, 2, 0, 0, 1, 10000);
    public static DeliveryQuantityLine B => new(2, 2, 0, 0, 0, 0, 0, 0, 0, 1, 15000);
    public static IReadOnlyList<DeliveryApprovedPriceLine> Prices => DeliveryPricingPolicy.Approve(
        new[] { new DeliveryPriceLine(1, 8, 20000), new DeliveryPriceLine(2, 2, 30000) });
    public static DeliverySettlementRequest Cash => new(DeliverySettlementMethod.Cash, 120000, 120000, 0, false, null, null, 0, null);
    public static DeliverySettlementRequest Bank => new(DeliverySettlementMethod.ConfirmedBankTransfer, 0, 0, 120000, true, "BANK-TEST-001", 1, 0, null);
    public static DeliverySettlementRequest Credit => new(DeliverySettlementMethod.CustomerCredit, 0, 0, 0, false, null, null, 120000, new(1, 99, true, true));
    public static DeliveryWorkflowFacts Complete => new(true, true, true, true, true, true, true, false);

    public static DeliverySettlementPlan Plan(DeliverySettlementRequest? request = null,
        DeliveryActor? actor = null, DeliveryClosingCounter? counter = null, DeliveryState state = DeliveryState.ReadyToSettle,
        IReadOnlyList<DeliveryQuantityLine>? quantities = null)
        => DeliverySettlementPolicy.Plan(Origin, counter ?? CounterB, actor ?? Cashier, state,
            quantities ?? new[] { A, B }, Prices, request ?? Cash, Departed, Reconciled, Closed);

    public static void Error(string code, Action action)
        => Assert.Equal(code, Assert.Throws<DeliveryRuleException>(action).Code);
}

[Trait("Category", "DeliveryD01")]
public sealed class DeliveryD01T01StateTests
{
    // Business oracle: every unlisted state/action pair is forbidden, including all terminal-state mutations.
    public static IEnumerable<object[]> Matrix()
    {
        var allowed = new Dictionary<(DeliveryState, DeliveryCommand), DeliveryState>
        {
            [(DeliveryState.Created, DeliveryCommand.StartPicking)] = DeliveryState.Picking,
            [(DeliveryState.Picking, DeliveryCommand.SubmitPicking)] = DeliveryState.AwaitingApproval,
            [(DeliveryState.AwaitingApproval, DeliveryCommand.ApprovePicking)] = DeliveryState.ReadyForHandover,
            [(DeliveryState.AwaitingApproval, DeliveryCommand.ReopenPicking)] = DeliveryState.Picking,
            [(DeliveryState.ReadyForHandover, DeliveryCommand.ReopenPicking)] = DeliveryState.Picking,
            [(DeliveryState.ReadyForHandover, DeliveryCommand.Handover)] = DeliveryState.HandedOver,
            [(DeliveryState.HandedOver, DeliveryCommand.Depart)] = DeliveryState.Delivering,
            [(DeliveryState.Delivering, DeliveryCommand.RecordOutcome)] = DeliveryState.AwaitingReconciliation,
            [(DeliveryState.AwaitingReconciliation, DeliveryCommand.RecordOutcome)] = DeliveryState.AwaitingReconciliation,
            [(DeliveryState.AwaitingReconciliation, DeliveryCommand.Reconcile)] = DeliveryState.ReadyToSettle,
            [(DeliveryState.ReadyToSettle, DeliveryCommand.Settle)] = DeliveryState.Settled,
            [(DeliveryState.Created, DeliveryCommand.CancelBeforeHandover)] = DeliveryState.Cancelled,
            [(DeliveryState.Picking, DeliveryCommand.CancelBeforeHandover)] = DeliveryState.Cancelled,
            [(DeliveryState.AwaitingApproval, DeliveryCommand.CancelBeforeHandover)] = DeliveryState.Cancelled,
            [(DeliveryState.ReadyForHandover, DeliveryCommand.CancelBeforeHandover)] = DeliveryState.Cancelled,
            [(DeliveryState.HandedOver, DeliveryCommand.RequestCancellation)] = DeliveryState.CancellationPending,
            [(DeliveryState.Delivering, DeliveryCommand.RequestCancellation)] = DeliveryState.CancellationPending,
            [(DeliveryState.AwaitingReconciliation, DeliveryCommand.RequestCancellation)] = DeliveryState.CancellationPending,
            [(DeliveryState.CancellationPending, DeliveryCommand.ConfirmCancellation)] = DeliveryState.Cancelled,
            [(DeliveryState.ReadyForHandover, DeliveryCommand.ReassignCourier)] = DeliveryState.ReadyForHandover,
            [(DeliveryState.HandedOver, DeliveryCommand.ReassignCourier)] = DeliveryState.HandedOver
        };
        foreach (var state in Enum.GetValues<DeliveryState>())
        foreach (var command in Enum.GetValues<DeliveryCommand>())
            yield return new object[] { state, command, allowed.TryGetValue((state, command), out var next) ? (int)next : -1 };
    }

    [Theory, MemberData(nameof(Matrix))]
    public void D01T01_FullTransitionMatrix(DeliveryState state, DeliveryCommand command, int next)
    {
        if (next < 0) D01Data.Error("TRANSITION_DENIED", () => DeliveryWorkflowPolicy.Apply(state, command, D01Data.Complete));
        else Assert.Equal((DeliveryState)next, DeliveryWorkflowPolicy.Apply(state, command, D01Data.Complete));
    }

    [Fact]
    public void D01T01_UnchangedCompletePickingCanProceedWithoutApproval()
        => Assert.Equal(DeliveryState.ReadyForHandover, DeliveryWorkflowPolicy.Apply(DeliveryState.Picking,
            DeliveryCommand.SubmitPicking, D01Data.Complete with { HasPickingChanges = false }));

    [Theory]
    [InlineData(DeliveryCommand.SubmitPicking, DeliveryState.Picking)]
    [InlineData(DeliveryCommand.ApprovePicking, DeliveryState.AwaitingApproval)]
    public void D01T01_IncompletePickingCannotProceed(DeliveryCommand command, DeliveryState state)
        => D01Data.Error("PICKING_INCOMPLETE", () => DeliveryWorkflowPolicy.Apply(state, command, D01Data.Complete with { PickingComplete = false }));

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void D01T01_HandoverRequiresActualPickingGoodsAndCustody(bool complete, bool goods, bool courier, bool custody)
        => D01Data.Error("HANDOVER_INCOMPLETE", () => DeliveryWorkflowPolicy.Apply(DeliveryState.ReadyForHandover,
            DeliveryCommand.Handover, D01Data.Complete with { PickingComplete = complete, HasPickedGoods = goods, CourierAssigned = courier, CustodyHandoverConfirmed = custody }));

    [Theory]
    [InlineData(DeliveryCommand.Reconcile, DeliveryState.AwaitingReconciliation)]
    [InlineData(DeliveryCommand.Settle, DeliveryState.ReadyToSettle)]
    [InlineData(DeliveryCommand.ConfirmCancellation, DeliveryState.CancellationPending)]
    public void D01T01_ReportIsNotReconciliation(DeliveryCommand command, DeliveryState state)
    {
        D01Data.Error("RECONCILIATION_REQUIRED", () => DeliveryWorkflowPolicy.Apply(state, command, D01Data.Complete with { GoodsReconciled = false }));
        D01Data.Error("RECONCILIATION_REQUIRED", () => DeliveryWorkflowPolicy.Apply(state, command, D01Data.Complete with { MoneyReconciled = false }));
    }

    [Theory]
    [InlineData(DeliveryCommand.RequestCancellation, DeliveryState.Delivering)]
    [InlineData(DeliveryCommand.ConfirmCancellation, DeliveryState.CancellationPending)]
    public void D01T01_DeliveredGoodsCannotBeCancelled(DeliveryCommand command, DeliveryState state)
        => D01Data.Error("DELIVERED_CANNOT_CANCEL", () => DeliveryWorkflowPolicy.Apply(state, command, D01Data.Complete with { HasDeliveredGoods = true }));

    [Fact]
    public void D01T01_UnknownEnumIsDenied()
        => D01Data.Error("STATE_INVALID", () => DeliveryWorkflowPolicy.Apply((DeliveryState)999, DeliveryCommand.Settle, D01Data.Complete));
}

[Trait("Category", "DeliveryD01")]
public sealed class DeliveryD01T02QuantityMoneyTests
{
    [Fact]
    public void D01T02_Golden220000Quote120000SaleStock94Cost60000ShortageNoDebt()
    {
        Assert.Equal(220000m, D01Data.Prices.Sum(x => x.Net));
        var dispatched = DeliveryQuantityPolicy.Evaluate(D01Data.A with { Delivered = 0, ReportedReturn = 0, AcceptedReturn = 0 });
        Assert.Equal(92m, 100 + dispatched.PhysicalStockDelta);
        Assert.False(dispatched.GoodsReconciled);
        var reported = DeliveryQuantityPolicy.Evaluate(D01Data.A with { AcceptedReturn = 0 });
        Assert.Equal(92m, 100 + reported.PhysicalStockDelta); // A phone report does not receive goods.
        Assert.Equal(2m, reported.PendingReturnReceipt);
        var received = DeliveryQuantityPolicy.Evaluate(D01Data.A);
        Assert.Equal(94m, 100 + received.PhysicalStockDelta);
        Assert.Equal(60000m, received.SaleCost);
        Assert.Equal(2m, received.OutstandingQuantity);
        var missing = DeliveryQuantityPolicy.Evaluate(D01Data.B);
        Assert.Equal(0m, missing.PhysicalStockDelta);
        Assert.Equal(2m, missing.OutstandingQuantity);
        var sale = D01Data.Plan();
        Assert.True(sale.CreatesSale);
        Assert.Equal(120000m, sale.SaleAmount);
        Assert.Equal(120000m, sale.ShiftCashAmount);
        Assert.Equal(0m, sale.CustomerDebt);
    }

    [Theory]
    [InlineData(DeliverySettlementMethod.Cash, 120000, 0, 0)]
    [InlineData(DeliverySettlementMethod.ConfirmedBankTransfer, 0, 120000, 0)]
    [InlineData(DeliverySettlementMethod.CustomerCredit, 0, 0, 120000)]
    public void D01T02_IndependentSettlementMethodsOnlyChargeActualDelivery(DeliverySettlementMethod method, int cash, int bank, int debt)
    {
        var request = method == DeliverySettlementMethod.Cash ? D01Data.Cash : method == DeliverySettlementMethod.ConfirmedBankTransfer ? D01Data.Bank : D01Data.Credit;
        var plan = D01Data.Plan(request);
        Assert.Equal(120000m, plan.SaleAmount);
        Assert.Equal(cash, plan.ShiftCashAmount);
        Assert.Equal(bank, plan.NonCashAmount);
        Assert.Equal(debt, plan.CustomerDebt);
    }

    [Fact]
    public void D01T02_CourierShortCashCannotBecomeCustomerDebtOrFakeCash()
        => D01Data.Error("CASH_HANDOVER_DIFFERENCE", () => D01Data.Plan(D01Data.Cash with { ReceivedCash = 110000 }));

    [Fact]
    public void D01T02_PhaseOneRejectsMixedPayment()
        => D01Data.Error("MIXED_PAYMENT_UNSUPPORTED", () => D01Data.Plan(D01Data.Credit with { ReportedCustomerCash = 70000, ReceivedCash = 70000, CreditAmount = 50000 }));

    [Fact]
    public void D01T02_PhaseOneRejectsAdvanceAtQuoteAndSettlement()
    {
        D01Data.Error("ADVANCE_UNSUPPORTED", () => DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(1, 1, 20000) }, advanceAmount: 10000));
        D01Data.Error("ADVANCE_UNSUPPORTED", () => D01Data.Plan(D01Data.Cash with { AdvanceAmount = 10000 }));
    }

    [Fact]
    public void D01T02_ZeroDeliveredClosesWithoutSaleInvoiceOrDebtPlan()
    {
        var plan = D01Data.Plan(D01Data.Cash with { ReportedCustomerCash = 0, ReceivedCash = 0 },
            quantities: new[] { D01Data.A with { Delivered = 0, ReportedReturn = 8, AcceptedReturn = 8 }, D01Data.B });
        Assert.False(plan.CreatesSale);
        Assert.Equal(0m, plan.SaleAmount + plan.CustomerDebt + plan.ShiftCashAmount + plan.NonCashAmount);
    }

    [Fact]
    public void D01T02_FollowupIsNew60000SaleWithoutChangingFirst120000()
    {
        var first = D01Data.Plan();
        var missing = DeliveryQuantityPolicy.Evaluate(D01Data.B);
        Assert.Equal(2m, missing.OutstandingQuantity);
        var next = DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(3, 2, 30000) });
        Assert.Equal(60000m, DeliveryPricingPolicy.Charge(next, new Dictionary<int, decimal> { [3] = 2 }).Single().Amount);
        Assert.Equal(120000m, first.SaleAmount);
        Assert.Equal(0m, first.CustomerDebt); // Contact/waiver never modifies the first settlement.
    }

    [Theory]
    [InlineData("2", "24", "1.5", "36", "60000")]
    [InlineData("2.5", "1", "1.25", "1.25", "25000")]
    public void D01T02_SellingAndBaseUnitsRemainDistinct(string ordered, string multiplier, string delivered, string baseDelivered, string charged)
    {
        static decimal D(string v) => decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
        var q = D(ordered); var d = D(delivered);
        var line = new DeliveryQuantityLine(9, q, q, q, d, q - d, q - d, 0, 0, D(multiplier), 1000);
        Assert.Equal(D(baseDelivered), DeliveryQuantityPolicy.Evaluate(line).DeliveredBase);
        // Explicit prices: 40,000/box and 20,000/kg.
        var approved = DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(9, q, multiplier == "24" ? 40000m : 20000m) });
        Assert.Equal(D(charged), DeliveryPricingPolicy.Charge(approved, new Dictionary<int, decimal> { [9] = d }).Single().Amount);
    }

    [Fact]
    public void D01T02_DiscountsStayWithApprovedLinesAndProrateDeliveredQty()
    {
        var prices = DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(1, 8, 20000, 8000), new DeliveryPriceLine(2, 2, 30000) }, orderDiscount: 21200);
        Assert.Equal(15200m, prices[0].AllocatedOrderDiscount);
        Assert.Equal(6000m, prices[1].AllocatedOrderDiscount);
        var charged = DeliveryPricingPolicy.Charge(prices, new Dictionary<int, decimal> { [1] = 6, [2] = 0 });
        Assert.Equal(102600m, charged.Sum(x => x.Amount));
        Assert.Equal(0m, charged[1].Amount);
    }

    [Fact]
    public void D01T02_WholeDongAllocationIsStableAndHalfRoundsAwayFromZero()
    {
        var prices = DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(3, 1, 1), new DeliveryPriceLine(2, 1, 1), new DeliveryPriceLine(1, 1, 1) }, orderDiscount: 1);
        Assert.Equal(1m, prices.Single(x => x.LineId == 1).AllocatedOrderDiscount);
        Assert.Equal(1m, prices.Sum(x => x.AllocatedOrderDiscount));
        var half = DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(9, 2, 1000.25m) });
        Assert.Equal(2001m, half[0].Net);
        Assert.Equal(1001m, DeliveryPricingPolicy.Charge(half, new Dictionary<int, decimal> { [9] = 1 }).Single().Amount);
    }

    [Fact]
    public void D01T02_LargeValidDiscountCannotOverflowIntermediateArithmetic()
    {
        var approved = DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(1, 1, 9000000000000000m), new DeliveryPriceLine(2, 1, 1) }, 4500000000000000m);
        Assert.Equal(4500000000000000m, approved.Sum(x => x.AllocatedOrderDiscount));
        Assert.Equal(4500000000000001m, approved.Sum(x => x.Net));
    }

    [Theory]
    [InlineData(-1, 8, 8, 6, 2, 2, "QUANTITY_INVALID")]
    [InlineData(8, 9, 8, 6, 2, 2, "QUANTITY_EXCEEDS_SOURCE")]
    [InlineData(8, 7, 8, 6, 2, 2, "QUANTITY_EXCEEDS_SOURCE")]
    [InlineData(8, 8, 8, 7, 2, 2, "OUTCOME_EXCEEDS_DISPATCH")]
    [InlineData(8, 8, 8, 6, 2, 3, "QUANTITY_EXCEEDS_SOURCE")]
    public void D01T02_BadQuantityIsRejected(int ordered, int picked, int dispatched, int delivered, int reported, int accepted, string code)
        => D01Data.Error(code, () => DeliveryQuantityPolicy.Evaluate(D01Data.A with { Ordered = ordered, Picked = picked, Dispatched = dispatched, Delivered = delivered, ReportedReturn = reported, AcceptedReturn = accepted }));

    [Fact]
    public void D01T02_UnitConversionCannotSilentlyRoundOrOverflowStorage()
    {
        D01Data.Error("QUANTITY_INVALID", () => DeliveryQuantityPolicy.Evaluate(new(1, 0.0001m, 0.0001m, 0.0001m, 0, 0, 0, 0, 0, 0.0001m, 1)));
        D01Data.Error("QUANTITY_INVALID", () => DeliveryQuantityPolicy.Evaluate(new(1, 100000000m, 100000000m, 100000000m, 0, 0, 0, 0, 0, 100000000m, 1)));
    }

    [Fact]
    public void D01T02_DamageLossNeedsManagerAndCannotBeSoldOrReceivedAsGoodStock()
    {
        var line = D01Data.A with { ReportedReturn = 1, AcceptedReturn = 1, Damaged = 0.5m, Lost = 0.5m };
        var pending = DeliveryQuantityPolicy.Evaluate(line);
        Assert.False(pending.GoodsReconciled);
        var approved = DeliveryQuantityPolicy.Evaluate(line with { ExceptionsApproved = true });
        Assert.True(approved.GoodsReconciled);
        Assert.Equal(93m, 100 + approved.PhysicalStockDelta);
        Assert.Equal(60000m, approved.SaleCost);
        Assert.Equal(10000m, approved.ExceptionCost);
    }

    [Fact]
    public void D01T02_UnreceivedGoodsBlockSettlement()
        => D01Data.Error("GOODS_UNRECONCILED", () => D01Data.Plan(quantities: new[] { D01Data.A with { AcceptedReturn = 1 }, D01Data.B }));

    [Fact]
    public void D01T02_UnsupportedOrCorruptPricesAreExplicitErrors()
    {
        D01Data.Error("PRICE_FEATURE_UNSUPPORTED", () => DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(1, 1, 100) }, hasConditionalPromotionOrVoucher: true));
        D01Data.Error("PRICE_FEATURE_UNSUPPORTED", () => DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(1, 1, 100) }, additionalTaxOrFee: 10));
        D01Data.Error("DISCOUNT_EXCEEDS_PRICE", () => DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(1, 1, 100, 101) }));
        D01Data.Error("DISCOUNT_EXCEEDS_PRICE", () => DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(1, 1, 100) }, orderDiscount: 101));
        D01Data.Error("PRICE_SNAPSHOT_INVALID", () => DeliveryPricingPolicy.Charge(new[] { new DeliveryApprovedPriceLine(1, 8, 160000, 0, 0, 159999) }, new Dictionary<int, decimal> { [1] = 6 }));
        D01Data.Error("PRICE_LINES_MISMATCH", () => DeliveryPricingPolicy.Charge(D01Data.Prices, new Dictionary<int, decimal> { [1] = 6, [999] = 0 }));
    }

    [Theory]
    [InlineData(119999)]
    [InlineData(120001)]
    public void D01T02_ActualCashMustMatchSale(int cash)
        => D01Data.Error("PAYMENT_AMOUNT_MISMATCH", () => D01Data.Plan(D01Data.Cash with { ReceivedCash = cash, ReportedCustomerCash = cash }));

    [Fact]
    public void D01T02_HugePricesAndCostsRejectBeforeArithmeticOverflow()
    {
        D01Data.Error("MONEY_INVALID", () => DeliveryPricingPolicy.Approve(new[] { new DeliveryPriceLine(1, 99999999999999m, 9999999999999999m) }));
        D01Data.Error("COST_INVALID", () => DeliveryQuantityPolicy.Evaluate(new(1, 99999999999999m, 99999999999999m,
            99999999999999m, 99999999999999m, 0, 0, 0, 0, 1, 999999999999m)));
    }

    [Fact]
    public void D01T02_ValuationPrecisionIsNotRoundedToCashDong()
    {
        var plan = DeliveryQuantityPolicy.Evaluate(new(1, 1, 1, 1, 1, 0, 0, 0, 0, 1, 10000.123456m));
        Assert.Equal(10000.1235m, plan.SaleCost);
    }

    [Fact]
    public void D01T02_ExistingSixDecimalMultiplierIsPreservedWhenBaseQtyIsRepresentable()
    {
        var plan = DeliveryQuantityPolicy.Evaluate(new(1, 100000, 100000, 100000, 100000, 0, 0, 0, 0, 0.123456m, 1));
        Assert.Equal(12345.6m, plan.DeliveredBase);
    }

    [Fact]
    public void D01T02_MixedFifoFragmentsRetainSourceCostsAndReturnedLayer()
    {
        var fragments = new[]
        {
            new DeliveryCostFragment(501, 11, 101, 4, 4, 0, 0, 0, 9000),
            new DeliveryCostFragment(502, 11, 101, 4, 2, 2, 0, 0, 11000)
        };
        var costs = DeliveryQuantityPolicy.EvaluateFragments(D01Data.Origin, D01Data.A, fragments);
        Assert.Equal(58000m, costs.SaleCost);
        Assert.Equal(22000m, costs.ReturnedCost);
        Assert.Equal(0m, costs.ExceptionCost);
        D01Data.Error("COST_SOURCE_MISMATCH", () => DeliveryQuantityPolicy.EvaluateFragments(D01Data.Origin,
            D01Data.A, new[] { fragments[0], fragments[1] with { WarehouseId = 22 } }));
        D01Data.Error("COST_FRAGMENTS_INVALID", () => DeliveryQuantityPolicy.EvaluateFragments(D01Data.Origin,
            D01Data.A, new[] { fragments[0], fragments[1] with { AllocationId = 501 } }));
        D01Data.Error("COST_QUANTITY_MISMATCH", () => DeliveryQuantityPolicy.EvaluateFragments(D01Data.Origin,
            D01Data.A, new[] { fragments[0] with { DeliveredBase = 3 }, fragments[1] }));
    }
}

[Trait("Category", "DeliveryD01")]
public sealed class DeliveryD01T03PermissionTests
{
    public static IEnumerable<object[]> Roles()
    {
        var roles = new[]
        {
            ("Picker", DeliveryCapability.View | DeliveryCapability.Pick, new[] { DeliveryCapability.View, DeliveryCapability.Pick }),
            ("Courier", DeliveryCapability.View | DeliveryCapability.ReportOwnOutcome, new[] { DeliveryCapability.View, DeliveryCapability.ReportOwnOutcome }),
            ("Cashier", D01Data.Cashier.Capabilities, new[] { DeliveryCapability.View, DeliveryCapability.Finalize, DeliveryCapability.ReconcileCash, DeliveryCapability.ConfirmBankTransfer, DeliveryCapability.Credit })
        };
        foreach (var role in roles)
        foreach (var capability in Enum.GetValues<DeliveryCapability>().Where(x => x != DeliveryCapability.None))
            yield return new object[] { role.Item1, role.Item2, capability, role.Item3.Contains(capability) };
    }

    [Theory, MemberData(nameof(Roles))]
    public void D01T03_RoleCapabilityMatrix(string role, DeliveryCapability capabilities, DeliveryCapability operation, bool allowed)
    {
        Assert.NotEmpty(role);
        var actor = D01Data.Actor(capabilities);
        if (allowed) DeliveryAccessPolicy.Ensure(actor, D01Data.Origin, operation);
        else D01Data.Error("CAPABILITY_FORBIDDEN", () => DeliveryAccessPolicy.Ensure(actor, D01Data.Origin, operation));
    }

    [Fact]
    public void D01T03_StoreWarehouseLegalOwnerAreAllBoundaries()
    {
        D01Data.Error("STORE_FORBIDDEN", () => D01Data.Plan(actor: D01Data.Cashier with { StoreId = 2 }));
        D01Data.Error("SOURCE_FORBIDDEN", () => D01Data.Plan(actor: D01Data.Cashier with { WarehouseIds = new HashSet<int> { 22 } }));
        D01Data.Error("SOURCE_FORBIDDEN", () => D01Data.Plan(actor: D01Data.Cashier with { LegalEntityIds = new HashSet<int> { 102 } }));
        D01Data.Error("CLOSING_COUNTER_FORBIDDEN", () => D01Data.Plan(counter: D01Data.CounterB with { StoreId = 2 }));
    }

    [Fact]
    public void D01T03_FinalizePermissionAloneCannotConfirmCashBankOrCredit()
    {
        var limited = D01Data.Actor(DeliveryCapability.Finalize);
        D01Data.Error("CAPABILITY_FORBIDDEN", () => D01Data.Plan(D01Data.Cash, limited));
        D01Data.Error("CAPABILITY_FORBIDDEN", () => D01Data.Plan(D01Data.Bank, limited));
        D01Data.Error("CAPABILITY_FORBIDDEN", () => D01Data.Plan(D01Data.Credit, limited));
    }

    [Theory]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 2)]
    public void D01T03_CreditRequiresExistingCustomerEligibility(bool active, bool debtAllowed, int store)
        => D01Data.Error("CUSTOMER_CREDIT_FORBIDDEN", () => D01Data.Plan(D01Data.Credit with { Customer = new(store, 99, active, debtAllowed) }));

    [Fact]
    public void D01T03_TransferRequiresServerBankConfirmationAndReference()
    {
        D01Data.Error("BANK_UNCONFIRMED", () => D01Data.Plan(D01Data.Bank with { BankVerified = false }));
        D01Data.Error("BANK_UNCONFIRMED", () => D01Data.Plan(D01Data.Bank with { BankReference = null }));
        D01Data.Error("BANK_UNCONFIRMED", () => D01Data.Plan(D01Data.Bank with { BankAccountStoreId = 2 }));
    }

    [Fact]
    public void D01T03_NoEmptyOrUnknownCapabilityBypass()
    {
        D01Data.Error("CAPABILITY_FORBIDDEN", () => DeliveryAccessPolicy.Ensure(D01Data.Cashier, D01Data.Origin, DeliveryCapability.None));
        D01Data.Error("CAPABILITY_FORBIDDEN", () => DeliveryAccessPolicy.Ensure(D01Data.Actor((DeliveryCapability)262144), D01Data.Origin, (DeliveryCapability)262144));
    }

    [Fact]
    public void D01T03_CreditCannotSilentlySwitchTheFrozenCustomer()
        => D01Data.Error("CUSTOMER_CREDIT_FORBIDDEN", () => D01Data.Plan(D01Data.Credit with { Customer = new(1, 100, true, true) }));
}

[Trait("Category", "DeliveryD01")]
public sealed class DeliveryD01T04ArchitectureTests
{
    [Fact]
    public void D01T04_ClosedOriginShiftDoesNotControlNewCounterAndNextDayRevenue()
    {
        // No origin-shift-open field exists in settlement context: original provenance remains immutable.
        var origin = D01Data.Origin;
        var plan = D01Data.Plan();
        Assert.Equal(1, plan.CreatedTerminalId);
        Assert.Equal(1001, plan.CreatedShiftId);
        Assert.Equal(2, plan.SettlingTerminalId);
        Assert.Equal(2002, plan.SettlingShiftId);
        Assert.Equal(22, plan.SettlingCounterWarehouseId);
        Assert.Equal(11, plan.SourceWarehouseId);
        Assert.Equal(101, plan.SourceLegalEntityId);
        Assert.Equal(new DateOnly(2026, 10, 6), plan.CreationDate);
        Assert.Equal(new DateOnly(2026, 10, 6), plan.DispatchDate);
        Assert.Equal(new DateOnly(2026, 10, 6), plan.ReconciliationDate);
        Assert.Equal(new DateOnly(2026, 10, 7), plan.RevenueDate);
        Assert.Equal(origin, D01Data.Origin);
    }

    [Theory]
    [InlineData(false, 20)]
    [InlineData(true, 999)]
    public void D01T04_SettlingShiftMustBeOpenAndOwnedByActor(bool open, int owner)
        => D01Data.Error("CLOSING_COUNTER_FORBIDDEN", () => D01Data.Plan(counter: D01Data.CounterB with { IsOpen = open, ShiftOwnerUserId = owner }));

    [Fact]
    public void D01T04_NormalPosActualGuardStillRejectsDifferentShiftAndCompletedCart()
    {
        var guard = typeof(POSService).GetMethod("EnsureCanBeCurrentCart", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(guard);
        var order = new Order { Id = 501, StoreId = 1, POSShiftId = 1001, Status = OrderStatus.Draft };
        var wrongShift = Assert.Throws<TargetInvocationException>(() => guard.Invoke(null, new object[] { order, 2002 }));
        Assert.IsType<PosAppException>(wrongShift.InnerException);
        guard.Invoke(null, new object[] { order, 1001 });
        order.Status = OrderStatus.Completed;
        var completed = Assert.Throws<TargetInvocationException>(() => guard.Invoke(null, new object[] { order, 1001 }));
        Assert.IsType<PosAppException>(completed.InnerException);
    }

    [Fact]
    public void D01T04_BusinessDatesUseUtcPlusSevenBoundary()
    {
        var before = new DateTimeOffset(2026, 10, 6, 16, 59, 59, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 10, 6), DeliveryValues.BusinessDate(before));
        Assert.Equal(new DateOnly(2026, 10, 7), DeliveryValues.BusinessDate(before.AddSeconds(1)));
    }

    [Fact]
    public void D01T04_NoReplayMutationAfterSettlementAndPlansHaveNoPostingSideEffect()
    {
        Assert.Equal(D01Data.Plan(), D01Data.Plan());
        D01Data.Error("TRANSITION_DENIED", () => DeliveryWorkflowPolicy.Apply(DeliveryState.Settled, DeliveryCommand.Settle, D01Data.Complete));
        var after = DeliveryQuantityPolicy.Evaluate(D01Data.A);
        Assert.Equal(94m, 100 + after.PhysicalStockDelta);
        // Durable replay/concurrent finalization uniqueness is a D02/D08 SQL acceptance gate.
    }
}

[Trait("Category", "DeliveryD01")]
public sealed class DeliveryD01T05ProxyReportTests
{
    [Fact]
    public void D01T05_PhoneReportRetainsActorCourierSourceAndSeparateServerCustomerTimes()
    {
        var actor = D01Data.Actor(DeliveryCapability.ReportOnBehalf, 20);
        var serverAt = D01Data.Departed.AddMinutes(25);
        var customerAt = D01Data.Departed.AddMinutes(20);
        var report = DeliveryReportPolicy.Record(actor, D01Data.Origin, DeliveryState.Delivering, 44, 44,
            DeliveryReportSource.Phone, D01Data.Departed, serverAt, customerAt);
        Assert.Equal(20, report.ActorUserId);
        Assert.Equal(44, report.CourierUserId);
        Assert.Equal(DeliveryReportSource.Phone, report.Source);
        Assert.Equal(serverAt, report.RecordedAtUtc);
        Assert.Equal(customerAt, report.CustomerReceivedAtUtc);
        Assert.NotEqual(report.CustomerReceivedAtUtc, report.RecordedAtUtc);
    }

    [Fact]
    public void D01T05_CourierMayReportOnlyOwnAssignedOrder()
    {
        var actor = D01Data.Actor(DeliveryCapability.ReportOwnOutcome, 44);
        Assert.Equal(44, DeliveryReportPolicy.Record(actor, D01Data.Origin, DeliveryState.Delivering, 44, 44,
            DeliveryReportSource.WebByCourier, D01Data.Departed, D01Data.Departed.AddMinutes(25)).ActorUserId);
        D01Data.Error("COURIER_MISMATCH", () => DeliveryReportPolicy.Record(actor, D01Data.Origin, DeliveryState.Delivering, 45, 44,
            DeliveryReportSource.WebByCourier, D01Data.Departed, D01Data.Departed.AddMinutes(25)));
    }

    [Fact]
    public void D01T05_ProxyRequiresSpecificPermissionAndCannotPretendCourierSubmitted()
    {
        var actor = D01Data.Actor(DeliveryCapability.ReportOwnOutcome);
        D01Data.Error("CAPABILITY_FORBIDDEN", () => DeliveryReportPolicy.Record(actor, D01Data.Origin, DeliveryState.Delivering, 44, 44,
            DeliveryReportSource.Phone, D01Data.Departed, D01Data.Departed.AddMinutes(25)));
        actor = D01Data.Actor(DeliveryCapability.ReportOnBehalf);
        D01Data.Error("PROXY_SOURCE_INVALID", () => DeliveryReportPolicy.Record(actor, D01Data.Origin, DeliveryState.Delivering, 44, 44,
            DeliveryReportSource.WebByCourier, D01Data.Departed, D01Data.Departed.AddMinutes(25)));
    }

    [Theory]
    [InlineData(DeliveryState.ReadyToSettle)]
    [InlineData(DeliveryState.Settled)]
    [InlineData(DeliveryState.Cancelled)]
    [InlineData(DeliveryState.CancellationPending)]
    public void D01T05_LateReportsCannotRewriteLockedOutcomes(DeliveryState state)
        => D01Data.Error("REPORT_LOCKED", () => DeliveryReportPolicy.Record(D01Data.Actor(DeliveryCapability.ReportOnBehalf), D01Data.Origin,
            state, 44, 44, DeliveryReportSource.AtStore, D01Data.Departed, D01Data.Departed.AddHours(2)));

    [Fact]
    public void D01T05_Elapsed25MinutesOrReportedDeliveryNeverAutoFinalizesMoney()
    {
        var elapsed = DeliveryReportPolicy.Elapsed(D01Data.Departed, D01Data.Departed.AddMinutes(25));
        Assert.Equal(TimeSpan.FromMinutes(25), elapsed);
        Assert.Equal(DeliveryState.AwaitingReconciliation, DeliveryWorkflowPolicy.Apply(DeliveryState.Delivering, DeliveryCommand.RecordOutcome, D01Data.Complete));
        D01Data.Error("SETTLEMENT_NOT_READY", () => D01Data.Plan(state: DeliveryState.Delivering));
        D01Data.Error("SETTLEMENT_NOT_READY", () => D01Data.Plan(state: DeliveryState.AwaitingReconciliation));
        Assert.False(DeliveryQuantityPolicy.Evaluate(D01Data.A with { AcceptedReturn = 0 }).GoodsReconciled);
    }

    [Fact]
    public void D01T05_TimestampsAndSourcesCannotBeForgedOrFutureDated()
    {
        var actor = D01Data.Actor(DeliveryCapability.ReportOnBehalf);
        var serverAt = D01Data.Departed.AddMinutes(25);
        D01Data.Error("CUSTOMER_TIME_INVALID", () => DeliveryReportPolicy.Record(actor, D01Data.Origin, DeliveryState.Delivering, 44, 44,
            DeliveryReportSource.Phone, D01Data.Departed, serverAt, serverAt.AddMinutes(1)));
        D01Data.Error("TIMELINE_INVALID", () => DeliveryReportPolicy.Record(actor, D01Data.Origin, DeliveryState.Delivering, 44, 44,
            DeliveryReportSource.Phone, D01Data.Departed, D01Data.Departed.AddMinutes(-1)));
        D01Data.Error("UTC_REQUIRED", () => DeliveryReportPolicy.Record(actor, D01Data.Origin, DeliveryState.Delivering, 44, 44,
            DeliveryReportSource.Phone, D01Data.Departed, serverAt.ToOffset(TimeSpan.FromHours(7))));
        D01Data.Error("REPORT_SOURCE_INVALID", () => DeliveryReportPolicy.Record(actor, D01Data.Origin, DeliveryState.Delivering, 44, 44,
            (DeliveryReportSource)999, D01Data.Departed, serverAt));
    }
}
