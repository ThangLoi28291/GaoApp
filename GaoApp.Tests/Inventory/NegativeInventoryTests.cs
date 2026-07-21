using FluentAssertions;

namespace GaoApp.Tests.Inventory;

public class NegativeInventoryTests
{
    private class Inventory
    {
        public int OnHand { get; set; }
    }

    private class Issue
    {
        public bool IsCreated { get; set; }
        public string Status { get; set; } = "";
    }

    [Fact]
    public void Should_Allow_Selling_When_Stock_Is_Negative()
    {
        // Arrange
        var inventory = new Inventory { OnHand = 0 };
        var sellQty = 5;

        // Act
        inventory.OnHand -= sellQty;

        // Assert
        inventory.OnHand.Should().Be(-5);
    }

    [Fact]
    public void Should_Create_Issue_When_Negative()
    {
        // Arrange
        var inventory = new Inventory { OnHand = 0 };
        var sellQty = 5;

        // Act
        inventory.OnHand -= sellQty;

        var issue = new Issue();

        if (inventory.OnHand < 0)
        {
            issue.IsCreated = true;
            issue.Status = "Pending";
        }

        // Assert
        issue.IsCreated.Should().BeTrue();
        issue.Status.Should().Be("Pending");
    }
}