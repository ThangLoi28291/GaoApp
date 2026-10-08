using GaoApp.Domain.Delivery;

namespace GaoApp.Tests.Delivery;

[Trait("Category", "DeliveryD06")]
public sealed class DeliveryD06PolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void Route_selection_accepts_the_locked_minimum_and_maximum(int count)
        => DeliveryTripPolicy.Selection(Enumerable.Range(1, count).ToArray());

    [Theory]
    [InlineData("empty")]
    [InlineData("too-many")]
    [InlineData("duplicate")]
    [InlineData("nonpositive")]
    [InlineData("null")]
    public void Invalid_route_selection_never_collapses_distinct_stops(string fault)
    {
        int[]? ids = fault switch {
            "empty" => [], "too-many" => Enumerable.Range(1, 51).ToArray(),
            "duplicate" => [1, 1], "nonpositive" => [1, 0], _ => null };
        Assert.Equal("ORDERS_INVALID", Assert.Throws<DeliveryRuleException>(() => DeliveryTripPolicy.Selection(ids)).Code);
    }

    [Theory]
    [InlineData(false, 7, 7)]
    [InlineData(true, 8, 7)]
    [InlineData(true, 0, 0)]
    public void Physical_receipt_requires_a_checked_attestation_and_the_actual_named_courier(
        bool checkedReceipt, int confirmedCourier, int responsibleCourier)
        => Assert.Equal("PHYSICAL_RECEIPT_REQUIRED", Assert.Throws<DeliveryRuleException>(() =>
            DeliveryTripPolicy.PhysicalReceipt(checkedReceipt, confirmedCourier, responsibleCourier)).Code);

    [Fact]
    public void Explicit_same_courier_receipt_is_valid_without_a_required_free_text_note()
    {
        DeliveryTripPolicy.PhysicalReceipt(true, 7, 7);
        Assert.Null(DeliveryTripPolicy.NormalizeNote("  "));
        Assert.False(string.IsNullOrWhiteSpace(DeliveryTripPolicy.StaffAttestation));
        Assert.Equal("Lý do", DeliveryTripPolicy.Reason("  Lý do  "));
        Assert.Equal(500, DeliveryTripPolicy.NormalizeNote(new string('x', 500))!.Length);
        Assert.Equal("NOTE_INVALID", Assert.Throws<DeliveryRuleException>(() =>
            DeliveryTripPolicy.NormalizeNote(new string('x', 501))).Code);
        Assert.Equal("REASON_REQUIRED", Assert.Throws<DeliveryRuleException>(() => DeliveryTripPolicy.Reason("  ")).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("%%%")]
    [InlineData("AA==")]
    [InlineData("AAAAAAAAAAAA")]
    [InlineData(" AAAAAAAAAAA=")]
    public void Version_requires_the_canonical_eight_byte_server_identity(string value)
        => Assert.Equal("VERSION_INVALID", Assert.Throws<DeliveryRuleException>(() => DeliveryTripPolicy.Version(value)).Code);

    [Fact]
    public void Dispatch_child_identity_has_a_fixed_domain_separated_vector_and_is_order_specific()
    {
        var root = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal(Guid.Parse("84a65d22-33a2-0bf9-329f-21e6caff216c"), DeliveryTripPolicy.ChildCommandId(root, 42));
        Assert.NotEqual(root, DeliveryTripPolicy.ChildCommandId(root, 42));
        Assert.NotEqual(DeliveryTripPolicy.ChildCommandId(root, 42), DeliveryTripPolicy.ChildCommandId(root, 43));
        Assert.NotEqual(DeliveryTripPolicy.ChildCommandId(root, 42), DeliveryTripPolicy.ChildCommandId(Guid.NewGuid(), 42));
        Assert.Equal("REQUEST_INVALID", Assert.Throws<DeliveryRuleException>(() => DeliveryTripPolicy.ChildCommandId(Guid.Empty, 42)).Code);
        Assert.Equal("REQUEST_INVALID", Assert.Throws<DeliveryRuleException>(() => DeliveryTripPolicy.ChildCommandId(root, 0)).Code);
        Assert.Equal("REQUEST_INVALID", Assert.Throws<DeliveryRuleException>(() => DeliveryTripPolicy.ChildCommandId(root, 42, "depart")).Code);
    }

    [Theory]
    [InlineData(DeliveryState.ReadyForHandover, true)]
    [InlineData(DeliveryState.HandedOver, true)]
    [InlineData(DeliveryState.Delivering, false)]
    public void Existing_D01_allows_predeparture_responsibility_transfer_and_denies_a_direct_courier_rename_after_departure(
        DeliveryState state, bool allowed)
    {
        var facts = new DeliveryWorkflowFacts(CourierAssigned: true, CustodyHandoverConfirmed: true);
        if (allowed) Assert.Equal(state, DeliveryWorkflowPolicy.Apply(state, DeliveryCommand.ReassignCourier, facts));
        else Assert.Equal("TRANSITION_DENIED", Assert.Throws<DeliveryRuleException>(() =>
            DeliveryWorkflowPolicy.Apply(state, DeliveryCommand.ReassignCourier, facts)).Code);
    }

    [Theory]
    [InlineData(DeliveryState.HandedOver)]
    [InlineData(DeliveryState.Delivering)]
    public void Exported_cancellation_is_only_pending_until_goods_and_money_are_reconciled(DeliveryState state)
    {
        Assert.Equal(DeliveryState.CancellationPending,
            DeliveryWorkflowPolicy.Apply(state, DeliveryCommand.RequestCancellation, new()));
        Assert.Equal("RECONCILIATION_REQUIRED", Assert.Throws<DeliveryRuleException>(() =>
            DeliveryWorkflowPolicy.Apply(DeliveryState.CancellationPending, DeliveryCommand.ConfirmCancellation, new())).Code);
    }
}
