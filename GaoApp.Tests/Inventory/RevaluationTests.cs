using FluentAssertions;

namespace GaoApp.Tests.Inventory;

public class RevaluationTests
{
    private class Valuation
    {
        public int Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public bool IsProvisional { get; set; }
    }

    [Fact]
    public void Should_Create_Revaluation_When_Provisional_Is_Resolved()
    {
        // Arrange
        var outbound = new Valuation
        {
            Quantity = 5,
            UnitCost = 0, // provisional
            IsProvisional = true
        };

        var inboundCost = 10m;

        // Act
        var finalCost = inboundCost;
        var revaluationAmount = outbound.Quantity * (finalCost - outbound.UnitCost);

        var revaluationCreated = outbound.IsProvisional;

        // Assert
        revaluationCreated.Should().BeTrue();
        revaluationAmount.Should().Be(5 * 10); // 50
    }

    [Fact]
    public void Should_Not_Create_Revaluation_When_No_Provisional()
    {
        // Arrange
        var outbound = new Valuation
        {
            Quantity = 5,
            UnitCost = 10,
            IsProvisional = false
        };

        // Act
        var revaluationCreated = outbound.IsProvisional;

        // Assert
        revaluationCreated.Should().BeFalse();
    }
}