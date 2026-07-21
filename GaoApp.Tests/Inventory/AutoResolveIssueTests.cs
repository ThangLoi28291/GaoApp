using FluentAssertions;

namespace GaoApp.Tests.Inventory;

public class AutoResolveIssueTests
{
    private class Layer
    {
        public int Quantity { get; set; }
        public decimal Cost { get; set; }
    }

    [Fact]
    public void Should_Resolve_Negative_Stock_By_FIFO_Multiple_Inbound()
    {
        // Arrange
        var negativeQty = 5;

        var inboundLayers = new Queue<Layer>();
        inboundLayers.Enqueue(new Layer { Quantity = 2, Cost = 10 });
        inboundLayers.Enqueue(new Layer { Quantity = 2, Cost = 11 });
        inboundLayers.Enqueue(new Layer { Quantity = 1, Cost = 12 });

        var totalResolved = 0;
        var totalCost = 0m;

        // Act
        while (negativeQty > 0 && inboundLayers.Any())
        {
            var layer = inboundLayers.Peek();

            var take = Math.Min(layer.Quantity, negativeQty);

            totalCost += take * layer.Cost;
            totalResolved += take;

            layer.Quantity -= take;
            negativeQty -= take;

            if (layer.Quantity == 0)
                inboundLayers.Dequeue();
        }

        var resolved = negativeQty == 0;

        // Assert
        resolved.Should().BeTrue();
        totalResolved.Should().Be(5);
        totalCost.Should().Be(2 * 10 + 2 * 11 + 1 * 12); // 10+10+11+11+12 = 54
    }
}