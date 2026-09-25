using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;

namespace GaoApp.Tests.Invoices;

public sealed class InvoiceInputStockOpeningTests
{
    [Theory]
    [InlineData(2, 96, 55, 0, 43)]
    [InlineData(181, 1060, 635, 0, 606)]
    [InlineData(-10, 20, 5, 2, 3)]
    public void Opening_is_separate_and_preserves_available_balance(int opening, int received, int issued, int held, int available)
    {
        var day = new DateTime(2025, 5, 31, 17, 0, 0, DateTimeKind.Utc);
        InvoiceInputStockMovement Row(string key, decimal change, bool isOpening, int offset) => new()
        {
            Key = key, WarehouseId = 1, ProductVariantId = 1, ProductName = "Product", Code = "SKU",
            DateUtc = day.AddDays(offset), Kind = change < 0 ? "decrease" : "increase",
            Change = change, IsOpening = isOpening, SourceCode = key
        };
        var rows = new[] { Row("opening", opening, true, 0), Row("receipt", received, false, 1),
            Row("issued", -issued, false, 2), new InvoiceInputStockMovement
            { Key = "held", WarehouseId = 1, ProductVariantId = 1, DateUtc = day.AddDays(3), Kind = "hold", Held = held } };
        // Searching by one document must still show the full product balance, including the opening.
        var balance = Assert.Single(InvoiceInputStockReadService.BuildPage(rows, new() { Keyword = "receipt" }).Balances);
        Assert.Equal(opening, balance.Opening);
        Assert.Equal(received, balance.Received);
        Assert.Equal(issued, balance.Issued);
        Assert.Equal(available, balance.Available);
        Assert.Equal(rows.Sum(x => x.Change), balance.Remaining);
        Assert.Equal(balance.Remaining, rows[^1].After);
    }
}
