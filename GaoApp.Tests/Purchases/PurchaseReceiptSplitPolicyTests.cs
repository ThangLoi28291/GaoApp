using FluentAssertions;
using GaoApp.Application.Services.Purchases;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptSplitPolicyTests
{
    [Fact]
    public void Two_targets_support_partial_quantity_and_exact_base_conservation()
    {
        var plan = Build(
            [SourceLine(10, 10m)],
            [Target(1, Allocation(10, 6m)), Target(2, Allocation(10, 4m))]);

        plan.Targets.Should().HaveCount(2);
        plan.Targets[0].Lines.Single().Quantity.Should().Be(6m);
        plan.Targets.Sum(x => x.Lines.Sum(y => y.BaseQuantity)).Should().Be(10m);
    }

    [Fact]
    public void Three_targets_are_supported_and_residual_order_is_deterministic()
    {
        var plan = Build(
            [SourceLine(10, 3m, freight: 100m)],
            [Target(3, Allocation(10, 1m)), Target(1, Allocation(10, 1m)), Target(2, Allocation(10, 1m))],
            freightTotal: 100m,
            capitalizeFreight: true);

        plan.Targets.Select(x => x.TargetIndex).Should().Equal(1, 2, 3);
        plan.Targets.Select(x => x.FreightTotal).Should().Equal(33.33m, 33.33m, 33.34m);
    }

    [Theory]
    [InlineData(9.998)]
    [InlineData(10.001)]
    public void Under_or_over_allocation_is_rejected(decimal allocated)
    {
        var action = () => Build(
            [SourceLine(10, 10m)],
            [Target(1, Allocation(10, allocated)), Target(2, Allocation(10, 0.001m))]);

        action.Should().Throw<InvalidOperationException>().WithMessage("*bảo toàn*");
    }

    [Fact]
    public void Every_result_must_have_a_positive_allocation()
    {
        var action = () => Build(
            [SourceLine(10, 10m)],
            [Target(1, Allocation(10, 10m)), Target(2, Allocation(10, 0m))]);

        action.Should().Throw<InvalidOperationException>().WithMessage("*ít nhất một*");
    }

    [Fact]
    public void Non_representable_display_quantity_is_rejected_instead_of_drifting()
    {
        var action = () => Build(
            [SourceLine(10, 2m, factor: 3m)],
            [Target(1, Allocation(10, 1m)), Target(2, Allocation(10, 1m))]);

        action.Should().Throw<InvalidOperationException>().WithMessage("*đơn vị nhập*");
    }

    [Fact]
    public void Primary_capitalized_freight_uses_source_line_basis_and_conserves_total()
    {
        var plan = Build(
            [SourceLine(10, 10m, freight: 100m)],
            [Target(1, Allocation(10, 6m)), Target(2, Allocation(10, 4m))],
            freightTotal: 100m,
            capitalizeFreight: true);

        plan.UsedD8Fallback.Should().BeFalse();
        plan.Targets.Select(x => x.FreightTotal).Should().Equal(60m, 40m);
        plan.Targets.SelectMany(x => x.Lines).Sum(x => x.FreightAllocation).Should().Be(100m);
    }

    [Fact]
    public void D8_zero_price_noncapitalized_fallback_uses_target_base_quantity()
    {
        var plan = Build(
            [SourceLine(10, 3m, lineAmount: 0m, freight: 0m)],
            [Target(1, Allocation(10, 1m)), Target(2, Allocation(10, 2m))],
            freightTotal: 100m,
            capitalizeFreight: false);

        plan.UsedD8Fallback.Should().BeTrue();
        plan.Targets.Select(x => x.FreightTotal).Should().Equal(33.33m, 66.67m);
        plan.Targets.Sum(x => x.FreightTotal).Should().Be(100m);
        plan.Targets.SelectMany(x => x.Lines).Should().OnlyContain(x => x.FreightAllocation == 0m);
    }

    [Fact]
    public void Different_supplier_requires_explicit_payment_confirmation()
    {
        var action = () => Build(
            [SourceLine(10, 10m)],
            [Target(1, Allocation(10, 5m)), Target(2, Allocation(10, 5m), supplierId: 99)],
            sourceSupplierId: 1);

        action.Should().Throw<InvalidOperationException>().WithMessage("*xác nhận thanh toán*");
    }

    private static PurchaseReceiptSplitPlan Build(
        IReadOnlyList<PurchaseReceiptSplitSourceLine> lines,
        IReadOnlyList<PurchaseReceiptSplitTargetInput> targets,
        decimal freightTotal = 0m,
        bool capitalizeFreight = false,
        int sourceSupplierId = 1)
        => PurchaseReceiptSplitPolicy.Build(new PurchaseReceiptSplitPolicyInput
        {
            SourceSupplierId = sourceSupplierId,
            SourceIsMerchandisePaid = true,
            SourceMerchandisePayeeName = "Supplier",
            HasFreight = freightTotal > 0m,
            CapitalizeFreightInInventoryCost = capitalizeFreight,
            FreightTotal = freightTotal,
            Lines = lines,
            Targets = targets
        });

    private static PurchaseReceiptSplitSourceLine SourceLine(
        int id, decimal baseQuantity, decimal factor = 1m,
        decimal lineAmount = 10m, decimal freight = 0m)
        => new(id, baseQuantity, factor, lineAmount, freight);

    private static PurchaseReceiptSplitAllocationInput Allocation(int lineId, decimal baseQuantity)
        => new(lineId, baseQuantity);

    private static PurchaseReceiptSplitTargetInput Target(
        int index,
        PurchaseReceiptSplitAllocationInput allocation,
        int supplierId = 1)
        => new(index, supplierId, null, null, false, [allocation]);
}
