using FluentAssertions;

namespace GaoApp.Tests.Inventory;

public class FifoTests
{
    private class Layer
    {
        public int Quantity { get; set; }
        public decimal Cost { get; set; }
    }

    [Fact]
    public void Should_Consume_FIFO_Correctly()
    {
        // Arrange
        var layers = new Queue<Layer>();

        layers.Enqueue(new Layer { Quantity = 10, Cost = 10 });
        layers.Enqueue(new Layer { Quantity = 10, Cost = 12 });

        var sellQty = 15;
        var totalCost = 0m;

        // Act
        while (sellQty > 0 && layers.Any())
        {
            var layer = layers.Peek();

            var take = Math.Min(layer.Quantity, sellQty);

            totalCost += take * layer.Cost;

            layer.Quantity -= take;
            sellQty -= take;

            if (layer.Quantity == 0)
                layers.Dequeue();
        }

        // Assert
        totalCost.Should().Be(10 * 10 + 5 * 12); // 100 + 60 = 160
    }
}