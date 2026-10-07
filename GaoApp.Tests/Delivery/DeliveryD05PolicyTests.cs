using System.Globalization;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Delivery;

namespace GaoApp.Tests.Delivery;

[Trait("Category", "DeliveryD05")]
public sealed class DeliveryD05PolicyTests
{
    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(DeliveryState.Created, true)]
    [InlineData(DeliveryState.Picking, true)]
    [InlineData(DeliveryState.AwaitingApproval, true)]
    [InlineData(DeliveryState.ReadyForHandover, true)]
    [InlineData(DeliveryState.HandedOver, false)]
    [InlineData(DeliveryState.Delivering, false)]
    [InlineData(DeliveryState.AwaitingReconciliation, false)]
    [InlineData(DeliveryState.ReadyToSettle, false)]
    [InlineData(DeliveryState.CancellationPending, false)]
    [InlineData(DeliveryState.Settled, false)]
    [InlineData(DeliveryState.Cancelled, false)]
    public void Stock_evidence_always_blocks_pre_handover_cancellation(DeliveryState state, bool allowed)
    {
        Assert.Equal(allowed, DeliveryInventoryPolicy.CanCancelBeforeHandover(state, false));
        Assert.False(DeliveryInventoryPolicy.CanCancelBeforeHandover(state, true));
    }

    [Theory]
    [InlineData(true, null, "6", "0")]
    [InlineData(true, "0", "6", "0")]
    [InlineData(false, "2", "6", "0")]
    [InlineData(true, "0.0001", "6", "0.0006")]
    [InlineData(true, "3.1234", "2", "6.2468")]
    public void Only_active_explicit_physical_reports_produce_exact_base_holds(
        bool active, string? reported, string multiplier, string expected)
        => Assert.Equal(Number(expected), DeliveryInventoryPolicy.HeldBaseQuantity(
            active, reported is null ? null : Number(reported), Number(multiplier)));

    [Fact]
    public void Unrepresentable_physical_conversion_does_not_round_up_to_a_hold()
        => Assert.Equal("BASE_QUANTITY_INVALID", Assert.Throws<DeliveryRuleException>(() =>
            DeliveryInventoryPolicy.HeldBaseQuantity(true, 0.0001m, 0.5m)).Code);

    [Theory]
    [InlineData("8", "2", "6")]
    [InlineData("0.0003", "0.0001", "0.0002")]
    public void Exact_received_budget_accepts_only_the_remaining_dispatch_quantity(string dispatched, string received, string take)
        => DeliveryInventoryPolicy.RequireReceivedBudget(Number(dispatched), Number(received), Number(take));

    [Theory]
    [InlineData("8", "2", "6.0001", "RETURN_BUDGET_EXCEEDED")]
    [InlineData("8", "8", "0.0001", "RETURN_BUDGET_EXCEEDED")]
    [InlineData("8", "0", "0", "RETURN_BUDGET_EXCEEDED")]
    [InlineData("8", "-0.0001", "1", "QUANTITY_INVALID")]
    [InlineData("8", "0", "0.00001", "QUANTITY_INVALID")]
    public void Invalid_or_exhausted_return_budget_never_becomes_an_inbound(string dispatched, string received, string take, string code)
        => Assert.Equal(code, Assert.Throws<DeliveryRuleException>(() =>
            DeliveryInventoryPolicy.RequireReceivedBudget(Number(dispatched), Number(received), Number(take))).Code);

    [Theory]
    [InlineData("-0.0005", false, "-0.001")]
    [InlineData("0.0005", false, "0.001")]
    [InlineData("-0.0001", true, "-0.0001")]
    [InlineData("0.0001", true, "0.0001")]
    [InlineData("99999999999999.9999", true, "99999999999999.9999")]
    public void Promoted_legacy_delta_and_exact_Delivery_delta_keep_their_separate_steps(string raw, bool delivery, string expected)
        => Assert.Equal(Number(expected), InventoryPostingPrecisionPolicy.EffectiveDelta(Number(raw), delivery));

    [Theory]
    [InlineData("0.0001", false, "BELOW_LEGACY_STEP")]
    [InlineData("-0.0001", false, "BELOW_LEGACY_STEP")]
    [InlineData("0.00001", true, "INVENTORY_PRECISION")]
    [InlineData("100000000000000", true, "INVENTORY_PRECISION")]
    public void New_below_step_or_out_of_precision_requests_are_explicit_failures(string raw, bool delivery, string code)
        => Assert.Contains(code, Assert.Throws<BusinessRuleException>(() =>
            InventoryPostingPrecisionPolicy.EffectiveDelta(Number(raw), delivery)).Message, StringComparison.Ordinal);
}
