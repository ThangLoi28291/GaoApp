using FluentAssertions;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseOrderWorkflowPolicyTests
{
    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.ReturnedForRevision)]
    public void Draft_and_returned_orders_should_be_commercially_editable(PurchaseOrderStatus status)
        => PurchaseOrderWorkflowPolicy.IsCommerciallyEditable(status).Should().BeTrue();

    [Theory]
    [InlineData(PurchaseOrderStatus.PendingApproval)]
    [InlineData(PurchaseOrderStatus.Rejected)]
    [InlineData(PurchaseOrderStatus.Approved)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public void Submitted_or_terminal_orders_should_not_be_editable(PurchaseOrderStatus status)
        => PurchaseOrderWorkflowPolicy.IsCommerciallyEditable(status).Should().BeFalse();

    [Fact]
    public void Authorized_creator_should_be_allowed_to_review_own_order()
    {
        var action = () => PurchaseOrderWorkflowPolicy.EnsureCanReview(
            PurchaseOrderStatus.PendingApproval,
            reviewingUserId: 42);

        action.Should().NotThrow();
    }

    [Fact]
    public void A_different_authorized_user_should_be_allowed_to_review()
    {
        var action = () => PurchaseOrderWorkflowPolicy.EnsureCanReview(
            PurchaseOrderStatus.PendingApproval,
            reviewingUserId: 84);

        action.Should().NotThrow();
    }

    [Fact]
    public void Review_should_require_an_authenticated_actor()
    {
        var action = () => PurchaseOrderWorkflowPolicy.EnsureCanReview(
            PurchaseOrderStatus.PendingApproval,
            reviewingUserId: null);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*người duyệt*");
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Rejected)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    [InlineData(PurchaseOrderStatus.FullyReceived)]
    [InlineData(PurchaseOrderStatus.ShortClosed)]
    public void Terminal_orders_should_not_be_cancellable(PurchaseOrderStatus status)
    {
        var action = () => PurchaseOrderWorkflowPolicy.EnsureCanCancel(status, false);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Any_received_quantity_should_block_cancel()
    {
        var action = () => PurchaseOrderWorkflowPolicy.EnsureCanCancel(
            PurchaseOrderStatus.PartiallyReceived,
            hasReceivedGoods: true);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*đã phát sinh nhận hàng*");
    }

    [Fact]
    public void Source_commercial_command_should_not_accept_product_unit_or_quantity()
    {
        var properties = typeof(UpdateSourcePurchaseOrderCommercialLineRequest)
            .GetProperties()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        properties.Should().Contain(new[]
        {
            nameof(UpdateSourcePurchaseOrderCommercialLineRequest.PurchaseOrderLineId),
            nameof(UpdateSourcePurchaseOrderCommercialLineRequest.UnitPriceBeforeVat),
            nameof(UpdateSourcePurchaseOrderCommercialLineRequest.TaxId)
        });
        properties.Should().NotContain(new[]
        {
            "ProductVariantId", "ProductUnitConversionId", "UnitId", "Quantity", "ConversionFactor"
        });
    }
}
