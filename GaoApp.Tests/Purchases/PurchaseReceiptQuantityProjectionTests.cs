using GaoApp.Application.Services.Purchases;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptQuantityProjectionTests
{
    [Theory]
    [InlineData(10, 0, 0, 0, 10)]
    [InlineData(10, 4, 0, 0, 6)]
    [InlineData(10, 0, 0, 3, 7)]
    [InlineData(10, 4, 0, 3, 3)]
    [InlineData(10, 4, 1, 5, 0)]
    public void Create_calculates_available_quantity(
        decimal ordered, decimal confirmed, decimal shortClosed, decimal inFlight, decimal expected)
    {
        var result = PurchaseReceiptQuantityProjection.Create(
            ordered, confirmed, shortClosed, inFlight);
        Assert.Equal(expected, result.AvailableToAllocateQuantity);
    }

    [Fact]
    public void Create_rounds_all_quantities_to_three_decimals()
    {
        var result = PurchaseReceiptQuantityProjection.Create(10.0004m, 1.0004m, 0m, 2.0006m);
        Assert.Equal(6.999m, result.AvailableToAllocateQuantity);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(10, -1, 0, 0)]
    [InlineData(10, 0, -1, 0)]
    [InlineData(10, 0, 0, -1)]
    [InlineData(10, 8, 3, 0)]
    [InlineData(10, 4, 0, 7)]
    public void Create_fails_closed_for_inconsistent_values(
        decimal ordered, decimal confirmed, decimal shortClosed, decimal inFlight)
        => Assert.Throws<InvalidOperationException>(() =>
            PurchaseReceiptQuantityProjection.Create(ordered, confirmed, shortClosed, inFlight));

    [Fact]
    public void Different_receipt_units_share_one_canonical_allocation_pool()
    {
        var orderedCanonical = PurchaseReceiptQuantityConversionPolicy.ToCanonical(10m, 12m);
        var threeBoxes = PurchaseReceiptQuantityConversionPolicy.ToCanonical(3m, 12m);
        var thirtySixBottles = PurchaseReceiptQuantityConversionPolicy.ToCanonical(36m, 1m);

        Assert.Equal(threeBoxes, thirtySixBottles);
        var result = PurchaseReceiptQuantityProjection.Create(
            orderedCanonical, 0m, 0m, threeBoxes + thirtySixBottles);
        Assert.Equal(48m, result.AvailableToAllocateQuantity);
    }
}
