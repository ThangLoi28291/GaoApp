using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class ReceivingWorkbenchArchitectureContractTests
{
    [Fact]
    public void Mixed_components_share_one_aggregate_purchase_order_allocation()
    {
        var result = PurchaseReceivingAggregatePolicy.Calculate(
            orderedQuantity: 96m,
            orderedFactor: 1m,
            confirmedOrderedQuantity: 0m,
            shortClosedOrderedQuantity: 0m,
            otherInFlightBaseQuantity: 0m,
            componentBaseQuantities: [144m, 2m]);

        Assert.Equal(146m, result.CurrentReceiptBaseQuantity);
        Assert.Equal(50m, result.ConfirmedOverdeliveryAfterReceipt);
        Assert.Equal(0m, result.RemainingBaseQuantity);
    }

    [Fact]
    public void Receiving_entities_expose_durable_session_outside_and_idempotency_state()
    {
        var document = new StockDocument
        {
            ReceivingSessionState = ReceivingSessionState.Active,
            ReceivingRevision = 2
        };
        var line = new StockDocumentLine
        {
            ReceiptAllocationKind = ReceiptAllocationKind.OutsidePo,
            OutsidePoDecisionStatus = OutsidePoDecisionStatus.Pending
        };
        var action = new PurchaseReceivingAction
        {
            CommandId = Guid.NewGuid(),
            ActionType = PurchaseReceivingActionType.Add
        };

        Assert.Equal(ReceivingSessionState.Active, document.ReceivingSessionState);
        Assert.Equal(OutsidePoDecisionStatus.Pending, line.OutsidePoDecisionStatus);
        Assert.NotEqual(Guid.Empty, action.CommandId);
    }

    [Theory]
    [InlineData(96, 0, 0, 48, 48, 0)]
    [InlineData(96, 48, 0, 48, 48, 48)]
    [InlineData(96, 96, 0, 48, 48, 96)]
    public void Aggregate_overdelivery_uses_confirmed_plus_all_current_components(
        decimal ordered, decimal confirmed, decimal inFlight,
        decimal first, decimal second, decimal expectedOverdelivery)
    {
        var result = PurchaseReceivingAggregatePolicy.Calculate(
            ordered, 1m, confirmed, 0m, inFlight, [first, second]);
        Assert.Equal(expectedOverdelivery, result.ConfirmedOverdeliveryAfterReceipt);
    }
}
