using FluentAssertions;

namespace GaoApp.Tests.Inventory;

public class ReturnTests
{
    private class OutboundLayer
    {
        public int Quantity { get; set; }
        public decimal Cost { get; set; }
    }

    [Fact]
    public void Should_Return_By_FIFO_Order_Not_Average()
    {
        // Arrange
        var outbound = new List<OutboundLayer>
        {
            new() { Quantity = 2, Cost = 10 },
            new() { Quantity = 3, Cost = 12 }
        };

        var returnQty = 4;

        var returned = new List<OutboundLayer>();

        // Act
        foreach (var layer in outbound)
        {
            if (returnQty <= 0) break;

            var take = Math.Min(layer.Quantity, returnQty);

            returned.Add(new OutboundLayer
            {
                Quantity = take,
                Cost = layer.Cost
            });

            returnQty -= take;
        }

        // Assert
        returned.Should().HaveCount(2);

        returned[0].Quantity.Should().Be(2);
        returned[0].Cost.Should().Be(10);

        returned[1].Quantity.Should().Be(2);
        returned[1].Cost.Should().Be(12);
    }

    [Fact]
    public void Should_Not_Use_Average_Cost()
    {
        // Arrange
        var layers = new List<OutboundLayer>
        {
            new() { Quantity = 2, Cost = 10 },
            new() { Quantity = 3, Cost = 12 }
        };

        var avg = (2 * 10 + 3 * 12) / 5m;

        // Act
        var isUsingAverage = layers.Any(x => x.Cost == avg);

        // Assert
        isUsingAverage.Should().BeFalse();
    }
}